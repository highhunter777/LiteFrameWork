# 实施任务清单：大厅进房离房链路样板简化

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 先写完整入场与离房行为失败测试 | - | 完成 | AC2～AC8：每类关键竞态/清理均有可编译可运行的行为用例与明确red或现有已满足green记录；至少晚到取消真实断言red；两窗口与卸载/导航不延期到T4；场景断言清单冻结并对照后续迁移。 | python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600；主代理verify --red确认真实断言失败，保存按用例分类结果。 | dsh session-1ae08d74-641c-4dab-beae-c439e3030248 authored 4 test files; supervisor compiled 0 errors; corrected stimulu… |
| T2 | 集中取票入场与取消所有权 | T1 | 完成 | AC1/AC2/AC3/AC7：Procedure只消费端口；Main→Match排队失效零建连接、DisplayToken脱离和重复占用正确；三入口与GM/账号重入同实例；无程序集新边。生产dsh禁止写测试；T6第一轮映射及入场用例green后才done。全量RoomFlow仍属于T3的预期red逐项记录，不能计通过。 | python scripts/gate/unity-tests.py --mode editor --filter RoomFlowEntry --timeout 600；python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600（记录所有结果，仅T3尚未实现的生命周期预期red可留给T3，其他失败返工）；python scripts/gate/unity-tests.py --mode editor --filter MetaEntryEditModeTests --timeout 600；dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName~AccountReentryTests\|FullyQualifiedName~ProcedureBoundaryTests\|FullyQualifiedName~账号重入_401到回登录页单点接线" --nologo | Real dsh production sessions in evidence reports (A/B/C disjoint source patches), independent T6 test session session-1… |
| T3 | 完成离房清理与房间失败恢复 | T2 | 完成 | AC3～AC7：Match→Battle排队未接管会话由句柄持有，Main覆盖时释放恰一次；卸载/清理完成前阻断重进，登录落点优先。生产dsh禁止写测试；T6第二轮映射完成后，完整RoomFlow（含T1/T2）及验证列所列全部MetaEntry/Core/PageFlow/Net命令必须全部green，任一失败返工不得done，不得skip/取消/过滤空集换绿；逐套留实际输出，T2 known-red清单逐项在T3关闭。测试/实现会话ID、不重叠写集和断言对照留痕。 | python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600；python scripts/gate/unity-tests.py --mode editor --filter MetaEntryEditModeTests --timeout 600；dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName~AccountReentryTests\|FullyQualifiedName~ProcedureBoundaryTests\|FullyQualifiedName~账号重入_401到回登录页单点接线" --nologo；python scripts/gate/unity-tests.py --mode editor --filter PageFlowBattleMenuEditModeTests --timeout 600；dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter "FullyQualifiedName~AccountSessionFactoryTests\|FullyQualifiedName~RoomClientLeaveTests" --nologo | Takeover verified T3 end-to-end: Unity compile clean; RoomFlow 24/24 (including T3 failure tests), MetaEntry 13/13, Pag… |
| T4 | 补齐真实页面操作与独立回归证据 | T3, T6 | 完成 | AC2～AC9：自动化通过，真实页面可操作；未验环境明示不能计通过，服务端/Unity正常启动退出不破坏用户内容。 | unity command run_script --help确认当前签名后执行AgentScripts/LobbyRoomFlow/RunRoomFlowUi.cs；unity command capture_game_view --help确认签名后截图至Artifacts/LobbyRoomFlow；python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600 | Real UI (raycast hit + pointer/keyboard) on local Meta/Room/Mongo chain: login, quick enter, leave, re-enter, create ro… |
| T5 | 设计回写与样板说明 | T4 | 完成 | AC1/AC8：文档与实际实现一致，设计不含施工状态、证据进施工卷；索引/链接/Doc门禁通过，无重复权威。 | python scripts/gate/test.py --suite Doc；核对设计/说明与实际调用链 | Independent docs session (session-6218077b + follow-up 3c4ae638) wrote back design/instructions/progress per verified c… |
| T6 | 独立迁移测试调用与保留全部行为断言 | T1 | 完成 | AC1～AC8：记录测试作者/实现作者不同dsh会话ID与写集；T1场景/断言迁移前后逐条对照，只有调用适配变化；所有必需回归通过；原M3接线守卫由行为或等强装配守卫接替，不删除约束。 | python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600；dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName~AccountReentryTests\|FullyQualifiedName~ProcedureBoundaryTests\|FullyQualifiedName~账号重入_401到回登录页单点接线" --nologo | T6 second round complete after takeover: independent test author session-1ae08d74 preserved all 9 frozen T1 assertions … |

## 任务详情

### T1 · 先写完整入场与离房行为失败测试

- 描述: 独立dsh测试作者先在现有可调用API/阶段上写全部可观察行为断言，使用只转调真实服务/阶段的测试适配器固定行为场景。必须覆盖取消晚到、重复/旧A新B、Main→Match排队失效、JoinAck→Battle排队被Main覆盖、必要卸载未完成阻断再入场/登录优先级。禁止把新API未出现作为延期理由：用当前入口/真实阶段测试相同外部行为，后续只由独立测试作者更换适配器调用，不改场景/断言；确实无法用现有接口观察的用例须主代理说明并补必要最小可编译契约后先跑断言red，不能编译错误或测试替身自测当red。T1测试/助手最多每目录5文件。

- 文件: Assets/Tests/EditMode/Flow/RoomFlow/

### T2 · 集中取票入场与取消所有权

- 描述: 由dsh实现纯票据端口和实质ProcedureFlow，Meta脱离阶段切换；RoomFlowLifetime控制取票/排队/接管；同批迁移生产装配、MetaEntrySession、账号重入和GM建房。测试签名/接线迁移交给独立dsh测试会话，禁止实现作者降低断言。删除旧IAppFlow走Unity资产管线，维护源链接。

- 文件: Assets/LiteGame/Core/Flow/IAppFlow.cs, Assets/LiteGame/Modules/Meta/Abstractions/, Assets/LiteGame/Modules/Meta/Client/MetaEntryService.cs, Assets/LiteGame/Modules/Meta/MetaSegment.cs, Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs, Assets/LiteGame/Modules/Procedure/Flow/RoomFlowLifetime.cs, Assets/LiteGame/Modules/Procedure/Flow/AccountReentry.cs, Assets/LiteGame/Modules/Procedure/ProcedureSegment.cs, Assets/LiteGame/Modules/Procedure/Shared/ProcedureArgs.cs, Assets/LiteGame/Modules/Procedure/SubModules/StagesSubModule/Boot/ProcedureMain.cs, Assets/LiteGame/Modules/Procedure/SubModules/StagesSubModule/Play/ProcedureMatch.cs, Assets/LiteGame/Modules/Procedure/SubModules/StagesSubModule/Play/ProcedureBattle.cs, Assets/LiteGame/App/AppComposition.cs, Assets/LiteGame/App/PageServices/MetaEntrySession.cs, Assets/LiteGame/Editor/Gm/GMTestModePage.cs, Assets/LiteGame/Modules/UI/Pages/Screens/Hub/Page_LobbyLogic.cs, Assets/LiteGame/Modules/Procedure/Flow/RoomFlowOperation.cs, Assets/LiteGame/Modules/Meta/Call/MetaErrorCodes.cs

### T3 · 完成离房清理与房间失败恢复

- 描述: dsh实现Match/Battle一次所有权交接、连接/输入/视图/RoomBridge/Back/遮罩清理，必要场景卸载await；完成前阻断新入场。页面只发离房意图，统一PageFlow在清理完成后回大厅/结算；登录导航代次压过旧回调。房间失败复用FeedbackService retryable:false确认后回大厅。

- 文件: Assets/LiteGame/Modules/Procedure/Flow/RoomFlowLifetime.cs, Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs, Assets/LiteGame/Modules/Procedure/Flow/PageFlow.cs, Assets/LiteGame/Modules/Procedure/SubModules/StagesSubModule/Play/ProcedureMatch.cs, Assets/LiteGame/Modules/Procedure/SubModules/StagesSubModule/Play/ProcedureBattle.cs, Assets/LiteGame/Modules/UI/Pages/Screens/Hub/Page_RoomLogic.cs, Assets/LiteGame/Modules/UI/Pages/Screens/Settings/Page_SettingsLogic.cs, Assets/LiteGame/App/PageServices/RoomBridge.cs

### T4 · 补齐真实页面操作与独立回归证据

- 描述: 仅负责最终真实UI操作与全量回归，不承担首次补写关键行为断言。独立dsh测试/操作会话通过AgentScripts/LobbyRoomFlow/RunRoomFlowUi.cs经unity command run_script/eval驱动实际页面；Raycast命中后pointer/真实键盘输入，不直调入场/离场API。覆盖三入口/取消/等待与对局退出/重进，输出页面/资源副作用及capture_game_view截图到Artifacts/LobbyRoomFlow。主代理核验。

- 文件: Assets/Tests/EditMode/Flow/RoomFlow/, Assets/Tests/UI/PlayMode/Flow/RoomFlow/, AgentScripts/LobbyRoomFlow/

### T5 · 设计回写与样板说明

- 描述: dsh文档会话根据实际已验证源码回写现有设计/说明与客户端重构线，全文搜索IAppFlow/旧进房副作用引用同步；架构图只经现有生成器从图源重生成，不手改生成HTML。设计不写施工状态，证据追加既有卷索引，Docs门禁通过；主代理写.workflow并评审。

- 文件: Docs/设计文档/架构/商业级通用客户端框架总设计.md, Docs/设计文档/架构/客户端依赖注入专项设计.md, Docs/说明/02-架构总览与程序集分层.md, Docs/说明/03-客户端框架与运行时.md, Docs/说明/04-服务端与房间内核.md, Docs/说明/08-UI与页面逻辑层.md, Docs/说明/14-代码地图与根目录.md, Docs/说明/17-全项目数据流.md, Docs/说明/架构图/图源-应用壳.json, Docs/说明/架构图/Html/应用壳.html, Docs/施工进度/客户端重构线.md

### T6 · 独立迁移测试调用与保留全部行为断言

- 描述: 仅测试dsh会话处理T1测试适配器与旧MetaEntry/M3/PageFlow测试签名接线迁移，禁止生产作者改测试。T2写完API后先做第一轮调用映射并验证T1入场断言；T3写完后再做生命周期映射及全量回归。该任务可与T2/T3状态同时doing但文件写入严格串行、Unity操作单占，T2/T3各自只有本轮测试映射与相应验证通过才done；T6最终done在T3映射完成且T1全部断言对照确认后。既有参数/错误/精确流程与账号重入同实例约束逐条保留，不删用例、不降强度、不skip。

- 文件: Assets/Tests/EditMode/Flow/RoomFlow/, Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs, Assets/Tests/EditMode/UI/Page/PageFlowBattleMenuEditModeTests.cs, Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs, Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj

