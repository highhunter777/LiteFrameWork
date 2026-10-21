# 工具链 v2：回放落盘与现场包

## 背景与目标

用户已裁决工具链 v2 覆盖调试、开发、交付三面，采用**直接替换**而非永久双轨，第一刀先做“回放落盘 + 现场包”。

当前痛点：联机问题只能靠 `MismatchReport` 计数、散落日志和人工复现；`SimRunRecord`/`SimReplayRunner` 有数据模型与首分歧定位能力，但无序列化、无生产调用方；服务端权威房间没有可离线重放的命令记录；客户端没有一键现场包。结果是“每个 bug 都要重新约一局”，与路线图 T0 的“可复现现场”目标相反。

本次交付目标：

1. 权威房间的每一局都能产出一份**可离线重放**的记录，重放出首个分歧帧与对应命令位置。
2. 房间终态自动产出**现场包**（记录 + 服务端日志尾 + 身份/配置元数据），客户端在 checksum 不一致时产出本地现场包（日志 + 帧号 + 身份）。
3. 重放器使用真实的 `RoomRuntime` 执行链路，而不是复制一份 Sim 步进逻辑；保证延迟补偿、测试规则、席位与输入门语义一致。
4. 以 L1 测试先行的方式交付，并给出可执行的验证与回滚路径。

## 需求澄清与假设

已确认：

- 范围：调试 + 开发 + 交付全量 v2；本次只交付第一刀“回放落盘 + 现场包”。
- 共存方式：直接替换。每项能力落地后成为唯一路径，不维护永久双轨；旧指标可保留，但不得成为第二套回放/诊断实现。
- 第一刀：回放落盘 + 现场包，优先复用现有确定性/诊断地基。

本次采用的关键假设：

1. **在线房间不能只用 `SimRunRecord` 输入回放**：服务端 `RoomRuntime.StepFrame` 在 `SimStep` 之后执行延迟补偿（`LagCompensator.CompensateFire`）与 `MatchSystem.Tick`，且开火输入携带 `viewFrame`/`ackSnapshot`；这些上下文不在 `SimInputFrame` 里。纯输入回放无法复现权威结果。因此 v2 在线回放采用**房间命令日志**，通过真实 `RoomRuntime` 重放；`SimRunRecord/SimReplayRunner` 继续服务离线纯 Sim/批跑诊断，不删除、不复制其契约。
2. 重放机与录制机使用**同一构建、同一套表、同一张地图**；记录头携带 `buildHash`、配置摘要、地图摘要；任一不匹配则拒绝重放并给出可操作错误，而不是静默跑出假分歧。
3. 记录与现场包**默认只落本地**，本刀不做上传/CDN/GM 下载端点；云端产物通过宿主文件目录取回，后续再补安全下载通道。
4. 录制默认开启但受容量上限与配置开关约束；录制绝不能阻塞或炸掉房间权威循环。
5. 服务端文件写入只发生在宿主层；`Assets/RoomServer/Runtime` 保持 R11 纯度（禁 IO/proto/LiteNet/系统时钟）。

## 范围与非目标

### 范围内

- 房间命令日志记录器（内核侧，纯内存）：
  - 记录 `RoomCommand`（Join/Input/Tick/StartEarly/Disconnect/Rebind/RestoreAck/Shutdown）与输入帧的深拷贝。
  - 每个权威步进后的帧号 + 全量 checksum。
  - 测试规则变更事件（`SetTestRules` 的帧号与规则快照）。
  - 容量上限、截断标记、版本号。
- 宿主侧记录封装与编码：
  - `buildHash`、`FixedConfig.Digest`、地图摘要、房间/对局身份、seed、配置快照、结束原因、服务端日志尾、客户端 mismatch 摘要。
  - 版本化 JSON + GZip，原子写盘（临时文件 + 替换），文件保留上限。
- 房间重放器：
  - 从记录重建 `RoomConfig`/`RoomRuntime`，按记录顺序喂命令，逐步比对 checksum。
  - 输出首个分歧帧、对应命令序号、期望/实际 checksum；配置/构建/地图/表身份不符时 fail-fast。
- 服务端现场包：
  - 房间终态（Closed/Aborted）自动落盘记录 + 日志尾 + manifest。
  - 日志尾经 `HostLog` 有界环或配置日志文件尾读取（单源二选一，不新增第二份日志设施）。
  - 写入失败只计数/告警，不影响房间收尾。
- 客户端现场包：
  - `BattleContext` checksum 不一致时落盘 `mismatch.json` + 最近日志 + Player.log 尾 + 身份信息，限频、限大小。
  - 新增 Assets 下 `.cs` 一律经 unity-pipeline 导入并生成 `.meta`，不手写 `.meta`。
- 测试与文档：
  - L1 录制/重放/编码/截断/身份校验用例；客户端 bundle 纯逻辑用例（可行时）。
  - 更新 v2 路线图与施工记录，登记工具用法与已知边界。
  - 同步 `scripts/gate/toolchain.json`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`（新增 CLI/目录后）。

### 非目标

- 不做云端上传、CDN、GM 下载端点、符号服务器。
- 不做 native crash handler/minidump；fatal 现场包在后续 T0 补。
- 不做客户端完整对局重放；客户端本刀只产 mismatch 现场包，权威重放由服务端记录承担。
- 不做 world-ring 字段级 diff 的完整落盘；本刀先给首分歧帧 + checksum + 命令序号，字段 diff 作为下一刀。
- 不改热更、构建、CI、开发 Player、性能埋点链路（属 v2 后续 T1/T2）。
- 不新增第二套 Sim 步进/回放实现；重放必须走真实 `RoomRuntime`。

## 现状与约束

- `RoomRuntime.Execute(in RoomCommand, List<RoomOutput>)` 是房间唯一入口；命令种类有限，`ClientInputBatch` 携带 `Frame/AckSnapshot/ViewFrame/Count/Frames`（`Assets/RoomServer/Runtime/RoomCommand.cs`、`ClientInputBatch.cs`）。
- `RoomRuntime.StepFrame`：`SimStep.Step(..., advanceMatch:false)` → 捕获事件/快照/输入历史 → `ConsumePendingFire`（延迟补偿）→ `MatchSystem.Tick` → 最终快照捕获（`Assets/RoomServer/Runtime/RoomRuntime.cs:597-632`）。这就是重放必须复用的执行链。
- `RoomRuntime` 处于 `Assets/RoomServer/Runtime`，R11 纪律禁 Console/系统时钟/文件 IO/proto/LiteNet。记录器只能纯内存；文件写入归 `Server/RoomServer`。
- `RoomInstance` 构造 `RoomRuntime` 时注入 `matchId = RoomId + ":" + Guid` 与 `gameplayDigestBase`；`matchId` 参与出生点选择，重放必须使用相同值（`Server/RoomServer/RoomInstance.cs:307-309`）。
- 测试规则是运行态持有件，`RoomInstance`/`RoomWorkerDriver` 在驱动开头调用 `Runtime.SetTestRules`；中局 GM 变更会影响权威步进，记录必须捕获（`RoomWorkerDriver.cs:147-148`、`ServerHost.cs:1739`）。
- 客户端 checksum 不一致在 `BattleContext.OnSnapshot` → `_mirror.ApplyAndReconcile` 返回 true → `SendMismatch(snapshot.Frame)`（`Assets/LiteClient/Runtime/Battle/BattleContext.cs:341-345`）；`Log.Recent` 只有 32 条环（`Assets/LiteFramework/Scripts/Core/Logging/Log.cs:26-40`）。
- 现有 `SimRunRecord/SimReplayRunner` 的数据模型完整，但生产/测试均无调用方（`Docs/项目说明/13-调试手册.md:382-388`），且其执行模型不接 `RollbackSim`/延迟补偿。
- 已有身份面：`BuildHash`（BuildHash.g.cs）、`FixedCombatConfig.Digest`（含表字节基摘要 + 本局规则初值）、`SimMapDigest`（地图文本摘要）。重放前必须校验。
- 服务端已有 `HostLog`、`Ops` 计数、结算 Outbox 的原子写盘先例；本刀复用“宿主层写盘、失败不反噬权威循环”的纪律。
- 测试工程 `Tests/LiteNet.Tests` 已引用 `Server/RoomServer/RoomServer.csproj` 与 `Assets/RoomServer`，可直接跑房间级 L1 用例。
- 工程约定：源码改动前查两份质量登记表；共享源改动需重算 BuildHash。本刀优先把记录器放在 `Assets/RoomServer/Runtime/Diagnostics`（房间内核，不在共享源集 LiteSim/LiteNet 闭包内；若触碰共享源则按红线重算）。

## 候选方案与取舍

| 方案 | 做法 | 优点 | 致命问题 | 结论 |
| --- | --- | --- | --- | --- |
| A. 纯 `SimRunRecord` 输入回放 | 录制 seed + 初始世界 + 每帧输入，`SimReplayRunner` 重跑 | 复用现成模型；体积小 | 丢失 `viewFrame/AckSnapshot`、延迟补偿、测试规则中局变更、席位/出生上下文；在线房间必然假分歧 | 否 |
| B. 快照轨迹回放 | 录每帧/每 N 帧权威快照，离线按快照播放 | 一定能复现“看到的画面”；实现直接 | 不能重算/不能定位 1-ulp 逻辑分歧；体积大；不是“重放”而是“录像” | 否（可作为后续表现回放补充） |
| C. 房间命令日志 + 真实 `RoomRuntime` 重放 | 录命令与每步 checksum；重放构造同配置 RoomRuntime，按序喂命令 | 复用唯一执行链；覆盖延迟补偿/规则/席位/输入门；可定位首分歧帧 | 记录体积与录制开销；依赖构建/表/地图一致 | **采用** |
| D. 客户端本地回滚记录 | 在 `RollbackSim` 侧录最终输入/状态 | 贴近客户端表现 | 回滚/快照纠偏使“最终输入”不是单向前向流；跨端不可复现；内核纪律与线程边界复杂 | 否（仅用于客户端 mismatch 元数据） |

选择 C。它不是“第二套 Sim”，而是把唯一权威执行链变成可重放资产；这是 v2“直接替换”的关键：以后在线回放的唯一实现就是它，不再另写输入回放器。

## 设计要点

### 1. 分层与所有权

```text
Assets/RoomServer/Runtime/Diagnostics/RoomRunRecorder.cs   // 纯内存，R11 安全
        ↑ 记录命令/步进 checksum/规则事件
Assets/RoomServer/Runtime/RoomRuntime.cs                    // 在 Execute / StepFrame 两个点调用
        ↓ 暴露只读快照 TakeRunRecord()
Server/RoomServer/Diagnostics/RoomRunEnvelope.cs           // 宿主身份封装 + JSON/GZip 编解码
Server/RoomServer/Diagnostics/RoomRunStore.cs              // 原子写盘/保留/失败计数
Server/RoomServer/Diagnostics/RoomReplayRunner.cs          // 重建 RoomRuntime，按序喂命令，比对 checksum
Server/RoomServer/Diagnostics/RoomDiagnosticsService.cs    // 终态触发、日志尾、客户端 mismatch 汇总
Server/ReplayTool/                                         // CLI：replay <file>，输出首分歧
Assets/LiteClient/Runtime/Diagnostics/ClientBundleWriter.cs // 纯文件封装（可注入路径）
Assets/LiteClient/Runtime/Battle/BattleContext.cs          // mismatch 触发点
```

### 2. `RoomRunRecord`（内核侧，纯内存）

- 版本：`FormatVersion = 1`。
- 头：`RoomId`、`MatchId`、`ExpectedPlayers`、`Seed`（StartMatch 解析后的值）、`StartedAtCommandIndex`、`EndReason`、`Truncated`。
- 命令：按 `Execute` 到达顺序记录 `Kind` + 所需字段；`ClientInput` 深拷贝 `Count` 帧（不引用调用方复用数组）。
- 步进：每步 `Frame` + `SimChecksum.ComputeChecksum(AuthSim)`（全量口径）；只记录真正推进的步（`StepsCount` 增加时）。
- 规则事件：`SetTestRules` 调用时记录 `Frame` + `NoDeath/InfiniteAmmo/HeadHitLineOverride/HeadshotRadiusOverride`（为空则 null）。
- 上限：`MaxCommands`（默认 400_000）与 `MaxSteps`（默认 100_000）；超限置 `Truncated=true` 并停止追加，不抛、不阻塞。
- 只读快照：`TakeRunRecord()` 返回深拷贝/不可变视图，供宿主在房间静默后取走；录制器不再被房间线程写。

### 3. 捕获点

- `RoomRuntime.Execute` 入口：`_recorder?.RecordCommand(in cmd)`（在 dispatch 之前，保证顺序与真实消费一致）。
- `RoomRuntime.SetTestRules`：`_recorder?.RecordTestRules(AuthSim.Frame, rules)`。
- `RoomRuntime.StepFrame` 末尾（`MatchSystem.Tick` 之后、最终 `SnapshotHistory.Capture` 之后）：`_recorder?.RecordStep(AuthSim.Frame, SimChecksum.ComputeChecksum(AuthSim))`。
- 录制器由构造参数/宿主配置注入；null = 关闭录制，零额外开销。

### 4. 宿主封装与编码

`RoomRunEnvelope` 包含：

- `schemaVersion`、`buildHash`、`fixedConfigDigest`、`gameplayDigestBase`、`mapId`、`mapDigest`、`roomId`、`matchId`、`expectedPlayers`、`seed`、`startedAtUtc`、`endedAtUtc`、`endReason`、`truncated`。
- `config`：重放所需的 `RoomConfig` 字段（Rules、MatchTimeLimitMs、WaitingTimeoutMs、ExpectedPlayers、MapId、Seed）。
- `record`：`RoomRunRecord` 的 JSON DTO。
- `serverLogTail`：`HostLog` 最近 N 条/最近 M KB（服务端日志门面需提供只读尾；若没有，先用现有日志输出文件尾读，路径可配置）。
- `clientMismatch`：最近一次 `MismatchReport` 的帧号/时间/席位（服务端已有 `Ops.LastMismatchFrame`；可选扩展）。
- `notes`：截断原因/写入告警。

编码：`System.Text.Json` + `GZipStream`；原子写盘 = `*.tmp` 写入 + `File.Replace/Move`；文件名 `room-<sanitized room>-<match>-<utc>.json.gz`。保留：按目录文件数/总大小上限淘汰最旧（默认 100 个/512MB）。写入失败只 `HostLog.Warn` + 计数，不抛进房间循环。

### 5. 重放器

输入：记录文件 + 当前构建的 `RoomConfig` 加载器/地图加载器/表实例。

步骤：

1. 校验 `buildHash` 与本进程常量一致；`fixedConfigDigest` 与 `FixedCombatConfig.Capture(values, weapons, gameplayDigestBase)` 重算值一致；`mapDigest` 与加载地图的 `SimMapDigest` 一致。任一不符 fail-fast，输出“录制 vs 当前”的对照。
2. 用记录头构造 `RoomConfig`（`RoomId/MatchId/ExpectedPlayers/Rules/Seed/MapId/MatchTimeLimitMs/WaitingTimeoutMs`；`Seed` 用记录值，避免 `DeriveSeed(nowMs)` 差异）。
3. 构造 `RoomRuntime`（同一个 `matchId`、`gameplayDigestBase`、values/weapons；录制器置 null）。
4. 按命令顺序 `Execute`；忽略输出列表；每步与记录 checksum 比对；首个不一致 → 输出 `frame`、`commandIndex`、`expected/actual`，停止。
5. 若记录 `Truncated`：先重放已记录部分，报告“只覆盖到 frame X”，不把截断当通过。
6. 退出码：0 = 全一致；2 = 首分歧；3 = 身份/格式不匹配；4 = IO/解析错误。

CLI：`Server/ReplayTool` 控制台项目，`dotnet run --project Server/ReplayTool -- replay <file>`；同时提供 `--json` 机器可读输出。重放器核心放在 `Server/RoomServer/Diagnostics`，CLI 只做参数/输出。

### 6. 服务端现场包触发

- 房间终态（`Closed/Aborted`）且 `TryDestroyTerminalRoom` 判定静默后：从 `RoomInstance` 取 `Runtime.TakeRunRecord()`，组装 envelope + 日志尾，写盘。
- 客户端 mismatch：服务端已有 `Ops.NoteMismatch`；本刀先把 mismatch 帧/时间记录进房间诊断上下文，随终态 envelope 落盘。**不在 mismatch 当场写盘**（房间仍在运行、录制器仍被房间线程写；避免并发与阻塞）。这是本刀的已知边界，后续用“房间线程封印 → 宿主异步写”的通道补即时落盘。
- Worker/宿主两种执行形态：取记录只允许在房间静默后（宿主线程）；不跨线程读内核可变结构。
- 目录：`RoomServerConfig.Diagnostics.Directory`（默认空 = 不落盘）；测试/本地服显式传临时目录。生产/云侧配置显式开启，默认不改变现有行为。

### 7. 客户端现场包

- 触发：`BattleContext.OnSnapshot` 中 `ApplyAndReconcile` 返回 true（已 `SendMismatch`）后调用 `ClientBundleWriter.CaptureMismatch(...)`；`OnStartGame` 的 `ConfigHash` 不符也触发（拒局现场）。
- 内容：`manifest.json`（buildHash、configDigest、matchId/roomId、frame、localEntityId、reconcileCount、时间、平台/版本）、`log-recent.txt`（`Log.Recent` 全 32 条）、`player-log-tail.txt`（`Application.persistentDataPath/Player.log` 尾 64KB，存在才读）、`mismatch.json`。
- 限频：同一对局最多 3 份，间隔 ≥30s；单份 ≤2MB；写失败只 `Log.Warning`，不影响对局。
- 路径：`Application.persistentDataPath/diagnostics/`；`manifest.json` 里写绝对路径，DevHUD 后续可显示。
- 不上传、不包含密钥/票据/GM Key；日志尾按行截断，避免把 `.env`/PSK 带出（文件本身不在日志里）。

### 8. 直接替换与旧面处置

- 在线重放唯一实现 = `RoomReplayRunner`；不新增“客户端输入回放”作为并行在线方案。
- `Ops.MismatchReports/LastMismatchFrame` 保留为指标（不是第二套诊断资产）。
- `SimRunRecord/SimReplayRunner` 保留给离线纯 Sim/批跑；本刀不接生产，不删除。
- 现有 `HostLog`/`DiagTrace` 保留为日志面；现场包是它们的归档，不替代实时日志。
- 本刀不动构建/热更/CI；v2 后续按路线图 T1/T2 替换。

## 通用性与扩展性

- **单一执行链**：重放走真实 `RoomRuntime`，不复制 `StepFrame`；以后新增命令/系统，只要命令仍从 `Execute` 进入、步进仍走 `StepFrame`，录制/重放自动覆盖。
- **稳定版本化**：`schemaVersion` + `FormatVersion`；新增命令种类时在 DTO 加字段（旧记录按默认值），或升版本并明确拒绝旧版。
- **身份可扩展**：envelope 已含 build/config/map 三个身份；后续加 `contentReleaseId`、`protocolVersion` 等只加字段，不改重放核心。
- **路径/容量/开关**：全部走 `DiagnosticsOptions`，不散落魔法值；目录、上限、保留策略单源。
- **客户端 bundle**：`ClientBundleWriter` 只依赖路径端口与字符串内容，便于后续加截图、状态 dump、上传通道，不绑定具体 UI。
- **不做过度抽象**：本刀不引入通用二进制序列化框架、不做跨平台 world codec；JSON/GZip + DTO 足够，等有性能证据再换。

## 测试策略

测试先行（红测）：

1. `RoomRunReplayTests`（`Tests/LiteNet.Tests`）：
   - 固定房间（2 人、固定 seed/map/matchId）→ Join×2 → StartEarly → 输入×N → Tick×N → 录制；用新 `RoomRuntime` 重放，逐步 checksum 全等。
   - 篡改记录中某帧输入 → 重放报告**准确的首分歧帧**；不早不晚。
   - 截断记录（`MaxCommands/MaxSteps` 小值）→ 重放覆盖已记录段并报告 `Truncated`，不宣称全一致。
   - JSON+GZip round-trip：命令/输入/checksum/规则事件/头字段逐项保留。
   - `buildHash`/`fixedConfigDigest`/`mapDigest` 任一不匹配 → 重放拒绝且错误含“录制 vs 当前”。
   - 测试规则中局变更（`SetTestRules`）→ 重放同一帧应用，checksum 一致；缺事件则分歧可定位。
2. `ClientBundleTests`（纯逻辑或 EditMode，视实现依赖）：
   - mismatch 触发写出 manifest/log 文件；限频/大小上限生效；路径可注入；失败不抛。
3. 纪律门禁：
   - R11 扫描：`Assets/RoomServer/Runtime/Diagnostics` 无 IO/proto/LiteNet/系统时钟。
   - `python scripts/codegen/gen-build-hash.py --check-shared`（若本次触碰共享源；预计不触碰，仍作为 verify 附加检查）。
4. 真实验证命令：
   - `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`
   - `python scripts/gate/doc-lint.py`
   - 定向：`dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter FullyQualifiedName~RoomRunReplay`

**红测要求**：在实现前先写用例并运行，失败必须是“类型/成员不存在”或断言失败，不得是环境错误。

## 验收标准

- AC1 录制：一局 L1 房间对跑产生 `RoomRunRecord`，含命令、步进 checksum、身份头；`Truncated=false`（正常规模）。
- AC2 重放：`RoomReplayRunner` 对同一记录重放，`FirstDivergenceFrame=-1`，checksum 数 = 记录步数。
- AC3 定位：篡改一帧输入后，重放报出准确的首分歧帧与命令序号。
- AC4 落盘：房间终态写出 `.json.gz` 现场包；原子写；保留上限生效；写失败不影响房间收尾。
- AC5 客户端：checksum mismatch 写出客户端现场包（manifest + 最近日志 + Player.log 尾），限频/大小上限生效，无密钥。
- AC6 身份：buildHash/configDigest/mapDigest 不匹配时重放拒绝，错误可操作。
- AC7 门槛：L1 全绿、定向用例全绿、R11 纪律扫描通过、doc-lint 0 violation。
- AC8 文档：v2 路线图回写“本刀已落”的边界与后续项；施工记录登记命令/证据/未决项。

## 风险与回滚

| 风险 | 影响 | 缓解 | 回滚 |
| --- | --- | --- | --- |
| 录制开销/内存 | 房间 tick 变慢或 OOM | 默认上限 + 配置开关；录制只做深拷贝不 IO；上线前用 Ops 观察；超阈值停录 | 配置关闭录制，代码路径保留但 no-op |
| 重放假分歧 | 工具不可信 | 身份三校验（build/config/map）+ 同构 `RoomRuntime` + 固定 seed/matchId；先跑 L1 真值用例 | 删除/禁用重放 CLI；现场包仍可人工分析 |
| 宿主/Worker 并发 | 读到半截记录 | 只在房间静默后取；录制器返回不可变快照；不在房间线程写盘 | 关闭自动落盘，仅手动导出 |
| 日志含敏感信息 | 泄漏风险 | 本地 only、大小上限、不带上行密钥；文档明确 | 删除产物；收紧日志尾行过滤 |
| 文件 IO 失败 | 丢现场 | 失败只告警/计数；不反噬房间 | 关闭落盘；保留内存记录到终态日志 |
| 记录格式演进 | 旧文件不可读 | schemaVersion/FormatVersion；拒绝旧版并提示 | 保留旧解码器或归档旧文件 |
| v2 范围蔓延 | 第一刀做不完 | 本刀严格限定“服务端记录 + 重放 + 双端现场包”；T1/T2 另开交付 | 用本 plan 的范围/非目标裁剪 |

**回滚路径**：`DiagnosticsOptions.Enabled=false` 即停止录制/落盘；`RoomRuntime` 的 recorder 注入为 null；新增文件全部为独立新增，不改既有协议/快照/BuildHash 闭包；无数据迁移。
