# 代码评审：工具链 v2：回放落盘与现场包

> 运行 toolchain-v2-replay · 评审对象：正式施工后的工作区改动

## 变更范围

- 内核：`Assets/RoomServer/Runtime/Diagnostics/RoomRunRecord.cs`、`RoomRunRecorder.cs`；`RoomRuntime.cs` 接线（命令/规则/seed/步进 checksum/`TakeRunRecord`）。
- 宿主：`Server/RoomServer/Diagnostics/` 五件（信封/编解码、存储、重放、配置、落盘服务）；`RoomInstance`/`ServerHost`/`RoomServerConfig`/`InboundHandlers`/`InboundPorts`/`HostLog` 接线。
- CLI：`Server/ReplayTool/`（默认仓库表 + 身份复算；`--defaults` 仅测试/离线）。
- 客户端：`LiteFramework/Scripts/Core/Diagnostics/DiagnosticsBundleWriter.cs`（零引擎）、`LiteClient/Runtime/Diagnostics/ClientDiagnosticsBundle.cs`、`BattleContext` 触发点。
- 测试：L1 五组 15 例 + EditMode 1 例；文档/配置/工具清单回写。
- 明确不在本批：热更、构建、CI、开发 Player、崩溃捕获/符号、上传/下载。

## 正确性与边界

- **重放走真实 `RoomRuntime`**：不复制 `StepFrame`，延迟补偿/`MatchSystem.Tick`/输入门语义与权威链一致；`MatchId`/`Seed`/`gameplayDigestBase`/配置/地图/表身份均从记录/当前装载复算，避免“同记录不同环境”的假分歧。
- **记录有界**：命令/步进/规则事件封顶；超限 `Truncated` 且停止追加；重放对截断显式报告，不宣称整局一致。
- **仅终态落盘**：录制器只在房间线程写、只在房间静默后取走；避免跨线程读半截记录。mismatch 帧号随终态记录落盘（即时 flush 为已登记后续）。
- **身份闸**：buildHash/fixedConfigDigest/mapDigest 任一不符拒绝重放；CLI 默认加载仓库表复算，`--defaults` 只服务默认表/离线记录。
- **客户端限频**：同局最多 3 份、默认 30s；`CanAccept` 在高成本日志/Player.log 读取前提前返回；失败清理半成品目录并只告警。
- 已知边界：录制器对“声明帧数与数组长度不一致”的非法输入批次只做截断标记并记录可拷贝部分（宿主 `InputGate` 本应拒绝此类包）；生产链由输入闸门与协议上限保证，不构成正常路径缺陷。

## 测试充分性

- 回放：同记录一致、篡改第 42 帧准确定位、截断、身份不符、测试规则中局生效。
- 编解码/存储：命令/输入/checksum/规则事件往返保真；4 写保留 2、无 `.tmp`。
- 服务端现场包：身份/记录/地图/日志尾/mismatch 可解码；保留上限。
- 客户端：manifest/日志/Player.log 尾写出；同局限频/上限。
- CLI：默认表 0、真实仓库表 0、篡改 2、身份 3；入口与 `dotnet run` 同一 `Main`。
- 缺口：未做生产负载基准（录制开销）；未做云上首份真实记录；崩溃捕获/字段级 diff 未覆盖（均登记为后续）。

## 通用性与扩展性

- 单一执行链：新增命令只要仍经 `RoomRuntime.Execute`、步进仍经 `StepFrame`，录制/重放自动覆盖；不新增第二套 Step 编排。
- 版本化：`FormatVersion`/`SchemaVersion` + DTO；新增字段按默认值兼容，破坏性变更升版本并拒绝旧版。
- 配置化：目录/上限/保留/日志尾均走 `RoomDiagnosticsOptions`，代码默认关闭；无散落魔法路径。
- 客户端写入器零引擎、可注入路径；BattleContext 只保留触发点；后续可加 DevHUD 手动导出、fatal tap、上传通道而不改核心。
- CLI 通过 `ServerTableLoader`/`SimMapDigest`/`RoomReplayRunner` 复用现有单源，不复制表/地图逻辑。

## 安全与性能

- 安全：产物本地 only、无上传；不含密钥/票据；有大小与数量上限；身份不符 fail-fast；R11 内核无 IO/proto/LiteNet；release 不新增调试命令面。
- 性能：录制为纯内存拷贝，命令/步进上限约束内存；文件 IO 只在终态宿主线程；客户端限频在读日志前生效；`HostLog` 环形尾固定 256 行。**未做生产负载基准**，云上首局需观察 tick/Ops。
- 可靠性：写盘失败只计数/告警，不反噬房间收尾；`RoomRunStore` 原子写；客户端写失败只 `Log.Warning`。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 中 | `RoomDiagnosticsService` 的 mismatch 字典按 roomId 永久增长（房间销毁不清理） | `TakeMismatch` 取走即 `Remove`；房间终态落盘后条目消失 | 已修复 |
| R-2 | 中 | 客户端现场包写盘中途失败会留下半成品目录 | `Write` 捕获失败时删除本次新建目录 | 已修复 |
| R-3 | 中 | 客户端限频在组装日志/读取 Player.log 之后才生效，和解风暴时浪费 IO | 新增 `CanAccept`，`ClientDiagnosticsBundle` 在读取前提前返回 | 已修复 |
| R-4 | 低 | 畸形记录 DTO 空负载会 NRE，CLI 归为 IO 退出码 | `ToEvent` 对 Command/Step/TestRules 空负载抛 `InvalidDataException`；CLI 增 `ArgumentException`/`InvalidOperationException` → 3 | 已修复 |
| R-5 | 低 | 若干未使用 using | 清理 `RoomRunEnvelope`/`BattleContext` | 已修复 |
| R-6 | 低 | 非法输入批次只截断标记并记录可拷贝部分 | 生产链由 `InputGate`/协议上限拒绝；在评审与文档中登记为边界，不冒充无损记录 | 已知边界 |
| R-7 | 低 | mismatch 即时 flush 未做 | 终态落盘 + 帧号随记录落盘；即时 flush 需线程封印通道，登记后续 | 已知边界 |
| R-8 | 低 | `RoomServerConfig.diagnostics` 解析无 L1 钉住（云侧配置启用前风险） | 新增配置解析用例：绝对路径/上限/日志尾；3/3 通过 | 已修复 |

无阻塞问题。

## 复测记录

- 修复后定向：`dotnet test ... --filter RoomRunReplay|RoomRunCodec|ClientBundle|RoomDiagnosticsService|ReplayToolCli` → **16/16**。
- 修复后 L1 全量：`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **1801/1801**。
- 修复后 Unity：pipeline refresh + recompile → **0 错**；EditMode `ClientDiagnosticsBundleTests` → **1/1**。
- 修复后 Doc：`python scripts/gate/doc-lint.py` → **0 violation / 0 warning**。
- 交付验证：`workflow.py verify` → **PASS**（L1 38.4s，doc 0.8s）。
- BuildHash：`gen-build-hash.py --check-shared` → **OK**，`459a3145904693b7`（110 文件）——本批未改共享源集。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 工具链 v2 交付（自评审）
- 评审时间: 2026-10-21

评审依据：变更严格落在“服务端记录/重放 + 双端现场包”范围；在线重放复用唯一 `RoomRuntime` 执行链；身份/截断/限频/失败路径均有真实用例或明确边界；发现的中等风险（mismatch 字典泄漏、半成品目录、限频前 IO）已修复并复测。生产负载基准与即时 flush 作为已登记后续，不阻塞本刀。
