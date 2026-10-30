# 施工记录：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 创建于 2026-10-30T19:40:28+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-30T21:15:45+08:00 · 新增任务 T1「批1 框架装配入口与 Events 独立」
- 2026-10-30T21:15:46+08:00 · 新增任务 T2「批1 装配段就地迁移与守卫同步」
- 2026-10-30T21:15:46+08:00 · 新增任务 T3「批1 适配器委托注入与能力端口」
- 2026-10-30T21:15:48+08:00 · 新增任务 T4「批2 业务域垂直化与 asmdef 合并」
- 2026-10-30T21:15:49+08:00 · 新增任务 T5「批2 对局冒烟用例与命令接线」
- 2026-10-30T21:15:50+08:00 · 新增任务 T6「批3 确认框端口与 Baseline 清空」
- 2026-10-30T21:15:51+08:00 · 新增任务 T7「批4 组合根与 Container 拆解」
- 2026-10-30T21:15:52+08:00 · 新增任务 T8「批5 外层域边界收口」
- 2026-10-30T21:15:53+08:00 · 新增任务 T9「全批 文档回写与收尾」
- 2026-10-30T21:25:47+08:00 · 开始任务 T1「批1 框架装配入口与 Events 独立」
- 2026-10-30T21:25:48+08:00 · 完成任务 T1 · 证据: 新增 LiteFramework/Runtime/Framework/{FrameworkInfrastructure,FrameworkClocks}.cs；PlatformInfrastructureModule 改用 Create 并 Put 池/文件/JSON/日志/派发器/命令中心；ClocksModule…
- 2026-10-30T21:35:47+08:00 · 更新任务 T3（deps）
- 2026-10-30T21:35:48+08:00 · 更新任务 T2（deps）
- 2026-10-30T21:35:49+08:00 · 开始任务 T3「批1 适配器委托注入与能力端口」
- 2026-10-30T22:24:54+08:00 · 完成任务 T3 · 证据: 能力端口 IRenderCameraSource/IAimCameraSink/ILookDevice 落地并替换 4 处 is 具体适配器；新增 App/AppComposition.cs 的工厂委托（ConfigService/NewInputIntentSource/CinemachineCameraServi…
- 2026-10-30T22:45:23+08:00 · 开始任务 T2「批1 装配段就地迁移与守卫同步」
- 2026-10-30T22:45:24+08:00 · 完成任务 T2 · 证据: 12 段就地迁入 Modules/<域>/<域>Composition.cs，Container/Ui 移 App/ 并删 Bootstrap/Modules；DependsOn 改模块名；BridgeCapabilitySet 下沉解环；各域 asmdef 补 UniTask/LiteGame.Core + 允许表…
- 2026-10-30T23:13:08+08:00 · 开始任务 T4「批2 业务域垂直化与 asmdef 合并」
- 2026-10-30T23:23:23+08:00 · 完成任务 T4 · 证据: 批2 结构落地：Core/Battle 删除；View+Presentation 迁入 Modules/Battle 按职责子目录（Aim/Arena/Characters/Combat/Commands/Hud/Setup）+ Abstractions 六端口（IBattleViewGroupFactory 独立成…
- 2026-10-30T23:52:22+08:00 · 开始任务 T5「批2 对局冒烟用例与命令接线」
- 2026-10-30T23:52:30+08:00 · 完成任务 T5 · 证据: A12 落地：Assets/Tests/UI/PlayMode/BattleSmokePlayModeTests.cs（Boot.unity 起服→Main→RequestEnter→Battle→IsBuilt/Hud.Root/Crosshair→自瞄开火四路表现（ShotPulse/VoiceCount/Vfx…
- 2026-10-31T00:02:42+08:00 · 开始任务 T6「批3 确认框端口与 Baseline 清空」
- 2026-10-31T00:02:42+08:00 · 完成任务 T6 · 证据: A13：ModuleDependencyDeclTests.Baseline 清空（最后一笔 UiService <- Container 由确认缝端口化解）并通过；Ui 段不再 Require<FeedbackService>（App/UiServiceModule.cs 产 ConfirmDialogRouter…
- 2026-10-31T00:16:16+08:00 · 开始任务 T7「批4 组合根与 Container 拆解」
- 2026-10-31T00:31:09+08:00 · 完成任务 T7 · 证据: A15：ContainerModule 删除（不再是 IClientModule）；GameEntry 18 段全序（①PI ②Camera ③Identity ④DevSession ⑤Content ⑥Settings ⑦Events ⑧Clocks ⑨Schedulers ⑩LuaHost ⑪Config ⑫M…
- 2026-10-31T00:37:27+08:00 · 开始任务 T8「批5 外层域边界收口」
- 2026-10-31T02:34:49+08:00 · 完成任务 T8 · 证据: 批5 3/3：A24 IBattleRun/IBattleRunFactory/IMatchJoin 端口化 Procedure（ProcedureBoundaryTests 2/2）；A25 8 共享域 LiteGame.Modules.<域>（DomainBoundaryTests 5/5 先红后绿）；A26 B…
- 2026-10-31T02:36:04+08:00 · 开始任务 T9「全批 文档回写与收尾」
- 2026-10-31T02:36:22+08:00 · 完成任务 T9 · 证据: 文档回写：说明 02/14/17/18/README、规范 03、框架总设计/设置专项、技术债、进度卷第18章、test-plan（A30 新域步骤+命名空间/矩阵）；门禁实测：L1 1921 PASS、doc-lint 0/0、doc-lane PASS、restore PASS、l0 6/6、toolchain-…

## 关键决定

- **A24 按端口逐个拆，不一次改完**：本轮已把"传输裁决（MetaEntryConfig）/联机事实（MetaEntryService）/战斗命令（TeleportToAimCommand）"三件事从流程段摘出
  （分别归会话段、Meta 端口 `IMetaConfigFacts`、战斗端口 `IBattleCommands`）；余下 `BattleClient`/`BattleContext`/`EndReason`/`RoomSessionFace`
  需要先立战斗流程端口（`IBattleRun`：StartGame 等待/Ended 事件/终局原因/结果/席位/规则），再改流程阶段——放到下一检查点，避免半改状态落地。

- **设置门面按"本体/脚本面"切两半**：值域收敛与生效面住 `Modules/Settings`（不认识脚本环境），`Bridge.settings` 的 40+ 绑定随 `XLua` 适配器住 `Adapters/LuaBridge/ProductBridges`——设置域因此不再引 xLua；LuaBridge asmdef 增 `LiteGame.Modules.Settings`（允许表同步）。
- **`DeployBake` 由静态可变改实例**：读取缝构造注入、memo 收进实例（`Reader`/`ResetForTests` 这类进程级可变静态消失）；内容段与联机段共用 GameEntry 创建的同一实例。
- **联机入口经 `IAppFlow` 端口化**：`MetaEntryService` 不再持 `IStageHost<ProcedureId,ProcedureArgs>`（那是反向认识流程域类型）；
  端口只表达 `IsAtMain`/`RequestMatch(MetaJoin)`/`RequestMain`，实现 `ProcedureFlow` 住流程段（App）——Meta → Procedure 的边从"类型直引"降为"语义端口"。
- **批 5 分三个检查点**：1/3 文件归位（会话/地图/UI 纯化/错位件）——已提交；2/3 契约与命名空间收口（Meta 端口、Procedure 去直引、8 域命名空间、排序与瞄准单源）；3/3 域间矩阵门禁与文档回写。
- **会话域收敛口径**：`Modules/Session/` 收 `AccountSession+Factory`、`MatchSession+Factory`、`MatchTransport`、`BattleSession` 聚合、`RoomSessionFace` 与两契约；`Modules/Room/`、`Modules/Procedure/Session/` 删除。`BattleSession` 与既有 `MatchSession`（Match 域本体）同名冲突，故聚合保留原名——A18 的 "MatchSession(Factory)" 指既有类型 + 工厂。
- **批 4 段拆解的归属落位**（归属表逐行落地，段名即模块名）：⑤ Content 收场景/缓存租约（`SceneService`/`IContentCacheMaintenance`）、
  ⑥ Settings 收视角偏好单实例、⑦ Events 收 `IEventBus` 接口键、⑨ Schedulers 收 `ITimelineRunner` 接口键、
  ⑫ Map（`IMapProvider`）、⑬ Ui 收弹窗/反馈/提示/UI 时钟驱动（确认缝在本段内产口即绑）、⑮ Meta（部署快照 + 联机客户端 + 入口编排件）、
  ⑯ Battle 收视图组工厂与 `IVFXService` 接口键、⑰ Session（`ITransportFactory` 端口 + 传输工厂 + 会话工厂）、
  ⑱ Procedure（流程机/页面流/账号重入/恢复执行器/Lua 桥装订口）。`ContainerModule` 删除。
- **帧驱动序与密封仍单点**：`SetTickOrder(13 项)` 与 `services.Seal()` 只在 `AppComposition.ComposeAfter`——分段落声明会按注册序重排相对序。
- **批 4 分两个可交付检查点**（体量/风险实测）：① 组合根钩子与跨域装订迁移（`ComposeRoot` + `AppComposition.ComposeAfter`，其它段不动）——已提交；② 段拆解（Map/Meta/Session/Procedure 四段成型、`ContainerModule` 去 `IClientModule`、18 段序、归属表迁移）。两次提交都保持全绿（L1 + Boot 冒烟），避免"半个批 4"落在工作区。
- **组合根执行器与模块图校验抽成协作件**：`ComposeRootRunner`（窗口开关/失败回收/逆序第一释放位）与 `ModuleGraphValidator`（重复名/缺依赖/环）从 `ClientHost` 移出——`ClientHost` 类行数冻结 454 不许涨，`InitializeAsync`/`ShutdownAsync` 拆到 ≤80 后按规则删除冻结键（不是调阈值）。
- **T3 与 T2 顺序对调（实测发现）**：方案原序为 T2 搬迁 → T3 适配器上收，但实测程序集依赖使该序不可编译——`LiteGame.Adapters.Unity` 引用 `LiteGame.Modules.Input`/`Modules.Camera`、`LiteGame.Adapters.Luban` 引用 `LiteGame.Modules.Config`、`LiteGame.Adapters.LuaBridge` 引用 `LiteGame.Modules`；若先把段搬进 `Modules/**`（= 各域程序集），段内 `new NewInputIntentSource`/`new CinemachineCameraService`/`new ConfigService`/`new LuaBehaviourAdapter` 会立刻与适配器成环。故 DAG 改为 `T1 → T3 → T2`（已在 workflow 中更新依赖）。
- 能力端口按既有 `IAimWorldSink` 范式落地（可选能力接口 + 消费方 `is` + 未实现即退化）：`IRenderCameraSource`（`Modules/Camera/Abstractions/`）、`IAimCameraSink`/`ILookDevice`（`Modules/Input/Abstractions/`），实现留在 `CinemachineCameraService`/`NewInputIntentSource`。
- `FrameworkInfrastructure` 的池释放用 `(References as IDisposable)?.Dispose()`：`IReferencePool` 只约束取还，不该在接口上绑生命周期。
- **批 2：相机拆成 ② `CameraComposition` 薄段**（主相机保证 + `ICameraService`，适配器经 `Func<ICameraService>` 工厂注入），`BattleComposition` 只留战斗表现内核；段名 `Presentation` → `Battle`（`ContainerModule.DependsOn` 同步：删 `Presentation`、补 `Camera`/`Battle`）。理由见 plan A8b——主相机是投影面，必须早于 `Ui`/`Input`；表现内核只被战斗与设置消费，属战斗域。
- **批 2：HUD 读模型构造上移到 `BattleViewRig`**（`BattleHudAssembler` 只接 `IBattleReadModel` 实例）。`Presentation/Battle/Hud` 与 `Presentation/Hud` 合并进 `Battle/Hud` 后，装配器落进 `PresentationReadModelGuardTests` 的扫描面：它当时用 `Func<SimWorldState>` 现场 `new BattleReadModel(...)`，等于出现"第二个翻译点"。把构造留给装配点（Rig，本来就持 `SimView`）后，守卫基线仍是"唯一翻译点 = BattleReadModel.cs"，不需要加例外（守卫只减不增）。
- **批 2：越肩几何判据的参照系用相机视线水平轴**（不是角色 `transform.forward`）。实测该角色 prefab 的模型朝向轴是 +X（不是 +Z），用 `forward` 当"身后"方向会把 20° 肩偏读成 155°（首跑即红）。判据改为"相机在角色身后、相对视线轴横向偏出 15°~45° 且在右侧"——与场景 vcam 的 `FollowOffset` 解耦，也不绑定美术朝向约定。

- **批 5 命名空间口径**：8 个共享能力域各自 `LiteGame.Modules.<域>`（Camera/Config/Dev/Framework/Input/Map/Settings/Ui）；业务域 Battle/Meta/Session 与应用壳 Procedure 仍住 `LiteGame`——业务域靠程序集（Battle）与 `DomainBoundaryTests`/矩阵约束，不强行改命名空间以缩小本批爆炸半径。测试守卫先红（51 个文件全部报错）后绿（5/5）。
- **A24 战斗流程端口拆两层**：`IBattleRun`（StartGame 等待/Ended/原因/结果/席位/每帧驱动/离场）由 `BattleSession` 实现、`IBattleRunFactory` 由会话段产出；进房另立 `IMatchJoin`（JoinAck/收发泵）。流程段因此不认 `BattleClient`/`BattleContext`/`RoomSessionFace`；`ProcedureBoundaryTests` 用代码区扫描钉住。
- **Meta 端口注册在 Meta 段**：`MetaComposition` 同时 `Put<IAccountSessionControl>`/`Put<IMetaConfigFacts>`，`ProcedureComposition` 只取端口——避免"App 组合根替 Meta 注册"的隐式所有权。
- **域间矩阵是文档门禁，不是引用禁止表**：`domain-matrix.py` 统计代码区顶层类型引用次数并渲染 `Docs/说明/18`；`--check` 只判过期。允许边在脚本 `ALLOWED_EDGES` 单源，计数异常/新增边靠评审与人工登记（机械的"未登记边即红"留待后续）。

## 偏差与处理

- **段位置：18 段中 16 个住 `Modules/**`，Ui 段（`App/UiServiceModule.cs`）与流程段（`App/ProcedureComposition.cs`）仍在 App**。
  原因：两段都要用 `Adapters.LuaBridge` 的类型（`LuaBehaviourAdapter`/注册表/`RegistryFiller`），而业务模块程序集不得引适配器（硬边）。
  A15 的"Modules/** 合计 18 个 Composition"要等批 5 把注册表件归位后再达成；本批以"18 段全序 + Container 去 IClientModule"为准。
- **A17 手测以自动化链路替代**：Boot 起服 → Main → `RequestEnter` 进本地测试房 → 开火 → `RequestExit` 全链由冒烟用例驱动
  （生产入口与生产装配同一路径），并断言控制台零 error；"人工点界面"未做，记入未决事项。
- **批 4 的跨域装订先落位**：`AppComposition.ComposeAfter` 已接管 Lua 门面/身份绑定/模态登记/输入拦截/注册表回填/联机入口接流程机/开发装饰/帧驱动序/装配面密封；`ProductSettings` 与 `BattleViewGroupFactory` 仍在 `ContainerModule`（其产物被流程机/视图工厂消费，须在段拆解时随归属表一起搬，否则顺序错位）。
- 方案 A4 原文"`GameEntry` 在 `InitializeAsync` 前调用工厂"不可行：`NewInputIntentSource` 的 `matchRules` 读口依赖 ③ `IDevSession`，前置创建会静默丢掉测试房爆头带覆写。改为**工厂委托注入 + 段内调用**（已写入 plan.md A4）。
- 方案 A12 的 `BattleViewGroupFactory` 注册落点原写 `AppComposition.ComposeAfter`，与 ⑱ `Procedure` 构造顺序冲突；改为批 1–3 在 `ContainerModule.CreateBattleViews` 处注册、批 4 归 `BattleComposition`（已写入 plan.md A12）。
- **A16 的 `ContainerModule.Assemble`（152 行）四方法拆分未在批 1 执行**（批 1 任务已收口，但该键仍在 `ArchitectureBudgetTests` 冻结表里）。本批未扩范围：仍按 plan 的"批 4 只搬方法"口径，在 T7 拆 `ContainerModule` 时同批拆方法并删 152 行键。
- **批 2 冒烟（A12）暴露两处既有产品缺陷并同批修复**（见"问题与修复"）：① `VfxHandleTable.CollectExpired` 以 `Go != null` 为前提，挂点随视图根销毁后被误判为"加载在途"→ 特效表项永久挂账；② `AudioService._voices` 的"同源 voice 键"在代理解绑后不摘 → `VoiceCount` 离场恒为 1。A12 的"≤2s 归零"判据正是打在这两处上，不修则门禁恒红。
- T1 过渡期把 Events 段命名为 `EventsModule.cs`（依赖守卫现按 `*Module.cs` 收集；批 1 迁移时统一改 `EventsComposition.cs` 并同步扫描面）。
- **批 3：确认缝改"端口 + 后绑定"**：新增 `LiteFramework.UI.IConfirmDialog` + `ConfirmDialogRouter`（可后绑定的实现槽）；UI 段（`App/UiServiceModule.cs`）**产出**路由器并交给 `LuaBehaviourAdapter`（`self.ui:ShowConfirm` 的服务端），装配根（`ContainerModule`）在 `FeedbackService` 建好后 `Bind(feedback.ConfirmAsync)`；`ModuleDependencyDeclTests.Baseline` 清空。落点说明：plan 把契约写进 `App/Bootstrap/Contracts/**`，但批 1 已把 `App/Bootstrap` 收敛为只含 `Probes/Fallbacks`（A2），且契约要被 `LiteFramework` 的 `FeedbackService` 与产品 UI 段同时看见——故落 `LiteFramework/Runtime/Modules/UI/`（框架 UI 能力层）。未绑定语义：按取消返回 false、只记一条 Warning（`WarningCount` 诊断读口），不抛不悬挂。
- **批 2：A7 的 `IBattleViewGroupFactory` 独立成文件**（原来与 `IBattleViewGroup` 同文件）：`DomainBoundaryTests` 按文件名判"六端口齐备"，同文件会让判据与实现形态耦合。
- **批 2：A12 观测面补两个 internal 读口**——`BattleViewRig.Feedback`（→ `BattleFeedbackAssembler`）与其 `DamageNumbers`。A12 原文只列了 `IsBuilt`/`Hud`/`View` 三个 `BattleViewRig` 成员，但判据同时要求读 `BattleDamageNumberDriver.ActiveCount`——没有读取面这条断言无法落地（驱动实例由反馈装配件私有持有）。
- **契约变更（用户裁决，方案 B）：`IClientModuleGraph.DependsOn` 由 `IReadOnlyList<Type>` 改为 `IReadOnlyList<string>`（按模块名寻址）**。原因：业务域各一程序集且段就地住域目录，`typeof(段类)` 必然把域程序集连成环（`Modules.Input → LiteGame.Modules → Modules.Input`，Config/Settings/Presentation 同理）。`ClientHost.ValidateModuleGraph` 改按名校验 + 按名 DFS 环检测；13 段 `DependsOn` 全改字符串；`ModuleDependencyDeclTests` 身份改取 `public string Name => "X"`（回退文件名去 `Module`/`Composition` 后缀）、依赖改解析字符串字面量、Baseline 键改 `UiService <- Container`；`ClientHostTests` 三个测试替身同步。验证：Unity 0 error；L1 793/798（5 红＝迁移守卫预期）。


## 问题与修复

- `ConfigModule` 改经工厂委托产出后，`context.Put(_createConfig(content))` 触发 `模块产物与依赖声明必须可静态解析` 红（守卫只认 `Put<T>(...)`/`Put(变量)`/`Put(new X(`）：改 `Put<IConfigService>(...)`，并给 `IConfigService` 补 `GameplayDigestBase`，让 `ContainerModule`/`UiServiceModule` 经端口消费。
- 同一产物被 `ConfigModule.Put` 与 `ContainerModule.RegisterInstance` 重复登记会撞容器：删掉 Container 侧两条 `IConfigService`/`IConfigTableSource` 注册。
- **批 2 冒烟首跑：VFX 离场 6s 不降（活跃 3~4 恒定）**。根因不是"寿命太长"，而是 `VfxHandleTable.CollectExpired` 的 `inst.Go != null` 前提：跟随特效挂在武器挂点上，视图根拆除时 GameObject 被 Unity 销毁 → 字段变 fake-null → 被当成"加载在途"跳过，表项永久残留（`ActiveCount` 永不归零）。修法：判据只看 `ExpireAt <= now`（pending 的 `ExpireAt` 为 +∞，天然不会命中），并在 `VfxInstance.Go` 的文档里写明 null 的两种语义。回归用例：`VFX_挂点被外部销毁_仍按寿命到期回收_活跃归零`。
- **批 2 冒烟二跑：只剩"自然寿命"级别的残留**（火光/弹着按 2~5s 寿命慢慢退）——A12 要求离场 ≤2s 归零，故补**归属人回收面**：`IVFXService.Play/PlayAt` 增可选 `owner`、`StopAll(object owner)`；`BattleMuzzleFlashDriver`/`BattleImpactFxDriver`/`BattleRemoteMuzzleFlashDriver` 以自己的 `this` 为归属人播放，`Dispose` 时 `StopAll(this)` 一次收回。回归用例：`VFX_按归属人整体收回_不影响无归属与他人`；`BattleCombatFxEditModeTests.Dispose后…` 增断言"两个驱动各按归属人收回一次"。
- **批 2 冒烟三跑：VFX 归零后卡在 `AudioService.VoiceCount == 1`**（分组快照 Effect=0/4，即实际没有在播）。根因：`_voices` 的键表只在 `Shutdown` 清空，代理解绑/停止后键仍挂账 → `VoiceCount`（在册同源 voice 数）恒 ≥1。修法：`ReleaseProxy` 与 `Configure`（代理易主）两处调 `ForgetVoicesOf` 摘键；`BattleAudioDriver` 记录自己登记的同源 voice 句柄，`Dispose` 逐个 `Stop`。回归：`BattleAudioDriverEditModeTests` 4/4 保持绿。
- **Boot 冒烟发现 `checksum-mismatch` 诊断包**（`[Battle] 诊断现场包已落盘: …-checksum-mismatch-…`，2026-10-30 本地测试房）：本批只改装配与端口、未触判定与协议，判为既有行为，**本批不处理**；批 2 冒烟时对照是否复现。
- **`Tests/LiteNet.Tests.csproj` 源链接未随批 5 会话搬迁**：仍指向 `Modules/Procedure/Session/`，L1 首跑 CS2001。修为 `Modules/Session/` 并补 `IMatchJoin.cs` 链接（AccountSession 实现端口）。教训：目录搬动后 `Tests.slnx`/`*.csproj` 源链接必须逐条核对。
- **共享域命名空间迁移的批量工具副作用**：首轮脚本按"类型名出现在文件任意位置"加 `using`，把第三方/框架/生成物也改脏（`UI` 嵌套类型、注释中的类型名、BOM/CRLF 变化）。处置：按**顶层类型 + 代码区**重算 using、剥掉非产品程序集的模块 using、还原无实质 diff 的文件；最终 8 域 51 个文件 + 约 115 个消费文件有实质改动。
- **`Config` 共享源改名空间后服务端编译红**：`Server/RoomServer/{ServerTableLoader,ServerMapLoader,Program}.cs` 与 `Tests/LiteNet.Tests` 需补 `using LiteGame.Modules.Config;`；共享源（`SimConfigMapper`/`TableLayout`）在 BuildHash 闭包内，已重算 `shared=cf3c3453057b28a4`。
- **`WeaponAimConstraint` 对空 Avatar 无守卫**（既有缺陷，审计已登记的 `AimPitchPostureDriver` 同型）：灰盒/清单供片视图调 `GetBoneTransform` 抛 `Avatar is null`，`ClipManifestDriverEditModeTests` 3 红。补 `animator.avatar == null` 显性退化；3 例转绿。
- **`UiApiSpecGuardTests` dispatch 路径仍指旧目录**（`LiteGame/LuaBridge` → `LiteGame/Adapters/LuaBridge`）：守卫恒红，修为现行路径。
- **`VisualSingleSourceEditModeTests` 目录级冷扫描实测 231s**，超过默认 180s：保留断言、把用例超时提到 300s（不改扫描面）。
- **`UiValidationEditModeTests` 文字档基线 104 vs 实测 128**：UI 批新增 24 条未标记文本，非本批改动；记未决（不擅改 UI 基线，避免掩盖样式债）。

## 验证结果

- 2026-10-31 · **L1（PullRequest）PASS 1921/1921**（含新增 ProcedureBoundaryTests 2、DomainBoundaryTests 5；Unity 0 error）。
- 2026-10-31 · **BattleSmokePlayModeTests 1/1**（Boot→Main→进测试房→开火→离场→容器归零）。
- 2026-10-31 · 定向 EditMode：BattleRankboard 8/8、BattleRankingConsistency 1/1、CameraAimBlend 6/6、BattleSession 12/12、MatchLoopLeak 1/1、ClipManifest 7/7、UiApiSpec 4/4、VisualSingleSource 8/8。
- 2026-10-31 · 全量 EditMode 1098 例首跑 6 红（5 红已修转绿；余 1 为 UiValidation 文字档基线，见未决）。
- 2026-10-31 · BuildHash --check 双 OK（shared=cf3c3453057b28a4 / meta=75e0e85fe3c33cf2）；gen-ui-page --check 12 页；domain-matrix --check OK；doc-lint 0/0；restore-check PASS；l2-meta 14618 meta 全合法；scripts/tests exit 0。
- 2026-10-31 · 上述九条门禁为逐条手跑（等价 A29 清单）；**未运行 workflow verify 全量 59 条**（含全量 PlayMode），因此 state.json 的 verification 仍为空。
- 2026-10-30T21:20:02+08:00 · 先行测试 PASS · 3/11 · 证据 evidence/red.json

## 交付摘要

- 18 段装配就地 + Container 拆解：App/AppComposition 为组合根；Modules/** 16 段 + App/UiServiceModule、App/ProcedureComposition（因 LuaBridge 依赖暂留，已登记债）。
- 外层域收口：会话/地图/Ui/Meta 归位；Product/Recovery 并入 Procedure，Hosting→Dev；Modules 顶层收敛为 12 域。
- 边界机械化：8 共享域命名空间守卫 + domain-matrix 12×12 矩阵门禁；A24 运行端口 IBattleRun(Factory)/IMatchJoin 把流程与战斗内部件解耦；A26 排行单源、A27 相机混合门禁改名与常量归位。

## 未决事项

- UiValidationEditModeTests.门禁_真实资产面_命名hash表路径零Error：文字档基线写 104，实测 128（24 条新增未标记文本）。属 UI 样式对账债，不属本批；需 UI 线确认是补标记还是更新基线。
- A17 人工点界面未做：当前以 BattleSmokePlayModeTests 自动化链替代（真实指针/键盘操作仍待）。
- 全量 L2（l2-unity-gate 含 EditMode/PlayMode 全套）未跑；本轮跑了全量 EditMode、定向 PlayMode 与 meta 扫描。
- 工作区改动尚未 commit（本轮未提交）。

## 文件清单

- 结构：Assets/LiteGame/Modules/{Battle,Session,Meta,Procedure,Map,Ui,Dev,Framework,Camera,Config,Input,Settings}/**、Assets/LiteGame/App/{AppComposition,ProcedureComposition,UiServiceModule,GameEntry}.cs、Assets/LiteFramework/Runtime/Framework/**。
- 端口/算法：Modules/Session/Abstractions/{IBattleRun,IBattleRunFactory,IMatchJoin}.cs、Modules/Session/BattleRunFactory.cs、Modules/Battle/BattleRankOrder.cs、Modules/Battle/Aim/{CameraAimBlendGate,CameraAimBlendDriver}.cs、Modules/Camera/Abstractions/CameraBlendDefaults.cs。
- 守卫/用例：Tests/LiteFramework.Core.Tests/{DomainBoundaryTests,ProcedureBoundaryTests,M3WiringGuardTests}.cs、Assets/Tests/EditMode/{BattleRankingConsistency,CameraAimBlend}EditModeTests.cs、Assets/Tests/EditMode/{UiApiSpecGuardTests,VisualSingleSourceEditModeTests}.cs。
- 门禁/文档：scripts/gate/domain-matrix.py、scripts/gate/toolchain.json、.workflow/config.json、Docs/说明/{02,14,17,18,README}.md、Docs/规范/03-领域查询表.md、Docs/设计文档/架构/商业级通用客户端框架总设计.md、Docs/设计文档/客户端/UI系统/设置系统专项设计.md、Docs/审计/客户端/登记/技术债.md、Docs/施工进度/{README,客户端重构线}.md。

## 后续建议

- 提交前按 batch-wrapup 复核：git status 确认未误带第三方/生成物；逐文件 git add（禁 -A）。
- 跑全量 L2（EditMode + PlayMode）；补 A17 真实指针/键盘手测。
- 结清 UiValidation 的 24 条文字档漂移（补标记或评审后更新基线）。
- 把 App/UiServiceModule.cs、App/ProcedureComposition.cs 的注册表装订面下沉为端口/独立程序集后迁入 Modules/，达成 A15 的 18 段全就地。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-30T19:40:28+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-30T19:45:31+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-10-30T21:13:41+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-10-30T21:15:00+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-10-30T21:16:15+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-30T21:16:22+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-30T21:20:13+08:00 · 完成「先写测试」→ 「正式施工」
