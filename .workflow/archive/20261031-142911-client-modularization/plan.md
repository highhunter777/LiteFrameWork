# 方案：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 交付 A/3
> 证据母本：`.workflow/active/module-boundaries/plan.md`（含 147 文件域审计、403 文件机械扫描、程序集环实测，本文件只引结论不复述）

## 背景与目标

- 现状（实测）：
  1. 13 个 `IClientModule` 实现全在 `App/Bootstrap/Modules/`，与 `Modules/`（15 个功能域目录）形成两处同名"模块"；其中仅 5 段对应产品域，另 8 段装配的是框架能力（无对应域目录）——这是两个目录并存的真实原因。
  2. 按技术层切产品代码：`Modules/Ui`、`Modules/View`、`Modules/Presentation` 三个横向域收纳了本属战斗域的东西（HUD、Fx、音频、Rig、Look、SimView、动画），业务系统无法自持自己的 UI 与表现。
  3. `LiteGame.Core` 混入业务端口（`Core/Battle/{IBattleView,IBattleReadModel,IMatchIdentity}`），与"只放基础设施与工具"定位冲突。
  4. 装配粒度粗（`Clocks` 顺带产 `EventBus`；`Presentation` 一段同时造 HUD 与世界表现；`ContainerModule` 承担 11 域编排且冒充第 13 个模块），并有唯一隐性反向依赖（`UiService` 懒取 `Container` 的 `FeedbackService`）。
  5. 域间耦合无机械约束：机械扫描查出 10 条真实违规（含 `Procedure→Battle/Meta` 8 条、`Ui` 引战斗具体类、契约放错位置）。
- 目标（可观察）：
  - 四类模块 + 五条依赖规则落地为守卫（`DomainBoundaryTests` / `CompositionBudgetTests` / `ModuleDependencyDeclTests`），违规可红。
  - `LiteGame.Core` 只含基础设施与工具；业务端口全部住各业务域 `Abstractions/`。
  - `Modules/Battle` 成为完整战斗垂直切片（Match/Arena/Characters/Combat/Aim/Hud/Settlement/Setup/Commands）；`Modules/View`、`Modules/Presentation` 不再存在。
  - `App/Bootstrap/Modules/` 不再存在；装配段就地 `<域>Composition.cs`，且不 `new` 任何适配器。
  - `ContainerModule` 不再是 `IClientModule`；`Clocks` 不再产 `EventBus`；`ModuleDependencyDeclTests.Baseline` 为空。
  - Unity 重编译 0 error、L1 全量通过、Boot Play 冒烟通过、`wf verify` 九条门禁全绿。

## 需求澄清与假设

- 已确认答复（用户原话要点）：
  1. "B"（域实现归位）+"Composition"（目录命名）；装配段文件名 `<域>Composition.cs`、去掉 `Module` 后缀；
  2. "F 走方案 a"（`ClientHost` 装配末段钩子，组合根改普通类）；
  3. "不能把 bootstrap 里的模块目录和外层的合并吗……一个模块就是一个功能模块"；"合并 + 拆分一次性做到位"；
  4. **"core 不能沾染业务啊，只能放基础设施和工具"**；**"战斗系统有自己的 ui 和表现，未来什么局外背包系统、活动系统，难道全把 ui 和表现代码堆到那些表现程序集吗"**；**"我们的模块划分要竖向的，要高内聚低耦合的，模块化的"**；
  5. "直接把 Presentation 拆了，还有 ui 和表现不应该是功能模块自持吗"；
  6. **"怎么还有 View,Presentation 这些意义不明的东西"** → 域内子目录按职责命名，禁 `View`/`Presentation`/`Domain`/`Logic`；定案 `Match`/`Arena`/`Setup`；
  7. "Map / Room 按你的来"；"新建 `Modules/Session/`"；"瞄准双事实源按你的来"。
- 采用假设：
  - 类名保留 `XModule`，文件名 `<域>Composition.cs`。
  - **业务域按需独占程序集**：`LiteGame.Modules.Battle` 吸收 `LiteGame.Modules.View` + `LiteGame.Presentation`（三者并一）；`Meta`/`Session` 暂住 `LiteGame.Modules`，边界由命名空间 + `DomainBoundaryTests` 承担，后续需要编译隔离时再拆（拆时同步允许表）。
  - 外层域用"拆命名空间"取得可校验边界，不拆程序集。
  - `Core/Dev`（开发基础端口）与 `Core/View`（纯计算）留在 Core。
- 假设影响：若要求域内三层各一程序集，批 2 增加 2 个 asmdef 与允许表条目，结构等价。
- **修订（第五轮评审后）**：`Camera` 由"纯契约域"升为"契约 + 薄段"——`PresentationModule` 现在同时产主相机/相机服务/租约/实体/音频/VFX，拆分后主相机必须早于 `Ui`（启动页投影面）与 `Input`（瞄准相机），相机服务必须早于 `Procedure`；因此 `CameraComposition` 落 ②（新段，+1）。`Map` 亦升为有段域（+1）。总段数 13 → 18（批 1 14 / 批 2 15 / 批 4 18）。

## 范围与非目标

- 范围内：批 1 装配段演化、批 2 业务域垂直化、批 3 `IConfirmDialog` 端口、批 4 组合根、批 5 外层域边界（Session 建域 / Map 充实 / Ui 纯化 / Meta 契约归位 / 命名空间收口 / 口径单源 / 瞄准改名），以及全部守卫与表同步。
- 非目标：
  1. 不把 `IClientModule` 实现搬进 `LiteFramework`；不把框架装配段放进 `LiteGame.Core`（硬边禁止）。**允许的框架改动白名单（只读投影/装配钩子，不新增业务语义）**：`FrameworkInfrastructure.Create`（扩 `IMainThreadDispatcher`/`CommandCenter`）、`Clocks.Create`、`Events.Create`、`ClientHost.ComposeRoot`（含为 `ComposeAfter` 开放非模块装配窗口——`ModuleContext.Put` 在模块窗口关闭后会抛）、`VfxService.ActiveCount`（框架文件、战斗域持有）。
  2. 不新增任何横向 UI/表现程序集；`Modules/Ui` 只保留跨业务共享的 UI 能力。
  3. 不改"具体键 vs 接口键"的产物暴露面（独立议题）。
  4. 不动 `Bootstrap/Probes`、`Bootstrap/Fallbacks`；不清理既有 `CS0105`（本批实质修改的文件除外）。
  5. 本轮不建局外背包/活动域，但目录与程序集口径为其预留。
  6. 同步层去耦、判定参数化、生成器收口 → 属交付 B/C，不在本运行。

## 现状与约束

- 关键代码事实（详见母本 §12.2 全量扫描）：
  - `GameEntry.RegisterModules` 注册 13 个模块（注册序＝初始化序＝关闭逆序）；`Bootstrap` 内无类定义。
  - `ModuleDependencyDeclTests.Baseline` 当前含 `UiServiceModule <- ContainerModule`；`ClocksModule.DependsOn` 已补 `PlatformInfrastructureModule`。
  - 硬编码 `Bootstrap/Modules` 目录：**4 个文件硬编码目录 + 6 个文件 7 处需改**（`ModuleDependencyDeclTests`、`ModuleScopeAdoptionTests`、`ArchitectureBudgetTests`（`ContainerModule.Assemble` = 152 行）、`M3WiringGuardTests`×2、`IdentityEntryGuardTests`、`DisciplineScanner` 白名单）。
  - `LiteGame.Core.asmdef`：`references:["LiteSim"]`、`noEngineReferences:true`，硬边禁引 `LiteFramework.*`/`LiteGame.*`/`LiteNet`/`RoomServer.*`（且被 L1 源链接编译）。
  - 真违规 10 条：`Ui/ResultRanking.cs→PlayerIdentity`、`Ui/PageFlow.cs→ILoginSurface`、`Procedure→Battle::{BattleClient,BattleContext,EndReason,TeleportToAimCommand}`、`Procedure→Meta::{MetaEntryService,MetaEntryConfig}`、`Procedure→Room::RoomSessionFace`；`Room→BattleClient` 登记为允许边。
- **开工基线（工作区 WIP，非 HEAD）**：`HEAD=9176b409`，另有 2026-10-30 17:33–17:39 的未提交准备性改动（已实测 Unity 编译 0 error 0 warning），本方案在此基线上继续（用户裁决）。相对 HEAD 的既有变化，方案必须按此口径理解：
  - `Modules/Battle/` 已拆出 12 个文件（含 `BattleClient`/`BattleContext`/`SnapshotMirror`/`MatchWorldFactory`/`PlayerIdentity`/`GameplayMatchIdentity`/`Abstractions/IBattleDevDiagnostics`），`Modules/Battle/Commands/` 已存在。
  - `Modules/View/{Abstractions,Feedback/Abstractions}`、`Modules/Procedure/Abstractions`、`Modules/Room/{Abstractions,RoomSessionFace.cs}`、`Modules/Product/Abstractions`、`Modules/Settings/Look/`、`Modules/Ui/{UiPageResolver.cs,LuaUIData.cs,Generated/LuaKeys.g.cs}` 已存在。
  - `RoomBridge.cs`/`ProductBridges.cs` 已移至 `Adapters/LuaBridge/`；`ModuleDependencyDeclTests.cs` 已新增。
  - **`LiteGame.Modules → LiteGame.Presentation` 与 `→ LiteGame.Adapters.LuaBridge` 两条边已在 WIP 中删除**（`Adapters.LuaBridge` 反向引用 `LiteGame.Modules` 实现端口）；`Modules.View` 已引用 `Modules.Battle` 与 `Modules.Input`。
- 约束（程序集环，WIP 实测两条仍成立）：
  - `Adapters.Unity → Modules.Input / Modules.Camera`、`Adapters.Luban → Modules.Config` ⇒ 域装配段不得 `new` 适配器，适配器选择上收 `AppComposition`。
  - `Modules.Presentation` 引用 `LiteGame.Adapters.Unity`（`Battle/Rig/BattleViewAssets.cs`、`BattleViewSceneAssembler.cs` 用其根命名空间 `LiteGame.View.Animation`）⇒ 批 2 合并后 `LiteGame.Modules.Battle` **必然保留**这条边，须登记进允许表；"换适配器只改 `AppComposition`"只对"创建点"成立，不含表现层复用框架动画/相机内核。
  - HUD 不得并入 `Modules/Ui`（`Modules/Ui` 属共享能力程序集，HUD 是战斗域职责）。
- 工程约束：`Assets/` 下移动必须走 `AssetDatabase.MoveAsset`（`.meta`/GUID）；C# 9；`Nullable=disable`；禁用文件级命名空间；不触 BuildHash 共享源集；`run_shell_command` 不可用（`python`/`dotnet` 经 Unity 进程侧 `Process.Start`）；复杂度预算（方法 ≤ 80 行、类 ≤ 400 行）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| V1 | 业务域垂直自持：战斗域吸收 View/Presentation/Hud；一业务域一程序集 | 新业务可整块复制形态；共享层不被业务污染 | 三 asmdef 合并、搬迁量大 | **采用** |
| V2 | 保留横向 `Ui`/`View`/`Presentation` | 零搬迁 | 业务 UI/表现持续堆进共享程序集 | 否决 |
| V3 | 垂直目录 + 域内三层各一 asmdef | 保留编译隔离 | asmdef +2、允许表更复杂 | 备选 |
| M1 | 装配段就地 `<域>Composition.cs`；框架接线集中 `Modules/Framework/`；适配器上收 | 一目录=一域；域代码零适配器依赖 | 装配段改接端口 | **采用** |
| M2 | 就地但装配段自 new 适配器 | 变化小 | `Input`/`Config` 成环 | 否决 |
| C1 | 框架提供 `FrameworkInfrastructure/Clocks/Events.Create` 入口（不实现 `IClientModule`） | 构造正确性收回框架 | 动框架 | **采用** |
| B1 | `IConfirmDialog` + `ConfirmDialogRouter` 后绑定 | 顺序合法、Baseline 清空 | 2 个小文件 | **采用** |
| F1 | `ClientHost.ComposeRoot` + `AppComposition` 普通类 | 组合根不再是模块 | 改框架 + 引导用例 | **采用** |
| G1 | 共享域拆命名空间 + `DomainBoundaryTests` | 边界可校验 | 大量 `using` 增补 | **采用** |
| S1 | 新建 `Modules/Session/` | 会话所有权归位 | 共享程序集内新增目录 | **采用** |

推荐：V1 + M1 + C1 + B1 + F1 + G1 + S1。贯穿原则：**业务域自持 Domain/Ui/Presentation；共享层只放能力；装配段就地但只认端口。**

## 设计要点

### 四类模块与依赖规则（守卫化）

| 类 | 成员 | 规则 |
|---|---|---|
| 业务域（竖向） | `Battle`、`Meta`、`Session`、（规划）`Inventory`、`Activity` | 一目录 + 一装配段 + `Abstractions/` + 按职责命名的子目录；自带 UI 与表现；**编译隔离按需**——需要独立 asmdef 时才建并登记允许表（`Battle` 独占程序集；`Meta`/`Session` 暂住 `LiteGame.Modules`，边界由命名空间 + `DomainBoundaryTests` 约束） |
| 应用壳 | `Procedure`（阶段 + 页面流 + 加载遮罩 + 常量） | 只编排，不认识业务内部 |
| 共享能力 | `Ui`（页面目录/寻址/Lua UI 数据）、`Input`、`Settings`、`Config`、`Camera`、`Map`、`Dev` | 只提供能力，不含业务 |
| 框架接线 | `Framework/`（8 段） | 只调框架装配入口 |

依赖规则：① 业务域→共享能力只经 `Abstractions/`；② 共享能力✗→业务域（除"低层定义端口、高层实现"）；③ 业务域之间✗直连（除已登记允许边）；④ 域内 `Ui/Presentation → Domain → Abstractions` 单向；⑤ 唯一例外 `AppComposition`。

**命名规则（防回潮）**：域内子目录必须表达职责（`Match`/`Arena`/`Characters`/`Combat`/`Aim`/`Hud`/`Settlement`/`Setup`），禁止 `View`/`Presentation`/`Domain`/`Logic`；唯一保留技术名 = `Abstractions/`。

### 目标目录（交付 A 完成后）

```
Assets/LiteGame/
├─ Core/            Dev/（IDevSession·ILocalTestHooks）· View/（纯计算）
├─ App/             GameEntry.cs · AppComposition.cs · Bootstrap/{Probes,Fallbacks}/
└─ Modules/
   ├─ Framework/    8 段框架接线（PlatformInfrastructure/Identity/DevSession/Content/
   │                Events/Clocks/Schedulers/LuaHost + 各自 Composition）
   ├─ Ui/           仅 UI 能力：UiComposition·UiPageResolver·LuaUIData·Generated/
   ├─ Input/        InputComposition（接 IIntentSource 端口）
   ├─ Settings/     SettingsComposition
   ├─ Config/       ConfigComposition（接 Func<IConfigService> 端口）
   ├─ Camera/       契约（端口 + 数学）+ **薄段 CameraComposition**（主相机保证 + 相机服务，适配器由工厂注入）
   ├─ Procedure/    应用壳：Stages/ · PageFlow.cs · LaunchLoading.cs · ProcedureComposition
   ├─ Session/      AccountSession(Factory)·MatchSession(Factory)·RoomSessionFace·SessionComposition
   ├─ Meta/         Abstractions/ · Wire/ · Ui(MetaBridge) · MetaComposition
   ├─ Map/          MapComposition · MapBoundsMarker · MapTopology · BattleScenes · MapProvider
   ├─ Battle/       【垂直自持】Abstractions / Match / Arena / Characters / Combat /
   │                Aim / Hud / Settlement / Setup / Commands + BattleComposition
   └─ Dev/          开发装配缝（DevHooks + Null 实现）
```

装配段 **18** 个（`AppComposition` 不计）；拆 `Product`/`Recovery`，`Hosting`→`Dev`。注册序（批 4 后，**按"谁在 `InitializeAsync` 里 `Require` 谁"排出的全序**）：`①PlatformInfrastructure ②Camera ③Identity ④DevSession ⑤Content ⑥Settings ⑦Events ⑧Clocks ⑨Schedulers ⑩LuaHost ⑪Config ⑫Map ⑬Ui ⑭Input ⑮Meta ⑯Battle ⑰Session ⑱Procedure`；拆除＝逆序。

- `Camera` 升为薄段（②）：现状 `PresentationModule` 同时建"主相机 + CameraService + 租约/实体/音频/VFX"四域，拆分后**主相机与相机服务**必须早于 `Ui`（启动页要有投影面）与 `Input`（瞄准相机注入）。
- `Input` 不再在初始化期取相机：`_source.SetAimCamera(...)` 的调用移到 `ComposeAfter`（经 `IAimCameraSink`/`IRenderCameraSource` 能力端口），对局内仍由 `BattleViewRig` 用渲染相机二次注入。
- `Battle` 自持表现内核（`PrefabLeaseCache`/`EntityService`/`AudioService`/`VfxService` + 视图/动画/HUD/反馈）——它们只被战斗与设置装配消费，属战斗域。
- `Settings` 产出 `GameSettings`/`SettingService`/**`LookPreferences`（单实例）**：**接口保持只读**（`ILookPreferences` 不加 `Refresh`），`Refresh()` 留在具体类；`SettingsComposition` 建一个 `LookPreferences` 并同时 `Put` 具体键（供 `ProductSettings` 刷新）与 `ILookPreferences` 键（供 `Battle` 只读），`ProductSettings` 构造改收**具体 `LookPreferences`**（不再自建）——否则设置页改灵敏度后对局不生效（静默行为退化），且"表现件不写偏好"仍可机械判定。
- `Session` 在 `Battle` 之后：`MatchSessionFactory` 构造 `BattleContext`/`BattleClient`（Session→Battle 是实测方向）。
- `Procedure` 最后：`StageMachine`/`ProcedureBattle` 消费 Meta/Session/Battle/Ui/Map 的产物。
- `Map` 独立成段（`MapProvider` 依赖 `IConfigService`，`BattleScenes` 被流程消费）；先前"Map 无装配段"的口径作废。
- **传输工厂端口**：`Session/Abstractions/ITransportFactory`（`MatchTransport Create(bool testRoom)`），实现放 `AppComposition`（它可引用 Procedure/Meta 部署配置）；`SessionComposition` 只接端口。
- `AppComposition` 的两种角色：①适配器**工厂**（`GameEntry` 在 `InitializeAsync` 前调用并把产物经构造注入各段）；②`ComposeAfter(ModuleContext)`（全部段成功后、`SealAssembly` 前）做跨域装订与迟注册。

### 能力端口（消掉"表现层认具体适配器"）

现状 4 处 `is` 具体适配器（批 2 后仍会落进 `Modules/Battle/**`，属 A4 检查面），沿用既有 `IAimWorldSink` 范式——**可选能力接口 + 消费方 `is` 能力接口**，实现留在适配层：

| 现状调用点 | 现状判据 | 改成 | 端口住 | 实现者 |
|---|---|---|---|---|
| `Battle/Rig/BattleViewRig.cs:169` | `_camera is CinemachineCameraService ccs && _input.Source is NewInputIntentSource source` | `_camera is IRenderCameraSource cam && _input.Source is IAimCameraSink sink` | `Modules/Camera/Abstractions/`、`Modules/Input/Abstractions/` | `CinemachineCameraService`、`NewInputIntentSource` |
| `Battle/Feedback/BattleFeedbackAssembler.cs:189` | `_camera is CinemachineCameraService ccs` | `_camera is IRenderCameraSource cam` | 同上 | 同上 |
| `Battle/Look/BattleLookController.cs:134,142` | `input.Source is NewInputIntentSource source`（取 `LookDelta`/`ShoulderSwapPressed`） | `input.Source is ILookDevice device` | `Modules/Input/Abstractions/` | `NewInputIntentSource` |

- `IRenderCameraSource { void EnsureCamera(); Camera TryGetRenderCamera(); }`（`Modules.Camera` 已 `noEngineReferences:false`，可为 `Camera`）。
- `IAimCameraSink { void SetAimCamera(Camera camera); }`、`ILookDevice { Vector2 LookDelta { get; } bool ShoulderSwapPressed { get; } }`（与 `IAimWorldSink` 同文件同范式）。
- 行为等价：未实现端口的设备源/相机仍返回 null/跳过——与当前 `is 具体实现` 的退化路径一致（测试替身继续可用）。
- 守卫：`Modules/**` 不出现 `is CinemachineCameraService` / `is NewInputIntentSource`（A4 改为可搜的端口名）。

### 批 4 注册归属（`ContainerModule` 拆解映射，段数 15 → 18）

归属按**改动动机**分类，且必须满足"段 A `InitializeAsync` 里 `Require` 的产物由更早的段产出"（位置为批 4 后的 18 段序）：

| 现注册项（`ContainerModule`） | 去向 | 依据 |
|---|---|---|
| `_scenes = new SceneService()`、`IContentCacheMaintenance`、`ActivationTransactionStore` 相关 | **`ContentComposition`**（⑤） | 场景与缓存租约都走内容包 |
| `IMainThreadDispatcher`、`CommandCenter` + 命令注册 | **`PlatformInfrastructure`**（①，经 `FrameworkInfrastructure.Create`） | 框架基础设施；`CommandCenter` 只依赖 `IReferencePool` |
| `IMapProvider`（`new MapProvider(config)`）、`BattleScenes` | **`MapComposition`**（⑫） | 依赖 `IConfigService`（⑪） |
| `DialogService`/`FeedbackService`/`ToastTicker`、`DotweenUiClockDriver`、`UiLuaRegistry` 等 | **`UiComposition`**（⑬） | 纯 UI 能力；`ServiceOwnership` 由 `ContainerOwned` 改段内 `Scope.Register` |
| `IInputService`/设备源 | **`InputComposition`**（⑭，设备源由 `AppComposition` 工厂产出） | 输入能力；相机注入改 `ComposeAfter` |
| `MetaEntryService`/`MetaEntryConfig`、`accountDeviceId` 闭包 | **`MetaComposition`**（⑮） | 局外业务 |
| `PrefabLeaseCache`/`EntityService`/`AudioService`/`VfxService`、`BattleViewGroupFactory`、战斗装配件 | **`BattleComposition`**（⑯） | 战斗域自持表现 |
| `IAccountSessionFactory`/`IMatchSessionFactory`、`RoomSessionFace`、`IAccountSessionControl` 实现、`gameplayDigestBase` 闭包 | **`SessionComposition`**（⑰）；`transportFactory` 经 `ITransportFactory` 端口注入 | 会话所有权 |
| `StageMachine`、`PageFlow`、`LaunchLoading`、`ErrorRecoveryService`、`AppRecoveryActions`、`IAccountReentry` | **`ProcedureComposition`**（⑱） | 应用壳 |
| `Bridge.Bind*` 跨域装订、`metaEntry.BindMachine(fsm)`、`ProductSettings`（创建 + `Bind` + `BindGlobals`；**构造注入 Settings 段的同一 `ILookPreferences` 实例**，不再自建）、`LuaRegistryRefillService`、`ui.RegisterModal`×2、`identity.BindContent`、`IInputService.RegisterBlocker`、`IInputService` 的瞄准相机迟绑定、`Factory<ITransportFactory>` 实现、`DevHooks.Decorate(...)` + `_devDecoration.Dispose()`、`DevHooks.WrapTransportFactory(...)`、`ProductBridges`/`RoomBridge` | **`AppComposition.ComposeAfter`**（非段） | 跨域连线/开发装饰，组合根唯一例外 |
| `EventBus` | `EventsComposition`（⑦） | 批 1 已独立 |
| `SetTickOrder(...)`（**13 项**）与 `_services.Seal()` | **单点声明**：`SetTickOrder` 在 `AppComposition.ComposeAfter` 一次性按完整 13 项声明（段只 `Put` 实例）；`Seal()` 同处最后一步 | 分段落声明会按"段注册序"重排全局相对序 = 行为变更 |
| `context.Scope.Register(new DelegatedDisposable(DisposeModuleResources))`（命令注销 → services.Dispose → scenes.ReleaseAll） | 拆成各段自己的 `Scope.Register`（命令中心/PI、容器、场景/Content、战斗表现/Battle）；`AppComposition` 用根 scope 登记自身释放 | 非模块拿不到 `ModuleContext.Scope`，租约必须由拥有方登记 |

- **`SetTickOrder` 期望序（13 项，A15a 的比对基准）**：`IMainThreadDispatcher` → `IWorldClock` → `IUIClock` → `DotweenUiClockDriver` → `UIService` → `ILogicScheduler` → `IUIScheduler` → `ITimelineRunner` → `IVFXService` → `DialogService` → `ToastTicker` → `ICommandCenter` → `StageMachine<>`。

### 薄装配判据（守卫化）

允许：`Require/Get`、调框架 `Create`、`Put`/`RegisterInstance`/`Scope.Register`、`DependsOn`、释放逻辑、一眼可读的形态选择。
禁止：手拼框架构造参数、`for/while/foreach`、域内业务算法、跨域业务连线（组合根除外）、`new` 适配器、`is/强制转换具体适配器类型`（改经能力端口）。
**允许的 `new` 清单（封闭，扩项须登记）**：`PlatformInfrastructure`/`Clocks`/`Events` 走框架 `Create`；`SettingsComposition` 可 `new GameSettings`/`SettingService`/`LookPreferences`（本域产物）；`ContentComposition` 可 `new SceneService()`；`AppComposition` 可 `new` 三个适配器（相机/输入/配置）。其余装配段一律不得 `new` 服务实现。

### 批次与子步

| 批 | 内容 | 段数变化 | 子步 |
|---|---|---|---|
| 1 装配段演化 | 框架入口 → **12 段就地** `<域>Composition.cs` + `Events` 独立 → 门面瘦身 → 适配器上收 + 能力端口 → 守卫/表同步；`ContainerModule.cs` 移 `App/`（仍是 `IClientModule`） | 13 → 14 | T1 框架入口、T2 搬迁、T3 Events、T4 门面瘦身、T5 适配器上收 + 能力端口、T6 守卫 |
| 2 业务域垂直化 | `View`+`Presentation`(+`Hud`) → `Battle/{Match,Arena,Characters,Combat,Aim,Hud,Settlement,Setup,Commands}` + 表现内核归 Battle；**相机拆出为 `CameraComposition`**；三 asmdef 合一；`Core/Battle` 端口下沉 | 14 → 15 | T1 端口下沉、T2 搬迁、T3 asmdef 合并、T4 引用者、T5 冒烟 |
| 3 端口 | `IConfirmDialog` + `Router`，Baseline 清空 | 15 | — |
| 4 组合根 | `ContainerModule` 拆成 `Procedure`/`Session`/`Meta` 三段 + 新增 `MapComposition` + `AppComposition`（`ClientHost.ComposeRoot` 驱动）；Container 不再实现 `IClientModule` | 15 → 18 | T1 Host 钩子、T2 拆段、T3 装配迁移、T4 用例 |
| 5 外层域边界 | Session 域细化（会话件实际迁入）、Map 充实、Ui 纯化、Meta 契约归位、命名空间收口、口径单源、瞄准改名 | 18 | G1~G5 |

## 通用性与扩展性

- 根因：按技术层切产品代码，业务系统无法自持 UI/表现，共享层被迫成为业务垃圾场；域边界只由目录承担。
- 扩展点：新增业务域 = `Modules/<域>/{Abstractions + 按职责命名的子目录}` + `<域>Composition.cs` + 一行 `RegisterModules`（+ 需要独占程序集时改 `LiteGame.Modules.asmdef` 引用与 `ClientAsmdefAllowlistTests` 一行）；不改任何共享能力域代码。新增框架能力 = 框架加 `Create` 入口 + 装配段一行；换适配器 = 只改创建点（`AppComposition`），消费方经能力端口不受影响。
- 判据固化：业务域 = 目录 + 装配段 + `Abstractions/`（程序集按需）；共享层只放能力；纯契约域不需装配段；装配段只认端口（不认具体适配器）。

## 测试策略

- L1：`ModuleDependencyDeclTests`（扫描面 = `Modules/**/*Composition.cs` ∪ `App/**/*Composition.cs` ∪ 过渡期 `App/ContainerModule.cs`）、新增 `CompositionBudgetTests`（段数清单 + 行数预算 + 零循环）、新增 `DomainBoundaryTests`（四类模块 + 五条规则；首版 `Baseline` = 10 条真违规；批 1 首跑对 `Modules/View`/`Modules/Presentation`/`Core/Battle` 存在判红）、`ModuleScopeAdoptionTests`（`Required` 键重写）、`ArchitectureBudgetTests`（152 行键随迁）、`ClientAsmdefAllowlistTests`。
- EditMode：`ClientCompositionTests` 风格覆盖 `ComposeRoot` 成功/失败回滚/取消；HUD/相机/动画面。
- PlayMode：新增 `BattleSmokePlayModeTests`（A12 判据）；Play 冒烟另跑 Boot 起服（段数、主相机、日志落盘、事件总线）。
- 门禁：批 5 新增 `scripts/gate/domain-matrix.py` 并接入 `.workflow/config.json` 的 `commands`（`--check`），A28 用它钉住 12×12 矩阵；批 2 新增 `battle-smoke-playmode` 条目（A12）。两处 config 变更**先于**当批取红/取绿执行（`verify` 指纹按命令集计算，改配置会使旧证据过期）。
- 手测：登录 → 开房 → 进对局 → 退出（批 4 必做）。
- 先失败后通过：新守卫先写并在旧结构跑到断言失败（`wf verify --red` 留证）。`.workflow/config.json` 的 `red_commands` 追加三条只跑新 fixture 的 L1 过滤器：`FullyQualifiedName~ModuleDependencyDeclTests`、`FullyQualifiedName~CompositionBudgetTests`、`FullyQualifiedName~DomainBoundaryTests`；取红时机——`CompositionBudgetTests` 在批 1 的 T2（搬迁）前跑：断言"13 个 Composition 存在 + `App/Bootstrap/Modules` 不存在"，搬迁前实测 0 个 Composition 且目录存在 → 红；`DomainBoundaryTests` 的"路径不存在"断言在**批 2 开头**取红（`Modules/View`、`Modules/Presentation`、`Core/Battle` 仍存在）、批 2 末转绿；`ModuleDependencyDeclTests` 的 Baseline 清空在批 3 取红（改 Baseline 前用例对"欠账已消"判红）。

## 验收标准

**批 1（装配段演化）**
- [ ] A1：`App/Bootstrap/Modules/` 不存在；`Modules/**` 下有 **13** 个 `<域>Composition.cs` = `Framework/` 8 个（`PlatformInfrastructure`/`Identity`/`DevSession`/`Content`/`Clocks`/`Schedulers`/`LuaHost` 七个迁移 + 新增 `Events`）+ `Ui/`/`Config/`/`Presentation/`/`Input/`/`Settings/` 各 1；`ContainerModule.cs` 已移至 `App/` 且仍 `: IClientModule`（批 4 拆解）。段数账：12 就地迁移 + 1 新增 Events + 1 Container（App/）= 14 段。**迁移段所在 asmdef 的引用集按编译结果补齐并同步 `ClientAsmdefAllowlistTests`**（已实测首例：`Modules.Input` 需加 `LiteGame.Core`——`Func<IDevSession, IIntentSource>` 用到 `IDevSession`）。
- [ ] A2：`App/Bootstrap` 只剩 `Probes/`、`Fallbacks/`，内容零改动（`git diff` 为空）。
- [ ] A3：`LiteFramework` 提供 `FrameworkInfrastructure.Create`/`Clocks.Create`/`Events.Create`；`Modules/**Composition.cs` 不再出现 `new ReferencePoolService(`/`new LogService(`/`new FileSystem(`/`new WorldClock(`/`new EventBus(`。
- [ ] A4：**生产代码**（`Assets/**`，`Assets/Tests/**` 的测试装配直建不受约束）里 `new CinemachineCameraService`/`new NewInputIntentSource`/`new ConfigService` 只出现在 `Assets/LiteGame/App/AppComposition.cs`（工厂方法），`Modules/**` 不出现这三个 `new`、也不出现 `is CinemachineCameraService`/`is NewInputIntentSource`；调用链写死（**避免 `Modules.* → App` 环**）：`AppComposition` 暴露**工厂委托**——`Func<IContentService, IConfigService>`、`Func<IDevSession, IIntentSource>`、`Func<ICameraService>`；`GameEntry` 构造各段时把委托**注入构造参数**，各段在自己的 `InitializeAsync` 里调用（`Config`/`Input`/`Presentation`），`new` 只出现在 `AppComposition` 方法体内；**段不得直接调用 `AppComposition` 的静态方法**（`Modules.Config`/`Modules.Input`/`Modules.Battle` 不引 `LiteGame.App`，反向引用成环）；也不前置创建设备源（`matchRules` 读口依赖 `IDevSession` ③，须段内取）；批 4 起同一文件的 `ComposeAfter` 参与 `ComposeRoot`；`InputComposition.DependsOn` 去掉 `PresentationModule`（只留 `DevSessionModule`），瞄准相机改为 `ComposeAfter` 经端口迟绑定（**批 1–3 过渡落点**：`ContainerModule.Assemble` 在 `Seal()` 前调用 `AppComposition.ComposeAfter(context)`；批 4 原样迁到 `ClientHost.ComposeRoot`。可判据：初始化完成、首次 `Sample` 前设备源 `Camera == null`（不得为 `Camera.main`）；`ComposeAfter` 后变为 `Camera.main`；4 处判据改经能力端口（`IRenderCameraSource`/`IAimCameraSink`/`ILookDevice`）。豁免面显式登记：`Assets/Tests/EditMode/{TpsAimPointEditModeTests,TpsCameraLookEditModeTests,ConfigSnapshotEditModeTests,MapProviderEditModeTests}.cs`。
- [ ] A5：`EventsComposition` 独立产出 `EventBus`；`ClocksComposition` 不再 `new EventBus`。
- [ ] A6：**6 个文件 / 7 处**守卫路径全部更新（下列**行号仅作参考**，锚点以符号/断言串为准，同批改动会使行号漂移）——`ModuleDependencyDeclTests`（`ModulesRelative` 常量）、`ModuleScopeAdoptionTests`（`Required` 表 + 目录拼接）、`ArchitectureBudgetTests`（`ContainerModule.Assemble` 152 行键：批 1 按 A16 拆成 4 个 ≤80 行方法后**删除该键**，规则③要求降阈值内必须删；批 2 另随迁 **3 个键**到 `Modules/Battle/**`——`ClassCaps` 的 `CharacterLocomotionDriver`(524)、`MethodCaps` 的 `CharacterLocomotionDriver.Tick`(177) 与 `BattleDamageNumberDriver.Tick`(82)；先删旧键再加新键，任一漏改即 stale 红）、`M3WiringGuardTests.cs:52/65`、`IdentityEntryGuardTests.cs:16`、`DisciplineScanner.cs:225`。四条配套改写：①`ModuleScopeAdoptionTests` 改"双根基 + 键→根基映射"（`Modules/**` 找 `<域>Composition.cs`，`App/` 找过渡期 `ContainerModule.cs`），`Required` 键在批 1 改为 `InputComposition.cs`/`ContainerModule.cs`/`UiComposition.cs`/`PresentationComposition.cs`/`LuaHostComposition.cs`，**批 2 再把 `PresentationComposition.cs` 改 `BattleComposition.cs`，批 4 删 `ContainerModule.cs` 键**；②`ModuleDependencyDeclTests` 扫描面 = `Modules/**/*Composition.cs` ∪ `App/**/*Composition.cs` ∪ 过渡期 `App/ContainerModule.cs`，**`App/AppComposition.cs` 排除在 `DependsOn` 覆盖之外**（只参与"产物不得重复登记"检查）；③复核 `PageDefinitionGuardTests`/`VisualSingleSourceEditModeTests`/`CheatBoundaryGuardTests` 扫描根；④**模块身份改由类声明提取**（`class XxxModule` / `sealed class XxxComposition`），不再用"文件名=类名"（现 `Parse` 以 `Path.GetFileNameWithoutExtension` 建身份，改名后 `DependsOn => typeof(PlatformInfrastructureModule)` 会被判"不存在的模块"）；文件→类回退保留并加一条解析器自测。**取红/取绿口径**：`CompositionBudgetTests` 在 T2 搬迁前用 `wf verify --red` 单独取红（0 个 Composition + `App/Bootstrap/Modules` 存在），搬迁后转绿；批末"L1 全绿"指**迁移完成后**的全量，不把故意红的守卫混进批末门禁；`DomainBoundaryTests` 的"路径不存在"断言在**批 2 开头**（搬迁前）取红、批 2 末转绿。Unity 重编译 0 error。

**批 2（业务域垂直化）**
- [ ] A7：`Assets/LiteGame/Core/Battle/` 不存在；`Modules/Battle/Abstractions/` 含 `IBattleView`/`IBattleReadModel`/`IMatchIdentity`/`IBattleViewGroup`/`IBattleViewGroupFactory`/`IBattleDevDiagnostics`；`LiteGame.Core` 只剩 `Dev/`+`View/`，asmdef 不变。
- [ ] A8：`Modules/View`、`Modules/Presentation` 目录不存在；内容按职责落在 `Modules/Battle/{Match,Arena,Characters,Combat,Aim,Hud,Settlement,Setup,Commands}`；两个 asmdef 删除；`LiteGame.Modules → LiteGame.Presentation` 边保持删除（WIP 已删，须复核不回潮）。
- [ ] A8b：`Modules/Camera/CameraComposition.cs` 新建（主相机保证 + `ICameraService`，`CinemachineCameraService` 由 `AppComposition` 工厂注入），位置在 `Ui`/`Input` 之前；`PresentationComposition` 改名为 `BattleComposition` 并保留表现内核（`PrefabLeaseCache`/`EntityService`/`AudioService`/`VfxService`）。
- [ ] A9：`Modules/Battle/` 下不出现 `View`/`Presentation`/`Domain` 目录名（守卫钉死）；全仓 `Modules/**` 不再有这三个词作域或职责目录名。
- [ ] A10：`LiteGame.Modules.Battle` 为战斗域唯一程序集；`ClientAsmdefAllowlistTests` 全绿（引用者已改）。
- [ ] A10a：合并后的 `LiteGame.Modules.Battle` 引用集逐条登记进允许表——`LiteGame.Modules.Config`/`Modules.Settings`/`Modules.Input`/`Modules.Camera`/`LiteGame.Adapters.Unity`/`LiteGame.Core`/`LiteFramework.Runtime`/`LiteNet`/`LiteSim`/`Luban.Runtime`/`Luban.Tables`/`UniTask`/`Unity.TextMeshPro`；`ClientAsmdefAllowlistTests` 同时删 `Modules.View`、`LiteGame.Presentation` 两行，并改所有引用者行（`App`/`Modules`/`Modules.View` 反向/`Adapters.Dev`/`Tests`/`Editor`/**`Assets/Tests/UI/PlayMode/LiteGame.UI.PlayModeTests.asmdef`；**同批**新建 `Modules/Battle/AssemblyInfo.cs`：friend 集合 = 现有 `Modules/Presentation/AssemblyInfo.cs` 的 4 条（`LiteGame.App`/`LiteGame.Modules`/`LiteGame.Adapters.Dev`/`LiteGame.EditModeTests`）**+ `LiteGame.UI.PlayModeTests`**，并删除 Presentation 那份）。**硬边口径同批改**：`ClientAsmdefAllowlistTests` 的 `硬边_反向与跨层引用一律失败` 现对 `LiteGame.Modules.Battle` 执行 `ForbidPrefix("LiteGame.Adapters.")`——**改前该 case 对现 `Modules.Battle` 为绿（它未引适配器），合并后必然红**，故同批改 case：Battle 仅放行 `LiteGame.Adapters.Unity`，其余 `LiteGame.Adapters.*` 仍禁，注释改为"表现内核对框架动画内核的唯一例外（已登记）"（**只改 `Allowed` 字典不够**）。
- [ ] A11：战斗域内单向 `Hud/表现 → 业务 → Abstractions` 无回环（`DomainBoundaryTests` 覆盖；目录名不含 `View`/`Presentation`）。
- [ ] A12：Play 冒烟由**仓内 PlayMode 用例** `Assets/Tests/UI/PlayMode/BattleSmokePlayModeTests.cs`（宿主 asmdef `LiteGame.UI.PlayModeTests`）判定，写进 `.workflow/config.json` 的 `commands`（名 `battle-smoke-playmode`，形态同现有 `ui-*-playmode`）。**执行载体三段式**：①**harness**＝加载 `Boot.unity` 起服（`ClientHost` 全链 + 本地测试房），`StageMachine<ProcedureId, ProcedureArgs>` **经 `GameEntry.TakeContainer().Require<...>()` 解析**后轮询 `Started && Current == ProcedureId.Main`（≤10s；只读 `Current` 会误命中 `default`），随后 `TestModeRuntime.Current.RequestEnter()` 进测试局、`RequestExit()` 离局（`TestModeRuntime` 为 dev 三宏 API，`ProcedureMain` 是唯一消费点）；②**rig 存取口**＝`IBattleViewGroupFactory` 增 `IBattleViewGroup LastCreated { get; }`（XML 注释"仅测试/诊断用"；**返回 `IBattleViewGroup`，测试向下转型到 internal 的 `BattleViewRig`**，friend 见 A10a）；实现形态写死：`BattleViewGroupFactory.Create` 返回**内部装饰器**（包装真 rig），`_last` 存**真 `BattleViewRig`**，装饰器 `Dispose` 时 `Interlocked.Exchange(ref _last, null)`；**注册落点 = 构造该工厂的那一段**——批 1–3 = 过渡期 `ContainerModule.CreateBattleViews` 处 `RegisterInstance<IBattleViewGroupFactory>`（该工厂当前由这里唯一构造、只作构造参数传递、未注册任何键）；**批 4 = `BattleComposition` 构造并 `RegisterInstance`**，`ProcedureComposition` 改 `Require<IBattleViewGroupFactory>()` 取（`Procedure` 在 `Battle` 之后，顺序成立）；`ComposeAfter` 只做装订、不注册该键；测试经 `GameEntry.TakeContainer()` 解析该接口（消费面登记进 `Docs/审计/客户端/登记/技术债.md` 新增的"`TakeContainer` 消费清单"一节 + `GameEntry` 注释）；③**观测面（均为新增成员，明确列出）**＝`BattleViewRig` 增 `internal bool IsBuilt`、`internal BattleHudAssembler Hud => _hudAssembler`、`internal BattleViewSceneAssembler View => _viewAssembler`；`BattleHudAssembler` 增 `internal GameObject Root => _hudInstance`（并保留既有 `Crosshair`）；④**出场前断言**＝`IsBuilt`、`Root.activeInHierarchy`、`Hud.Crosshair != null`，开火后 `Crosshair.ShotPulseCount>0`、`AudioService.VoiceCount>0`、`VfxService.ActiveCount>0`、`BattleDamageNumberDriver.ActiveCount>0`、**越肩几何（带容差）**：相机在角色右后方，水平夹角 ∈ [15°,45°]、水平距离 ∈ [1.5,4.0]m、相机高度高于角色胸口 ≤1.0m（①既有/②新增已在前面标注：`VoiceCount`/`DamageNumber.ActiveCount` 既有，`VfxService.ActiveCount` 新增）；**离场断言只对容器级服务与工厂**（rig/HUD 已销毁，不得读其成员）：`LastCreated == null`（或 `HasLiveGroup == false`）、`AudioService.VoiceCount` 与 `VfxService.ActiveCount` **≤2s 超时轮询**归零（`BattleViewRig.Dispose` 注释写明"在播代理自然播完"，即时断言会假红）。friend 表见 A10a。`exec_runtime_script` 仅作人工补充证据，不作判据。

**批 3（端口）**
- [ ] A13：`ModuleDependencyDeclTests.Baseline` 空集且通过；`Modules/Ui/UiComposition.cs` 不再 `Require<FeedbackService>`。
- [ ] A14：确认框 Play 冒烟可正常返回；未绑定场景只记一条 Warning 不抛。

**批 4（组合根）**
- [ ] A15：`ContainerModule` 不再是 `IClientModule`；`Modules/**` 合计 **18** 个 `<域>Composition.cs`（算式：批 1 的 14 段 − `ContainerModule` + `CameraComposition` + `Procedure`/`Session`/`Meta` 三段 + `MapComposition` = 18）；注册序为设计要点里的 18 段全序；`AppComposition` 由 `ClientHost.ComposeRoot` 驱动；窗口内 `Put` 成功、窗口外 `Put` 抛。
- [ ] A15a：`ContainerModule` 的注册项按"批 4 注册归属表"全部落位，无遗失且顺序可启动：用"装配后 `Require<T>` 全可用 + `SetTickOrder` 回读等于归属表的 **13 项期望序** + 释放探针（`IDisposable` 计数）在关停后归零"三件用例判定；归属表逐行可核对（场景/缓存→Content，派发器/命令中心→PI，地图→Map，弹窗/反馈/提示→Ui，设备→Input，Meta→Meta，表现内核+战斗→Battle，会话→Session，流程→Procedure，跨域装订/开发装饰/`ITransportFactory` 实现→`ComposeAfter`）；**`LookPreferences` 单实例断言**：`ProductSettings` 与 `Battle` 拿到同一实例（用例：改灵敏度 → Battle 注入的 `ILookPreferences.SensitivityMultiplier` 同步变化）；`ProductSettings` 类行数上限进 `CompositionBudgetTests`（≤300 行、方法 ≤80），防设置门面成为下一个膨胀点。
- [ ] A16：`ComposeRoot` 成功/失败回滚/取消三路径有用例；**预算口径先定、非事后自证**：`CompositionBudgetTests` 断言 `AppComposition` 类 ≤ 400 行、每个方法（含 `ComposeAfter`）≤ 80 行，超限即红——因此 `ComposeAfter` 必须按装订组拆成 `BindLuaFacades`/`BindSettings`/`BindModals`/`BindIdentity`/`BindInput` 等小方法；**行数口径**：①`ContainerModule.Assemble`（152 行）在**批 1** 先按职责拆成 `CreateServices`/`CreateSessions`/`CreateProcedure`/`CreateBindings` 四个 ≤80 行方法（仍留 `ContainerModule` 内，行为不变），批 4 只搬方法；②`ClientHost` 454 行 / `InitializeAsync` 83 行 / `ShutdownAsync` 86 行的冻结只许降——新增 `ComposeRoot` 用抽等价私有方法抵消（净增 ≤ 0），确需上调须在评审记录写明理由并更新冻结表。
- [ ] A17：手测"登录 → 开房 → 进对局 → 退出"全链通过，`GameEntry` 无新增 Error 日志。

**批 5（外层域边界）**
- [ ] A18：`Modules/Session/` 含 `AccountSession(Factory)`、`MatchSession(Factory)`、`RoomSessionFace` + 2 契约 + `SessionComposition`（原名 `Modules/Room/`、`Procedure/Session/` 下的会话件全部迁入，两处目录清空）；`SessionComposition` 已在注册序第 ⑰ 位。
- [ ] A19：`MatchSessionFactory` 引用的**类型集不含 `Modules/Procedure/**` 下的类型**（类型→声明文件反查，与 `DomainBoundaryTests` 同机制；现状实测两处：`BattleScenes.ResolveMapId` 与 `IMapProvider`（声明在 `Modules/Procedure/Setup/MapProvider.cs`），两者随 A20 迁 `Modules/Map/`，两项同批收口），方向 `Session → Map`。
- [ ] A20：`Modules/Map/` 收编 `BattleScenes`、`MapProvider`（断言两文件路径在 `Modules/Map/` 下）；`MapBoundsMarker` 移动后仍被 `SimMapBaker` 读取（编译通过 + 引用点存在）；`TrainingGroundLayout` 的反射程序集名等于实际 asmdef 名，且有 EditMode 用例解析成功（非"注释改准"这类主观判据）。
- [ ] A21：`Modules/Ui/` 只含 `UiComposition`/`UiPageResolver`/`LuaUIData`/`Generated/`；`ResultRanking` 移 `Battle`、`PageFlow` 移 `Procedure`。
- [ ] A22：`Meta/Abstractions/` 含 `IAccountSessionControl`、`ILoginSurface`；`Meta → Procedure` 经 `IAppFlow` 端口化。
- [ ] A23：其余错位件归位：`DeployBake` → `Modules/Framework/`（去可变静态）、`ProductSettings` 门面本体 → `Settings`（**三处同批**：①`LiteGame.Modules.Settings.asmdef` 已引 `LiteFramework.Runtime`（无需新增该条）；②`LiteGame.Adapters.LuaBridge.asmdef` **须新增 `LiteGame.Modules.Settings` 引用**并由 `ClientAsmdefAllowlistTests` 登记该边（`InternalsVisibleTo` 只放开可见性，不能替代程序集引用）；③`Settings/AssemblyInfo.cs` 增 `InternalsVisibleTo("LiteGame.Adapters.LuaBridge")`；`BindGlobals`/静态装订面**留 `Adapters/LuaBridge/ProductBridges.cs`**，`s_instance`/`Instance()` 提为 `internal`——**不走 `LuaBridge → App` 反向边（会与 `App → LuaBridge` 成环）**）、`LTextCatalog` → `Config`、`LuaLayout` → `Framework/`、`TeleportToAimCommand` 与 handler 同域；`LaunchLoading`/`CrosshairFeel` 保持原域。
- [ ] A24：`Procedure` 不再直接引用 `BattleClient`/`BattleContext`/`BattleContext.EndReason`/`TeleportToAimCommand`/`RoomSessionFace`/`MetaEntryService`/`MetaEntryConfig`。
- [ ] A25：8 个共享域各自命名空间；`DomainBoundaryTests` 先红后绿；纪律扫描 0 违规；`Room → BattleClient` 作为允许边显式登记。
- [ ] A26：（**用户裁决项，非本目标派生**）排序（击杀降序→EntityId 升序）与玩家序号单源，`ResultRanking` 与局内排行榜同实现，有一致性用例。
- [ ] A27：（**用户裁决项，非本目标派生**）"瞄准"两事实改名区分（`AimHoldGate` → 相机混合语义），`0.3s` 常量归相机侧；**黄金值取生产路径**：改名前用固定输入序列记录 `ICameraService.SetAiming` 的翻转时刻（`AimHoldGateEditModeTests` 扩一例走生产链路，不直接断言 `private const` 符号），改名后同一断言不改仍绿；禁止"或快照对比"式自由选择。
- [ ] A28：批 5 同批产出三件：`Docs/说明/18-域间引用矩阵.md`（**12 个 `Modules/` 顶层域** × 12 的"域→域"类型引用计数表）、`scripts/gate/domain-matrix.py`、`.workflow/config.json` 的 `commands` 条目（名 `domain-matrix`，`--check` 过期即红）。批 1~4 不接入该门禁（避免文档缺失/树在迁移中导致必红）。

**全批**
- [ ] A29：`.workflow/config.json` 的 `commands` **全绿**（含既有 61 条与新增 `battle-smoke-playmode`、`domain-matrix`；其中 `L1`/`doc`/`restore`/`l0`/`toolchain-tests`/`toolchain-check`/`toolchain-docs-check`/`doc-lane`/`l2-meta` 必在列）；Unity 重编译 0 error。
- [ ] A30：`test-plan.md` 写明"新建一个业务域需要的步骤"：①`Modules/<域>/` 目录（`Abstractions/` + 按职责命名的子目录）②按需建独立 asmdef 并改 `LiteGame.Modules.asmdef` 引用 + `ClientAsmdefAllowlistTests` 一行（不建独立程序集则跳过；**若该域需引 `LiteGame.Adapters.*`，还须改该用例的硬边 case 与注释并登记例外**）③`<域>Composition.cs` ④`GameEntry.RegisterModules` 一行 + 前置段 `DependsOn` ⑤端口只经 `Abstractions/`；并明确"不改任何**共享能力域代码**，但会改两处守卫表（asmdef 允许表、模块依赖声明）"；例外写明：需要全局服务（场景/命令中心/流程机/桥装订）的域，注册仍落 `AppComposition` 末段；**跨程序集门面/装订**（如 `ProductSettings` 由 `LuaBridge` 装订）须"类型落域 + 装订留调用方 + asmdef 引用与允许表一行"三处同步。
- [ ] A31：文档回写：`Docs/说明/02-架构总览与程序集分层.md`、`Docs/说明/17-全项目数据流.md`、`Docs/说明/18-域间引用矩阵.md`（新增）、`Docs/规范/03-领域查询表.md`、`Docs/设计文档/架构/商业级通用客户端框架总设计.md`、`Docs/设计文档/客户端/UI系统/设置系统专项设计.md`、`Docs/设计文档/客户端/内容/热更与内容发布专项设计.md`、`Docs/审计/客户端/登记/技术债.md`、进度卷新章；文档门禁（`doc`/`toolchain-docs-check`/`doc-lane`）全绿。

## 风险与回滚

- 风险：
  1. 批 2 合并三个 asmdef：允许表与所有引用者（App/Modules/Adapters.Dev/Editor/测试）都要改，漏一处编译红（可见）。
  2. 批 2 搬迁量大（约 70 文件）：漏搬 `.meta` 会让 GUID 重生成、prefab/场景脚本变 missing（本项目两次实伤）。
  3. 战斗域内分层若出现回环，需拆端口，可能改动 `BattleContext` 装配面。
  3b. 批 2 合并后同一程序集内同时存在 `LiteGame.View`（原 `Modules/View`）与 `Adapters.Unity` 的 `LiteGame.View.Animation` 命名空间段，可能出现 `CS0104`/`CS0433`（编译可见，不静默）。
  4. `ComposeRoot` 与 `SealAssembly` 时序错 → 运行期 `Put` 抛/产物缺失，多在进对局暴露。
  5. 批 5 命名空间收口可能 `CS0104` 同名二义。
  6. 并行线在场（`CS0105` 噪声）→ 重叠文件逐文件确认归属；提交逐文件 `git add`，禁 `-A`。
  7. 能力端口替换 `is 具体适配器` 后若端口未实现（测试替身/未来设备源），相机注入与视角增量会退化为 null——与现状退化路径一致，但必须有用例覆盖"未实现端口不抛、只跳过"。
  8. 批 4 拆 `ContainerModule` 时，`Put`（`CommandCenter`/`StageMachine`/`ErrorRecoveryService`）依赖 `ModuleContext` 装配窗口；`ComposeRoot` 窗口必须覆盖全部三段且早于 `SealAssembly`，否则启动即抛/进对局才暴露。
- 监控：每子步 `Unity 重编译 0 error`；每批定向 EditMode + L1 全量；装配序/表现相关必做 Boot Play 冒烟；批 4 追加完整手测；`.meta` 用 `git status` 核对成对出现。
- 回滚：开工基线是当前工作区 WIP（用户裁决在其上继续），回滚以**批/子步提交**为界——每批收口提交一次，失败 `git revert <该批提交>`；批 2 删除 asmdef 后 `AssetDatabase.MoveAsset` 反向搬回不足以恢复引用集，必须整批 revert。`wf rewind` 到对应阶段并记录原因。
