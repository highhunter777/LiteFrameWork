# 方案：大厅进房离房链路样板简化

> 运行：lobby-room-flow；日期：2026-11-08（Asia/Hong_Kong）。施工记录归本运行，现行设计在施工验证后回写已有设计文档。

## 背景与目标

用户先讨论客户端理解成本，再明确“先把‘大厅 → 进房 → 离房’做成样板”。样板以完整业务动作聚合职责，使读者从页面入口能找到一个实质编排入口，并沿现有两个阶段看到连接与清理。目标不是减少行数，而是消除 Meta 请求的隐含阶段切换、取消与离房的分散控制，保持真实业务路径可操作。

## 需求澄清与假设

- 已确认意图：实施大厅→进房→离房样板，保留现有框架能力，以首条业务链验证简化方式。
- 本批假设：采用局部流程重构；不改变服务端协议、账号生命周期模型、战斗算法、资源收集或页面布局。Account/Match 名称和生命周期的全量纠偏属于第二批，不能在本批顺手扩大。
- 交互语义：页面进房结果成功表示票据已校验、流程已接受进入请求；JoinAck 后才表示房间连接确认。房间等待页可以沿用现有预先展示行为，等待期间由既有加载反馈阻断。
- 需求待确认：下文 AC1～AC9 经独立评审后提交用户逐条确认，确认前不进入任务拆解、测试或源码施工。
- 失败语义：取票错误在大厅提示；握手/准备失败在本批补齐房间恢复动作，清理完成后展示既有反馈组件提供的错误确认，再返回大厅取新票重试。当前启动最小错误页没有房间返回/重试入口，不能依赖它。

## 范围与非目标

范围包括快速进入、选房进入、普通建房进入；进房请求取消/重复点击；等待 JoinAck 取消/失败；等待开局与对局中主动离开；账号重入取消在途入场；GM 显式 sandbox 建房进入的生产兼容。

不包含 Account/Match 全量重命名或重新划分 Scope，新增业务状态机/事件总线/统一门面体系，整体目录搬迁，UI 视觉调整，Sim/Net/Sync/协议/表源修改，云端发布或客户端打包。既有本地测试直入路径保持可用，但不能代替真实页面路径验收。

## 现状与约束

1. `App/PageServices/MetaEntrySession.cs:71-92` 把 DisplayToken 的检查放在 await Meta 请求之后。
2. `Modules/Meta/Client/MetaEntryService.cs:99-145` 的进房/建房在内部 JoinWith 直接经 IAppFlow 请求 Match；因此请求返回前就发生阶段切换，调用者的取消检查较晚。此为代码结构风险，尚未用失败测试复现，不声称已证实运行事故。
3. `Core/Flow/IAppFlow.cs` 与 `Procedure/Flow/ProcedureFlow.cs` 主要转发阶段请求；`AppComposition.BindMetaEntry` 另做一次反向装订。Procedure asmdef 已引用 Meta，可把实质入场编排归 Procedure 而无须新增程序集边。
4. `ProcedureMatch` 创建连接、等待 JoinAck、移交 Session；`ProcedureBattle` finally 清视图/运行/会话并卸场景。保留这两段执行职责，避免重写已有异步状态机。
5. `Page_RoomLogic.Leave` 先替换大厅再请求离场；MetaEntryService.LeaveMatch 在 Main 直接 no-op，未处理仍在取票的请求。页面恢复与流程清理的职责需要在同一套离场契约里明确。
6. `AccountReentry` 通过 IAccountSessionControl 清登录态、离房、回登录；`Editor/Gm/GMTestModePage.CreateTestRoomAsync` 直调 Meta.CreateRoomAsync 并依赖其隐含进入副作用，必须同批迁移。
7. 已登记债包括 Battle 端口的 RoomClient 逃生口及既有宿主退出债；本批不扩大这两处，不借本批重构战斗网络与宿主退出。
8. 工作区有大量既有未提交改动/移动，源码位置与 HEAD 不同。`evidence/baseline.json` 记录本批前关键文件哈希、状态和 HEAD；不得 reset、stash 或恢复用户的其他修改。评审必须比较本批前内容，不仅比较 HEAD。
9. 必须遵循规范02/03、09及设计总纲；源码按原编码/行尾做局部修改。新增/退役 Assets 文件经 Unity 导入/AssetDatabase 管线维护 meta；相关双轨引用与结构登记同步，禁止手改 meta 或生成工程文件。

## 候选方案与取舍

| 方案 | 核心思路 | 收益 | 成本与风险 | 结论 |
|---|---|---|---|---|
| A | 保留现有分工，仅给 Meta 加 ct、补页面判断 | 改动小 | Meta 仍控制应用流程，反向装订与离场分散不变，难以成为可复制样板 | 不采用 |
| B | 将现有 ProcedureFlow 从转发件改为实质入场/离场编排，Meta 只提供请求事实；复用 Match/Battle 两阶段 | 业务入口集中，取消在副作用前裁决，去掉一次后绑定与旧转发端口 | 需要迁移账号重入和 GM 消费者，补针对性测试 | 推荐 |
| C | 同时重做 Account/Match Scope、UI 端口和所有目录 | 一次统一很多概念 | 横跨登录与战斗，难以隔离样板收益与回归，超过第一批范围 | 留后续评估 |

## 设计要点

### 接口、依赖与装配

- 在 Meta 的既有 Abstractions 放置一个窄 `IRoomFlow` 契约，表达取票并请求入场、建房并请求入场和离房；支持普通建房及GM已有的显式模板/口令建房，返回复用 MetaCall<JoinTicketInfo>，保留错误码映射，不将 UI 数据带入流程域。不新增 asmdef，不给零引擎 Core 引入 UniTask/MetaWire。
- 改造既有 `ProcedureFlow` 实现该契约与既有 `IAccountSessionControl`。它消费 Meta 暴露的窄 `IRoomTicketSource` 与现有阶段机端口，负责请求有效性、票据端点校验、进入/离开请求及在途操作失效处理；不是新增一个只转发的包装层。
- Meta在既有Abstractions增加 `IRoomTicketSource`，只提供本批需要的纯取票/建房取票、AccountId/IsLoggedIn及Logout；不暴露令牌，不引用流程或UI。MetaEntryService实现此端口并由MetaSegment登记；ProcedureFlow不持具体Meta服务，保留ProcedureBoundaryTests的Forbidden约束。IRoomFlow与IRoomTicketSource分别承担应用动作边界和底层票据边界，均有当前生产消费者。
- MetaEntryService 的取票/建房 API 以方法名或明确契约表达“只请求票据”；传递 CancellationToken 到已有 IMetaClient API，保留登录信息、部署事实、模板配置和错误结果，不再持 IAppFlow、不再 BindFlow、不再发 RequestMain/RequestMatch。
- `MetaEntrySession` 的登录、列表、战绩仍用 Meta；快速进入/选房/建房/离房改用 IRoomFlow。转换页面数据和错误码的职责保留，避免页面接触 MetaWire 和阶段机。
- `ProcedureSegment` 在同一个初始化窗口先构造本批局部的RoomFlowLifetime，再构造阶段机和 ProcedureFlow，再登记 IRoomFlow/IAccountSessionControl，AccountReentry 使用这个控制实例。MetaSegment 停止登记账号控制端口；账号事实 IMetaConfigFacts 仍由 Meta 提供。
- AppComposition.BindPageServices 直接 Require 已就绪的 IRoomFlow，移除 BindMetaEntry 后装订；现有 PageContext 不做全量重写。旧 IAppFlow 仅在全仓确认无生产消费者后退役，AssetDatabase 删除及 csproj 源链接同步同批完成。
- GM 显式 sandbox 建房通过同一个 IRoomFlow 进入，模板/密钥仍由现有部署注入；不输出密钥、不硬编码房号、不访问云端做发布。

### 入场与取消

1. ProcedureSegment内创建共享的内部 `RoomFlowLifetime`，只管理本条链路的操作占用、取消、所有权移交和完成信号。它不注册新模块、不引入通用FSM；每次操作拿到不可被外部改写状态的句柄，内部使用CancellationTokenSource和UniTaskCompletionSource。ProcedureFlow、Match/Battle消费同一生命周期实例；本地测试直入由Match采用同一机制建立句柄，避免形成第二套离场收口。
2. 页面发出带 DisplayToken 的请求；流程先检查 ct、当前是否允许入场、生命周期占用（包括取票、排队、连接、运行、清理、错误恢复）。
3. 首个有效请求占用本实例的入场操作；快速进入、选房和建房使用同一套互斥/失效机制。重复请求返回稳定的忙结果，不发第二次 HTTP、不创建第二个会话。
4. Meta 异步请求传递 ct；流程在 await 后再次检查 ct、操作代次、账号有效性/阶段有效性，再校验票据端点。服务端或假传输忽略取消、晚到成功时，也不能推进已失效流程。
5. Request(Match)只表示排队：句柄随ProcedureArgs传递，并保留Queued占用与独立取消令牌。入队成功后解除源页面DisplayToken的绑定，避免主动替换房间页时取消已经接受的操作，但流程仍持有句柄直到阶段实际接管。此窗口的离房/重入使句柄失效；Current仍为Main时不Request(Main)，让已排队的Match实际进入后先检查句柄并零建连接地回Main/保留登录落点。在该无效请求被消费前拒绝新入场，不靠反射修改pending或放开阶段机重入守卫。
6. Match实际OnEnter在创建连接前校验句柄有效性并尝试一次接管。成功后等待JoinAck用阶段令牌与LoadingToken；Match→Battle再次显式移交同一句柄。正常连接的操作直到Battle清理完成均保留占用。 Match→Battle同样是排队窗口：已建会话在下一阶段实际接管前仍受该操作句柄托管，不能在Request(Battle)返回时成为无Owner资源。若离房/重入用Main覆盖排队的Battle，原操作必须回收尚未接管的会话并完成清理；加入JoinAck→排队Battle→离房覆盖→Advance Main用例，断言Battle未接管、未接管会话恰好释放一次、清理完成前新入场被拒绝。
7. 清理在途标记时只清自己的操作；旧操作 finally 不得清除新请求的 busy 状态。顺序测试覆盖取消A→发起B→A晚到→B完成。
8. 票据失败或端点非法：无阶段切换、无连接创建，页面恢复按钮和反馈。JoinAck拒绝/超时、准备失败属于本次房间操作恢复结果，不直接送入只支持重启的ProcedureError；阶段先释放本次会话并发出清理完成结果，再由流程经FeedbackService错误确认与PageFlow返回大厅；再次进入必须取新票。启动/装配Fatal仍走原错误处理。

### 离房与页面恢复

- 房间页、对局设置页、账号重入和 GM 同类入口均用流程入口；离房首先使未提交的入场操作失效，即使当前阶段仍为 Main 也不能漏掉在途取票。
- 对 Match/Battle 的离开仍由现有阶段取消与 finally 释放处理，输入接线→视图→运行/Match资源→连接会话的既有顺序保持；解绑 RoomBridge、加载反馈、Back 策略及场景恢复需逐项验证。
- 页面只发起离房意图，不独立抢先展示可操作大厅。RoomFlowLifetime为每次操作持有可等待的资源清理完成信号；Match在未移交时负责完成，移交后仅Battle负责完成。完成信号意味着连接/订阅/视图/输入/Back清理及本次必要场景卸载均已结束，不能用Current==Main、OnLeave返回或finally到达代替。
- ProcedureFlow观察同一操作完成结果，统一推进PageFlow恢复/结算或房间错误反馈；进入Main仍受生命周期占用阻断。只有当前句柄的清理和恢复动作结束才放行下一次入场，旧完成只能结清自己的句柄。必要卸载改为await现有ISceneService.UnloadAdditiveAsync，本次只卸本次拥有的场景，取消后的清理使用独立于已取消阶段的根生命周期，宿主关闭保留既有退出路径。卸载失败必须显性报告且保持入场阻断，不能声称已清零。
- PageFlow持每实例导航代次；每个房间操作捕获代次，ShowLogin同步推进代次并确定登录落点，宿主退出同样使旧恢复失效。任何离房/结算/错误确认导航提交前都检查操作与页面代次及RootToken。账号重入的Logout同步使在途句柄失效，Leave保持幂等；旧清理完成后不覆盖登录页。
- 当前 Match 取消路径缺少完整页面恢复协作，需要让等待 JoinAck 取消也经过同一个 PageFlow 收口。房间失败补齐实际恢复动作：资源清理后经既有FeedbackService.ShowErrorAsync（retryable:false，单按钮确认）展示可确认错误，再回大厅手动重新进入；不自动重放旧票据或无限重试。
- 本批不改 AccountSession 实现与 Scope 定义；文档明确其当前为进房时产生的可移交句柄，完整账号生命周期纠偏单列后续。

## 通用性与扩展性

共性根因是请求事实与应用流程混合、取消判定晚于副作用、UI 导航先于异步清理。用既有流程域统一操作有效性和离场收口，可同时覆盖快速进入、选房、普通/GM建房与账号重入。只有一个当前需要的窄契约，不增加通用请求框架；状态使用每实例字段与 CancellationToken，不新增 public static 可变状态，不按特定房号/场景/玩家数特判。列表和战绩等无关查询不借此批改造。

## 测试策略

- 独立只读调查已完成（子代理 Lorentz，01a1bafa-f2e7-7a62-82e9-835819dbb498）。现有 `Tests/LiteFramework.Core.Tests`（xUnit）覆盖 AccountReentry/ProcedureBoundary/M3接线，`Tests/LiteNet.Tests`（xUnit）覆盖 AccountSessionFactory/RoomClientLeave；Unity EditMode 覆盖 MetaEntry/PageFlow/BattleRun/BattleSession。新增可控延迟 Meta 客户端和阶段机行为测试，沿用端口而非真实网络等待制造竞态。
- 迁移 `Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs` 中 FakeAppFlow/BindFlow 及精确移交断言的归属：Meta测试保留参数/错误契约，流程测试承担阶段切换与取消；`Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs` 的账号重入守卫改为验证新控制实现与同实例装配，不能只删除原接线约束。Core.Tests当前没有源链接ProcedureFlow；新增行为测试若选dotnet必须明确补齐实际源集，不能通过删依赖或编译错误伪造red。
- 定向dotnet回归命令：`dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter "FullyQualifiedName~AccountReentryTests|FullyQualifiedName~ProcedureBoundaryTests|FullyQualifiedName~账号重入_401到回登录页单点接线"`；`dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter "FullyQualifiedName~AccountSessionFactoryTests|FullyQualifiedName~RoomClientLeaveTests"`。
- 必要Unity定向fixture通过 `python scripts/gate/unity-tests.py --mode editor --filter <fixture>` 执行：MetaEntryEditModeTests、MetaClientEditModeTests、PageFlowBattleMenuEditModeTests、FeedbackServiceEditModeTests、BattleRunPortEditModeTests、BattleSessionEditModeTests以及本批新增流程/RoomBridge行为测试。生成的Unity csproj不手改，不用dotnet test冒充Unity测试。
- 测试子代理先写行为测试：晚到响应不得发起入场、重复请求只有一次请求/迁移、旧请求清理不污染新请求、Main期间离房取消在途取票；在生产修复前观察预期断言失败。不能以缺类型/编译错误当 red。新增确定性用例覆盖取票成功并入队、Advance前离房/账号重入/重复请求；可控卸载未完成时大厅不可操作且不得再次入场，卸载完成后恰好一次恢复；ShowLogin使旧房间清理/错误反馈的导航失效。
- 现有 Meta、账号重入、Match/Battle 生命周期、RoomBridge 与程序集边界用例做回归。测试实现不删除旧行为断言，而是按新纯票据契约重组其归属。
- 主代理基于任务前快照审核施工 diff，并运行定向用例、L1 PullRequest、l0依赖扫描、注释与目录预算、Doc和工具链登记检查；Unity recompile 要求 0 错误。
- 当前共享 .workflow/config.json 有67项commands、19项red_commands，另一活动运行tps-shoulder-camera处于implement。进入tests-first前必须先明确本批验证/red的执行配置与证据归属，保证其他运行命令定义不受影响；此前共享配置保持原样。已通过CLI独立root配置与同一运行目录junction完成本批命令隔离，详情见evidence/workflow-context.json；原共享配置sha256核对未变，其他活动运行定义保持原样。命令使用argv包装显式在项目目录执行，不手改state阶段，不删除历史命令或降低门禁。
- Play/真机验收使用 unity-pipeline 真实指针/键盘驱动，先做按钮射线命中检查，覆盖大厅→快速/选房/建房→等待页→离房→再次进入，并覆盖等待取消。可控延迟/拒绝用于自动化错误路径，与真实操作冒烟的证据分开。 已只读确认当前工程Editor端口7801可达；当前Editor不暴露RuntimeOnly的simulate_pointer。Editor Play采用已有 `FeedbackSurfacePlayModeTests.ClickReachable` 的 EventSystem.RaycastAll→pointer down/up/click 手法，经pipeline eval/run_script执行，按实际页面点击并断言副作用；键盘Back须派发真实输入路径，不能直接调用取消函数。
- 本地 Meta/Room 与 Unity 环境不具备时，准确记录未验证项，不把直调API或单元测试冒充真实操作通过；不得私自发布云服务或重打Player。

## 验收标准

- [ ] AC1 结构：从页面进入 IRoomFlow/ProcedureFlow 后能看到取票→有效性检查→请求Match；连接/清理沿现有 Match/Battle 阶段可追踪。Meta 无阶段切换与 BindFlow；不新增仅转发的运行层、不新增程序集依赖边。
- [ ] AC2 正常入场：快速进入、选房进入、普通建房均只接受一次入场，并保留服务器生成的房号/地图/人数；票据接受与 JoinAck 确认的含义可区分，房间等待页展示正常。
- [ ] AC3 取消隔离：取票前取消、取票中取消/页面隐藏、离房后晚到响应、旧A取消后新B成功及入队后Advance前离房/重入/重复进入场景中，旧请求不能创建连接、跳页或覆盖新请求状态。
- [ ] AC4 失败恢复：未配置/未登录、票据拒绝、非法地址不进入Match；JoinAck拒绝/超时与准备失败释放本次会话，通过本批补齐的房间错误确认返回大厅后可再次进入；启动Fatal仍走原处理。
- [ ] AC5 主动离开：等待JoinAck可用加载面Back取消，等待开局可点离房，对局中可从设置离开；完成清理后回大厅，重复离房幂等，可再次进入；账号重入保持登录页落点。
- [ ] AC6 生命周期：离场后旧连接/运行、房间投影订阅、输入接线、加载遮罩与Back策略不残留；一次会话只转移一次所有权，释放所有者可追踪，必要卸载完成前不放行下一次入场，离场旧回调不污染下一次会话或登录页。
- [ ] AC7 兼容：账号重入、GM sandbox建房并进入、本地测试直入与正常结算页面契约仍成立；凭据不进入日志；无需修改服务端协议、Sim/Net/Sync或表数据。
- [ ] AC8 自动验证：有真实断言red→green证据；本批定向回归、L1、依赖/目录/注释/文档/必要登记守卫和Unity编译通过。既有失败须以本批前基线区分且如实披露，未执行项不能计通过。
- [ ] AC9 可操作性：通过射线命中自检和真实指针/键盘完成正常进入、等待离房/取消、再次进入；每步断言页面与清理副作用。只直调API不算通过，环境不足则保持未验收。

## 风险与回滚

- 本批最大的风险是改变页面DisplayToken与阶段所有权的交界、离场异步完成后覆盖账号重入/下一次进入、遗漏GM旧副作用消费者。用可控响应顺序与生命周期事件测试约束，不通过新加延时解决。
- 大量既有工作区修改的风险：每个施工任务基于当前文件快照应用最小补丁，施工子代理不得恢复HEAD版本，不允许对本批外文件批量整理或格式化。
- 回滚以任务前文件快照/本批差异撤销为准；Assets退役恢复经Unity资产管线，不用git reset --hard、clean或整文件覆盖用户改动。当前仅落盘方案，尚无生产改动。
- 回写：验证后更新既有客户端总设计/DI专项/说明03、08、17和客户端施工进度卷的相关章节；设计只描述最终职责，证据与未决项进本运行及施工卷；需要新增债时按登记规范记录。

## 首轮评审修订映射

- P-1：增加真实票据窄端口，Procedure保持零具体Meta服务依赖。
- P-2：Queued归流程持有、Match前验活、无效排队请求先消费再放行，明确DisplayToken脱离点。
- P-3：内部共享生命周期句柄/完成信号、等待本次必要卸载、恢复期间阻断、导航代次保护登录/退出落点；未改框架FSM。
- P-4：房间失败在清理完成后走Feedback错误确认→大厅→新取票，纠正“既有错误页能回大厅”的错误假设。
- P-5：命令隔离为进入测试前显式前置项，目前保持共享配置不变。

## 任务评审后执行约束

- T1在生产修复前覆盖完整可观察行为（含两个排队窗口和卸载/登录优先级），不延期至T4。测试适配器仅转调现有真实服务/阶段；后续接口变化由独立测试作者映射调用，场景与断言冻结。不可测试的必要契约先明确最小编译面、再真实断言red，环境/编译故障不计red。
- T2/T3生产会话无测试写权限；T6由独立dsh测试会话分两轮迁移调用并核对旧断言，和生产写入/Unity操作串行。T2/T3不等T6最终done，但各自done前必须取得当轮T6交接与全量RoomFlow验证。T6最终在T3测试映射结束后done，T4依赖T3及T6。
- T3完成前重跑完整RoomFlow（包括T1/T2）、AccountReentry、MetaEntry、PageFlow和AccountSessionFactory/RoomClientLeave回归。所有实现派单记录测试/生产会话ID和不重叠写集。
- T4只做最终UI路径与回归；入口AgentScripts/LobbyRoomFlow/RunRoomFlowUi.cs，经当前pipeline run_script/eval参数执行；Artifacts/LobbyRoomFlow保存断言/截图。具体CLI以当前--help为准。
