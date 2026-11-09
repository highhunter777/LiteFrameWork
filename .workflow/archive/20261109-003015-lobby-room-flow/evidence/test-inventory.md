# 只读测试调查摘要

调查子代理：Lorentz（01a1bafa-f2e7-7a62-82e9-835819dbb498），2026-11-08；此调查不代表测试通过。

- dotnet项目：Tests/LiteFramework.Core.Tests（xUnit/net8），Tests/LiteNet.Tests（xUnit/net8）。
- 现有 AccountReentryTests、ProcedureBoundaryTests、M3WiringGuardTests、AccountSessionFactoryTests、RoomClientLeaveTests 是本批回归入口。
- MetaEntryEditModeTests含 FakeAppFlow/BindFlow 与取票后请求Match断言，需随职责迁移保留行为约束。
- M3WiringGuardTests的账号重入断言按源码字符串要求Meta实现IAccountSessionControl，需替换为新控制实现/同实例接线证据。
- MetaEntrySession、ProcedureMatch、ProcedureBattle、RoomBridge缺少直接链路行为覆盖；工厂/运行端口用例不能冒充页面全链。
- GMTestModePage.CreateTestRoomAsync直接调用Meta.CreateRoomAsync并依赖“已创建并进入”副作用，须迁移并验证模板/地图/密钥/房号/失败busy复位。
- 现有 PageFlowBattleMenuEditModeTests、FeedbackServiceEditModeTests、BattleRunPortEditModeTests、BattleSessionEditModeTests、MatchLoopLeakEditModeTests提供相邻回归。
- Editor端口7801可达。可用eval/run_script/capture_game_view；simulate_pointer是RuntimeOnly，当前Editor无此命令。真实点击参考Assets/Tests/UI/PlayMode/Presentation/FeedbackSurfacePlayModeTests.cs的ClickReachable：先EventSystem.RaycastAll命中，再派发pointer down/up/click。
- 完整大厅进房离房点击脚本尚无现成实现；本批必须补实际页面路径验证。
