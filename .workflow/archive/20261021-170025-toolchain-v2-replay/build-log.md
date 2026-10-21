# 施工记录：工具链 v2：回放落盘与现场包

> 运行 toolchain-v2-replay · 创建于 2026-10-21T16:33:05+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T16:34:16+08:00 · 新增任务 T1「内核录制器与权威捕获点」
- 2026-10-21T16:34:17+08:00 · 新增任务 T2「房间重放器核心」
- 2026-10-21T16:34:17+08:00 · 新增任务 T3「记录封装、JSON+GZip 编解码与原子存储」
- 2026-10-21T16:34:17+08:00 · 新增任务 T4「服务端终态现场包接线」
- 2026-10-21T16:34:17+08:00 · 新增任务 T5「客户端 mismatch 现场包」
- 2026-10-21T16:34:17+08:00 · 新增任务 T6「ReplayTool CLI 与退出码」
- 2026-10-21T16:34:17+08:00 · 新增任务 T7「测试补齐与全量门禁」
- 2026-10-21T16:34:18+08:00 · 新增任务 T8「文档回写与工具用法」
- 2026-10-21T16:34:53+08:00 · 新增任务 T9「先写红测：回放/编码/客户端 bundle」
- 2026-10-21T16:34:53+08:00 · 更新任务 T1（acceptance, deps, verify）
- 2026-10-21T16:34:53+08:00 · 更新任务 T2（acceptance, deps, verify）
- 2026-10-21T16:34:54+08:00 · 更新任务 T3（deps）
- 2026-10-21T16:34:54+08:00 · 更新任务 T4（acceptance, deps, verify）
- 2026-10-21T16:34:54+08:00 · 更新任务 T5（acceptance, deps, verify）
- 2026-10-21T16:34:54+08:00 · 更新任务 T6（deps）
- 2026-10-21T16:34:54+08:00 · 更新任务 T8（acceptance, files）
- 2026-10-21T16:34:54+08:00 · 更新任务 T7（deps）
- 2026-10-21T16:54:55+08:00 · 开始任务 T9「先写红测：回放/编码/客户端 bundle」
- 2026-10-21T16:54:56+08:00 · 完成任务 T9 · 证据: evidence/red.json：exit=1，11.2s；缺 RoomServer.Diagnostics / RoomServer.Runtime.Diagnostics / LiteFramework.Diagnostics 类型（预期红，非环境错误）
- 2026-10-21T16:55:03+08:00 · 开始任务 T1「内核录制器与权威捕获点」
- 2026-10-21T16:55:03+08:00 · 完成任务 T1 · 证据: RoomRunRecord/RoomRunRecorder + RoomRuntime.Execute/SetTestRules/StartMatch/StepFrame/TakeRunRecord 接线；深拷贝输入/上限/截断；RoomRunReplayTests 5/5；Unity recompile 0 错；L…
- 2026-10-21T16:55:03+08:00 · 开始任务 T2「房间重放器核心」
- 2026-10-21T16:55:03+08:00 · 完成任务 T2 · 证据: RoomReplayRunner 重建同配置 RoomRuntime 按序喂命令；首分歧帧/命令序号/截断/身份 fail-fast；篡改 42 帧定向通过；定向 15/15
- 2026-10-21T16:55:03+08:00 · 开始任务 T3「记录封装、JSON+GZip 编解码与原子存储」
- 2026-10-21T16:55:04+08:00 · 完成任务 T3 · 证据: RoomRunEnvelope JSON+GZip + RoomRunStore 原子写/保留；codec/store 2/2；文件可解码、无 .tmp
- 2026-10-21T16:55:04+08:00 · 开始任务 T5「客户端 mismatch 现场包」
- 2026-10-21T16:55:04+08:00 · 完成任务 T5 · 证据: DiagnosticsBundleWriter + ClientDiagnosticsBundle + BattleContext mismatch/ConfigHash 触发；L1 ClientBundle 2/2；Unity 编译 0 错；EditMode 1/1
- 2026-10-21T16:55:04+08:00 · 开始任务 T4「服务端终态现场包接线」
- 2026-10-21T16:55:04+08:00 · 完成任务 T4 · 证据: RoomDiagnosticsService + RoomServerConfig diagnostics + HostLog 256 行尾；现场包 2/2（临时目录产物/保留）；L1 1800/1800
- 2026-10-21T16:55:04+08:00 · 开始任务 T6「ReplayTool CLI 与退出码」
- 2026-10-21T16:55:04+08:00 · 完成任务 T6 · 证据: Server/ReplayTool 构建 0 警告/0 错误；CLI 4/4（默认表 0 / 真实仓库表 0 / 篡改 2 / 身份 3）
- 2026-10-21T16:55:04+08:00 · 开始任务 T7「测试补齐与全量门禁」
- 2026-10-21T16:55:05+08:00 · 完成任务 T7 · 证据: L1 全量 1800/1800；doc-lint 0/0；gen-build-hash --check-shared OK（459a3145904693b7，110 文件）
- 2026-10-21T16:55:05+08:00 · 开始任务 T8「文档回写与工具用法」
- 2026-10-21T16:55:05+08:00 · 完成任务 T8 · 证据: 路线图 T0 修正；13-调试手册 §8.5；GM与调试线 ch4；工具链清单/toolchain.json/代码地图；doc-lint 0/0

## 关键决定

- **在线回放改用房间命令日志**：执行到 `RoomRuntime.StepFrame` 时确认权威链在 `SimStep` 后还有延迟补偿与 `MatchSystem.Tick`，且 `viewFrame/ackSnapshot` 不在 `SimInputFrame` 内；纯输入回放会假分歧。改为录 `RoomCommand` + 逐步 checksum + 规则事件，重放走真实 `RoomRuntime`（单一执行链）。`SimRunRecord/SimReplayRunner` 保留给离线纯 Sim/批跑。
- **内核纯内存、宿主才 IO**：录制器住 `Assets/RoomServer/Runtime/Diagnostics`，无 IO/proto/LiteNet；只在房间终态静默后由 `ServerHost` 取走并落盘。
- **身份三闸**：`buildHash`、`fixedConfigDigest`、`mapDigest` 任一不符拒绝重放；CLI 默认加载仓库真实表复算固定摘要，`--defaults` 仅服务默认表/离线测试记录。
- **记录有界**：命令/步进/规则事件各自封顶，超限置 `Truncated` 并停止追加；绝不抛进权威循环。
- **服务端诊断默认关闭**：`RoomServerConfig.diagnostics` 缺省 = 不写盘、行为零变化；`deploy/roomserver.windows.json` 仅作为配置源启用，未执行部署。
- **客户端现场包本地 only**：mismatch/配置身份不符触发；同局最多 3 份、默认 30s 间隔、单份限大小；不上传、不含密钥。
- **客户端请求组装外提**：`ClientDiagnosticsBundle` 只负责 Unity/Log 取值，`DiagnosticsBundleWriter` 零引擎、可 L1 测；BattleContext 只保留触发点。
- **服务端日志尾**：`HostLog` 增 256 行私有环形尾（`RecentTail`），不新增日志框架，也不在房间线程读文件。

## 偏差与处理

- **方案初稿的 SimRunRecord 在线回放假设被推翻**：交付前发现延迟补偿/`viewFrame` 上下文缺失；改为房间命令日志 + 真实 `RoomRuntime` 重放，并回写设计路线图 T0。这是本次最大设计修正，已在方案/路线图中记录。
- **服务端 mismatch 即时落盘未做**：原计划“mismatch 现场包”，实际房间仍在运行、录制器仍被房间线程写；为避免并发读半截记录，改为终态统一落盘 + mismatch 帧号随记录落盘。即时 flush 列为后续。
- **HostLog 尾用有界环而非日志文件尾**：宿主当前无文件 sink；环形尾更可靠且不依赖部署重定向。
- **CLI 增加真实表装载**：原方案仅默认表；为避免“只能重放测试记录”，CLI 默认走 `ServerTableLoader.LoadFromRepo()` 并复算身份，新增 `--defaults` 逃生口给 L1 记录。
- **RoomInstance 构造链扩展**：为把诊断选项传给 `RoomRuntime`，六个构造重载都补了可选参数；`IInboundDiagnostics` 补 `NoteMismatch` 并由适配器转发。
- **Unity 资产流程**：新增 Assets 下 `.cs` 统一经 pipeline `AssetDatabase.Refresh` 生成 `.meta`，未手写 `.meta`；Unity 编译 0 错。

## 问题与修复

- 红测阶段：测试项目缺 `RoomServer.Diagnostics`/`RoomServer.Runtime.Diagnostics`/`LiteFramework.Diagnostics` 类型，编译失败——预期红，记录在 `evidence/red.json`。
- `RoomRunRecorder` 缺 `using LiteSim;`（`MatchTestRules` 找不到）——补 using。
- `RoomRunEventDto.From`/`RecordDto.From` 把属性传给 `in` 参数，CS8156——改为先取局部变量。
- `ReplayTool` 的 `BuildHash` 在 `LiteNet` 命名空间而非 `LiteNet.Protocol`——补 `using LiteNet;`；重复 using 一并清理。
- CLI 测试引用 Exe 后 `Program.Main` 不可见/解析歧义——`Program`/`Main` 改 public，测试用 `ReplayTool.Program.Main` 完全限定。
- 篡改用例最初把第 42 帧 `MoveX` 改成 1f，而原值本来就是 1f（偶数帧）——改为 -1f 后准确定位到第 42 帧。
- `IInboundDiagnostics` 没有 `NoteMismatch`——补接口方法并在 `ServerHost.InboundHostAdapter` 转发到 `_diagnostics`。
- Unity 测试首次 `run_tests` 连接被重置——按 pipeline 约定开 `set_autotick` + `--async_tests true` 轮询 `test_status`，1/1 通过。
- 代码评审修复（R-1～R-5/R-8）：mismatch 字典取走即删、半成品目录清理、`CanAccept` 提前限频、畸形 DTO 显式 `InvalidDataException`、未使用 using、diagnostics 配置解析 L1 钉住；复测 16/16、L1 1801/1801、EditMode 1/1、doc 0/0。

## 验证结果

- 定向（回放/编码/存储/服务端/客户端/CLI）：**16/16 通过**（`dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter ...`）。
- L1 全量：**1801/1801 通过**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`，含纪律扫描/表守卫）。
- Unity：pipeline refresh + recompile → `compilationFailed=false`，**0 错**；EditMode `ClientDiagnosticsBundleTests` **1/1**。
- CLI：`dotnet build Server/ReplayTool/ReplayTool.csproj` **0 警告/0 错误**；退出码 0（默认表/真实仓库表）、2（篡改）、3（身份不符）均有 L1 用例。
- BuildHash：`gen-build-hash.py --check-shared` → **OK**，`459a3145904693b7`（110 文件）；本批未改共享源集。
- Doc：`python scripts/gate/doc-lint.py` → **0 violation / 0 warning**。
- 2026-10-21T16:37:15+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T16:56:12+08:00 · 验证 PASS · 2/2 · 证据 evidence/verify.json
- 2026-10-21T16:58:10+08:00 · 验证 PASS · 2/2 · 证据 evidence/verify.json
- 2026-10-21T17:00:09+08:00 · 验证 PASS · 2/2 · 证据 evidence/verify.json

## 交付摘要

工具链 v2 第一刀落地：权威房间按序记录命令/逐帧 checksum/测试规则事件，房间终态自动落盘带构建/配置/地图身份的 `.json.gz`；`Server/ReplayTool` 用真实 `RoomRuntime` 重放并定位首分歧帧，默认加载仓库表复算固定摘要；客户端在 checksum/配置身份不符时写本地现场包。服务端诊断默认关闭，`deploy/roomserver.windows.json` 已配置启用（未部署）。所有新增/改动均有 L1 或 EditMode 证据，未改共享源集 BuildHash；代码评审修复项已复测。

## 未决事项

- 服务端 mismatch 即时 flush：需“房间线程封印记录 → 宿主异步写”的通道；当前只在终态落盘。
- 字段级 world-ring diff 未落盘；当前只给首分歧帧 + checksum + 命令序号。
- 崩溃捕获/minidump、IL2CPP 符号归档、产物上传/GM 下载端点未做（T0 后续）。
- CLI 非仓库部署位置暂无 `--tables <dir>`；当前以 `fixedConfigDigest` fail-fast，不会静默跑偏差。
- 云侧 `diagnostics` 配置源已启用但未发布/重启，尚未在云上产生首份真实记录。

## 文件清单

- 内核：`Assets/RoomServer/Runtime/Diagnostics/RoomRunRecord.cs`、`RoomRunRecorder.cs`；`Assets/RoomServer/Runtime/RoomRuntime.cs`。
- 宿主：`Server/RoomServer/Diagnostics/RoomRunEnvelope.cs`、`RoomRunStore.cs`、`RoomReplayRunner.cs`、`RoomDiagnosticsOptions.cs`、`RoomDiagnosticsService.cs`；`Server/RoomServer/RoomInstance.cs`、`ServerHost.cs`、`RoomServerConfig.cs`、`InboundHandlers.cs`、`InboundPorts.cs`、`HostLog.cs`。
- CLI：`Server/ReplayTool/ReplayTool.csproj`、`Program.cs`。
- 客户端：`Assets/LiteFramework/Scripts/Core/Diagnostics/DiagnosticsBundleWriter.cs`；`Assets/LiteClient/Runtime/Diagnostics/ClientDiagnosticsBundle.cs`；`Assets/LiteClient/Runtime/Battle/BattleContext.cs`。
- 测试：`Tests/LiteNet.Tests/RoomRunReplayTests.cs`、`RoomRunCodecStoreTests.cs`、`RoomDiagnosticsServiceTests.cs`、`ClientBundleTests.cs`、`ReplayToolCliTests.cs`、`LiteNet.Tests.csproj`；`Assets/Tests/EditMode/ClientDiagnosticsBundleTests.cs`。
- 文档/配置：`Docs/设计文档/测试与性能/调试与开发工具链演进路线图.md`、`Docs/设计文档/README.md`、`Docs/项目说明/13-调试手册.md`、`Docs/施工进度/GM与调试线.md`、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`scripts/gate/toolchain.json`、`deploy/roomserver.windows.json`。
- Unity 导入生成的 `.meta`：上述新增 Assets 目录/文件对应 `.meta`。

## 后续建议

- 先在云侧测试服启用诊断并打一局，验证真实录制的体积/开销/保留策略；据此定默认上限。
- 下一刀做“房间线程封印 → 宿主异步写”的 mismatch 即时 flush，再补 world-ring 字段级 diff。
- CLI 增加 `--tables <dir>` 与 `--dump-world`，让非仓库部署也能重放/导出首分歧世界。
- 将 `ClientDiagnosticsBundle` 的触发扩展到 DevHUD 手动导出与 fatal 错误 tap。
- T0 剩余项按路线图：崩溃捕获/符号归档、产物上传/GM 下载；T1 再做开发 Player/弱网/中环。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T16:33:05+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T16:34:04+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T16:36:04+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T16:36:07+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T16:37:28+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T16:55:33+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T16:58:19+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T16:58:39+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T17:00:19+08:00 · 完成「回写与收尾」→ 完成
