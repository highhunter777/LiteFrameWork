# 方案：客户端架构重写（不继承旧设计）

> 运行 client-rewrite-architecture · 创建于 2026-11-11
> 状态：草案（待独立子代理评审与用户确认验收标准）

## 背景与目标

### 现状（代码事实）

- Unity 客户端 `Assets/LiteGame` 约 529 个 C# 文件 / 45,922 行；客户端框架 `Packages/com.litegame.framework` 约 350 个文件 / 22,638 行；另有 `LiteNet` 46 个文件 / 14,049 行、`LiteSim` 75 个文件 / 5,739 行（共享内核，非本次重写对象）。
- 现行启动是 `GameEntry → ClientHost → 17 个 IClientSegment → 流程机 7 阶段`；`ClientEntry.RegisterSegments`（Assets/LiteGame/App/ClientEntry.cs:81-107）逐段 new，`AppComposition.ComposeAfter`（AppComposition.cs:91-121）再做 7 处跨域装订与 12 项帧序。
- UI 页面由 `#uiform` 表 + `toolbox.py ui-page` 生成 `PageIds.g.cs` / `UIPageCatalog.g.cs`；页面逻辑是 `Page_<Name>Logic`（挂在 prefab 根），经 `PageServices` 取 7 个域端口；绑定走 `UIBindIndex` 全局索引。
- 现行程序集：产品侧 **20 个运行时 asmdef**（另 3 个编辑器件，总 23）；`LiteGame.App` references **28 项**（26 个项目程序集 + UniTask/TMP 2 个包程序集）；框架侧 4 个 asmdef；`Abstractions/` 目录 **14 个**；产品侧 `SubModules/` 包轴 10 个。
- 现行框架 UI 运行时 `Packages/com.litegame.framework/Runtime/Services/UI` 约 112 个文件 / 6.8k 行；产品页面 13 个文件 / 约 1.1k 行；`Assets/LiteGame/Editor/UITools` 约 95 个文件 / 1.0 万行；UI 绑定生成链约 1.36k 行。页面 prefab 广泛使用 `BindNode/BindName`（生产路径），但 `BindRoot/Designer` 生成类路径当前没有生产页面使用——两者必须分开表述，不能把 `BindNode` 当成可删的未用件。
- 现行验证基线：L1 最近 1886/1886 绿（`TestResults/summary-l1.json`，2026-11-11 21:55）；L2 最近一次真跑（2026-11-07）EditMode 1149 例 / 1115 过 / 32 失败、PlayMode 56 / 55 过，之后 `summary-l2.json` 为 `total=0` 的未真跑记录；HybridCLR 热更目录 `Library/HybridCLR`、`StreamingAssets/HybridCLR` 当前不存在，热更链在 Player 构建里是静默跳过——这两点是重写验收必须显式处理的基线风险。

### 要解决的问题（实测触碰面）

独立子代理按真实代码追踪的四个场景：

| 场景 | 最小手改 | 典型手改 | 生成/命令 | 主要跨目录 | 需理解的概念 |
|---|---:|---:|---|---|---:|
| S1 新增 UI 页面（参照 Records） | 6 个 | 11–14 个 | 2 个生成物 + `ui-page` + `Luban/gen.bat` + `build-hash` | `Luban/Data`→`UI/Content`→`UI/Pages`→`Editor/UITools`→`Procedure` | ≈10 |
| S2 修改登录按钮业务逻辑 | 1 个 | 4–10 个（换能力面/改调用链/跨端） | 0–2 | UI→Meta→Wire→Server | ≈6 |
| S3 战斗 HUD 增加元素 | 6 个 | 6–9 个 | 0（但需重打 UIHud 内容包） | 同一 asmdef 内 3–4 处 | ≈7 |
| S4 新增设置项 | 7 个 | 7–9 个 | 2 个 + 2 条命令 | Settings→UI→Luban | ≈8 |

根因不是“代码行数多”，而是**同一个意图被拆到多个技术层/登记面**：

1. **17 段装配 + 双文件组合根**：任何新能力必须穿过 段→容器→ComposeAfter→PageServices→页面；新增 tickable 还要插 `FrameSchedule.SetOrder`。
2. **全局生成目录/绑定索引**：`#uiform` → `PageIds`/`UIPageCatalog`；`BindRoot/BindNode` → `UIBindIndex`/生成类；页面控件名又在页面逻辑的字符串数组里出现第三份。
3. **端口与 SubModules 双份声明**：同一个功能声明在 `Abstractions/`、实现在 `Client/`、装配在 `Segment`/`ComposeAfter`，读的人要跨三处才能回答“这页逻辑在哪”。
4. **生命周期作用域叠加**：Root/Account/Match/Scene/UI 五层 Scope + 流程机 + 房间操作句柄 + 清理观察者；对战离场需要 `ProcedureBattle` 400+ 行编排。
5. **技术层目录当拆分轴**：`Abstractions/SubModules/Drivers/Model/Apply/Options` 把同一功能切开；`PageServices` 成为页面依赖的“上帝对象”。

### 目标（可观察）

1. **功能内聚**：一个功能 = 一个目录（代码、页面 prefab、页面数据、测试都在内）；功能对外只有一个入口文件。
2. **短链路**：修改一个功能只动其目录内文件，功能目录外 ≤2 个手改文件、0 次代码生成；新增一个功能 = 1 个功能目录 + 功能目录外 1 行（组合根）；删除功能 = 删目录 + 1 行，不改其他功能。
3. **低心智负担**：全局只保留 4 个概念——`ClientApp`（组合与顶层流程）、`Feature`（功能）、`Page`（界面）、`PlatformService`（内容/配置/UI/音频/输入/网络）。没有 DI 容器、没有 17 段、没有全局页面目录、没有命令总线、没有五层 Scope。
4. **不保留旧设计**：旧路线图的 AR1/AR2、≤5 文件/≤600 行、0 跨单元接口、17 段、PageServices、PageIds/UIPageCatalog、UIBindIndex/生成链、SubModules/Abstractions 全部作废；只保留外部硬约束（AOT/hot、BuildHash、Sim/Net 红线、资源与表管线）。
5. **可运行的迁移**：新客户端与旧客户端并行存在，旧客户端保持可编译可跑，直到新客户端完成 P0 闭环（P2 退出）后一次切换、删除旧树。

## 需求澄清与假设

### 已确认

- 用户明确：不保留旧设计；允许重写任何客户端代码；主代理只调度，施工与测试由子代理执行；先重新设计架构。
- 用户明确：功能内聚；修改和添加功能不能要长链路修改；降低心智负担。

### 采用假设（需用户确认）

- **A1 范围**：重写对象是 Unity 客户端产品层与客户端框架层（`Assets/LiteGame/**`、`Packages/com.litegame.framework/**`），**不重写** `LiteSim`、`LiteNet`、`LiteSync`、`Luban.Tables` 生成读码面、RoomServer/MetaServer、协议与表结构、美术资源。理由：这些是双端共享内核或外部服务，改动会触发 BuildHash/协议/服务端连锁，且不是“客户端心智负担”的主因。**例外**：`Assets/LiteGame` 内被服务端源链接的共享文件（见“共享源”）必须为删旧树而迁出——只改路径/清单，不改语义；迁出同批更新 csproj/`SHARED_LINK_FILES`/`META_EXTRA_FILES` 并重算 BuildHash。
- **A2 功能范围**：保留现有玩家可见功能（登录/大厅/房间/战斗/结算/设置/战绩/热更/错误恢复/Dev/GM），不做玩法增删；第一阶段先做 P0 闭环，Dev/GM 与编辑器工具后置。
- **A3 UI 资产**：保留现有 uGUI prefab 的视觉与控件命名；新页面在功能目录内**复制**一份 prefab 并替换根页面组件，旧 prefab 在切换前保持不动。不重做视觉设计。
- **A4 编辑器工具**：旧的 UI 工作台/登记/绑定生成链不迁移；只保留轻量资产校验（页面根组件、BindName 唯一性、收集目录覆盖）。若美术仍需要工作台，作为后续独立需求。
- **A5 迁移形态（用户 2026-11-11 选择：原地 strangler）**：新代码放 `Assets/LiteGame/Next/` 与 `Packages/com.litegame.framework/Next/`（临时程序集名 `LiteGameNext.*` / 场景 `BootNext.unity`），**旧目录留在原位**；逐功能替换，旧客户端经 `Boot.unity` 保持可运行；P0 闭环（P2）验收后切 `Boot.unity`，P3b 删除旧目录、把 `Next` 提升到正式路径/程序集名。不是独立 `Assets/LiteGameNext` 根。
- **A6 验收口径**：以“触碰面 + 冷启动可发现性 + 真实 PlayMode/Player 流程”判定，不以代码行数下降判定。

### 影响与风险

- 若用户要求“一次性删除旧树、期间不可运行”，迁移风险显著上升；本方案默认保留旧树直到切换。
- 若用户要求保留编辑器工作台/生成链，S1 的短链路目标会部分回退；本方案默认砍掉。
- 若用户要求第一阶段就覆盖全部功能（含背包/Dev/GM），工期与风险翻倍；本方案先 P0。

### 执行分工（用户约束）

- **主代理**：架构设计、方案/验收标准、任务拆解、调度、核验 diff、运行门禁、代码评审、回写与归档；不直接写生产代码。
- **方案/任务评审**：独立子代理（只读），主代理不得自评。
- **测试与施工**：施工子代理写测试、跑红测，再由另一施工子代理写实现；主代理核验后才算任务完成。
- **验收**：AC1–AC3 由本轮运行验收；AC4–AC5 是后续运行里程碑，不在本轮 `acceptance.md` 中作为一次性承诺。

## 范围与非目标

### 范围内

- 新客户端架构：`ClientApp` + `AppFlow` + `Feature` + `Page` + `PlatformService`。
- 新启动壳：`GameEntry`（AOT）/`ClientBootstrap`/`ClientApp`（hot）。
- 新 UI 运行时：页面栈/层、模态、输入拦截、转场、缓存、SafeArea、Toast/Confirm/Loading。
- 新页面：P0 核心四页（登录、大厅、房间=对局前等待页、结算；战斗 HUD 不是页）＋ P1 设置/战绩页。
- 新平台服务：Content/YooAsset、Config/Luban、Localization、Audio、Input、Meta HTTP、LiteNet 会话包装、Log。
- 新战斗功能边界：`BattleFeature` 对外只暴露 `RunAsync(MatchInfo) → BattleResult`；内部保留 Sim/Net/预测/视图/表现实现，逐步搬入功能目录。
- 新测试：功能单测、UI 运行时 EditMode、P0 PlayMode 端到端、Player 冒烟。
- 迁移与切换：旧树删除、程序集收敛、文档与索引回写。

### 非目标

- 不改 Sim 判定、网络协议、服务端、Luban 表结构、BuildHash 源集。
- 不重做美术、动画、特效、地图、UI 视觉。
- 不新增玩法/商业化功能。
- 不迁移旧 UI 工作台、UI 契约、UIApiSpec、绑定生成器。
- 不追求第一阶段就删除旧框架全部代码；切换后统一清理。

## 现状与约束

### 相关代码/模块

- 启动：`Assets/LiteGame/Bootstrap/GameEntry.cs`、`App/ClientEntry.cs`、`App/AppComposition.cs`、`Modules/Composition/**`。
- 框架：`Packages/com.litegame.framework/Runtime/{Core,Services,Adapters,Hosting}/**`。
- UI：`Packages/com.litegame.framework/Runtime/Services/UI/**`、`Assets/LiteGame/Modules/UI/**`、`Assets/LiteCodeGen/**`、`Assets/LiteGame/Editor/UITools/**`。
- 流程/会话：`Assets/LiteGame/Modules/Procedure/**`、`Modules/Session/**`、`Modules/Meta/**`。
- 战斗：`Assets/LiteGame/Modules/Battle/**`（约 110 个文件）、`Modules/Camera`、`Modules/Input`、`Modules/Settings`、`Modules/Config`、`Modules/Map`。
- 共享内核：`Packages/com.litegame.sim/**`、`Packages/com.litegame.net/**`、`Luban.Tables`、`Assets/GameData/**`。

### 外部硬约束（不可动）

1. **AOT/hot 唯一硬边界**（以 `scripts/gate/hot-boundary-check.py` 的分区表为单源）：AOT＝`LiteFramework`、**`LiteNet`**、`LiteGame.Bootstrap`、现役 `LiteCodeGen.Runtime`；hot＝`LiteGame`、`LiteSim`、`LiteSync`、`Luban.Tables`；`LiteNet` **不是 hot**。AOT 不得引用 hot；目标程序集收敛为 6 + `Dev`（AOT 三件 `LiteFramework`/`LiteNet`/`LiteGame.Bootstrap`，hot 三件 `LiteGame`/`LiteSim`/`LiteSync`；`Luban.Tables` 待吸收、`LiteCodeGen.Runtime` 待剥 BindNode 后删除/吸收）。守卫 `scripts/gate/hot-boundary-check.py`。
2. **BuildHash**：权威清单在 `scripts/codegen/gen-build-hash.py`，覆盖 `com.litegame.sim/{Scripts,Systems}`、`com.litegame.net/Core/{Proto,Protocol,Transport/Security}`、**`com.litegame.sync/{Shared,Snapshot,Input,Events}`**、`Assets/GameData/Generated`，外加 `SHARED_LINK_FILES` 4 件源链接共编件与 `META_*` 源集；改动后必须跑 `python scripts/codegen/toolbox.py build-hash`，出包门禁 `--check-shared`。本方案不改变这些源集的**语义**；共享源迁出只改路径并重算两个常量，且必须重跑服务端构建。
3. **R11/R13（共享内核/服务端）**：`Assets/RoomServer/Core/` 禁 Console/系统时钟/文件 IO/proto/LiteNet；机制面禁读静态可变数值。客户端重写不触碰这批代码；若共享源迁出涉及 `TableLayout` 等，按原规则执行。
4. **资源规范**：场景/prefab/material/SO/.meta 一律走 unity-pipeline CLI；只收集配置/**预制体模板**/场景三类入库；第三方资源先复制再引用。
5. **Luban**：表源改动跑 `Luban/gen.bat`；生成读码面进 BuildHash；表值走运行期 `CombatConfigDigest`。
6. **YooAsset/HybridCLR**：内容包与 hot 程序集装载走现行 `HotUpdateLoader`/`PatchRunner` 与收集组；新客户端必须接入同一套内容身份。
7. **日志/错误契约**：统一 `Log.*`；已关闭服务抛 `ObjectDisposedException`；程序员错误 fail-fast；内容降级 `Log.Warning` + 默认值。
8. **Unity 2022.3.55f1c1**；uGUI + TMP；UniTask；Cinemachine/InputSystem/YooAsset 版本不变。

### 共享源（删旧树前必须先迁出）

`Assets/LiteGame` 不是纯客户端目录，以下文件被服务端 csproj 源链接/共编，**不能随旧树删除**：

| 共享源 | 消费方 | 迁移必须同步 |
|---|---|---|
| `Assets/LiteGame/Modules/Config/Table/{SimConfigMapper,BattleStrategyTableReader,TableLayout,MapTableProjection}.cs` | RoomServer（`RoomServer.csproj` 出树链接）、`SHARED_LINK_FILES`、BuildHash | 两份 csproj 链接、`gen-build-hash.py` 的 `SHARED_LINK_FILES`、`TableLayout` 消费者、`build-hash` 重算、服务端构建 |
| `Assets/LiteGame/Modules/Meta/WireSubModule/**`（17 个 MetaWire 类型）+ `Abstractions/Room/JoinTicketInfo.cs` | MetaServer/RoomServer csproj 源链接、`META_EXTRA_FILES`、`MetaWire` 命名空间 | 两份 csproj 链接、`META_EXTRA_FILES`、`MetaWire` 引用点、`build-hash`/Meta 构建身份、服务端构建 |

**裁决（P1 执行，不再二选一）**：把这批文件迁到新 UPM 包 `Packages/com.litegame.shared/`，包内两个 hot asmdef：
- `LiteGame.MetaWire`（纯 DTO，无第三方引用）：`Meta/WireSubModule/**` + `JoinTicketInfo.cs`；
- `LiteGame.SimConfig`（引用 `Luban.Tables`/`LiteSim`）：`Config/Table/{SimConfigMapper,BattleStrategyTableReader,TableLayout,MapTableProjection}.cs`。

同步面：
1. `Server/RoomServer/RoomServer.csproj`/`Server/MetaServer/MetaServer.csproj` 的出树链接改到 `Packages/com.litegame.shared/...`。
2. `scripts/codegen/gen-build-hash.py` 的 `SHARED_LINK_FILES`/`META_EXTRA_FILES` 路径更新；`build-hash` 重算两个常量（路径进摘要，值会变）。
3. `scripts/gate/hot-boundary-check.py` 的 hot `paths` 加 `Packages/com.litegame.shared/`；`firstPartyRoots` 已覆盖 `Packages/com.litegame.`；`arch/units.json`/`ScanTargets` 登记新包。
4. **发布顺序**：先发 RoomServer/MetaServer（新 BuildHash）→ 再发客户端；迁出窗口内旧客户端与新服不匹配，按维护窗口处理，不做双版本兼容。
5. 迁出完成前不得删除 `Assets/LiteGame`；P0 骨架不触碰这批文件（P0 不接 Meta/Config 生产链）。

### 旧设计约束（本方案明确废除）

- 17 段 `IClientSegment`/`ClientHost`/`SegmentContext`/`DependsOn` 拓扑与 `ComposeAfter` 装订。
- `ServiceContainer`/`ClientContext` 实例登记与 `Put/Require/Get`。
- `ClientScope` 五层作用域树与 `AssetLease`（改为功能/页面/Session 的 `Lifetime` + `IDisposable`）。
- `StageMachine<ProcedureId>` 七阶段、`ProcedureArgs`、`RoomFlowOperation`/`RoomFlowLifetime` 观察者。
- `UIService/UINavigationController/UIForm/UIBindIndex/PageLogicBase/PageServices/IUIFormLogic`。
- `#uiform`/`PageIds`/`UIPageCatalog`/`UIPageManifest`/`ui-page`/`UIApiSpec`/`BindGenerator`。
- 产品侧 20 个运行时 asmdef、`Abstractions/`/`SubModules/`/`Services/` 技术层目录、每服务一接口。
- `EventBus`/`CommandBus` 双通道、`FrameSchedule` 12 项全局帧序。
- AR1（路径=命名空间）、AR2（≤5 文件/≤600 行）、0 跨单元接口。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| **A. 独立新树并行** | 新目录 `Assets/LiteGameNext` 重建 App/UI/Features/Platform；旧树不动；P0 验收后切 Boot、删旧树 | 旧客户端可跑；新架构不被旧代码妥协 | 迁移期双根/双程序集；资产复制；切换集中清理 | 未选（用户选择 B） |
| **B. 原地 strangler（用户选择）** | 新代码放 `Assets/LiteGame/Next` + `Packages/com.litegame.framework/Next`；旧目录原位保留；逐功能替换，`BootNext` 并行验证 | 不新增独立根；旧客户端可跑；可逐页对照；切换后删旧目录并提升 Next | 旧设计长期共存；组合根/容器/页面服务要同时支持两套；最容易退化成“旧设计加一层壳”；与“不保留旧设计”有张力 | **采用（用户 2026-11-11 裁决）** |
| C. 只做收敛重构 | 保留 17 段/PageServices/生成链，只删重复、并文件、减接口 | 改动小、风险低 | 触碰面仍 6–14 文件；根因不动；不满足用户目标 | 不推荐 |

**采用 B 的理由与约束**：用户明确选择原地 strangler。它保留“旧客户端可运行”与“逐功能验收”，但必须用硬约束防止退化成旧架构加壳：新代码只走 `ClientApp/Feature/Page/PlatformService`；`Next` 子树不得引用旧 `LiteGame.*`/`LiteFramework` 契约；旧目录只作为对照/回滚面，不进入新路径；P3b 必须删除旧目录并把 `Next` 提升为正式路径。若 B 在执行中出现“新页面仍走 PageServices/生成目录”或“Next 引用旧段/容器”，立即视为阻塞并回退该批。

## 设计要点

### 1. 总体形状

```text
Boot.unity
 └─ GameEntry (AOT MonoBehaviour)
      ├─ PlatformBoot (AOT): 日志/内容(YooAsset)/热更候选/配置装载/hot 程序集装载
      └─ ClientApp (hot)
           ├─ AppFlow: 线性 async 顶层流程
           ├─ Features: Account / Matchmaking / Battle / Result / Settings / Records / Dev
           └─ Platform: UiRuntime / Content / Config / Localization / Audio / Input / MetaClient / Log
```

- **AOT**：`LiteFramework`（一个程序集：日志、内容/YooAsset、热更、平台桥、启动错误面）＋ `LiteGame.Bootstrap`（GameEntry）。
- **hot**：`LiteGame`（一个程序集：App/Features/Platform）＋ `LiteSim`/`LiteSync`/`Luban.Tables`；`LiteNet` 在 AOT 侧（由 hot `LiteGame` 引用）。
- **Dev**：`LiteGame.Dev`（不进发布包）。
- **Editor**：`LiteGame.Editor`（只保留轻量校验；旧 UITools 不迁移）。

### 2. 目录蓝图

```text
Assets/LiteGame/Next/                    # 迁移期 Next 子树（原地 strangler）；切换后提升到 Assets/LiteGame
  Bootstrap/                             # AOT: GameEntry/BootstrapError/PlatformBoot
  Runtime/                               # hot asmdef "LiteGameNext"
    App/
      ClientApp.cs                       # 组合根 + Tick + Shutdown
      AppFlow.cs                         # 线性 async 流程
      AppContext.cs                      # 平台服务与生命周期
    Platform/
      Ui/                                # UiRuntime + PageBase + PageSpec + Widgets
      Content/                           # YooAsset 包装 + AssetHandle + SceneHandle
      Config/                            # Luban Tables 访问 + digest
      Localization/
      Audio/
      Input/
      Meta/                              # HTTP 客户端 + DTO
      Net/                               # RoomClient 包装
      Logging/
    Features/
      Account/                           # AccountFeature + LoginPage + AccountClient + Content/
      Matchmaking/                       # Lobby/Room 两页 + 房间列表/建房/进房
      Battle/                            # BattleFeature + Session/World/View/Hud/Input/Presentation + Content/
      Result/                            # ResultFeature + ResultPage
      Settings/                          # SettingsService + SettingsFeature + SettingsPage
      Records/                           # RecordsFeature + RecordsPage
    Shared/                              # 少量跨功能值类型/常量（不建技术层桶）
  Dev/                                   # 独立 asmdef
  Editor/                                # 轻量校验工具
  Content/                               # 字体/图标/共享 UI 控件（非功能专属）
```

**规则**：功能目录内允许 `View/`、`Content/` 等内聚子目录；禁止 `Abstractions/`、`SubModules/`、`Services/`、`Impl/`、`Common/`、`Utils/`。功能对外只暴露 `<Name>Feature.cs`；其他文件默认 `internal`。**例外**：页面类、页面数据类、Unity 组件类因序列化/挂载需要可 `public`，但仍约定不被其他功能直接引用（由 G2 测试检查）。

### 3. 组合与生命周期

```csharp
// ClientApp.CreateAsync：显式构造，无容器、无反射、无注册表；每个功能只拿 1–3 个依赖
var platform = await Platform.CreateAsync(ct);
var account = new AccountFeature(platform.Meta, platform.Settings);
var settings = new SettingsFeature(platform.Settings, platform.Ui);
var matchmaking = new MatchmakingFeature(platform.Meta, platform.Ui, account);
var battle = new BattleFeature(platform.Content, platform.Config, platform.Input,
    platform.Audio, platform.Ui, platform.Settings, account);
var result = new ResultFeature(platform.Ui, account);
var records = new RecordsFeature(platform.Ui, account);
var dev = DevFeature.CreateIfEnabled(platform);
var app = new ClientApp(platform, account, settings, matchmaking, battle, result, records, dev);
await app.RunAsync(ct);
```

- **不把 `Platform` 聚合体注入功能**：`Platform` 只住 `ClientApp`；功能构造参数是具体服务切片。这样避免新 `PageServices` 式的服务定位器。

- `Feature` 基类：`InitializeAsync(ct)` / `Tick(dt)`（可选）/ `ShutdownAsync()`；生命周期由 `ClientApp` 持有，关闭逆序。
- `Lifetime` 小工具：一个 CTS + `List<IDisposable>`，`Dispose` 时取消并按逆序释放；App/Feature/Session/Page 各持一个。没有父子域解析。
- 所有异步都带 `CancellationToken`；离开流程 = 取消 token + `await` 清理完成；不用全局阶段机/观察者。
- 静态状态只允许 `Log` 门面与编辑器重置点；不用 `public static` 可变字段。

### 4. AppFlow：用 async 调用栈代替流程机

```csharp
async UniTask RunAsync(CancellationToken ct)
{
    await _platform.Content.InitializeAsync(ct);
    await _platform.Config.LoadAsync(ct);

    await _account.RestoreOrLoginAsync(ct);          // 需要时打开 LoginPage
    while (!ct.IsCancellationRequested)
    {
        MatchInfo match = await _matchmaking.WaitForMatchAsync(ct);  // 只开 LobbyPage；返回票据事实
        BattleResult result = await _battle.RunAsync(match, ct);     // RoomPage + 场景/HUD/会话
        await _result.ShowAsync(result, ct);                        // ResultPage
        // 回到 Matchmaking；错误在顶层 catch → ErrorPage
    }
}
```

- **没有 `ProcedureId`、没有 `StageMachine`、没有 `ProcedureArgs`、没有房间操作句柄/清理观察者**。
- 进入/离开的顺序由 `await` 与 `try/finally` 保证：`BattleFeature.RunAsync` 在返回前完成场景卸载、会话释放、输入解绑；调用方拿到结果时资源已清理。
- 取消：App 取消 → 当前 `await` 抛 `OperationCanceledException` → 各功能 `finally` 清理；不需要“阶段回 Main”的中间态。
- 错误：功能返回 `Result<T>` 或抛 `ClientException`（带用户可读分类）；`AppFlow` 捕获后打开 `ErrorPage`，提供重试/回登录/退出。程序员错误直接 fail-fast 到错误页并记录。
- **终态处理表**：

| `BattleResult.Outcome` / 异常 | AppFlow 动作 |
|---|---|
| `Completed` | 打开 `ResultPage`；确认后回 `Matchmaking` |
| `LeftByUser` | 不打开结算，直接回 `Matchmaking` |
| `Aborted`（未开局散场） | 回 `Matchmaking`，Toast 提示 |
| `SessionFailed` | 回 `Matchmaking`，弹出可重试错误确认；不进入错误终态 |
| `OperationCanceledException` | 若 App token 取消 → 静默关闭；若页面/操作取消 → 回 `Matchmaking` |
| `ClientException.Auth` | 登出 → `LoginPage` |
| `ClientException.Fatal` | `ErrorPage`（不可重试）→ 退出/回登录 |
| `ClientException.Recoverable` | `ErrorPage`（可重试）→ 重试当前步骤或回大厅 |

- `ErrorPage` 属于 `Platform/Ui/Content` 的系统页（不是功能页面）；`UiRuntime.ShowErrorAsync` 负责打开。`Loading/Toast/Confirm` 同理。
- **Tick 所有权**：`ClientApp.Tick` 固定调用 `UiRuntime.Tick(dt)` → `BattleFeature.Tick(dt)`（仅活动对局）→ `DevFeature.Tick(dt)`；`BattleFeature.RunAsync` 只等待 `RunAsync` 内部 TCS 的完成信号，不自己驱动帧。暂停/失焦由 `GameEntry` 转发 `ClientApp.OnPause(bool)`；`BattleFeature` 在暂停时停止输入采样（网络泵是否继续由 P2 时序契约明确）。

### 5. Page 与 UiRuntime

```csharp
public sealed class LoginPage : PageBase
{
    public static readonly PageSpec Spec = new("Page_Login", PageLayer.Screen, fullScreen: true);
    private AccountFeature _account;
    public void Bind(AccountFeature account) => _account = account;

    protected override void OnShow() {
        SetText("Txt_Title", "UI.Login.Title");
        OnClick("Btn_GuestLogin", () => _account.LoginAsync());
    }
}
```

- `PageSpec` 是页面类上的静态事实（prefab 地址、层、全屏、缓存策略），**没有全局页面目录/表/ID/生成器**。
- `UiRuntime.OpenAsync<TPage>(PageSpec, data, Action<TPage> bind)`：加载 prefab → 以**非激活**状态实例化 → 找 `PageBase` → 调 `bind(page)` 注入依赖 → 激活 → 入栈 → `OnShow`；关闭时 `OnHide` → 取消页面 token → 归还 prefab → 缓存/销毁。先构造后激活是页面生命周期的硬约束，避免 `OnEnable` 先于依赖注入。
- `UiRuntime` 负责：层（Screen/Modal/Overlay/System）、页面栈、模态栈、输入拦截计数、Back（含平台返回）、转场、缓存预算、SafeArea、Toast/Confirm/Loading。目标 1.2–2k 行，内部按职责分 4–8 个文件，对外一个门面；必须保留旧运行时已证明必需的语义——页面展示代次/取消、关闭后归还 prefab 租约、缓存淘汰、模态射线遮蔽、Back 优先级（Loading→模态→页面）、焦点恢复、UI 时钟驱动转场/动效。不复制旧的 `UIForm` 状态机公开语义，但行为等价性由 PlayMode 用例钉住。
- **P1 parity 清单**（P0 只做最小栈；以下逐项在 P1 补齐并用用例钉住）：列表复用/滚动、LText 打字机与语言切换刷新、UI 动效中断复位、CanvasScaler 多端适配、InputSystem 导航/焦点、SafeArea 设备差异、页面缓存与关闭原因、模态遮挡与 Back 链。
- 页面绑定：`PageBase` 在 `OnCreate` 扫描自身子树一次，建立 `name → Transform` 字典；提供 `Bind<T>`/`OnClick`/`SetText`/`SetVisible`。**没有全局 `UIBindIndex`、没有 BindGenerator、没有 UIApiSpec**。prefab 的 `BindName` 命名约定保留（美术交接仍可用），但不生成代码。
- 页面数据是页面目录内的强类型 record/class；需要跨功能的数据由功能入口方法接收/返回，不搞 `IUIData` 全局接口。
- 页面依赖：功能在打开页面后调用 `page.Bind(feature, services...)`；页面只拿到自己需要的依赖（通常 1–3 个），不拿 `PageServices` 上帝对象。
- HUD 不是 Page：`BattleFeature` 自己加载 `Hud_Battle.prefab`，由 `BattleHudView` 直接驱动；不进导航栈、不受页面缓存影响。
- **系统页**：`ErrorPage`/`LoadingPage`/`Toast`/`Confirm` 属 `Platform/Ui/Content`，由 `UiRuntime` 直接管理，不属于任何 Feature；`ErrorPage` 必须在 P0 骨架中可用（启动/顶层失败兜底）。

### 6. Feature 规则

- 一个功能一个目录、一个公开入口 `<Name>Feature.cs`；对外方法返回强类型结果，事件只在“广播”语义使用（C# `event` 或 `Action`），请求/响应用直接方法调用。页面/组件/数据类因 Unity 序列化可 `public`，但不得被其他功能直接引用（G2 检查）。
- 功能可以引用其他功能的公开入口类；**不引入接口**，除非有第二种实现或测试替身。跨功能不引用内部类型/内部目录。
- 功能拥有自己的页面、prefab、数据、测试；资源放 `<Feature>/Content/`，收集组按功能目录通配（新增 prefab 不改收集清单）。
- 新增功能：建目录 + 在 `ClientApp` 构造一行；若参与顶层流程，在 `AppFlow` 加一次调用（≤2 文件）。删除功能：删目录 + 删构造/流程行；其他功能不改。
- 功能内部可以复杂（战斗），但复杂度被入口文件封住；读代码的人先看 `<Name>Feature.cs`，不需要先理解全局装配。

### 7. Platform 服务

| 服务 | 职责 | 对外最小 API |
|---|---|---|
| `ContentService` | YooAsset 初始化、包版本、资源/场景租约、释放 | `InitializeAsync`、`AcquireAsync<T>`、`LoadSceneAsync`、`ReleaseAll` |
| `ConfigService` | Luban 表装载、快照、digest | `LoadAsync`、`Tables`、`CombatDigest` |
| `LocalizationService` | 文案目录、语言切换、格式化 | `Get`、`Format`、`SetLocale` |
| `AudioService` | 音效/音乐、分组、音量 | `Play`、`Stop`、`SetVolume` |
| `InputService` | 设备源、意图采样、UI 阻断 | `Sample`、`SetBlocked`、`Intent` |
| `MetaClient` | Meta HTTP 端点、DTO、错误 | `GuestLoginAsync`、`RoomsAsync`、`CreateRoomAsync`、`JoinTicketAsync`、`SubmitResultAsync` |
| `NetSession` | `RoomClient` 包装、连接/收发/重连 | `ConnectAsync`、`SendInput`、`Poll`、`Disconnect` |
| `UiRuntime` | 页面/层/模态/转场/反馈 | `OpenAsync`、`CloseAsync`、`BackAsync`、`Toast`、`ConfirmAsync` |
| `Log` | 统一日志门面 | `Info/Warning/Error` |

- 平台服务是**具体类**（无接口），由 `Platform` 构造并显式传给功能；测试用子类/构造参数替身（仅在有测试替身需求时抽接口）。
- 内容/配置/音频/输入等第三方适配保留；旧 `LiteFramework` 中可复用的实现（YooAsset、Clock、Log、Pool、Animation Backend）可直接搬迁/裁剪，不保留其 Hosting/DI/UI 旧契约。

### 8. 战斗功能边界

```csharp
public sealed class BattleFeature
{
    public async UniTask<BattleResult> RunAsync(MatchInfo match, CancellationToken ct);
    public void Tick(float delta);
}
```

- `BattleFeature` 内部：`BattleSession`（Join/Reconnect/输入/快照/Sim）、`BattleWorld`（预测/镜像/和解）、`BattleView`（场景/Rig/角色/相机）、`BattleHud`、`BattlePresentation`（VFX/音频/命中反馈）、`BattleInput`。这些是功能内部件，不跨功能暴露。
- 保留 `LiteSim`/`LiteNet`/`LiteSync` 的确定性契约与 `RoomClient` 状态机；只替换“过程编排/作用域/端口/字段装配”。必须保留的隐式契约：`BattleContext.Tick` 五段顺序（收泵→输入采样/上行（先于 Sim 步进）→出站→Sim.Tick→视图）、`InputLeadFrames=3`、`viewFrame=SimView.RenderFrame`、`StartGame.ConfigHash` 复算、`JoinAck.reconnect_token` 一次性/绑定/TTL、只卸本局 `_ownedScenes` 且卸载完成才释放占用、快照环 16 > 视图缓冲 8、回滚/和解段事件静默。
- 开局：`RunAsync` 内按序打开 RoomPage → 连接 → 等 StartGame → 建世界/场景/视图/HUD → 循环 Tick → 等 MatchEnded → 冻结结果 → 逆序清理 → 返回 `BattleResult`。场景装载/卸载归 `BattleFeature` 内部（不再由外部流程持有），清理在返回前完成，调用方无需观察者。`RunAsync` 自身只 await 内部 TCS；帧推进由 `ClientApp.Tick → BattleFeature.Tick` 驱动，避免“谁在 await 期间 Tick”的歧义。
- 结算：`BattleResult` 是强类型：`Outcome{Completed,SessionFailed,Aborted,LeftByUser}`、`PlayerId`、`MatchEnded` 冻结数据、`ISettlementSummary` 行表、失败分类/用户可读原因；`ResultFeature` 只消费它，不认 `BattleContext`。
- 断线/重连/退出：`BattleSession` 内部状态机；失败返回 `BattleResult.Failed(reason)`，由 AppFlow 决定回大厅/错误页；用户主动退出走 `Leave`，不产生 `Ended` 事件。

### 9. 设置/账号/匹配/战绩

- `SettingsFeature`：`SettingsService` 持 `GameSettings`（分组/键/默认/值域/生效器）。页面 `SettingsPage` 直接绑定服务；新增设置 = `GameSettings` 定义 + 页面控件绑定（1–2 文件）。没有 `ISettingsPagePort`/`ProductSettings` 双门面。
- `AccountFeature`：登录/登出/令牌/Profile/历史；`LoginPage` 属于它。其他功能通过 `AccountFeature` 公开方法取令牌/Profile，不直接碰 HTTP。
- `MatchmakingFeature`：**只负责大厅页 + 房间列表/快速进入/建房/取 Join 票据**；对外 `WaitForMatchAsync(ct)` 返回 `MatchInfo`。**房间连接、RoomPage 等待、StartGame 归 `BattleFeature`**——票据到对局之间没有跨功能会话交接，也不需要房间操作句柄。
- `RecordsFeature`：战绩页；读 `AccountFeature` 的历史接口。若后续发现与 Account 永远一起改，可并入 Account。
- `ResultFeature`：结算页；吃 `BattleResult`，提交结算由 `AccountFeature`/`MetaClient` 完成。

### 10. 程序集与启动

```text
AOT:  LiteFramework (1) + LiteNet + LiteGame.Bootstrap (1)   [现役外加 LiteCodeGen.Runtime；迁移期另加 LiteFrameworkNext + LiteGameNext.Bootstrap，均在 P3b2 合并/改名]
hot:  LiteGame (1) + LiteSim + LiteSync + Luban.Tables
Dev:  LiteGame.Dev (独立，不进发布包)
Edit: LiteGame.Editor / LiteFramework.Editor
```

- **计数口径**：现役玩家侧不是 6 件——AOT 有 `LiteFramework.{Core,Services,Adapters,Hosting}` 四件 + `LiteNet` + `LiteGame.Bootstrap` + `LiteCodeGen.Runtime`（8 件），迁移期再加 `LiteFrameworkNext` + `LiteGameNext.Bootstrap` 两个临时 AOT 件（**AOT 共 9**），加上迁出的 `LiteGame.MetaWire`/`LiteGame.SimConfig` 两个 hot 件与产品 hot 多件；**目标 10→6 + `Dev`**，在 P3b1/P3b2（框架合并、产品合并、临时 AOT 件合并/改名、LiteCodeGen 吸收/删除、Luban.Tables 吸收）达成；计数以 `hot-boundary-check.py` 的分区表为准。
- 产品层不再按模块拆 asmdef；功能边界由目录 + 约定 + 一条 L1 架构测试（禁止跨功能内部引用）保证。
- `GameEntry`（AOT）只做：平台初始化 → 装载 hot 程序集（HybridCLR）→ `ClientApp.RunAsync` → 每帧 `ClientApp.Tick` → 退出 `Shutdown`。不再有 `ClientHost`/17 段/容器。
- 编辑器 Domain Reload Off：只复位 `GameEntry.s_booted` 与 `UiRuntime` 静态时钟；其余状态由实例持有。

**“不保留旧设计”的定界**（避免把“复用实现”误读成“保留契约”）：

| 类别 | 对象 | 处置 |
|---|---|---|
| 复用实现、换新契约 | YooAsset 适配、Log、时间/池、Audio、Input、Settings 存储、Animation Backend、Camera 适配 | 代码可复制/裁剪进新 `LiteFramework`/`LiteGame`；公共类型/命名空间按新设计重写，旧契约不保留 |
| 复用行为语义、重写代码 | 模态栈、Back 优先级、焦点恢复、页面缓存/展示代次、转场、SafeArea | 在 `UiRuntime` 内重写；用行为用例钉住，不复制旧 `UIForm` 状态机 |
| 不复用、随旧目录删除 | `IClientSegment`/`ClientHost`/`SegmentContext`、`ServiceContainer`、`ClientScope`、`EventBus`/`CommandBus`、`UIService`/`UIForm`/`UINavigationController`、`PageLogicBase`/`PageServices`/`UIBindBase`/`UIBindIndex`、`BindNode`/`BindRoot`、`IContentService`/`IFileStore` 旧公共契约 | 旧目录删除时一并删除；`Next` 不引用 |
| 保留不动 | `LiteSim`/`LiteNet`/`LiteSync`、`RoomClient`、`RollbackSim`、快照/输入/协议 | 外部共享内核；只包装 |
| 删除/吸收 | `LiteCodeGen.Runtime`、旧 UI 工作台/契约/生成器、旧测试、`#uiform`/`PageIds`/`UIPageCatalog` | P3b 统一删除/吸收；先剥 prefab BindNode |

### 11. 迁移与切换（P0/P1/P2/P3a/P3b）

| 阶段 | 内容 | 退出条件 | 归属 |
|---|---|---|---|
| **P0 骨架（本轮）** | `Assets/LiteGame/Next/` 子树、AOT/hot 程序集、`GameEntryNext`、`ClientApp`、`UiRuntime`、`Platform` 最小集（Log/Content loader/Settings 内存实现）、占位页 + 真实 `LoginPage`（UI 假账号）+ `ErrorPage`；旧目录原位 | `BootNext` 能进占位页/LoginPage/ErrorPage；新增 EditMode 测试通过；Next 路径无旧 17 段/PageServices/PageIds/UIPageCatalog | 本次运行 |
| **P1 P0 页面** | Account/Matchmaking/Settings/Records/Result 功能与页面；Meta/Content/Config/Localization/Audio/Input 接入；登录→大厅→房间可用 | 真实 Meta 本地/云端登录、建房/进房、设置/战绩页可用；PlayMode 覆盖 | 后续运行 |
| **P2 战斗** | `BattleFeature` 边界；搬入 Session/World/View/Hud/Input/Presentation；本地服端到端 | 新客户端完成一局：进房→开局→开火→结算→回大厅；PlayMode + Player 冒烟 | 后续运行 |
| **P3a 收口能力** | 热更/错误恢复/Dev/GM/重连平价；共享源迁出；旧 prefab BindNode 剥离；Player 冒烟接线 | AC4c/AC4d 通过；共享源迁出并重算 BuildHash；Player 冒烟可执行 | 后续运行 |
| **P3b1 删旧** | 切 `Boot.unity`；删旧目录（保留 `Next`）/旧基座/旧 asmdef/旧测试/旧生成物/旧收集组/旧场景引用 | 旧目录与旧测试删除；Next 仍可编译；提交 + tag | 后续运行 |
| **P3b2 提升** | 把 `Next` 提升到 `Assets/LiteGame/{Bootstrap,Runtime}`，程序集改回 `LiteFramework`/`LiteGame`；更新 `arch/client-roots.json`、G2/G3/ScanTargets/hot-boundary、收集组/构建/文档 | 路径单源更新；G2/G3 **非空扫描**；AOT Bootstrap 优先级复验；AC5 通过 | 后续运行 |

**删树/切换顺序（解决“旧测试同批删除”与“切换前旧失败清单不变”的冲突）**：
1. **P3a（旧树仍在、旧测试仍在）**：共享源迁出；热更/错误恢复/Dev/GM/重连平价；旧 prefab BindNode 剥离；BootNext 加入 Build Settings/HybridCLR 列表；跑新增测试 + 旧失败清单不变。
2. **切 Boot（旧树仍在）**：`Boot.unity` 指向新入口；**Build Settings 只保留新入口场景，移除旧 Boot/旧测试场景**；新 PlayMode/Player 冒烟；旧测试仍可编译、旧失败清单不变；提交 + tag。旧收集组/旧地址在 P3b 前暂时保留，但不得与新收集组地址冲突（切换前跑一次地址重复检查）。
3. **P3b1（删旧目录与删旧测试同一提交）**：删除旧目录（**保留 `Next`**）/旧基座/旧 asmdef/旧生成物/**旧收集组**/旧场景组件引用/旧测试；更新 `Tests.slnx`、`arch/units.json`、`ScanTargets`、`hot-boundary` 分区、构建/文档；再跑 L1/新 L2/Doc。提交 + tag。
4. **P3b2（提升）**：把 `Next` 提升到 `Assets/LiteGame/{Bootstrap,Runtime}`，程序集改回 `LiteFramework`/`LiteGame`；更新 `arch/client-roots.json` 单源（G2/G3/AC2 扫描、`hot-boundary` PARTITION、`ScanTargets` 共读）、收集组/构建/场景/文档；**G2/G3 必须做非空扫描**（断言扫描到的功能/页面数 >0），并复验 `Assets/LiteGame/Bootstrap/` 的 AOT 优先级。提交 + tag。

**删树前检查清单（AC5 必做）**：
1. 共享源已迁出，两份 csproj 链接、`SHARED_LINK_FILES`、`META_EXTRA_FILES` 已更新，`build-hash --check` 通过，服务端可构建；两端发布顺序已执行。
2. `Assets/Tests` 中引用旧 API 的用例在 P3b 与旧树同一提交删除/改写；新测试 asmdef 独立；`Tests.slnx`/根 `Tests/` 工程引用已更新。
3. 旧 asmdef、`#uiform` 行/生成物、`UIPageManifest`/`UIApiSpec`/`BindGenerator`、`LiteCodeGen.Runtime`/prefab 上的 `BindNode/BindRoot` 已删除；新 `BindName` 组件已替换。
4. YooAsset 收集组/地址、`Boot.unity`/`BootNext.unity` 场景列表、**旧场景内旧组件引用**（`GameEntry`/`DevStartupConfig`/页面逻辑组件）、BuildProfile、`HybridCLRSettings.hotUpdateAssemblies`、`link.xml`/AOT 元数据、`build-player.py`/`player-smoke.py`、`hot-boundary-check.py` 的 `PARTITION`/`firstPartyRoots` 已更新：AOT 路径加 `Assets/LiteGame/Next/Bootstrap/`（必须优先于 hot 的 `Assets/LiteGame/` 匹配）、`Packages/com.litegame.framework/Next/`；hot 路径加 `Assets/LiteGame/Next/Runtime/`。以上路径以 `arch/client-roots.json` 为单源，脚本/测试不得各自写死；P3b2 提升后同批改该文件并复验优先级。
5. `arch/units.json`（含 `arch/client-roots.json` 登记）、`ScanTargets`、`Docs/说明/03/08/14/17`、设计索引、归档处置已回写；Doc 门禁通过。

- 迁移期临时程序集名：`LiteGameNext.Bootstrap`/`LiteGameNext`/`LiteGameNext.Dev`；切换时统一改名。
- 旧目录保持不动；`Next` 复制所需 prefab 到功能 `Content/`；切换后删旧 prefab/目录。
- 回滚：切换前随时可切回 `Boot.unity`；切换后保留一个 git tag/分支，删旧树单独提交，可 revert。

### 12. 守卫与规则（只保留少量）

1. **G1 AOT/hot**：沿用 `scripts/gate/hot-boundary-check.py`。
2. **G2 功能边界**：新增 L1 测试扫描 `arch/client-roots.json` 的 `nextRuntime`（默认 `Assets/LiteGame/Next/Runtime`）下的 `Features/<Feature>/`，禁止引用其他功能内部类型（只允许 `<Name>Feature` 公开类型）；禁止 `Abstractions/`/`SubModules/`/`Services/`/`Impl/` 目录；扫描结果必须非空。
3. **G3 无全局页面目录**：扫描 Next 运行时根（路径从 `arch/client-roots.json` 单源读取，不写死），禁止 `PageIds`/`UIPageCatalog`/`UIBindIndex`/`BindGenerator`/`#uiform` 生成物；页面只能通过 `PageSpec` 打开。**反向守卫**：`Next` 不得引用旧 `LiteGame.*` 或旧 `LiteFramework.*` 契约（`ClientHost`/`ServiceContainer`/`ClientScope`/`UIService`/`UIBindIndex` 等）；**允许**引用 `LiteFrameworkNext` 的新平台类型（`Log`/`Lifetime`/`Content`/`Audio`/`Input`/`Settings`）与 `LiteSim`/`LiteNet`/`LiteSync`/`Luban.Tables`。最终提升后程序集名改为 `LiteFramework`/`LiteGame`，白名单按新类型名维护，不按旧类型名放行。
4. **G4 触碰面**：脚本/测试记录 S1–S4 的文件数；新增页面 ≤2 代码文件 + 1 prefab、0 处中央登记、≤1 次内容/本地化生成；新增功能功能目录外 ≤1 行（参与流程 ≤2 行）。
5. **G5 冷启动**：给新子代理功能目录树 + 一句需求，要求 ≤2 文件且 0 反问（人工/子代理验收）。
6. **G6 禁止硬编码/静态可变状态**：沿用现有纪律扫描的适用项；不新增 public static 可变字段。

## 通用性与扩展性

- **根因**：旧架构把“功能”拆成声明（Abstractions）、实现（Client/SubModules）、装配（Segment/ComposeAfter）、注册（表/生成物）、页面（PageLogic/PageServices）五个面；新架构把五面收回一个目录。
- **扩展点**：
  - 新功能：目录 + 组合根一行；不触碰其他功能。
  - 新页面：页面类 + prefab；由所属功能打开；不改全局。
  - 新设置：定义 + 页面绑定；不改端口。
  - 新战斗表现：功能内部 View/Hud 文件；不改流程/UI。
  - 新平台能力：`Platform` 服务 + 构造注入；需要第二种实现时才抽接口。
  - 新表/协议/Sim：仍走外部共享内核的既有生成链；不在客户端架构内制造第二套。
- **避免特判**：功能入口统一返回 `Result<T>`/抛 `ClientException`；UI 统一 `PageSpec`/`PageBase`；资源统一 `ContentService`；不按页面/功能写全局 if。

## 测试策略

### 先失败后通过

- **P0 先写静态契约测试**：`Assets/LiteGame/Next` 路径/asmdef、功能目录规则、禁止旧机制 token、页面只能经 `PageSpec` 打开。文件不存在时测试失败是预期的，但静态测试不能替代行为测试。
- **P0 再写 UiRuntime 行为测试**：用 fake prefab loader 打开/关闭/缓存/模态/Back/输入阻断；`PageBase` 绑定/解绑/缺控件抛错。先红后实现。
- **P0 的 `ContentService`** 用 `IContentLoader` 双实现：`EditorAssetLoader`（AssetDatabase，仅编辑器/PlayMode 骨架）+ P1 的 `YooAssetContentLoader`（生产）；接口因存在第二实现而成立。P0 不承诺 Player 冒烟。
- **P0 用真实 `LoginPage`（UI 假账号）验证“页面 2 文件”口径**，占位页只用于启动链。
- **P1/P2 再写 PlayMode**：`BootNext` → 占位页 → 登录 → 大厅 → 房间（假 Meta/本地服）→ 战斗（本地服）→ 结算；之前不能承诺端到端。
- **Player 冒烟**：`BootNext` 加入 Build Settings；`LiteGameNext` 加入 `HybridCLRSettings.hotUpdateAssemblies`/`link.xml`/AOT 元数据；`hybridclr.py install` 后再跑；否则只算 Editor/PlayMode 证据。

### 测试层级

| 层 | 用例 |
|---|---|
| L1（dotnet） | 架构规则、功能边界、触碰面脚本、纯 C# 的 Settings/Account/Matchmaking 状态与结果类型 |
| L2 EditMode | UiRuntime、PageBase、资产校验、场景/内容服务 fake |
| L2 PlayMode | 页面打开链、模态/Back、登录→大厅→房间、战斗本地服一局、结算 |
| Player | Windows IL2CPP 启动冒烟 + `BootNext` 到登录页 |
| 手动/子代理 | 冷启动可发现性；真实指针/键盘操作路径 |

### 验证命令

- `python scripts/gate/test.py --suite L1 --profile PullRequest`（只认新增/相关用例通过；现有 L1 基线 1886 绿）
- `python scripts/gate/l2-unity-gate.py`（或 `python scripts/gate/unity-tests.py --mode editor --filter <新用例>`）；本轮只要求新增/相关 EditMode 用例全绿；**不把“全量 L2 绿”作为本轮门槛**——当前 L2 基线有 32 个历史失败且最近一次 `summary-l2.json` 是 `total=0` 的未真跑记录。
- `python scripts/gate/hot-boundary-check.py`
- `python scripts/codegen/toolbox.py build-hash --check-shared`（只在触碰共享源集时）
- `python scripts/gate/test.py --suite Doc`
- Player 构建/冒烟按 `Docs/说明/11-构建与本地联测.md`；**验收前先补 `python scripts/build/hybridclr.py install`**，否则 hot 段静默跳过，Player 冒烟不构成热更证据。

## 验收标准

> 编号与 acceptance.md 一致；每条可观察、可判定。**本轮只验收 AC1–AC3**；AC4–AC5 是程序里程碑，后续运行分别验收。

### 本轮验收（本次 staged-delivery 运行）

- [ ] **AC1 架构设计成立**：新架构文档写入 `Docs/设计文档/架构/客户端重写架构总设计.md`，明确列出旧设计废除清单、目标目录/程序集/生命周期/UI/战斗边界、迁移阶段与守卫；设计索引登记、旧文档处置清单明确；独立子代理评审 PASS；`python scripts/gate/test.py --suite Doc` 通过。
- [ ] **AC2 Next 子树可运行**：`BootNext.unity` 在 60s 内启动，经 `GameEntryNext` → `ClientApp` → `UiRuntime` 打开一个页面；静态扫描 `Assets/LiteGame/Next` 0 处旧机制 token（17 段、`ClientHost`、`ServiceContainer`、`PageServices`、`PageIds`、`UIPageCatalog`、`UIBindIndex`）；新增/相关 EditMode 用例全绿。
- [ ] **AC3 短链路达标**（口径已按 Unity/本地化事实修正）：
  - 新增页面 = **2 个代码文件**（页面类 + 所属功能的打开调用点）+ 1 个 prefab；0 处**中央页面登记**；若页面有新文案 key，允许 1 次本地化表行/生成，不把这次生成算作“页面登记生成”。页面寻址 = `PageSpec.Address`（YooAsset AddressByFileName）+ prefab 根 `PageBase`；无全局注册表。
  - 改按钮逻辑 = 1 个代码文件（页面类）。
  - 新增功能 = 功能目录内文件 + 功能目录外 ≤1 行（`ClientApp` 构造）；若参与 AppFlow，再加 1 行（≤2 行）。
  - 新增设置 = ≤2 个代码文件（定义 + 页面绑定）+ 1 个 prefab + 可选 1 条本地化行；0 处页面目录生成。
  - 页面/组件/数据类因 Unity 序列化需要可 `public`；其余默认 `internal`。资产、收集组、测试、构建/场景接线不计“手改代码文件”，但同批记录在触碰面证据中。
  - 冷启动子代理 ≤2 个文件且 0 反问。
  - 与 G4 的预算统一为：≤2 个代码文件、0 处中央登记、≤1 次内容/本地化生成。

### 后续里程碑（不在本轮验收）

- [ ] **AC4a 核心闭环（P2 生效）**：登录 → 大厅 → 建房/进房 → 对局（本地服）→ 结算 → 回大厅；ErrorPage 可达；PlayMode 端到端用例通过；Player 冒烟无 `BootstrapError`（P2 先 `hybridclr.py install` 并接好 hot 清单）。
- [ ] **AC4b 旁路页面（设置 P1；战绩 P2）**：设置页 P1 可用；战绩页依赖结算，P2 可用；两者都不打断 AppFlow；PlayMode 覆盖。
- [ ] **AC4c 恢复（P2/P3a 生效）**：断线→重连→恢复（本地服/假传输至少一条用例）；401/令牌失效回登录；取消/退出不悬挂。
- [ ] **AC4d Dev/GM 平价（P3a 生效）**：F1/F2/F3 开发面、本地服、GM 进入/退出/传送与正式构建剔除；不要求编辑器工作台/预览矩阵。
- [ ] **AC5 切换完成（P3b1/P3b2 生效）**：P3b1 切 Boot（旧目录/旧测试仍在，新流程+旧失败清单不变）→ 删除旧目录（保留 `Next`）/旧测试/旧基座/旧 asmdef/旧生成物/旧收集组/旧场景组件引用；P3b2 把 `Next` 提升到正式路径/程序集名，更新 `arch/client-roots.json` 单源与 G2/G3/ScanTargets/hot-boundary，跑非空扫描 + AOT Bootstrap 优先级复验；共享源迁出完成并重算 BuildHash；`hot-boundary-check`、L1、新增/改写 L2、Doc 门禁通过；Player 冒烟与热更安装记录；文档索引回写。

## 风险与回滚

| 风险 | 影响 | 监控/发现 | 处理 |
|---|---|---|---|
| 战斗搬运破坏确定性/同步 | 对局不可玩/分叉 | 本地服 PlayMode 一局、快照/checksum 用例、Player 冒烟 | 保留 Sim/Net/Sync 不动；战斗边界内先搬会话/视图，再逐步替换；失败回滚该批 |
| HybridCLR/hot 程序集切换 | 真机/Player 装载失败 | `BootNext` Player 冒烟、`hot-boundary-check` | 沿用现有 `HotUpdateLoader`；先 Editor/PlayMode，再 Player；必要时保留旧 Boot；验收前先补装 hot 产物 |
| L2 基线不绿 | 无法用“全量 L2 通过”判定 | 每次只跑新增/相关 filter；记录旧失败清单 | 切换前：“新增用例全绿、旧失败不变”；切换后：旧测试同批删除/改写，跑新增/改写后的客户端用例并记录历史无关失败，不把全量绿当唯一门槛 |
| 新 UI 运行时行为回归 | 页面/模态/Back/焦点异常 | EditMode + PlayMode 页面用例、真实操作路径 | 行为清单逐条对照旧运行时；先移植 `UIModalStack`/`UIFocusCoordinator`/转场/展示代次语义，再删旧 |
| 旧目录/Next 双程序集冲突 | 编译/类型冲突 | L2 编译、asmdef 检查 | 临时 `Next` 命名空间/程序集；切换时一次性提升/改名；不共享同名类型；`Next` 禁止引用旧契约 |
| 范围蔓延（编辑器工具/背包/新玩法） | 工期/风险 | 任务清单与验收对照 | 非目标清单拒绝；新需求另开运行 |
| 旧树删除后无法回滚 | 无法短期恢复 | 切换前 git tag | 切换单独提交 + tag；删旧树与前一切换分离 |
| UI prefab 复制漂移 | 视觉/交互回归 | 页面截图对照、真实操作路径 | 复制后只替换根组件与绑定名；旧 prefab 在切换前不动；用现有视觉基线对照 |
| 资产收集/内容包漏项 | 真机缺资源 | 收集组校验、Player 冒烟 | 按功能目录通配收集；新增页面同批加收集规则/校验 |
| 共享源被误删 | 服务端编译/BuildHash 断裂 | 迁出清单、服务端构建、`build-hash --check` | 共享源迁出任务先行；未完成不得删 `Assets/LiteGame` |
| 旧测试/asmdef/生成物未同批处置 | 切换后 L2 编译失败 | 删树清单、L2 编译 | AC5 列为必做项；删树与切 Boot 分两个提交 |
| 旧 prefab 的 BindNode/LiteCodeGen 依赖 | 新 UI 运行时无法摆脱旧绑定 | prefab 扫描、`LiteCodeGen` 引用检查 | P1 复制页面时改用新 `BindName` 组件并移除 BindNode；P3 删 `LiteCodeGen.Runtime` |

**回滚路径**：P0–P2 阶段始终可通过 `Boot.unity` 回旧客户端；P3b 切换提交可 revert；删除旧树单独提交，保留 tag。
