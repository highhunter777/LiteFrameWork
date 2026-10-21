# 实施任务清单：工具链 v2：回放落盘与现场包

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 9/9（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 内核录制器与权威捕获点 | T9 | 完成 | RoomRunRecord/Recorder 在 RoomRuntime.Execute/SetTestRules/StepFrame 捕获命令、步进 checksum、规则事件；输入深拷贝；容量上限与截断；checksum 在 StepFrame 末尾稳定（排除瞬态缓冲，以 SimChecksum 定义为准）；R11 无 IO/proto/LiteNet；新 Assets 文件经 unity-pipeline 导入并生成 .meta | 定向 L1 RoomRunReplay 录制/截断用例；R11 纪律扫描；unity-pipeline 导入回读/ L2 meta 检查；L1 全量 | RoomRunRecord/RoomRunRecorder + RoomRuntime.Execute/SetTestRules/StartMatch/StepFrame/TakeRunRecord 接线；深拷贝输入/上限/截断；Room… |
| T2 | 房间重放器核心 | T1, T9 | 完成 | 从记录重建 RoomConfig/RoomRuntime（MatchId/Seed/gameplayDigestBase 与记录一致），按序喂命令并逐步比对 checksum；输出首分歧帧/命令序号；截断报告；buildHash/config/map 身份不符 fail-fast | 定向 L1 RoomRunReplay 重放/篡改/身份用例 | RoomReplayRunner 重建同配置 RoomRuntime 按序喂命令；首分歧帧/命令序号/截断/身份 fail-fast；篡改 42 帧定向通过；定向 15/15 |
| T3 | 记录封装、JSON+GZip 编解码与原子存储 | T1, T9 | 完成 | Envelope 带 buildHash/config/map 身份与日志尾；版本化 JSON+GZip；原子写盘/保留上限/失败计数；round-trip 保真 | 定向 L1 codec/store 用例；临时目录原子写与保留断言 | RoomRunEnvelope JSON+GZip + RoomRunStore 原子写/保留；codec/store 2/2；文件可解码、无 .tmp |
| T4 | 服务端终态现场包接线 | T1, T2, T3, T9 | 完成 | 房间 Closed/Aborted 且静默后取记录并落盘 envelope+日志尾；mismatch 摘要进 envelope；HostLog 尾经有界环或配置日志文件尾（单源二选一）；写失败不影响收尾；配置目录可关闭 | L1 房间终态用例 + 临时目录产物断言；HostLog/计数检查 | RoomDiagnosticsService + RoomServerConfig diagnostics + HostLog 256 行尾；现场包 2/2（临时目录产物/保留）；L1 1800/1800 |
| T5 | 客户端 mismatch 现场包 | T9 | 完成 | BattleContext checksum mismatch/ConfigHash 不符触发；manifest+Log.Recent+Player.log 尾；限频/大小上限/无密钥；失败只告警；新 Assets 文件经 unity-pipeline 导入并生成 .meta | 纯逻辑/EditMode 定向用例（写出/限频/上限）；unity-pipeline 导入回读/L2 meta 检查；L1 或 L2 定向 | DiagnosticsBundleWriter + ClientDiagnosticsBundle + BattleContext mismatch/ConfigHash 触发；L1 ClientBundle 2/2；Unity 编译 0… |
| T6 | ReplayTool CLI 与退出码 | T2, T3, T9 | 完成 | dotnet run --project Server/ReplayTool -- replay <file>；0/2/3/4 退出码；--json；对 L1 生成的记录 smoke 通过 | CLI smoke（生成记录→exit 0；篡改→exit 2；坏身份→exit 3） | Server/ReplayTool 构建 0 警告/0 错误；CLI 4/4（默认表 0 / 真实仓库表 0 / 篡改 2 / 身份 3） |
| T7 | 测试补齐与全量门禁 | T1, T2, T3, T4, T5, T6, T9 | 完成 | RoomRunReplay 覆盖 AC1-AC4/AC6；客户端 bundle 覆盖 AC5；L1 全绿；R11 扫描；doc-lint 0；buildHash --check-shared | pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest；python scripts/gate/doc-lint.py；python scripts/codegen/gen-build-hash.py --check-shared | L1 全量 1800/1800；doc-lint 0/0；gen-build-hash --check-shared OK（459a3145904693b7，110 文件） |
| T8 | 文档回写与工具用法 | T7 | 完成 | 路线图补 v2 第一刀边界/后续项；调试手册补 replay/bundle 用法；施工进度卷追加证据；工具链清单/代码地图同步；索引/链接通过 doc-lint | python scripts/gate/doc-lint.py；人工核对命令/路径 | 路线图 T0 修正；13-调试手册 §8.5；GM与调试线 ch4；工具链清单/toolchain.json/代码地图；doc-lint 0/0 |
| T9 | 先写红测：回放/编码/客户端 bundle | - | 完成 | 实现前写 RoomRunReplay/ClientBundle 用例并运行得到预期失败（缺类型/断言失败，不是环境错误）；覆盖 AC1-AC6 的失败路径 | dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter FullyQualifiedName~RoomRunReplay；record--red | evidence/red.json：exit=1，11.2s；缺 RoomServer.Diagnostics / RoomServer.Runtime.Diagnostics / LiteFramework.Diagnostics 类型… |

## 任务详情

### T1 · 内核录制器与权威捕获点

- 文件: Assets/RoomServer/Runtime/Diagnostics/RoomRunRecord.cs, Assets/RoomServer/Runtime/Diagnostics/RoomRunRecorder.cs, Assets/RoomServer/Runtime/RoomRuntime.cs

### T2 · 房间重放器核心

- 文件: Server/RoomServer/Diagnostics/RoomReplayRunner.cs

### T3 · 记录封装、JSON+GZip 编解码与原子存储

- 文件: Server/RoomServer/Diagnostics/RoomRunEnvelope.cs, Server/RoomServer/Diagnostics/RoomRunStore.cs, Server/RoomServer/RoomServerConfig.cs

### T4 · 服务端终态现场包接线

- 文件: Server/RoomServer/Diagnostics/RoomDiagnosticsService.cs, Server/RoomServer/RoomInstance.cs, Server/RoomServer/ServerHost.cs

### T5 · 客户端 mismatch 现场包

- 文件: Assets/LiteClient/Runtime/Diagnostics/ClientBundleWriter.cs, Assets/LiteClient/Runtime/Battle/BattleContext.cs

### T6 · ReplayTool CLI 与退出码

- 文件: Server/ReplayTool/ReplayTool.csproj, Server/ReplayTool/Program.cs

### T7 · 测试补齐与全量门禁

- 文件: Tests/LiteNet.Tests/RoomRunReplayTests.cs, Tests/LiteNet.Tests/ClientBundleTests.cs

### T8 · 文档回写与工具用法

- 文件: Docs/设计文档/测试与性能/调试与开发工具链演进路线图.md, Docs/项目说明/13-调试手册.md, Docs/施工进度/GM与调试线.md, Docs/工具链/技术支持.md, Docs/开发导航/代码地图.md, scripts/gate/toolchain.json

### T9 · 先写红测：回放/编码/客户端 bundle

- 文件: Tests/LiteNet.Tests/RoomRunReplayTests.cs, Tests/LiteNet.Tests/ClientBundleTests.cs

