# 施工记录：大厅进房离房链路样板

## 施工记录

2026-11-08，用户授权主代理分配任务和验收，具体实施交给dsh。验收委托已记录，流程经CLI进入任务评审；T1～T5已生成，未开始生产施工。真实dsh只读任务评审会话启动，但因MISSING_CREDENTIAL在执行前失败。
- 2026-11-08T20:33:17+08:00 · 更新任务 T1（acceptance, description, title, verify）
- 2026-11-08T20:33:18+08:00 · 新增任务 T6「独立迁移测试调用与保留全部行为断言」
- 2026-11-08T20:33:18+08:00 · 更新任务 T2（files, verify）
- 2026-11-08T20:33:18+08:00 · 更新任务 T3（files, verify）
- 2026-11-08T20:33:18+08:00 · 更新任务 T4（description, verify）
- 2026-11-08T20:33:18+08:00 · 更新任务 T4（deps）
- 2026-11-08T20:34:07+08:00 · 更新任务 T2（acceptance, verify）
- 2026-11-08T20:34:07+08:00 · 更新任务 T3（acceptance）
- 2026-11-08T20:34:07+08:00 · 更新任务 T5（description, files）
- 2026-11-08T20:39:05+08:00 · 更新任务 T3（acceptance）
- 2026-11-08T20:41:00+08:00 · 开始任务 T1「先写完整入场与离房行为失败测试」
- 2026-11-08T20:46:17+08:00 · 更新任务 T2（files）
- 2026-11-08T21:00:59+08:00 · 完成任务 T1 · 证据: dsh session-1ae08d74-641c-4dab-beae-c439e3030248 authored 4 test files; supervisor compiled 0 errors; corrected stimulus then workflow verify --red PASS (9/9 N…
- 2026-11-08T21:00:59+08:00 · 开始任务 T2「集中取票入场与取消所有权」
- 2026-11-08T21:19:26+08:00 · 更新任务 T2（files）
- 2026-11-08T21:27:29+08:00 · 开始任务 T6「独立迁移测试调用与保留全部行为断言」
- 2026-11-08T21:54:43+08:00 · 完成任务 T2 · 证据: Real dsh production sessions in evidence reports (A/B/C disjoint source patches), independent T6 test session session-1ae08d74-641c-4dab-beae-c439e3030248 pres…
- 2026-11-08T21:54:43+08:00 · 开始任务 T3「完成离房清理与房间失败恢复」
- 2026-11-08T21:59:34+08:00 · 完成任务 T6 · 证据: T6 first migration complete: independent dsh session-1ae08d74-641c-4dab-beae-c439e3030248 preserved all 9 frozen assertions, migrated Meta pure-ticket tests an…
- 2026-11-08T22:14:00+08:00 · 重新打开任务 T6：First migration round verified, but the defined task also owns T3 second-round recovery/API test migration; keep open until both rounds and final regression are complete.
- 2026-11-08T23:46:08+08:00 · 完成任务 T3 · 证据: Takeover verified T3 end-to-end: Unity compile clean; RoomFlow 24/24 (including T3 failure tests), MetaEntry 13/13, PageFlowBattleMenu 6/6, Core reentry/bounda…
- 2026-11-09T00:19:06+08:00 · 开始任务 T6「独立迁移测试调用与保留全部行为断言」
- 2026-11-09T00:19:06+08:00 · 完成任务 T6 · 证据: T6 second round complete after takeover: independent test author session-1ae08d74 preserved all 9 frozen T1 assertions (same names/scenarios; Lifecycle hash id…
- 2026-11-09T00:19:28+08:00 · 开始任务 T4「补齐真实页面操作与独立回归证据」
- 2026-11-09T00:19:28+08:00 · 完成任务 T4 · 证据: Real UI (raycast hit + pointer/keyboard) on local Meta/Room/Mongo chain: login, quick enter, leave, re-enter, create room, leave created, select room from real…
- 2026-11-09T00:24:31+08:00 · 开始任务 T5「设计回写与样板说明」
- 2026-11-09T00:24:31+08:00 · 完成任务 T5 · 证据: Independent docs session (session-6218077b + follow-up session-3c4ae638) wrote back design/instructions/progress per verified code, then amended after takeover…
- 2026-11-09T00:26:41+08:00 · 开始任务 T5「设计回写与样板说明」
- 2026-11-09T00:26:41+08:00 · 完成任务 T5 · 证据: Independent docs session (session-6218077b + follow-up 3c4ae638) wrote back design/instructions/progress per verified code, then amended after takeover facts. …

## 关键决定

测试与实现分离dsh会话；主代理负责状态、命令证据、最终代码评审。采用独立root配置及同一运行目录junction，保持原项目共享工作流配置和另一活动运行不变。

## 偏差与处理

当前缺少dsh headless路由opencode-go认证配置，无法取得独立任务评审。没有用本机子代理替代dsh施工，也没有把启动错误计为测试失败证据。

## 问题与修复

需通过dsh Models/credentials配置当前路由的OPENCODE_GO_API_KEY；不通过对话索取密钥、不读取.credentials.yaml、不更改模型。配置就绪后继续任务评审，再按T1先测试/T2-T3实现/T4真实操作/T5文档推进。


### dsh认证修复与独立任务评审

用户说明桌面opencode-go可用。核对安装包后确认headless bundle默认disabled credentials，因此未读取桌面管理凭据。主代理在本次CLI启动使用临时patch启用credentials服务，未读取/拷贝密钥、未修改用户profile/模型。真实模型请求恢复，dsh会话session-128282f0-318f-41e2-8ea3-121f44c7026b已完成任务评审（exit 0/final completed），提出B1～B3三项major。

主代理据此将T1扩为完整既有API/阶段行为失败测试，新增T6独立测试迁移任务，移除生产作者测试写集，并具体化全量RoomFlow/Core/Meta/PageFlow/Net回归。测试和实现交接串行、不同dsh会话；T4只负责最终真实UI。第二轮独立dsh任务复评正在执行。当前Unity ready/play stopped，生产源码仍未改。

## 阶段流转
- 2026-11-08T20:41:00+08:00 · 完成「子代理评审任务」→ 「子代理先写测试」
- 2026-11-08T21:00:59+08:00 · 完成「子代理先写测试」→ 「子代理正式施工」
- 2026-11-09T00:29:09+08:00 · 完成「子代理正式施工」→ 「测试代码」
- 2026-11-09T00:29:16+08:00 · 完成「测试代码」→ 「主代理评审代码」
- 2026-11-09T00:29:34+08:00 · 完成「主代理评审代码」→ 「回写与收尾」
- 2026-11-09T00:30:07+08:00 · 完成「回写与收尾」→ 完成

## 验证结果
- 2026-11-08T20:59:25+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json


### 测试先行及T2生产片交付

独立dsh测试作者session-1ae08d74-641c-4dab-beae-c439e3030248交付RoomFlow四个测试文件。主代理实际编译0错误、运行9用例9断言red，审核后要求先取消A再启动B及真实加载场景的刺激修正，重新verify --red仍9/9断言red。T1源码hash/断言清单与完整test_status已冻结，T1经CLI完成，进入implement。

施工前L1主代理实跑1961通过/0失败，证据pre-production-l1.json。dsh工具环境不可见宿主Editor，但主代理能连7801，因此所有Unity编译/验收由主代理执行，源码仍仅由dsh撰写。

T2生产会话session-72bdafc0-fd6b-4dfe-ae8a-76c1cacc5d99在只读调查期间遇到另批UI的OnButton→OnButtonClick并发变更，主代理保存独立基线并要求保留。T2拆为小片：A纯Meta（4文件已落并实读检查）、B操作寿命/实质流程（施工中）、C阶段与装配接线（后续）。原测试源码hash仍未被生产作者修改；T2未完成，当前旧调用方编译需待C/T6迁移。


### T2验收与T3失败基线

T2合并后宿主Unity编译0错；RoomFlow15项13green/2项T3已知red；MetaEntry13/13、Core账号重入/边界/同实例守卫8/8。旧IAppFlow通过dsh撰写的AssetDatabase一次性脚本删除，文件与meta磁盘不存在；同回调GUID缓存一度仍显示存在，随后域重载编译0错。T2已由CLI done。

独立测试作者补T3故障与RoomBridge：完整RoomFlow24项21green/3真实red（建连失败进Error、卸载未完放行、旧回调覆盖登录）。实际输出留t3-red-before-implementation.json。T3已start，dsh仅改生产。T6第一轮已验证，主代理曾错误done后立即reopen：该任务仍须承担T3第二轮，不能只凭第一轮结果宣告完成。

真实UI验收准备使用独立本地Meta/Room/Mongo链，不操作云服务或用户已有Mongo容器。Docker可用且mongo:8.0镜像已在本机；dsh撰写一次性本地启动/恢复脚本和射线点击脚本，主代理先审核再宿主执行。现有deploy.args须字节备份恢复，用户密钥不回显。

### 接管后：T3 收口与 T4 真实 UI 补跑（2026-11-08 夜）

原会话在 T3 生产末轮（20:23–20:26）后中断，状态未回填。接管后先复跑现状：Unity 编译 0 错；RoomFlow 24/24（含 T3 失败用例）、MetaEntry 13/13、PageFlowBattleMenu 6/6、Core 账号重入/边界 8/8、Net 会话/离房 8/8，证据 `evidence/t3-takeover-*.txt`。复核清理/恢复路径时发现并修复一处终态自旋：ProcedureFlow 在阶段机落 ProcedureId.Error 时观察者无限 Yield（每帧留 Forever 任务）；改为 Error 即退出观察、保持操作占用（符合“致命停机不放行”的设计），单行修改，交由独立生产子代理执行、宿主复核。

T4 真实 UI 在本机独立 Meta/Room/Mongo 链上补跑并留痕：登录、快速进入、离房、再次进入、普通建房、建房后离房、选房进入（真实列表行 R-CJK2A6，seats=2/expected=4）、房主开局（Boot→Arena01）、对局中 Esc→设置→退出确认→大厅、JoinAck 失败→错误确认→大厅。截图落 `Artifacts/LobbyRoomFlow`。未验证：等待 JoinAck 时加载面 Back 取消（pipeline run_script 串行，无法并发注入真实 Esc，改由自动化用例覆盖）、GM sandbox 建房真实页面、账号重入真实页面落点。详见 `evidence/t4-ui-acceptance-2026-11-08.md`。

环境已清理：`local_stack.py stop` 停止任务服务/容器并逐字节恢复 `Assets/StreamingAssets/deploy.args`；Unity 退出 Play；截图命令默认落点 `Assets/Artifacts` 已用 pipeline 删除；`DevStartupConfig.DirectToTraining` 仅编辑期内存改动，场景文件无差异。

### 接管收尾：T3/T6/T4/T5 完成与门禁转绿

- T3 收口：`ProcedureFlow` 致命终态自旋修复（单行，独立生产子代理）；RoomFlow 24/24、MetaEntry 13/13、PageFlow 6/6、Core 8/8、Net 8/8 复跑全绿后经 CLI 完成。
- T6 第二轮：两件定向用例 `Cleanup/RoomFlowCleanupFailureTests`、`Cleanup/RoomFlowLoadingCancelTests` + 辅助件 `RoomFlowCleanupDoubles.cs`，Rig 仅加可选注入；RoomFlow 26/26 全绿；9 条冻结断言同名保留、Lifecycle 哈希与 T1 清单一致；映射见 `evidence/t6-round-2-mapping.md`。
- T4：真实 UI 十步（登录/快进/离房/重进/建房/建房后离房/选房行 R-CJK2A6/房主开局/对局 Esc 退场/JoinAck 失败确认）留痕；截图 `Artifacts/LobbyRoomFlow/*.png`；未验证项如实披露（`evidence/t4-ui-acceptance-2026-11-08.md`）。环境已恢复（deploy.args 字节还原、Assets/Artifacts 删除、退出 Play）。
- T5：文档由独立文档子代理回写并在事实变化后追加修订；设计只描述最终设计，进度卷只保留最新进度；`doc-lint 0/0`、`--suite Doc` PASS。
- 门禁：L1 初次复跑暴露 2 条红——①本批 Meta 跨模块契约位置（已移至 `Meta/Abstractions/Room/` 并同步 3 个 csproj 源链接）；②批次外并行 WIP 的 `IHudDriver/IHudView` 位置（已纯迁移到 `Hud/Abstractions/`，如实登记为批次外修复）。最终 `L1 1961/1961 PASS`；comment-lint 0、dir-flat 无增长、l0 PASS、toolchain OK(2 既有 warning)。
- 残留风险：卸载失败仅假件注入；CancelWait 真实 UI 并发受 pipeline 串行限制、改由自动化覆盖；GM sandbox 与账号重入真实页面未验；未提交 git。
- 2026-11-09T00:26:01+08:00 · 验证 PASS · 11/11 · 证据 evidence/verify.json
- 2026-11-09T00:28:56+08:00 · 验证 PASS · 11/11 · 证据 evidence/verify.json

## 交付摘要

把「大厅 → 进房 → 离房」做成可复制的样板：Entry 只经 `IMetaSession` → `IRoomFlow`（`ProcedureFlow`）完成取票/有效性校验/Match 排队；Meta 退为纯票据源（`IRoomTicketSource`），不再隐含阶段切换或后绑定；一次入场由 `RoomFlowLifetime`/`RoomFlowOperation` 单占用、可取消、可移交，Match/Battle 共享同一寿命；离场由唯一观察者在清理（含必要场景卸载 await）完成后统一走 `RecoveryStep` 回大厅/结算/错误确认；登录代次保护旧恢复不覆盖登录页。页面、GM、账号重入、本地测试直入均已迁到同一入口。

## 验证结果

- 先行测试：T1 9/9 真实断言 red（`evidence/red.json`），修复后全绿。
- 定向：RoomFlow 26/26（含 Cleanup 两例）、MetaEntry 13/13、PageFlowBattleMenu 6/6、Core 8/8、Net 8/8、Unity 编译 0 错。
- 门禁：L1 1961/1961 PASS、l0 PASS、comment-lint 0、dir-flat 无增长、Doc 0/0、toolchain OK（2 条既有 warning）。
- `wf verify`：PASS，11/11 命令 exit=0（`evidence/verify.json`）。
- 真实 UI：登录/快速进入/离房/重进/普通建房/建房后离房/选房进入/房主开局/对局 Esc 退场/JoinAck 失败确认共 10 步真实指针或键盘完成（`evidence/t4-ui-acceptance-2026-11-08.md`，截图 `Artifacts/LobbyRoomFlow/*.png`）。
- 环境清理：本地 Meta/Room/Mongo 停止、任务容器删除、`deploy.args` 逐字节恢复、`Assets/Artifacts` 经 pipeline 删除、Unity 退出 Play。

## 未决事项

- AC5 真实 UI 侧「等待 JoinAck 用加载面 Back 取消」未验收（pipeline `run_script` 串行，无法并发注入真实 Esc）；同契约已由 `RoomFlowLoadingCancelTests` 自动化覆盖。
- AC7 的 GM sandbox 建房、账号重入真实页面落点未做真实 UI 复现（仅代码/自动化覆盖）。
- AC9 部分步骤无实时截图；真实 Unity 场景卸载失败未做真机注入（用例用假 `ISceneService` 抛错）。
- `RecoverRoomAsync` 的 `mergeKey: room.recovery` 合并去重未断言。
- 工作区仍有大量批次外/并行 WIP 改动（含 HUD 接口位置修复，见评审 C-3），本批未提交 git。

## 文件清单

- 契约/流程：`Assets/LiteGame/Modules/Meta/Abstractions/{IRoomFlow,IRoomTicketSource}.cs`、`Abstractions/Room/{MetaCall,MetaFailure,MetaFailureKind,RoomEndpoint,JoinTicketInfo}.cs`、`Procedure/Flow/{ProcedureFlow,RoomFlowLifetime,RoomFlowOperation}.cs`、`ProcedureSegment.cs`。
- 阶段/页面：`ProcedureMatch.cs`、`ProcedureBattle.cs`、`PageFlow.cs`、`Page_RoomLogic.cs`、`Page_SettingsLogic.cs`、`Page_LobbyLogic.cs`。
- 装配：`App/AppComposition.cs`、`App/PageServices/MetaEntrySession.cs`、`Modules/Meta/{MetaSegment,Client/MetaEntryService}.cs`、`Editor/Gm/GMTestModePage.cs`。
- 测试：`Assets/Tests/EditMode/Flow/RoomFlow/**`（Entry/Lifecycle/Ticket/Failure/Cleanup/Rig/Doubles）、`MetaEntryEditModeTests.cs`、`PageFlowBattleMenuEditModeTests.cs`、`Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs`。
- 验收工具：`AgentScripts/LobbyRoomFlow/{local_stack.py,RunRoomFlowUi.cs,DevBootSwitch.cs,HolderClient/**}`。
- 文档：`Docs/设计文档/架构/*`、`Docs/说明/{02,03,08,14,17}*`、`Docs/施工进度/客户端重构线.md`、`Docs/审计/客户端/登记/*`。

## 后续建议

- 第二批再评估 Account/Match 命名与 Scope 全量纠偏（本批刻意未扩大）。
- 把「卸载失败」纳入真机故障注入（可用测试装配替换 ISceneService 在生产装配下注入一次）。
- 给 `RecoverRoomAsync` 的合并键/去重补行为断言，或在 FeedbackService 暴露可观测 merge 面。
- 清理 `ProcedureBoundaryTests` 中残留的 `IAppFlow` 文案（测试作者维护）。
- 批次外 HUD 接口位置修复（`Hud/Abstractions/`）已披露，建议由所属并行线确认后保留。
