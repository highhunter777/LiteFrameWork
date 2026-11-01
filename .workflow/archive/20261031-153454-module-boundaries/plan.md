# 方案：客户端模块边界收口（业务域垂直自持 + 模块就地装配）

> 运行 module-boundaries · 定稿（合并修订 1~6，取代此前所有章节）
>
> **⚠️ 本运行已拆分为三个交付（2026-10-30 用户裁决 B），本文件仅作「证据母本」保留：**
> - 交付 A → `.workflow/active/client-modularization/`（批 1~5：竖向模块化、装配就地、外层域边界）
> - 交付 B → `.workflow/active/sync-and-sim-params/`（批 6~7：LiteSync 去耦、LiteSim 参数装载）
> - 交付 C → `.workflow/active/codegen-consolidation/`（批 8：生成器收口）
>
> 本文件保留全部实测证据（147 文件域审计、403 文件机械扫描、程序集环、生成器流程与腐烂路径），
> 新运行只在需要引用证据时回读本文件；**不要在本运行内推进阶段或施工**。

## 背景与目标

- 现状：
  1. 13 个 `IClientModule` 实现全在 `App/Bootstrap/Modules/`，与 `Modules/`（15 个功能域目录）形成两处同名"模块"；其中仅 5 段对应产品域，另 8 段装配的是框架能力（无对应域目录）。
  2. **按技术层切产品代码**：`Modules/Ui`、`Modules/View`、`Modules/Presentation` 三个横向域收纳了本属战斗域的东西（HUD、Fx、音频、Rig、Look、SimView、动画），业务系统无法自持自己的 UI 与表现。
  3. `LiteGame.Core` 混入业务端口（`Core/Battle/{IBattleView,IBattleReadModel,IMatchIdentity}`），与"只放基础设施与工具"冲突。
  4. 装配粒度粗（`Clocks` 顺带产 `EventBus`；`Presentation` 一段同时造 HUD 与世界表现；`Container` 承担 11 域编排且冒充第 13 个模块）；存在唯一隐性反向依赖（`UiService` 懒取 `Container` 的 `FeedbackService`）。
  5. 域间耦合无机械约束：全量扫描查出 10 条真实违规（含 `Procedure→Battle/Meta` 8 条、`Ui` 引战斗具体类、契约放错位置），2 个空壳域（`Map`/`Room`），2 处口径重复。
- 目标（可观察）：
  - 四类模块与五条依赖规则落地为守卫（`DomainBoundaryTests` / `CompositionBudgetTests` / `ModuleDependencyDeclTests`），违规可红。
  - `LiteGame.Core` 只含基础设施与工具；业务端口全部住各业务域 `Abstractions/`。
  - `Modules/Battle` 成为完整战斗垂直切片（含 View/Presentation/Hud）；`Modules/View`、`Modules/Presentation` 不再存在。
  - `App/Bootstrap/Modules/` 不再存在；装配段就地 `<域>Composition.cs`，且不 `new` 任何适配器。
  - `ContainerModule` 不再是 `IClientModule`；`Clocks` 不再产 `EventBus`；`ModuleDependencyDeclTests.Baseline` 为空。
  - Unity 重编译 0 error、L1 全量通过、`wf verify` 九条门禁全绿、Boot Play 冒烟通过。

## 需求澄清与假设

- 需要确认的问题：无（裁决已定）。
- 已确认答复（用户原话要点）：
  1. "B"（域实现归位）+"Composition"（目录命名）；装配段文件名 `<域>Composition.cs`、去掉 `Module` 后缀；
  2. "F 走方案 a"（`ClientHost` 装配末段钩子，组合根改普通类）；
  3. "按 `.workflow` 十阶段走，但先别开"；"检查外层域，也收到我们的 workflow 里"；
  4. "不能把 bootstrap 里的模块目录和外层的合并吗……一个模块就是一个功能模块"；"合并 + 拆分一次性做到位"；
  5. "Map / Room 按你的来"；"新建 `Modules/Session/`"；"瞄准双事实源按你的来"；
  6. "直接把 Presentation 拆了，还有 ui 和表现不应该是功能模块自持吗"；
  7. **"core 不能沾染业务啊，只能放基础设施和工具"**；**"战斗系统有自己的 ui 和表现，未来什么局外背包系统，活动系统，难道全把 ui 和表现代码堆到那些表现程序集吗"**；**"我们的模块划分要竖向的，要高内聚低耦合的，模块化的"**——本方案的根因裁决。
- 采用假设：
  - 类名保留 `XModule`，仅文件名去后缀。
  - **一个业务域一个程序集**（`LiteGame.Modules.Battle` 吸收 `LiteGame.Modules.View` + `LiteGame.Presentation`）；若后续要求域内三层各一程序集，按等价形态升级（多 2 个 asmdef）。
  - 外层域用"拆命名空间"取得可校验边界，不拆程序集（`Battle` 因合并例外）。
  - `Core/Dev`（`IDevSession`/`ILocalTestHooks`）与 `Core/View`（`ViewTransformMath`/`DamageNumber*`/`HitLocalRole`）留在 Core：前者是基础设施端口（移走会造成 `Input↔Hosting` 环），后者是纯计算工具。
- 假设影响与风险：若要求域内三层各一程序集，批 2 增加 asmdef 改名与允许表 3 条，工作量上浮、结构等价；若要求 HUD 并入 `Modules/Ui`，必成环（见约束），须改为端口化方案。

## 范围与非目标

- 范围内：
  1. **批 1 装配段演化**：13 段迁入各自域目录、删除 `App/Bootstrap/Modules/`；框架装配入口（`FrameworkInfrastructure`/`Clocks`/`Events.Create`）；`Events` 独立；适配器选择上收 `AppComposition`。
  2. **批 2 业务域垂直化**：`Modules/View` + `Modules/Presentation`（含 `Hud/`）→ `Modules/Battle/{View,Presentation,Hud}`；三 asmdef 合为 `LiteGame.Modules.Battle`；`Core/Battle` 业务端口下沉 `Modules/Battle/Abstractions/`。
  3. **批 3 端口**：`IConfirmDialog` + `ConfirmDialogRouter` 后绑定，`Baseline` 清空。
  4. **批 4 组合根**：`ContainerModule` → `AppComposition`（`ClientHost.ComposeRoot`）。
  5. **批 5 外层域边界**：新建 `Modules/Session/`；`Map` 充实；`Ui` 只剩 UI 基础设施；`Meta` 契约归位与 `IAppFlow` 端口化；8 个共享域命名空间收口；口径单源；瞄准改名；`TrainingGroundLayout` 反射名修正。
  6. 全部守卫与表同步（4 处硬编码路径、`ModuleScopeAdoptionTests`、`ArchitectureBudgetTests`、`ClientAsmdefAllowlistTests`、`StaticMutableAllowlistTests`）。
- 非目标：
  1. 不把 `IClientModule` 实现搬进 `LiteFramework`；不把框架装配段放进 `LiteGame.Core`（硬边禁止）。
  2. 不新增任何横向 UI/表现程序集；`Modules/Ui` 只保留跨业务共享的 UI 能力。
  3. 不拆 `PlatformInfrastructure` 的能力域（由框架入口薄化解决）。
  4. 不改"具体键 vs 接口键"的产物暴露面（独立议题）。
  5. 不动 `Bootstrap/Probes`、`Bootstrap/Fallbacks`；不清理既有 `CS0105`（本批实质修改的文件除外）。
  6. 本轮不建局外背包/活动域，但目录与程序集口径为其预留。
  7. 不把 `Camera` 变成有装配段的域（纯契约域：端口 + 数学）。

## 现状与约束

- 代码事实（本机实测）：
  - `GameEntry.RegisterModules` 注册 13 个模块（注册序＝初始化序＝关闭逆序）；`Bootstrap` 内无类定义。
  - `ModuleDependencyDeclTests`：`Baseline` 仅 `UiServiceModule <- ContainerModule`；`ClocksModule.DependsOn` 已补 `PlatformInfrastructureModule`。
  - 硬编码 `Bootstrap/Modules` 路径 4 处：`ModuleDependencyDeclTests`、`ModuleScopeAdoptionTests`、`ArchitectureBudgetTests`（`ContainerModule.Assemble` = 152 行）、`M3WiringGuardTests`。
  - `LiteGame.Core.asmdef`：`references:["LiteSim"]`、`noEngineReferences:true`；硬边禁引 `LiteFramework.*`/`LiteGame.*`/`LiteNet`/`RoomServer.*`，且被 L1 源链接进 dotnet 工程。
  - `LiteGame.App` 已引 `Modules`/`Modules.Battle`/`Modules.View`/`Modules.Config`/`Modules.Settings`/`Modules.Input`/`Modules.Camera`/`Presentation`/`Adapters.Unity`/`Adapters.Luban`/`Adapters.LuaBridge`。
  - **全量机械扫描**（`Assets/LiteGame` 403 个 .cs / 655 个声明类型，脚本 `.codely-cli/tmp/scan_vertical.py`）：Core 12 / 业务域 27 / 共享能力 35 / 应用壳 22 / 横向表现 62 / 组合根 4 / 装配段 13 / 适配器 73 / 编辑器 154。
    - 共享能力域互引成环 **0**（低耦合在能力层成立）。
    - 真违规 10 条：`Ui→Battle::PlayerIdentity`、`Ui→Meta::ILoginSurface`、`Procedure→Battle::{BattleClient,BattleContext,EndReason,TeleportToAimCommand}`（3 个会话件 + 2 个阶段）、`Procedure→Meta::{MetaEntryService,MetaEntryConfig}`、`Procedure→Room::RoomSessionFace`；另有 `Room→Battle::BattleClient`（登记为允许边）、11 条横向表现→Battle（批 2 后消失）。
    - 假阳性（已核实）：`MetaBridge`/`MetaWireContracts` 的 `EndReason` 是属性名；`Hosting→IBattleDevDiagnostics` 目标在 `Battle/Abstractions/`。
  - 契约放错位置：`IAccountSessionControl`、`ILoginSurface` 声明在 `Modules/Meta/AccountReentry.cs`（非 `Abstractions/`）。
- 约束：
  - `Assets/` 下移动/重命名必须走 `AssetDatabase.MoveAsset`（`.meta`/GUID 不丢）。
  - **程序集环（已实测三条）**：`Adapters.Unity → Modules.Input / Modules.Camera`；`Adapters.Luban → Modules.Config`；`LiteGame.Modules → LiteGame.Presentation`。因此：域装配段不得 `new` 适配器；HUD 不得并入 `Modules/Ui`；批 2 必须同时删除 `LiteGame.Modules → LiteGame.Presentation` 边。
  - `run_shell_command` 不可用；`python`/`dotnet` 经 Unity 进程侧 `Process.Start` 异步收流。
  - C# 9；`Nullable=disable`；`ImplicitUsings=disable`；禁文件级命名空间。
  - 不触 BuildHash 共享源集 ⇒ 无需两端重建。
  - 复杂度预算：方法 ≤ 80 行、类 ≤ 400 行；存量冻结只许降。
  - 阶段 6 需红测：`.workflow/config.json` 的 `red_commands` 追加只跑新增 fixture 的命令。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| V1 | 业务域垂直自持：战斗域吸收 View/Presentation/Hud；域内 `Abstractions/Domain/Ui/Presentation`；一业务域一程序集 | 新业务可整块复制形态；共享层不被业务污染 | 三 asmdef 合并、搬迁量大 | **采用** |
| V2 | 保留横向 `Ui`/`View`/`Presentation` | 零搬迁 | 业务 UI/表现持续堆进共享程序集 | 否决（用户否决） |
| V3 | 垂直目录 + 域内三层各一 asmdef | 保留编译隔离 | asmdef +2、允许表更复杂 | 备选 |
| M1 | 装配段就地 `<域>Composition.cs`；框架接线集中 `Modules/Framework/`；适配器上收 `AppComposition` | 一目录=一域；域代码零适配器依赖 | 装配段改接端口 | **采用** |
| M2 | 就地但装配段自 new 适配器 | 变化小 | `Input`/`Config` 成环 | 否决 |
| M3 | 只改名（`Modules`→`Domains`） | 零搬迁 | 不满足"一模块=一域" | 否决 |
| M4 | 框架装配段进 `Core` | 集中 | 破 Core 硬边，编不过 | 否决 |
| C1 | 框架提供 `FrameworkInfrastructure/Clocks/Events.Create` | 构造正确性收回框架 | 动框架 | **采用** |
| B1 | `IConfirmDialog` + `ConfirmDialogRouter` 后绑定 | Baseline 清空 | 2 个小文件 | **采用** |
| F1 | `ClientHost.ComposeRoot` + `AppComposition` 普通类 | 组合根不再是模块 | 改框架 + 引导用例 | **采用** |
| G1 | 共享域拆命名空间 + `DomainBoundaryTests` | 边界可校验 | 大量 `using` 增补 | **采用** |
| S1 | 新建 `Modules/Session/` | 会话所有权归位 | 共享程序集内新增目录 | **采用** |

推荐：V1 + M1 + C1 + B1 + F1 + G1 + S1。贯穿原则：**业务域自持 Domain/Ui/Presentation；共享层只放能力；装配段就地但只认端口。**

## 设计要点

### 1. 四类模块与依赖规则（守卫化）

| 类 | 成员 | 规则 |
|---|---|---|
| 业务域（竖向） | `Battle`、`Meta`、`Session`、（规划）`Inventory`、`Activity` | 一目录 + 一程序集 + 一装配段；域内 `Abstractions/Domain/Ui/Presentation`；自带 UI 与表现 |
| 应用壳 | `Procedure`（阶段 + 页面流 + 加载遮罩 + 常量） | 只编排，不认识业务内部 |
| 共享能力 | `Ui`（页面目录/寻址/Lua UI 数据）、`Input`、`Settings`、`Config`、`Camera`、`Map`、`Product`、`Recovery`、`Hosting` | 只提供能力，不含业务 |
| 框架接线 | `Framework/`（8 段） | 只调框架装配入口 |

依赖规则：① 业务域→共享能力只经 `Abstractions/`；② 共享能力✗→业务域（除"低层定义端口、高层实现"，如 `Meta/Abstractions/ILoginSurface`）；③ 业务域之间✗直连（除已登记允许边）；④ 域内 `Ui/Presentation → Domain → Abstractions` 单向；⑤ 唯一例外 `AppComposition`。

### 2. 目标目录

```
Assets/LiteGame/
├─ Core/                             ← 只放基础设施与工具
│  ├─ Dev/     IDevSession · ILocalTestHooks
│  └─ View/    DamageNumber* · HitLocalRole · ViewTransformMath
├─ App/
│  ├─ GameEntry.cs
│  ├─ AppComposition.cs              ← 组合根：创建适配器、注入端口
│  └─ Bootstrap/{Probes,Fallbacks}/
└─ Modules/
   ├─ Framework/                     ← 8 段：PlatformInfrastructure · Identity · DevSession
   │                                   Content · Events · Clocks · Schedulers · LuaHost
   ├─ Ui/                            ← 仅 UI 能力：UiComposition · UiPageResolver · LuaUIData · Generated/
   ├─ Input/InputComposition.cs      ← 接 IIntentSource 端口
   ├─ Settings/SettingsComposition.cs
   ├─ Config/ConfigComposition.cs    ← 接 Func<IConfigService> 端口
   ├─ Camera/                        ← 纯契约（端口 + 数学，无装配段）
   ├─ Procedure/                     ← 应用壳：Stages/ · PageFlow.cs · LaunchLoading.cs
   │                                   Shared/ · Setup/ · Transport/ · ProcedureComposition.cs
   ├─ Session/                       ← 会话所有权：AccountSession(Factory) · MatchSession(Factory)
   │                                   RoomSessionFace · Abstractions/ · SessionComposition.cs
   ├─ Meta/                          ← 局外业务域：Abstractions/ · Wire/ · Ui(MetaBridge) · MetaComposition.cs
   ├─ Map/                           ← 地图域：MapBoundsMarker · MapTopology · BattleScenes · MapProvider
   ├─ Battle/                        ← 战斗业务域（垂直自持，子目录按职责命名）
   │  ├─ BattleComposition.cs
   │  ├─ Abstractions/               IBattleView · IBattleReadModel · IMatchIdentity
   │  │                              IBattleViewGroup(F…ory) · IBattleDevDiagnostics
   │  ├─ Match/                     对局运行：BattleContext · BattleClient · SnapshotMirror
   │  │                              InputPipeline · MatchWorldFactory · PublicCombatFeed
   │  │                              PlayerIdentity · GameplayMatchIdentity · 诊断记录
   │  ├─ Arena/                     战场世界视图：SimView · 插值 · 和解 · 事件闸 · 实体绑定
   │  │                              EntityViewMap · VfxCatalog
   │  ├─ Characters/                角色与动画：CombatAnimMachine · Locomotion/Combat 阶段
   │  │                              驱动 · 骨架约束 · 动档案 Profile · GripBake
   │  ├─ Combat/                    战斗反馈与特效：命中反馈 · 伤害数字 · 枪口火光 · 激光
   │  │                              保护罩 · 战斗音频 · 命中分发
   │  ├─ Aim/                       视角与瞄准：BattleLookController · AimHoldGate · LookTuning
   │  ├─ Hud/                       对局 HUD：血条 · 击杀提示 · 复活 · 网络统计 · 准心 · 准心手感
   │  ├─ Settlement/                结算排行：ResultRanking
   │  ├─ Setup/                     进局装配：BattleViewRig · GroupFactory · SceneAssembler
   │  │                              Assets · WeaponMounts · HudAssembler · FeedbackAssembler
   │  └─ Commands/                  GM 命令：TeleportToAimCommand + Handler（同域）
   ├─ Dev/                           ← 开发装配缝：DevHooks + Null 实现
   └─ （Product / Recovery 解散，见批 5）
```

**命名规则（防回潮）**：域内子目录名必须表达**职责**（`Match`/`Arena`/`Characters`/`Combat`/`Aim`/`Hud`/`Settlement`/`Setup`），禁止 `View`/`Presentation`/`Domain`/`Logic` 这类技术分层名——它们不说明"这块做什么"，且 `View` 与 `Presentation` 在中文语境下同义、放在同一域内无法区分。唯一保留的技术名是 `Abstractions/`（表达"本域对外契约"）。守卫：`DomainBoundaryTests` 拒绝业务域下出现 `View`/`Presentation`/`Domain` 目录名。

未来同形态：`Modules/Inventory/`、`Modules/Activity/` 各自 `Abstractions/` + 按职责命名的子目录 + `<域>Composition.cs`。

### 3. 接口与流程

- `FrameworkInfrastructure.Create(FrameworkInfraOptions)` → `{ IReferencePool, IFileStore, ILog, IJsonCodec }`（内部保证 `Log.Install` → `SetHelper` → 条件结构化日志顺序）；`Clocks.Create()` → 三时钟；`Events.Create(pool, isDev)` → `EventBus`。
- `LiteGame.IConfirmDialog`（住 `App/Bootstrap/Contracts/`）+ `ConfirmDialogRouter`（未绑定返回 false + 一次性 Warning；`Bind` 幂等）。
- `ClientHost.ComposeRoot(Action<ModuleContext>)`：全部模块成功后、`SealAssembly()` 前开窗；失败/取消同样回收。
- 注册序（批 4 后，**16 段**＝8 框架接线 + 6 业务/支撑 + Ui + Procedure，`AppComposition` 不计段）：
  `①PlatformInfrastructure ②Identity ③DevSession ④Content ⑤Settings ⑥Events ⑦Clocks ⑧Schedulers ⑨LuaHost ⑩Config ⑪Ui ⑫Procedure ⑬Session ⑭Meta ⑮Battle ⑯Input`。拆除＝逆序。

### 2b. 通用 vs 功能内聚（分档与判据）

| 档 | 成员 | 判据 |
|---|---|---|
| **通用·框架层** | `LiteFramework`（UI/导航/弹窗/本地化/动画/音频/VFX/实体/相机/内容/配置/设置/身份/场景/日志/DI/事件/命令/FSM/池/时钟/调度/IO/序列化） | 零业务词汇，任何业务都可用 |
| **通用·基础设施/工具** | `LiteGame.Core`：`Dev/`（`IDevSession`/`ILocalTestHooks`）、`View/`（伤害数字/命中角色/变换数学） | 纯计算或端口，无业务语义 |
| **通用·共享能力（产品侧）** | `Framework/`（8 段框架接线）、`Ui`（页面目录/寻址/Lua UI 数据）、`Input`（设备→意图→三门）、`Settings`、`Config`、`Camera`（端口+数学）、`Map`（地图数据/标标注）、`Dev`（开发装配缝） | 被 ≥2 个业务域使用，或只服务装配；改它不需要业务知识 |
| **通用·应用骨架** | `Procedure`（阶段机 + 页面流 + 加载遮罩） | 所有业务共用的启动/流转骨架，不含具体业务 |
| **业务支撑** | `Session`（Account/Match 会话所有权 + 房间会话面） | 含业务语义但被 ≥2 个业务域共用（Battle/Meta） |
| **功能内聚（业务垂直切片）** | `Battle`（Match/Arena/Characters/Combat/Aim/Hud/Settlement/Setup/Commands）、`Meta`（账号/登录/大厅/房间/结算提交 + 自有 UI 桥 + Wire 契约）；规划 `Inventory`、`Activity` | 只有一个业务在用；类型名带业务词汇；**自持自己的 UI 与表现**；改动动机只来自该业务需求 |

- 装配段数：通用档 8（`Framework/`）+ 5（`Ui`/`Input`/`Settings`/`Config`/…）+ 1（`Procedure`）＝14；业务档 2（`Session`/`Meta`/`Battle` 中的段）——合计 16，与上面注册序一致。
- 薄装配判据：允许 `Require/Get`、调框架 `Create`、`Put`/`RegisterInstance`/`Scope.Register`、`DependsOn`、释放逻辑、一眼可读的形态选择；禁止手拼框架构造参数、`for/while/foreach`、域内业务算法、跨域业务连线（组合根除外）、`new` 适配器。

### 4. 批次与子步

| 批 | 内容 | 子步 |
|---|---|---|
| 1 装配段演化 | 框架入口 → 13 段就地 → `Events` 独立 → 适配器上收 → 守卫/表同步 | T1 框架入口、T2 搬迁、T3 Events、T4 门面瘦身、T5 适配器上收、T6 守卫 |
| 2 业务域垂直化 | `View`+`Presentation`(+`Hud`) → `Modules/Battle`；三 asmdef 合一；`Core/Battle` 端口下沉 | T1 端口下沉、T2 目录搬迁、T3 asmdef 合并与允许表、T4 引用者修复、T5 冒烟 |
| 3 端口 | `IConfirmDialog` + Router，Baseline 清空 | — |
| 4 组合根 | `ContainerModule` → `AppComposition`；`ClientHost.ComposeRoot` | — |
| 5 外层域边界 | `Session` 建域、`Map` 充实、`Ui` 纯化、`Meta` 契约归位与 `IAppFlow`、命名空间收口、口径单源、瞄准改名 | G1 矩阵、G2 归位、G3 命名空间、G4 口径、G5 收尾 |
| 6 LiteNet 去耦 | 新建 `LiteSync`（同步协议层）；`LiteNet` 删 `LiteSim` 引用 → 只剩传输；哈希源集与两端引用同步 | T1 建程序集、T2 搬映射、T3 改 hash、T4 引用者与守卫、T5 两端重建联测 |
| 7 LiteSim 参数化 | 判定几何常量改运行时装载；AOI/广播参数改服务端运行期可配（随批 6 迁入 `LiteSync`）；`RootMotion*` 迁表现侧**并改数据产物** | T1 几何装载面、T2 烘焙工具改产出、T3 AOI 可配、T4 RootMotion 迁出+改数据、T5 身份汇入、T6 守卫与门禁 |
| 8 生成器收口 | 全部生成统一走既有 `scripts/codegen/toolbox.py`（补 `all`/`trig`、禁直调、生成物头与输入指纹统一） | T1 toolbox 补齐、T2 门禁/CI 改道、T3 头部与指纹、T4 文档 |

## 通用性与扩展性

- 根因：按技术层切产品代码，业务系统无法自持 UI/表现，共享层被迫成为业务垃圾场；域边界只由目录承担，无机械约束。
- 扩展点：
  1. 新增业务域（背包/活动）：`Modules/<域>/{Abstractions,Domain,Ui,Presentation}` + `<域>Composition.cs` + 一行 `RegisterModules`，不改任何共享域。
  2. 新增框架能力：框架加 `Create` 入口，装配段加一行。
  3. 换适配器：只改 `AppComposition`，域代码零改动。
- 判据固化：业务域 = 目录 + 程序集 + 装配段；共享层只放能力；纯契约域（`Camera`）不需装配段；装配段只认端口。

## 测试策略

- L1：`ModuleDependencyDeclTests`（扫描面改 `Modules/**.cs` + `App/**.cs` 的 `*Composition.cs`）、新增 `CompositionBudgetTests`（预算 + 零循环）、新增 `DomainBoundaryTests`（四类模块 + 五条规则，首版 `Baseline` = 本方案的 10 条真违规）、`ModuleScopeAdoptionTests`（表重写）、`ArchitectureBudgetTests`（`AppComposition` 重估）、`ClientAsmdefAllowlistTests`（删 2 条、同步引用者）。
- EditMode：`ClientCompositionTests` 风格覆盖 `ComposeRoot` 成功/失败回滚/取消；`DeployBakeEditModeTests`；HUD/相机/动画面。
- PlayMode / Play 冒烟：Boot 起服（13 段、主相机、日志落盘、事件总线）；进一局看 HUD/准心/伤害数字/枪口火光/激光/音频/相机，离局无残留。
- 手测：登录 → 开房 → 进对局 → 退出（批 4 必做）。
- 红测：新增守卫先写并在旧结构跑到断言失败（`wf verify --red` 留证）；`red_commands` 追加三条只跑新 fixture 的命令。

## 验收标准

**批 1（装配段演化）**
- [ ] AC1：`App/Bootstrap/Modules/` 不存在；`Modules/**` 下有 13 个 `<域>Composition.cs`，位置与目标目录一致。
- [ ] AC2：`App/Bootstrap` 只剩 `Probes/`、`Fallbacks/`，内容零改动。
- [ ] AC3：`LiteFramework` 提供 `FrameworkInfrastructure.Create`/`Clocks.Create`/`Events.Create`；`Modules/**Composition.cs` 不再出现 `new ReferencePoolService(`/`new LogService(`/`new FileSystem(`/`new WorldClock(`/`new EventBus(`。
- [ ] AC4：`Modules/**` 不出现 `new CinemachineCameraService`/`new NewInputIntentSource`/`new ConfigService`/`is CinemachineCameraService`/`is NewInputIntentSource`；三者只在 `App/AppComposition.cs` 创建。
- [ ] AC5：`EventsComposition` 独立产出 `EventBus`；`ClocksComposition` 不再 `new EventBus`。
- [ ] AC6：四处守卫路径更新；`CompositionBudgetTests` 旧结构红、新结构绿；Unity 重编译 0 error；L1 全绿。

**批 2（业务域垂直化）**
- [ ] AC7：`Assets/LiteGame/Core/Battle/` 不存在；`Modules/Battle/Abstractions/` 含 `IBattleView`/`IBattleReadModel`/`IMatchIdentity`/`IBattleViewGroup`/`IBattleViewGroupFactory`/`IBattleDevDiagnostics`。
- [ ] AC8：`LiteGame.Core` 只剩 `Dev/`+`View/`，asmdef 仍 `references:["LiteSim"]`、`noEngineReferences:true`，无业务端口。
- [ ] AC9：`Modules/View`、`Modules/Presentation` 目录不存在；内容按职责落在 `Modules/Battle/{Match,Arena,Characters,Combat,Aim,Hud,Settlement,Assembly,Commands}`；`LiteGame.Modules.View.asmdef`、`LiteGame.Presentation.asmdef` 删除；`LiteGame.Modules → LiteGame.Presentation` 边删除。
- [ ] AC9b：`Modules/Battle/` 下不出现 `View`/`Presentation`/`Domain` 目录名（守卫钉死）；全仓 `Modules/**` 不再有 `View`/`Presentation` 作为**域或职责目录名**。
- [ ] AC9c：`Modules/Product`、`Modules/Recovery` 解散（`ProductSettings`→`Settings`、`LTextCatalog`→`Config`、`ILuaProductBridge`→Lua 桥面、`AppRecoveryActions`→`Framework/`）；`Modules/Hosting` → `Modules/Dev`。
- [ ] AC10：`LiteGame.Modules.Battle` 为战斗域唯一程序集；`ClientAsmdefAllowlistTests` 全绿（引用者已改）。
- [ ] AC11：战斗域内单向 `Hud/Presentation → View → Domain → Abstractions` 无回环（`DomainBoundaryTests` 覆盖）。
- [ ] AC12：Play 冒烟：进一局 HUD/准心/伤害数字/枪口火光/激光/音频/第三人称相机全正常，离局无残留（Canvas/音频/VFX/租约归零）。

**批 3（端口）**
- [ ] AC13：`ModuleDependencyDeclTests.Baseline` 空集且通过；`Modules/Ui/UiComposition.cs` 不再 `Require<FeedbackService>`。
- [ ] AC14：确认框 Play 冒烟可正常返回；未绑定场景只记一条 Warning 不抛。

**批 4（组合根）**
- [ ] AC15：`ContainerModule` 不再是 `IClientModule`；`AppComposition` 由 `ClientHost.ComposeRoot` 驱动；窗口内 `Put` 成功、窗口外 `Put` 抛。
- [ ] AC16：`ComposeRoot` 成功/失败回滚/取消三路径有用例；`ArchitectureBudgetTests` 中 `AppComposition` 预算按实测重设。
- [ ] AC17：手测"登录 → 开房 → 进对局 → 退出"全链通过，`GameEntry` 无新增 Error 日志。

**批 5（外层域边界）**
- [ ] AC18：`Modules/Session/` 建立（`AccountSession(Factory)`、`MatchSession(Factory)`、`RoomSessionFace` + 2 契约 + `SessionComposition.cs`）；`Modules/Room/`、`Procedure/Session/` 清空。
- [ ] AC19：`MatchSessionFactory` 不再依赖 `Procedure`（地图行键改为注入或 `IMapProvider`），方向 `Session → Map`。
- [ ] AC20：`Modules/Map/` 收编 `BattleScenes`、`MapProvider`；`MapBoundsMarker` 注释改准；`TrainingGroundLayout.cs:51` 反射程序集名修正。
- [ ] AC21：`Modules/Ui/` 只含 `UiComposition`/`UiPageResolver`/`LuaUIData`/`Generated/`；`ResultRanking` 已移 `Battle`、`PageFlow` 已移 `Procedure`。
- [ ] AC22：`Meta/Abstractions/` 含 `IAccountSessionControl`、`ILoginSurface`；`Meta → Procedure` 经 `IAppFlow` 端口化。
- [ ] AC23：其余错位件归位：`DeployBake` → `Modules/Framework/`（去可变静态）、`ProductSettings`+`LTextCatalog` → `Settings`/`Config`、`LuaLayout` → `Framework/`、`TeleportToAimCommand` 与 handler 同域（`Battle`）；`LaunchLoading`/`CrosshairFeel` 保持原域。
- [ ] AC24：`Procedure` 不再直接引用 `BattleClient`/`BattleContext`/`BattleContext.EndReason`/`TeleportToAimCommand`/`RoomSessionFace`/`MetaEntryService`/`MetaEntryConfig`（改经 `Session` 或端口）。
- [ ] AC25：8 个共享域各自命名空间；`DomainBoundaryTests` 先红后绿；纪律扫描 0 违规。
- [ ] AC26：排序（击杀降序→EntityId 升序）与玩家序号单源，`ResultRanking` 与局内排行榜同实现，有一致性用例。
- [ ] AC27："瞄准"两事实改名区分（`SimView.IsAiming` 保留；`AimHoldGate` 改相机混合语义），`0.3s` 常量归相机侧，行为零变化。
- [ ] AC28：`Room → BattleClient` 作为允许边显式登记（含理由）；`DomainBoundaryTests` 违规清零后 `Baseline` 为空。
- [ ] AC29：覆盖 15 域的"域 → 域"引用矩阵产出，复现方式写入 `test-plan.md`。

**全批**
- [ ] AC30：每批收口 `wf verify` 的 `L1`/`doc`/`restore`/`l0`/`toolchain-tests`/`toolchain-check`/`toolchain-docs-check`/`doc-lane`/`l2-meta` 全通过；Unity 重编译 0 error。
- [ ] AC31：`test-plan.md` 写明"新建一个业务域需要的 5 步"（目录 / 程序集 / 装配段 / 一行注册 / Abstractions），并确认不需要改任何共享域。
- [ ] AC32：文档回写完成（设计文档同步新结构、进度卷新章、审计登记按 git 事实回填）。

## 风险与回滚

- 风险：
  1. 批 2 合并三个 asmdef：允许表与所有引用者（App/Modules/Adapters.Dev/Editor/测试）都要改，漏一处编译红（可见、不静默）。
  2. 批 2 搬迁量大（约 70 文件）：漏搬 `.meta` 会让 GUID 重生成、prefab/场景脚本变 missing（本项目两次实伤）。
  3. 战斗域内分层若出现回环，需拆端口，可能改动 `BattleContext` 装配面。
  4. `ComposeRoot` 与 `SealAssembly` 时序错 → 运行期 `Put` 抛/产物缺失，多在进对局暴露。
  5. 批 5 命名空间收口可能 `CS0104` 同名二义。
  6. 并行线在场（`CS0105` 噪声）→ 重叠文件逐文件确认归属；提交逐文件 `git add`，禁 `-A`。
- 监控/发现：每子步 `Unity 重编译 0 error`；每批定向 EditMode + L1 全量；装配序/表现相关必做 Boot Play 冒烟；批 4 追加完整手测；`.meta` 用 `git status` 核对成对出现。
- 回滚：按子步/批次提交，失败 `git revert`；目录搬迁用 `AssetDatabase.MoveAsset` 反向搬回。`wf rewind` 到对应阶段并记录原因。

## 附录：外层域审计结论（147/147 文件全覆盖，两轮独立子代理逐行通读）

| 域 | 判定 | 代表证据 |
|---|---|---|
| Procedure | 混装（流程 + 会话所有权 + 地图数据 + UI 加载面 + 规则播种） | `Session/*`、`Setup/{MapProvider,LaunchLoading,MatchRulesSeeder}.cs` |
| Presentation | 混装（屏空间 HUD + 世界表现 + GM 命令处理器） | `Battle/Rig/BattleViewRig.cs:81`、`Battle/Commands/TeleportToAimHandler.cs` |
| Meta | 混装（HTTP/wire + 部署 IO + 账号重入 + Lua 门面） | `DeployBake.cs`（5 可变静态）、`AccountReentry.cs`、`MetaBridge.cs` |
| Ui | 混装（导航 + 结算算法 + 联机门面） | `ResultRanking.cs:48`、`PageFlow : ILoginSurface` |
| Config | 混装（端口 + 常量/摘要 + Sim 投影） | `LuaLayout.cs`、`SimConfigMapper.cs`、`MapTableProjection.cs` |
| Product | 错位 | `ProductSettings.cs`、`LTextCatalog.cs` |
| Battle | 基本自洽；1 处拆分错位 | 命令本体 vs 处理器 |
| View | 自洽（Animation→SimView 单向） | 依赖方向已核 |
| Settings | 自洽；1 处具体服务耦合 | `AudioSettingsApplier.cs`、`ILookPreferences.cs:13` |
| Input / Camera / Hosting / Recovery | 自洽 | Camera 为绝对叶子 |
| Room | 名存实亡 | 3 文件＝2 契约 + 1 透传门面 |
| Map | 名存实亡（组件须留运行时） | `MapBoundsMarker` 被 `SimMapBaker.cs:408` 读、场景序列化 |

## 修订 7：LiteNet 边界（2026-10-30 实测）

### 1. 职责实况（61 个 .cs）

- 纯网络：`Core/{Transport,Security,Client}`、`Core/Protocol/{PacketType,PacketCodec,PacketWriter}`、`Adapters/{KcpTransport*,kcp2k}`、`Core/Proto/Generated`（纯 BCL）。
- **不是网络本身的两处**：
  - `Core/Diagnostics/DiagTrace.cs`：通用诊断环（被 `LiteGame.Adapters.Dev` 的 `DebugKernel`/`DebugConsole`/`DevHud` 读），只有 `JoinKey` 是网络的 → **保留**，登记为"LiteNet 承载通用诊断环"的已知例外（`JoinKey` 是两端/两域口径对齐的单源，拆开有分叉风险）。
  - `Core/Diagnostics/WeakNet/*`：开发期弱网注入，只在 `DevAssemblyImpl` 与测试 `new`，却随运行时程序集发布 → **移 `LiteGame.Adapters.Dev`**（它只是 `IClientTransport` 装饰器；LiteNet 只留 `IClientTransport`/`IClientTransportErrorSource`）。**已定，并入批 5。**

### 2. `LiteNet → LiteSim` 能否去掉

实测耦合面：**11 个文件** `using LiteSim`，分四类——

| 类型 | 使用点 |
|---|---|
| 判定状态 `SimWorldState`/`EntitySlot` | `Protocol/{SnapshotSource,SnapshotCodec,SnapshotDiffer,SnapshotReassembler,AoiFilter}` |
| 输入结构 `SimInputFrame` | `Protocol/InputPacker`、`Client/{RoomClient,RoomOutbound,InputWindow}` |
| 判定常量 `SimConfig`（`AoiCellSize`/`AoiRadius`/`MaxEntities`/`AoiGridExtentMeters`） | `Protocol/{AoiFilter,ProtocolConstants}` |
| 值类型 `SimVector3` | `Protocol/{AoiFilter,CombatEventCodec,SnapshotSource}` |

- **能去掉，但只有一种形态**：把"协议 ↔ 判定状态映射"整体搬到第三个程序集（如 `Assets/LiteSync/`，依赖 `LiteNet`+`LiteSim`），LiteNet 只留包封装 + 传输 + 安全 + 会话外壳；`Proto` 生成物零 LiteSim 依赖，可留在 LiteNet。
- 因为映射必须同时认识两侧类型，"去掉引用"≠"去掉耦合"，只是把耦合搬进一个明确叫"同步协议"的模块。
- **硬代价（实测）**：
  1. `scripts/codegen/gen-build-hash.py`：`SHARED_TARGETS` 含 `Assets/LiteNet/Core/{Proto,Protocol}`，`SHARED_OUT_FILE = Assets/LiteNet/Core/Protocol/BuildHash.g.cs`，生成物 `namespace LiteNet` → 路径/命名空间/消费点（握手、`RoomClient`、`BuildIdentity`）全要改。
  2. 哈希值必变 → **两端必须重建**，`--check-shared` 门禁重跑。
  3. `RoomClient.SendInput(in SimInputFrame, …)` 要改字节面，传导到 `BattleClient` 与 RoomServer 调用点。
- **建议（待裁决）**：不搬，改为"收窄耦合面"——守卫钉死 `using LiteSim` 只允许出现在 `Core/Protocol/**` 与 `Client/{RoomClient,RoomOutbound,InputWindow}.cs`，其它文件出现即红。理由：这是本质耦合（线格式与判定状态一一对应，已有"协议单源"裁决）；本项目只有一套 sim、无第二消费者；搬走反而引入"形状双份 + 一次转换拷贝"的漂移风险。
- 若裁决要搬：作为批 6 独立执行，顺序 = 建 `LiteSync` → 搬 `Protocol/{Snapshot*,Input*,CombatEventCodec,AoiFilter,ProtocolConstants}` 与 3 个 Client 文件的 Sim 面 → 改 `gen-build-hash` 路径/命名空间 → 两端重建 → 联测。

### 3. 追加验收标准

- [ ] AC35（批 5）：`Assets/LiteNet/Core/Diagnostics/WeakNet/` 不存在；四个 WeakNet 类与 `WeakNetRuntime` 同居 `Assets/LiteGame/Adapters/Dev/`；`Tests/LiteNet.Tests` 的 WeakNet 用例改引用后仍绿。
- [ ] AC36（批 5）：`DiagTrace` 保留在 `LiteNet/Core/Diagnostics/` 并登记理由；Dev 侧只经 `DebugKernel` 门面读取，不新增直读点。
- [ ] AC37（批 5 或批 6）：`using LiteSim` 只出现在白名单文件（`Core/Protocol/**` + `Client/{RoomClient,RoomOutbound,InputWindow}.cs`）；若裁决搬迁，白名单收缩为空且 `LiteNet.asmdef` 删除 `LiteSim` 引用。

## 修订 8：批 6「LiteNet 去耦」定义（已裁决执行 C 方案）

> 本节为批 6 的权威定义，取代修订 7 第 2 节的"建议（待裁决）"。AC37 由批 6 承接（白名单收缩为空）。

### 1. 目标

`LiteNet` 只做传输，**零判定内核依赖**；"协议 ↔ 判定状态映射"整体迁入新程序集 `LiteSync`。依赖方向由 `LiteNet → LiteSim`（反向）修正为 `LiteSync → {LiteNet, LiteSim}`，两侧互不认识。

### 2. `LiteSync` 的职责定义

**一句话**：同步协议层——负责让两端看到同一个世界：把判定状态/输入/战斗事件编成线格式、再从线格式还原写回判定状态，并决定何时全量、何时差分、对谁裁 AOI。

**边界表**

| 层 | 负责 | 不认识 |
|---|---|---|
| `LiteNet`（传输） | 连接、KCP、加密信封、分片、包封装（`PacketType`/`PacketCodec`/`PacketWriter`）、线 DTO（`Proto`）、诊断环 | 判定状态、同步策略 |
| **`LiteSync`（同步协议）** | 状态↔线的双向映射、差分/全量判定、AOI 裁剪、基线推进、冗余输入窗口、重连相位与质量统计 | Unity、UI、业务决策 |
| `LiteSim`（判定） | 确定性模拟、checksum | 网络、线格式 |
| `LiteGame`（业务） | 玩法与表现 | 字节 |

**它拥有四块**
1. 状态线格式：`SnapshotCodec`（公共面/私有面）、`SnapshotDiffer`（基线差分）、`SnapshotReassembler`（客户端重建）、`AoiFilter`（可见集）。
2. 输入线格式：`InputPacker`、`InputWindow`。
3. 事件线格式：`CombatEventCodec`。
4. 同步策略与会话相位：`ISnapshotSource`（`BeginFrame`/`BuildFor`/`NeedsFull`）、`RoomClient`/`RoomOutbound`/`RoomInbound`/`RoomSession`/`ReconnectState`/`RoomClientDiagnostics`、`ProtocolConstants`。

**它明确不拥有**
- 不跑判定（不 Tick 世界、不算 checksum，只读 `LiteSim`）；
- 不碰表现（无 Unity/Transform/插值；插值在 `Battle/Arena`）；
- 不做业务决定（回哪个页面、是否提交结算归 `LiteGame`）；
- 不管字节怎么送到（KCP/信封/MTU/重传归 `LiteNet`）。

**判据表（改动动机 → 改哪里）**

| 改动动机 | 落点 |
|---|---|
| 换传输（KCP→QUIC）、改加密、改分片 | `LiteNet` |
| 加快照字段、改 AOI/冗余窗口/重连握手 | `LiteSync` |
| 改玩法规则、加 per-entity 判定状态 | `LiteSim`（状态）+ `LiteSync`（线格式） |
| 改背包页/结算页 | `LiteGame` |

**对外接口面（很窄）**
- 服务端：`ISnapshotSource`（`BeginFrame` / `BuildFor` / `NeedsFull` / 全量周期判据）。
- 客户端：`RoomClient`（`SendInput` / 快照接收与写回 / 相位与重连）。
- 分界线：**payload 怎么编码归 LiteSync，包怎么封与送归 LiteNet**。

**哈希源集要求（硬）**：`LiteSync` 属两端共编的同步协议，必须进 `SHARED_TARGETS`——漏登记 = 改了同步协议却不改 BuildHash = 握手通过但线格式不一致（静默分叉）。

### 3. 搬迁清单（文件级）

**`LiteNet → LiteSync`（13 件 + 各自 `.meta`）**

| 源路径 | 迁入 | 说明 |
|---|---|---|
| `Core/Protocol/Abstractions/SnapshotSource.cs` | `LiteSync/Snapshot/` | 签名含 `SimWorldState`/`SimVector3`，必须随迁 |
| `Core/Protocol/SnapshotCodec.cs` / `SnapshotDiffer.cs` / `SnapshotReassembler.cs` / `AoiFilter.cs` | `LiteSync/Snapshot/` | 状态↔线映射、差分、AOI |
| `Core/Protocol/InputPacker.cs` | `LiteSync/Input/` | `SimInputFrame ↔ Proto.InputFrame` |
| `Core/Protocol/CombatEventCodec.cs` | `LiteSync/Events/` | `CombatEventRecord ↔ wire Entry` |
| `Core/Protocol/ProtocolConstants.cs` | `LiteSync/` | 用 `SimConfig.AoiRadius` 等同步参数 |
| `Core/Client/RoomClient.cs` / `RoomOutbound.cs` / `RoomInbound.cs` / `RoomSession.cs` / `ReconnectState.cs` / `RoomClientDiagnostics.cs` / `InputWindow.cs` | `LiteSync/Client/` | 会话相位 + 输入窗口 + 快照应用（含 `SimInputFrame` 面） |

**`LiteNet` 保留**

| 路径 | 说明 |
|---|---|
| `Core/Protocol/{PacketType,PacketCodec,PacketWriter}.cs` | 包封装（payload 类型 + 长度 + 字节） |
| `Core/Protocol/BuildHash.g.cs` | 生成物**留在原位、保持 `namespace LiteNet`**，避免动握手消费点 |
| `Core/Proto/{battle.proto,Generated/**}` | 线 DTO，纯 BCL，零 `LiteSim` 依赖 |
| `Core/Transport/**`（含 `Security/`） | KCP/信封/地址/`IClientTransport`/`IClientTransportErrorSource` |
| `Core/Diagnostics/DiagTrace.cs` | 通用诊断环 + `JoinKey` 单源（AC36 保留） |
| `Core/Diagnostics/WeakNet/**` | **批 5 已裁：移 `LiteGame.Adapters.Dev`** |
| `Adapters/{KcpTransportClient,KcpTransportServer,kcp2k/**}` | 传输适配 |

**明确不迁**：`NetworkQuality.cs` / `NetworkQualityProbe.cs`（纯 RTT/丢包统计，无 sim 依赖，留 `LiteNet/Core/Client/`）。

### 4. 程序集与构建

- 新建 `Assets/LiteSync/LiteSync.asmdef`：`references: ["LiteNet","LiteSim"]`、`noEngineReferences: true`、`overrideReferences: true` + `precompiledReferences: ["Google.Protobuf.dll"]`。
- 新建 `Assets/LiteSync/LiteSync.csproj`（双轨）：`ProjectReference` 指 `LiteNet.csproj` + `LiteSim.csproj`，`PackageReference Google.Protobuf 3.36.1`，`LangVersion 9.0`。
- `LiteNet.asmdef` 删 `LiteSim`；`LiteNet.csproj` 删 `ProjectReference LiteSim`，并把已迁文件的 `Compile Include` 移除（其 `Core/Protocol/**`、`Core/Client/**` 通配要收窄为保留项）。
- 引用者加 `LiteSync`：`LiteGame.App`、`LiteGame.Adapters.Dev`、`LiteGame.Editor`、`Tests/EditMode`、`Tests/UI/PlayMode`、`Tests/LiteNet.Tests`（或新建 `Tests/LiteSync.Tests` 承接迁走的用例）、`Server/RoomServer`。
- 守卫同步：`ClientAsmdefAllowlistTests` 增 `LiteSync` 条目 + 引用者边；`ArchitectureGuardTests` 增两条硬边——`LiteNet` 不得引 `LiteSim`/`LiteSync`；`LiteSync` 不得引 `LiteGame.*`/`UnityEngine`。

### 5. 哈希源集改动

1. `scripts/codegen/gen-build-hash.py` 的 `SHARED_TARGETS` 增 `Assets/LiteSync`（或其子目录全集），保留 `Assets/LiteNet/Core/{Proto,Protocol}`、`Assets/LiteNet/Core/Transport/Security`。
2. `SHARED_OUT_FILE` 与生成物 `namespace LiteNet` **保持不变**（仍写 `Assets/LiteNet/Core/Protocol/BuildHash.g.cs`），避免改握手端 `LiteNet.BuildHash.Value` 消费点。
3. 哈希值必变 → **两端必须重建**，`--check-shared` 重跑并记录新值。
4. 注释头同步（gen-build-hash 顶部"源：LiteSim… + LiteNet/Core/Proto + LiteNet/Core/Protocol"需加 LiteSync）。

### 6. 子步与顺序

- T1 建 `LiteSync`（asmdef + csproj + 空壳目录），跑通编译与 L1（此时无行为变化）。
- T2 搬 13 件（`AssetDatabase.MoveAsset`，`.meta` 随迁）+ 收窄 `LiteNet.csproj` 通配 + 补 `LiteSync.csproj` 通配。
- T3 引用者与测试：`LiteGame.*` / `RoomServer` / `Tests` 加引用；迁走用例后跑定向。
- T4 改 `gen-build-hash.py`（源集 + 注释），重生成，`--check-shared` 记录新值。
- T5 守卫：允许表 + 两条硬边 + `using LiteSim` 白名单清空。
- T6 两端重建 + 联机冒烟（本地测试房 + 真 KCP 弱网）。

### 7. 追加验收标准

- [ ] AC38：`Assets/LiteSync/` 存在，`LiteSync.asmdef`（`references:["LiteNet","LiteSim"]`、`noEngineReferences:true`、Protobuf 预编译引用）与 `LiteSync.csproj` 齐备。
- [ ] AC39：`LiteNet.asmdef` 与 `LiteNet.csproj` 均不再引用 `LiteSim`；`Assets/LiteNet/**` 内 `using LiteSim` **零命中**（AC37 白名单清空）。
- [ ] AC40：搬迁清单 13 件全部到位且 `.meta` 随迁（`git status` 成对出现，无 GUID 重建）；`LiteNet` 保留项与清单一致。
- [ ] AC41：`gen-build-hash.py` 的 `SHARED_TARGETS` 含 `Assets/LiteSync`；`BuildHash.g.cs` 路径与 `namespace LiteNet` 未变；`--check-shared` 通过并记录新哈希值。
- [ ] AC42：`ClientAsmdefAllowlistTests` 含 `LiteSync` 条目且全绿；`ArchitectureGuardTests` 两条新硬边生效（`LiteNet` 不得引 `LiteSim`/`LiteSync`；`LiteSync` 不得引 `LiteGame.*`/`UnityEngine`）。
- [ ] AC43：L1 全量绿（含迁移后的同步/协议用例）；`Tests` 内迁移用例无丢失。
- [ ] AC44：两端重建后联机冒烟通过（本地测试房 + 真 KCP 弱网预设），两端 `BuildHash` 一致；`RoomServer` 与客户端均引用 `LiteSync`。
- [ ] AC45：文档回写（程序集引用图、代码地图、说明/04 双端数据流、进度卷新章），并在发布说明标注"两端需重建"。

### 8. 风险

- 哈希值变更导致旧构建互不兼容——本项目已裁决"客户端一个包都没发过"，无兼容负担；但仍须在两端同时重建后再联测。
- `LiteSync.csproj` 通配若与 `LiteNet.csproj` 重叠（同一文件被两侧编译）会 CS0433/重复定义——T2 必须逐目录核通配。
- `AssetDatabase.MoveAsset` 漏搬 `.meta` → GUID 重建、prefab/场景引用断裂（本项目两次实伤）。
- `RoomClient` 迁走后，`LiteGame`/`RoomServer` 的 using 与构造点要全量排查；漏一处即编译红（可见）。

## 修订 9：批 7「LiteSim 参数化」定义（已裁决：几何常量装载化 + AOI 可配 + RootMotion 迁出）

### 1. 目标

`LiteSim` 只保留**确定性结构常量 + 判定逻辑**；判定用的数值来自**装载面**（可运行期更新、进 digest）；表现数据（根运动）与网络广播参数不再住在判定内核。

### 2. 三类参数与处置（判据：是否参与位布局/协议结构、是否必须两端逐位一致）

| 类别 | 成员 | 处置 | 理由 |
|---|---|---|---|
| **结构常量（不动）** | `TickRate` `Dt` `MaxEntities` `InputDelay` `MaxCatchUp` `MaxPredictionLead` `MaxRollbackFrames` `InterpFrames` `LagCompHistory` `MaxInputHistory` `MaxRollbacksPerFrame` `CustomBytesPerEntity` `GlobalsBytes` `WeaponSlotsPerEntity` `ActionSlotsPerEntity` `StatusSlotsPerEntity` `MatchBagSlotsPerEntity` | **保持编译期常量** | 决定数组尺寸、proto 位布局、回滚环容量、checksum 折叠范围；运行期改 = 越界/协议不兼容 |
| **判定几何（装载化，含裁决常量）** | 烘焙族：`BodyBake.Radius/Height`、`HeadBake.Ratio/Radius`、`MuzzleBake.Forward/Right/Height`；**裁决常量族**：`HitscanRadius`(0.45)、`HeadshotDamageShift`(1)、`HipSpreadFactor`(4)、`CorpseFrames`(180)、`HardMaxSpeed`(25)、`FaceTurnRadPerSec`(12) | **改运行时装载**（13 字段全并入 `CombatValues` ← `#combatgeom`） | 都是影响判定结果/手感的数值，不参与位布局。**裁决常量不再豁免**——过去靠"编译期常量 + R13 常量面豁免"放行，装载化后豁免取消 |
| **广播 / AOI（可配）** | `SnapshotHz`、`AoiCellSize`、`AoiRadius`、`AoiGridExtentMeters` | 批 6 迁入 `LiteSync.ProtocolConstants`；批 7 改为**服务端运行期可配** | 实测只有服务端读（`AoiFilter`/`SnapshotSource`），客户端只收包 ⇒ 不需要两端同值 |
| **表现数据（迁出）** | `RootMotionData.g.cs`、`RootMotionCatalog` | **迁 `Modules/Battle/Arena/Generated/`** | 运行时唯一消费点是 `ContainerModule.cs:167` 的 `ContentDigest`；判定与动画驱动均零读取 |

### 3. 装载面设计（几何**并入 `CombatValues`**，不新增第二条参数通道）

**判据**：判定侧唯一的机制参数通道是 `SimStep.Step(..., in CombatValues values, WeaponTable weapons, MatchTestRules rules, ...)`；再开一个 `SimGeometryValues` 会让"哪个值走哪条通道"变成需要记的知识。因此**几何并入 `CombatValues`（8 → 15 字段）**，`SimStep` 与 9 个 `System.Run` 签名不动，改动集中在"取值表达式"。

**参数放哪**

| 参数 | 住哪 | 谁产 | 谁读 |
|---|---|---|---|
| 结构常量（`TickRate`/`MaxEntities`/回滚深度/各槽位数） | `LiteSim/Scripts/Config/SimConfig.cs`（`const`，不动） | 编译期 | 全体 |
| 玩法数值（移速/重力/射程/伤害/HP/窗长） | Luban `#combatnum`/`#movementconfig`/`#entityconfig` | `SimConfigMapper.BuildCombatValues` | `in CombatValues` |
| **判定几何**（身位/爆头带/枪口/命中宽容/转向速率/尸体帧/腰射散布/硬速上限） | **新表 `Luban/Data/#combatgeom.xlsx`（单行）** | 同上（多传一张表） | `in CombatValues` |
| AOI / 广播（`SnapshotHz`/`Aoi*`） | `LiteSync/ProtocolConstants.cs` 的 `AoiSettings` | 服务端装配点注入 | 只有服务端 |
| RootMotion 数据 | `Modules/Battle/Arena/Generated/RootMotionData.g.cs` + `RootMotionCatalog.cs` | 烘焙器产数据行 | 目前只有 `ContentIdentity.RootMotionDigest` |

**`#combatgeom.xlsx` 字段（单行，字段即几何）**：`body_radius` · `hitscan_height` · `hitscan_radius` · `head_ratio` · `head_radius` · `headshot_damage_shift` · `hip_spread_factor` · `corpse_frames` · `hard_max_speed` · `face_turn_rad_per_sec` · `muzzle_fwd` · `muzzle_right` · `muzzle_height`。接入四处：`Luban/__tables__.xlsx` 一行、`Config/TableLayout.cs` 的 `Files` 加 `"combatgeom"`（**判定面 ⇒ 同时进 `JudgmentDomains`**）、`ConfigService.TableDataFiles` 加一项、`ServerTableLoader` 自动同源。

**客户端装载链（实测形状，不新造）**

```
Assets/GameData/Config/*.bytes
  → ConfigService.ValidateCandidate(tables)          // 缺行/NaN/非正/超界 → 拒绝启动
  → ApplyCombatValues(tables)
      SimConfigMapper.BuildCombatValues(tables.Tbcombatnum, tables.Tbmovementconfig,
                                        tables.Tbentityconfig, tables.Tbcombatgeom)   // ← 新增第 4 参数
  → new CombatValues(...15 字段...)
  → CombatConfig.Publish(values)                     // 仅"表现面"便捷读口
```

**服务端装载链**

```
同一份 .bytes → ServerTableLoader → ServerTableLoad(Digest) → HostAssembly.Inputs（必填）
  → ServerHost → RoomInstance → RoomRuntime → FixedCombatConfig.Capture(values, weapons, base)
  → 房间摘要 CombatConfigDigest.WithTestRules(base, 规则位) → StartGame.ConfigHash 下发 → 客户端复算比对
```

**判定消费（唯一通道）**

```csharp
SimStep.Step(SimWorldState s, in SimMapData map, SimInputFrame[] inputs,
             in CombatValues values, WeaponTable weapons,
             bool advanceMatch = true, MatchTestRules rules = null, bool resolveCombat = true)
```

系统把原来读 `CombatConfig.BodyRadius/HitscanRadius/HeadHitLine/MuzzleOffset*` 的表达式改为读 `values.*`。

**生效时机与失败语义**

| 场景 | 行为 |
|---|---|
| 表缺失 / NaN / 非正 / 几何越界 | `ValidateCandidate` 拒绝启动 |
| 客户端未 `Publish` 就取几何 | 抛异常（**禁止**回落默认值，否则两端各用一套默认 → 每帧假和解） |
| 对局中改表 | 不生效（进对局前装载并随房间快照固化） |
| AOI 未 `Configure` | 用默认值 + Warning；`Configure` 晚于首个广播帧 → 拒绝 |

**一致性**：`tbcombatgeom` 进 `JudgmentDomains` ⇒ 几何值随 `CombatConfigDigest` → `GameplayDigestBase` → `ContentIdentity`（既有链路），两端不一致当场拒局。

**改动文件清单**

- LiteSim：`CombatConfig.cs`（删几何 `const`，读口改投影）、`CombatValues.cs`（+7 字段）、`Systems/ShootingSystem.cs` 及任何读几何的系统（改读参数）。
- 配置：`Modules/Config/SimConfigMapper.cs`（`BuildCombatValues` 加参数 + 校验）、`Adapters/Luban/ConfigService.cs`（多传一张表）。
- 表：`Luban/Data/#combatgeom.xlsx`（新）、`__tables__.xlsx`、`Config/TableLayout.cs`。
- LiteSync：`ProtocolConstants.cs`（`AoiSettings` + `Configure`）、`AoiFilter.cs`（去 `static readonly` 缓存）。
- 烘焙：`BodyCylinderBaker`/`HeadHitLineTuner`/`MuzzleOffsetBaker` 改产表行；`BakeTargets.cs` 四条死路径修正。
- RootMotion：`RootMotionData.g.cs` + `RootMotionCatalog.cs` → `Modules/Battle/Arena/Generated/`；`ContainerModule.cs:167` 改由装配点汇入；`gen-build-hash.py` 路径更新（**条目保留**）。

**明确不动（不在批 7 范围）**：`SimConfig` 结构常量、`SimMath`/`SimRaycast`/`SimSpread` 的算法 epsilon、`SimInputFrame` 按钮位、`EntitySlot` 标志位——动这些属于"全参数外部化"，要同时改协议与所有签名，另批评估。

### 4. 烘焙工具改产出

- `BodyCylinderBaker` / `HeadHitLineTuner` / `MuzzleOffsetBaker`：从"写 `*.g.cs`"改为"写 `tbcombatgeom` 表行（或导出 xlsx 数据行）"。
- `BakeTargets` 的四个 `*Output` 常量（`BodyBakeOutput`/`HeadBakeOutput`/`MuzzleBakeOutput`/`RootMotionOutput`）**当前仍写 `Assets/LiteSim/Core/Scripts/...`，该 `Core/` 目录早已拍平 → 死路径**，本批一并修正；`RootMotionOutput` 改为 `Arena/Generated` 路径。
- 编辑器工具（`HeadHitLineTuner`/`GmTestModePage`）读现值的方式改为读表/读装载值。

### 5. AOI 运行期可配

- `ProtocolConstants` 的四个 AOI/广播常量改为 `AoiSettings` 值对象 + 默认值 + `Configure(in AoiSettings)`；服务端装配点从房间配置/命令行注入，客户端不读。
- `AoiFilter` 现在用 `static readonly Cell/HalfCells` 缓存派生值 ⇒ **不得用静态缓存承载可配值**，改为实例初始化时计算（一进程多房间必须互不影响；即便当前一进程一房，也不留静态陷阱）。

### 6. RootMotion 迁出 + 改数据产物 + 身份汇入

**形态变更（本条并入批 7）**：`RootMotionData.g.cs` 是"逐帧数值数组写成代码"，属最坏的一种产物形态（改数据要重编译、4096 级数组塞进源文件）。本批改为**数据产物**：

1. 烘焙器产出 `Assets/GameData/Config/runtime/rootmotion.bin`（毫秒/毫米量化的逐帧数组，格式沿用现有 `RootMotionEntry` 字段序，加一个 `formatVersion` 头）。
2. `RootMotionCatalog` 改为**数据读口 + 装载面**：`Load(ReadOnlySpan<byte>)`（零依赖纯解析，住 `Modules/Battle/Arena/`），不再有 `partial` 生成部分。
3. `RootMotionDigest` 直接对 `.bin` 字节算 SHA-256（比现在的"规范化文本"更简单且逐位一致），移除 `BakedContentDigest` 常量。
4. 装载：客户端 `ConfigService`（与表同一装载阶段）读 `.bin` → `RootMotionCatalog.Load`；服务端当前**不装载**（它不读 RootMotion）。
5. `ContentIdentity` 仍按汇入式取 digest（`ContainerModule.cs:167` 改为传 `Arena` 提供的 digest）。
6. **BuildHash 语义随之澄清**：`.g.cs` 形态时它作为**代码**进 `SHARED_TARGETS`；改数据产物后 `.bin` 属**数值面**，**不进 BuildHash**（与 `.bytes` 同待遇），一致性由 `ContentIdentity.RootMotionDigest` 承担。⚠️ 将来若服务端要接线根位移，必须要么把 reader 下移到两端共用程序集、要么把该文件纳入"两端内容身份"清单——两者都要显式登记，不能默认。
7. **"未接线"状态登记**：判定 `Systems/*`、`Pipeline/*`、动画驱动均不读它（实测唯一消费点是 digest）；迁移不得顺手删代码，也不得拆掉 digest 链路。

### 7. 子步

- T1 `CombatValues` 扩 7 个几何字段（8 → 15）+ `CombatConfig` 删几何 `const` 改投影 + 未 `Publish` 即抛。
- T2 `#combatgeom.xlsx` 表 + 四处接入（`__tables__`/`TableLayout.Files`+`JudgmentDomains`/`ConfigService`/`ServerTableLoader`）+ `SimConfigMapper.BuildCombatValues` 加第 4 参数与校验。
- T3 系统改读 `values.*`（`ShootingSystem` 等原来读 `CombatConfig.*` 的点），机制面零静态读。
- T4 烘焙工具改产表行 + `BakeTargets` 四条死路径修正 + 删 `BodyBake/HeadBake/MuzzleBake.g.cs`。
- T5 AOI：`AoiSettings` + `Configure` + `AoiFilter` 去静态缓存 + 服务端装配点注入。
- T6 `RootMotion*` 迁 `Battle/Arena`（`AssetDatabase.MoveAsset`）+ `ContentIdentity` 汇入式 + `gen-build-hash.py` 路径更新（条目保留）。
- T7 守卫与门禁。

### 8. 追加验收标准

- [ ] AC46：`CombatValues` 含 7 个几何字段（body_radius/hitscan_height/hitscan_radius/head_ratio/head_radius/muzzle_fwd/right/height 等），几何经 `in CombatValues` 参数进判定；未 `Publish` 时几何读口抛异常（有用例）。
- [ ] AC47：`tbcombatgeom` 表入库、`SimConfigMapper` 映射、几何值进 `CombatConfigDigest`；改表后 `--check-shared` 哈希变化可观测。
- [ ] AC48：`BodyBake.g.cs`/`HeadBake.g.cs`/`MuzzleBake.g.cs` 不存在；判定几何不再来自编译期常量；烘焙工具输出表行。
- [ ] AC49：`BakeTargets` 四个 `*Output` 路径均存在（守卫：父目录不存在即红）；`LiteSim/Core/` 死路径消失。
- [ ] AC50：`AoiSettings` 可运行期注入且服务端生效（AOI 关闭/收缩档位有被测）；`AoiFilter` 无 `static readonly` 派生缓存。
- [ ] AC51：`LiteSim` 内不再出现 `RootMotion*`；`RootMotionCatalog`（数据读口）与 `rootmotion.bin` 装载面位于 `Modules/Battle/Arena/`；`RootMotionData.g.cs` 不存在。
- [ ] AC52：`ContentIdentity` 不引用 `LiteSim` 类型；装配点以 `(名称, digest)` 汇入，`RootMotionDigest` 取值来源为 `Arena`（对 `.bin` 字节算 SHA-256）。
- [ ] AC53：`rootmotion.bin` 属数值面**不进 `SHARED_TARGETS`**；`RootMotionData.g.cs` 的旧条目从源集移除；`--check-shared` 通过并记录新值；`ContentIdentity.RootMotionDigest` 端到端可观测（两端口径不一致可判）。
- [ ] AC54：守卫新增——`SimConfig` 不得含 `Aoi*`/`SnapshotHz`；`LiteSim` 不得引用 `RootMotion*`；`CombatConfig` 几何访问必须先装载。
- [ ] AC55：文档回写（判定参数装载面、地图/表现数据归属、进度卷新章）。
- [ ] AC56：`RootMotion` 的"数据已烘焙进两端哈希、执行层未接线"状态在生成物文件头与设计文档显式登记，避免被误判为死代码删除。
- [ ] AC57：接线前提固化——判定侧消费（`MovementSystem` 按 action 帧查 `RootMotionEntry`）；`LiteAnimator.applyRootMotion` 接线时必须为 false；Y 轴不进权威位移（`YPeakMm` 仅校验）。
- [ ] AC58：装载时序有用例——`ProcedurePreload` 装载后客户端才 `Publish`/服务端才注入几何；未装载时判定入口抛；对局中改表不生效（快照参数不中途变）。
- [ ] AC59：机制面零静态读——`Assets/LiteSim/Systems/**` 与 `Pipeline/**` 不再出现 `CombatConfig.<几何名>`（`BodyRadius`/`HitscanHeight`/`HitscanRadius`/`HeadHitLine`/`HeadshotRadius`/`MuzzleOffset*`/`FaceTurnRadPerSec`/`CorpseFrames`/`HipSpreadFactor`/`HardMaxSpeed`）；守卫白名单为空。
- [ ] AC60：`SimStep` 与 9 个 `System.Run` 签名未变（仅取值表达式改为 `values.*`），`FrameDriver`/`RollbackSim`/`SimReplayRunner` 调用点零改动。
- [ ] AC61：`#combatgeom` 四处接入齐全（`__tables__` 一行 / `TableLayout.Files` / `TableLayout.JudgmentDomains` / `ConfigService.TableDataFiles`），服务端同源直读无需另加清单；`--check-shared` 记录新哈希。

### 9. 风险

- 几何常量装载化把"编译期保证"换成"启动期校验"：漏装即运行期抛（有意设计，非静默）；但**表行缺失/字段错**会阻断启动，需要表守卫（`SimLayoutContractTests` 同款）先红。
- AOI 可配后服务端行为依赖注入顺序：`Configure` 必须早于首个广播帧，装配点要在建房前完成。
- `AoiFilter` 去静态缓存会改动热路径对象布局（每帧调用），需跑一次性能冒烟确认无回归。
- **`RootMotion` 迁目录但保留在哈希源集**：它是两端共享的只读玩法数据；一旦被误移出 `SHARED_TARGETS`，就变成"根位移数据两端不同、握手仍通过"的静默分叉。
- `RootMotion` 当前**未接线**（运行时只有 `ContentDigest` 被读）：迁移不得顺手删代码，也不得拆掉 digest 链路。
- `CombatValues` 从 8 字段扩到 15 会改构造签名：`SimConfigMapper`、`CombatValues.Default`、L1 用例（`CombatValuesEditModeTests` 等）与任何直接 `new CombatValues(...)` 的点都要同步；漏一处即编译红（可见）。
- 几何并入后，`CombatValues.Default` 必须与表值一致（既有"默认值=表值"守卫继续生效），否则未装载路径会静默用默认值。

### 10. 调试与导出（复用既有调参面，不新造机制）

**可复用件（实测存在）**：`IDebugTunableProvider` + `FeatureDebugTunables`/`WeaponDebugTunables`（`LiteGame/Adapters/Dev/Debug/Tunables/`，DevHUD/GM 自动收集渲染）；`BakingToolbox`（`LiteGame/Editor/Baking/`，四页：身位/爆头带/枪口/根位移）；`HeadHitLineTuner`（编辑器 + GM **共用同一导出口径**）；`BakeTargets.WriteGenerated`（现有导出实现，写 `.g.cs`）。

**调试链路（运行期）**

```
滑杆改值 → SimParamsOverrides（dev 三宏内 override 容器）
        → 表值 ← override 合成 CombatValues
        → CombatConfig.Publish(合成结果)        // 仅表现面读口
        → 下一局开房时取新实例传入 SimStep      // 机制面仍走 in 参数
```

约束：**判定参数对局中不生效**（提示"下一局生效"）——几何/数值进 `ConfigHash`，对局中换值会让两端摘要不一致。只有本地测试房（同机双端可同时重建）允许对局内试。

**导出链路（调试结果 → 表）**

```
GM 面板 / 编辑器滑杆 → 「导出到表」
  → SimParamsExporter（Editor）：仅改 Luban/Data/#combatgeom.xlsx 目标单元格（python + openpyxl，保留其余列与格式）
  → 自动 Luban/gen.bat（重生成 cfg.* 与 .bytes）
  → 自动 gen-build-hash.py --check-shared（打印新哈希）
  → 弹窗 diff：override vs 表原值 vs 新值 + "两端需同表/重建，否则联机拒局"
```

四条导出纪律：① **只写表、不写生成物**（生成物永远由 Luban 产，删除 `BakeTargets.WriteGenerated` 路径）；② 导出即改 digest，必须提示两端同步；③ 导出前跑 `ValidateCandidate` 同款校验（非正/越界/NaN 拒绝）；④ 导出前备份原 xlsx 到 `.codely-cli/tmp/`，支持一键还原。

**烘焙类**：`BakingToolbox` 四页保留，导出目标由 `.g.cs` 改为 `#combatgeom.xlsx`；根位移仍产 `RootMotionData.g.cs`（逐帧数组不适合塞表），路径改 `Modules/Battle/Arena/Generated/`。

**AOI 调试**：本地测试房在 GM 页给"全开 / 30m / 15m"三档 → `AoiSettings.Configure`，下次广播生效；真联机不暴露。

**新增/修改件**

| 件 | 内容 |
|---|---|
| `LiteGame/Adapters/Dev/Debug/Tunables/SimParamsDebugTunables.cs` | 分组可调项（Combat / Geometry 13 项 / Movement / Weapon / AOI），接 `IDebugTunableProvider` |
| `LiteGame/Adapters/Dev/Debug/Tunables/SimParamsOverrides.cs` | override 容器 + 合成 `CombatValues` |
| `LiteGame/Editor/Baking/SimParamsExporter.cs` | xlsx 单元格写回 + diff + 备份/还原 + 触发重生成 |
| `BakingToolbox` / `HeadHitLineTuner` / `MuzzleOffsetBaker` / `BodyCylinderBaker` | 导出目标改表；删 `WriteGenerated` 调用 |
| `LiteGame/Editor/Gm/GmTestModePage.cs` | 新增 "Sim 参数" 页（复用同一 provider）+ AOI 档位（本地测试房） |

**追加验收标准**

- [ ] AC62：`SimParamsDebugTunables` 接 `IDebugTunableProvider`，DevHUD/GM 自动出现全部判定参数滑杆（含几何 13 项）。
- [ ] AC63：滑杆改值 → 下一局生效；对局中改提示"下一局生效"（用例或手测记录）。
- [ ] AC64：编辑器与 GM 两处"导出到表"都写 `#combatgeom.xlsx`；导出后自动 `gen.bat` + `--check-shared` 并显示 diff 与"两端需重建"提示。
- [ ] AC65：`BakeTargets.WriteGenerated` 不再被任何烘焙工具使用；四条死路径修正；导出前备份可一键还原。
- [ ] AC66：调试面全部在 dev 三宏内（release 无 `SimParamsOverrides`/调参 provider），`StaticMutableAllowlistTests` 无新增 `public static` 可变字段。

## 修订 10：批 8「生成器收口」定义（已裁决）

### 1. 现状（实测：统一入口**已经存在**，只是没被强制）

`scripts/codegen/toolbox.py` 自述为 "the unified entry over scripts/codegen/*"，现有 7 个子命令：`build-hash` / `proto` / `luban` / `sync-code` / `ui-page` / `ui-api` / `refresh-hash`；文件头明写 "Direct per-script invocation stays valid (CI/gate callers keep their exact commands)" ——即**门禁与 CI 仍直调单脚本**，这就是"各自为政"的实情。`Luban/gen.bat` 已是"一行转发 `gen_luban.py`"的薄别名。

未纳入 toolbox 的：`SimTrigTableGenerator`（Unity 编辑器工具）、5 个 Unity 烘焙器（`BodyCylinderBaker`/`HeadHitLineTuner`/`MuzzleOffsetBaker`/`RootMotionBaker`/`WeaponGripBaker`）。

### 2. 各生成器的实际流程（逐个实测）

| # | 生成器 | 输入 | 输出 | 触发 | `--check` | 进 BuildHash |
|---|---|---|---|---|---|---|
| 1 | `gen_luban.py` | `Luban/Data/*.xlsx` + **解析 `TableLayout.cs`**（表清单单源，不另维护副本） | ①`GameData/Generated/*.cs` ②`GameData/Config/*.bytes` ③`LiteGame/Lua/Cfg/*.lua` ④`Modules/Ui/Generated/LuaKeys.g.cs`（staging 在 TestResults → 按扩展名发布） | `Luban/gen.bat` / toolbox / 直调 | ❌ 待补 | ①进（读码）②**不进**（数值面） |
| 2 | `gen_sync_code.py` | 8 个带 `[StateLayer]` 的 struct **源码**（`EntitySlot`/`WeaponRuntime`/`ActionRuntime`/`StatusSlotData`/`MatchBagSlot`/`ItemState`/`ProjectileState`/`ZoneState`） | `EntitySlot.Sync.g.cs`（checksum 折叠半部） | toolbox / 直调 | ❌ 待补 | ✅ |
| 3 | `gen_proto.py` | `battle.proto`（需 `--protoc`，版本须与运行时 Protobuf 包一致） | `Proto/Generated/Battle.cs` | toolbox / 直调 | ❌ 待补 | ✅ |
| 4 | `gen-build-hash.py` | `SHARED_TARGETS`（`LiteSim/{Scripts,Systems}`、`LiteNet/Core/{Proto,Protocol,Transport/Security}`、`GameData/Generated`）+ `SHARED_LINK_FILES` + `META_TARGETS` | `LiteNet/Core/Protocol/BuildHash.g.cs` + `MetaServer/Host/BuildHash.g.cs` | toolbox / 直调 | ✅ | 它就是哈希 |
| 5 | `gen-ui-page.py` | `#uiform.xlsx` + `UiPageManifest.json` + `Contracts/*.contract.json` | `Modules/Ui/Generated/UiPageCatalog.g.cs` | toolbox / 直调 | ✅ | ❌ |
| 6 | `gen-ui-api.py` | `UiApiSpec.json` | 5 个 `.g.cs`（`UIBindIndex.Api`/`UIBindBase.Api`/`UiApiManifest`/`UiApiShim`/`LuaBehaviourAdapter.Dispatch`）+ 一段 MD 文档区块（BEGIN/END 标记） | toolbox / 直调 | ✅ | ❌ |
| 7 | `refresh-hash.py` | — | 编排：`gen → RoomServer build → Unity recompile → 两端对账` | toolbox / 直调 | — | — |
| 8 | `toolbox.py` | — | 转发子命令、透传参数与退出码 | 人工 | — | — |
| 9 | `_codegen.py` / `_common.py` / `_uiform_row.py` | — | 共享 helper（`run_tool`/`fail`/`info`/`repo_root`） | 被 import | — | — |
| 10 | Unity 侧：`SimTrigTableGenerator` + 5 个烘焙器 | prefab / 动画曲线 / `RootMotionBakeConfig.asset` | `.g.cs`（`SimTrigTables`/`BodyBake`/`HeadBake`/`MuzzleBake`/`GripBake`/`RootMotionData`） | Unity 菜单 | ❌ | ✅（在 LiteSim 目录内时） |

**顺序约束（不是随手排）**：`build-hash` 的源集覆盖 `LiteSim/{Scripts,Systems}`、`LiteNet/Core/{Proto,Protocol}`、`GameData/Generated`——前四步任何一步产出新文件，都必须**最后**重算哈希，否则"改了代码、哈希没变"。

### 3. 腐烂路径清单（实证，脚本当前跑不通）

| # | 文件 | 写的路径 | 实际路径 | 后果 |
|---|---|---|---|---|
| 1 | `scripts/codegen/gen_proto.py`（`proto_dir`） | `Assets/LiteNet/Proto` | `Assets/LiteNet/**Core**/Proto` | 直接报 "battle.proto not found" |
| 2 | `scripts/codegen/gen_sync_code.py`（`OUT_FILE`） | `Assets/LiteSim/**Core**/Diagnostics/Generated/` | `Assets/LiteSim/Diagnostics/Generated/` | 写到不存在目录 |
| 3 | `Assets/LiteSim/Editor/SimTrigTableGenerator.cs:21`（`OutputPath`） | `Assets/LiteSim/**Core**/Scripts/SimTrigTables.cs` | `Assets/LiteSim/Scripts/Map/` | 同上 |
| 4 | `scripts/codegen/gen-build-hash.py`（模板注释头） | 注释写 `LiteSim.Core/Scripts` | 已拍平 | 文档误导 |
| 5 | `Assets/LiteGame/Editor/Baking/BakeTargets.cs`（四条 `*Output`） | `Assets/LiteSim/**Core**/Scripts/*.g.cs` | 已拍平 | 烘焙工具写空目录（批 7 处理） |

⇒ **批 8 的第一价值不是"统一长相"，而是先让生成器可信**：修路径 + 加"路径存在性"体检。

### 4. 目标：一个入口、一条依赖序、一套生成物纪律

| 项 | 做法 |
|---|---|
| 入口 | `python scripts/codegen/toolbox.py <tool>` 为**唯一**人工与门禁入口 |
| `all` 子命令 | 严格序 `luban → ui-page → ui-api → sync-code → proto → build-hash`；任一失败即非零退出并中止 |
| `all --check` | 不写盘，重算并与磁盘比对；任一处过期即非零（**这是真正的门禁**） |
| `doctor` 子命令（新） | 静态体检：解析每个生成器声明的**输入/输出**路径逐一 `exists`，缺目录/缺输入立即报——专治上面 5 处腐烂 |
| `trig` 子命令（新） | 确定性三角表生成（算法搬 Python，或转发 Unity 实现并校验产物逐字节一致） |
| 生成物头统一 | `// <auto-generated: toolbox/<tool> — DO NOT EDIT` + `input: <路径> sha256:<前 8 位>` + `regen: python scripts/codegen/toolbox.py <tool>`；**不写时间戳**（保幂等） |
| 幂等校验 | 每个 tool 支持 `--check`；补齐 `luban`/`sync-code`/`proto`（`ui-page`/`ui-api`/`build-hash` 已有） |
| 门禁接入 | `.workflow/config.json` 新增 `codegen-check` = `python scripts/codegen/toolbox.py all --check`，进 PullRequest 档（现状：**config 里没有任何 codegen 检查**） |
| 直调策略 | 门禁/CI/脚本内部一律经 toolbox；人工直调保留但打印 `[deprecated] use toolbox.py <tool>`；`Luban/gen.bat` 保留为兼容别名 |

### 5. 明确不做

- 不改 `Luban/gen.bat` 的底层行为（它仍是表链唯一真值入口；toolbox 只调用与串联）。
- 不把 Unity 烘焙器搬进 Python（必须读 prefab / 动画曲线；只做"入口登记 + 产物头统一 + 路径体检"）。
- 不动已稳定的产物格式（`cfg/*.cs`、`Battle.cs`、`.bytes`）。
- 不做"生成物统一成单一形态"——形态收敛属批 7（几何/根位移）与本批之外的其他产物，不在批 8 扩范围。

### 6. 子步

- T1 **修 5 处腐烂路径** + 加 `doctor`（先让工具箱可信；跑一次 `doctor` 应全绿）。
- T2 `toolbox.py` 增 `all`（严格序 + 失败即停）与 `trig`。
- T3 给 `luban`/`sync-code`/`proto` 补 `--check`；统一生成物头 + 输入指纹。
- T4 门禁接入 `codegen-check`；直调改为 deprecated 提示；`scripts/tests` 补 toolbox 转发用例（参数透传 + 退出码）。
- T5 文档：`scripts/README`、`Docs/规范/03-领域查询表.md`、进度卷新章。

### 7. 追加验收标准

- [ ] AC67：`toolbox.py all` 按严格序 `luban → ui-page → ui-api → sync-code → proto → build-hash` 跑完；任一失败即非零退出并中止。
- [ ] AC68：`toolbox.py trig` 产物与现有 `SimTrigTables.cs` 逐字节一致（证明迁移无行为变化）。
- [ ] AC69：`--check` 覆盖 `luban`/`sync-code`/`proto`/`build-hash`/`ui-page`/`ui-api`；改输入不重生成 → 对应 `--check` 红。
- [ ] AC70：门禁与 CI 不再直调 `scripts/codegen/*.py`（`Luban/gen.bat` 例外）；新增脚本直调即红。
- [ ] AC71：所有生成物首行含 `auto-generated: toolbox/<tool>` + 输入指纹；重复生成逐字节一致（幂等）。
- [ ] AC72：文档回写（`scripts/README`、领域查询表、进度卷）。
- [ ] AC73：`toolbox.py doctor` 在修复前对 5 处腐烂路径报红（逐条列出），修复后全绿；`gen_proto.py` 与 `gen_sync_code.py` 能实际跑通并产出到正确目录。
- [ ] AC74：`.workflow/config.json` 含 `codegen-check`（`toolbox.py all --check`）并进 PullRequest 档；"改了 `[StateLayer]` 字段但没重跑 `sync-code`" 会让该门禁变红。
- [ ] AC75：`Luban/gen.bat` 仍可用且行为与 `toolbox.py luban` 等价（同一脚本、同一退出码）。
