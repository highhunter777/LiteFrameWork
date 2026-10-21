# UI 工作台设计

> 状态：**W0/W1/W2 均落地**——场景 `UiWorkbench.unity`（由 `HUDDesign.unity` 改造：用户裁决复用避免历史场景累积）+ 面板（WorkbenchPanel：目录 Clone_FormRow/演示组五钮/状态行）+ 工具条（WorkbenchOverlay：语言行/状态注入三钮/HUD 预览/回写入口，页面层之上）+ `UIWorkbenchDriver`（DevHUD 程序集，整件套三宏；asmdef 补 UniTask/Unity.TextMeshPro/Luban.Tables/LiteGame.LuaBridge 引用）。W1 进度演示接线、W2 六项（含回写工具）逐项见 §6 与 [施工进度/UI工作台W2.md](../../../施工进度/UI线.md#ch7-UI工作台W2)。操作手册：[Docs/项目说明/08-UI工作台.md](../../../项目说明/08-UI工作台.md)（打开/就绪、开页、语言与边界态注入、HUD 预览、回写闭环、排障）；实机验证：可点性射线自检 36/36 + 模拟点击全链（见施工记录）
> 范围：dev-only 手测工作台——不必跑完整业务流程即可开/关任意已登记页、驱动加载/错误/Toast 反馈面、目检多语言与布局
> 上位：[UI框架总设计](UI框架总设计.md)（真装配、视觉单一来源、反馈面）；[UI测试开发专项设计](../../测试与性能/UI测试开发专项设计.md)（自动化与人工分工）
> 先例：~~`HUDDesign.unity`（场景内设计 UI）~~——**已改造为本工作台场景**（2026-10-11）；`SimSandbox` + 开发面程序集 `LiteGame.DevTool`（场景级调试件、标注"不担产品职责"、零产品程序集改动）
> 事实依据（2026-10-10 代码核对）：`GameEntry.TakeContainer` 公开给开发工具；`StageMachine.Current/Started`；`UIService.CloseAsync/CloseAllOpen/IsOpen`；`UINavigationController.GoAsync`；`UiInput.EnsureEventSystem`（运行时幂等建位）；`LiteGame.DevTool.asmdef` 已引 `LiteGame.App/UI`

## 0. 定位与边界

- 工作台 = **人工目检与制作迭代**载体：眼睛与手感归它；断言与证据归 L2 自动化（夜间门禁）——两者互不替代（[UI测试开发专项设计 §1.1](../../测试与性能/UI测试开发专项设计.md) 三类测试职责）。
- **不旁路装配**：场景跑**真装配根**（GameEntry → ClientHost → 全模块 → 流程机 Launch→Patch→Preload→Main 真跑），只换"落点"——开工作台面板而非玩法页/业务首屏。禁止自建 UIService/假加载器（"替身让真链路缺陷隐形"教训，[UI-U2](../../../施工进度/UI线.md#ch3-UI-U2)）。
- 面板本体 = **场景内 UI + 驱动件**，**不是 tbuiform 页面**（"独立工作台场景"裁决的直接读法）：免去 dev 页进 `Assets/ShareResource/UI/Screens` 收集路径与表行的生产污染（BaselineA/B 在 Screens 内属框架期先例，不构成业务期路径——业务 UI 不走代码生成，2026-10-04 裁决）。
- 默认仅编辑器 Play 使用；是否进开发档 Build Settings、发布剥离口径随 DevHUD/SimSandbox 现状登记（见 §5），本设计不另开机制。

## 1. 场景与装配

- **面板落点＝dev 预制体 `WorkbenchUI.prefab` ＋纯叠加 UI 场景 `UiWorkbench.unity`（W2 追加，2026-10-16 收敛）**：`WorkbenchPanel`＋`WorkbenchOverlay`＋驱动收在一个预制体里（**内容单源**，EditMode 守卫钉"驱动引用齐备 + 路径↔资产一致 + 场景不含面板件"）；`UiWorkbench.unity`＝**只有该预制体实例**（无引导件、无相机、无场景入口）——入口统一走 **Boot**（`DebugTuner.WorkbenchMode` / 开发页「UI 工作台」→ 加性加载），**不支持直接 Play 该场景**；退出经「设置菜单 → 退出工作台」→ 运行时卸载本场景（`SceneManager.UnloadSceneAsync`；Play 内 `EditorSceneManager` 无效）＋落回大厅页。dev 内容不在任何收集组，故加载**仅编辑器形态**。
- 场景 `Assets/Scenes/UiWorkbench.unity`（**已就位**——2026-10-11 由 `HUDDesign.unity` 改造：pipeline rename 保持 GUID，旧 HUD 树已清，`GameEntry`+主相机已挂；面板已抽预制体，见上）：
  - 最小件 = **GameEntry（引导件）+ 主相机**（对照 `Boot.unity` 启动场景同款最小形态）；输入底座 `[UIEventSystem]` 由 `UiInput.EnsureEventSystem()` 运行时幂等建位，无需场景 EventSystem 对象。
- 驱动件 `UIWorkbenchDriver.cs` 落 `Assets/LiteGame/DevTool/`（既有 dev 程序集 `LiteGame.DevTool`，已引 `LiteGame.App/UI`——SimSandbox 同款；**零产品程序集、零产品码分支**；2026-10-11 asmdef 补 `UniTask`/`Unity.TextMeshPro`/`Luban.Tables` 三引用）：
  - **取根**：`GameEntry.TakeContainer()`（公开口，注释明言"引导与开发工具取得已装配的根容器"）——Update 轮询至装配完成。
  - **就绪门控**：`StageMachine.Current == ProcedureId.Main` 后按钮启用（= 内容已初始化，开页安全）；未就绪置灰 + 状态行提示（如"启动中：Patch/Preload"）。
  - **真链路照跑**：编辑器下 Patch 走本地通道（未配 CDN = 无网络依赖离线形态，即快）；Main 阶段仍会加载玩法场景（TrainingGround）——面板不阻止（无害背景）；不做场景名门控跳过（保持零产品码分支；如成负担随 W2 评估）。
  - 说明：`ProcedureMain` 首屏自动打开随五页批C 接线后，工作台场景会继承该行为——面板一键可关；如需隔离随 W2 评估。

## 2. 面板结构（场景内）

建议名过[制作规范 §9](UI制作规范.md) 文法；dev 件不在 §9 强制面，词条从简（需要时按 §1 登记）：

**两块画布的分工（W2）**：`WorkbenchPanel`（排序低于页面层）承载"开页前后都用"的目录与演示面；`WorkbenchOverlay`（排序在页面层之上）承载**须在页面打开时也可点**的工具面——页面近乎不透明会盖住下面的面板，故语言/注入/HUD 预览/回写入口上浮到窄条工具条（紧凑两行，只占左下空地：不压 HUD 底栏血条与弹药、不遮页面主体）。

```
WorkbenchPanel                 场景面板根（Canvas + CanvasGroup；排序低于 UIRoot 语义层区间，页面上浮于它）
├─ Cot_Forms                   表单目录容器
│  ├─ Clone_FormRow            行模板（非激活；序列化引用接线：Button_Open / Button_Close / Txt_FormId）
│  └─ [运行时行：按 tbuiform 表逐行实例化]
├─ Cot_Demos                   反馈面演示组
│  ├─ Button_Loading           开加载阻断（BeginLoading）
│  ├─ Button_Progress          进度上报（LoadingScope.Report——随加载页批A 接口）
│  ├─ Button_EndLoading        收场（作用域 Dispose）
│  ├─ Button_Error             错误弹窗（ShowErrorAsync）
│  └─ Button_Toast             Toast（ShowToast）
└─ Txt_Status                  状态读数（流程阶段 / 就绪 / 语言 / HUD / IsLoading / 加载文案 / 最近操作结果）

WorkbenchOverlay               SRS）工具条根（Canvas sortingOrder 5000——页面层 100..499 之上；窄条不挡目检）
└─ Cot_Tools                   两行紧凑布局（宽 384）
   ├─ Button_LangZh / Button_LangEn / Txt_Locale   语言切换行（当前语言回读）
   ├─ Button_InjectEmpty / Button_InjectLong / Button_InjectReset   状态注入三钮
   ├─ Button_HudPreview         HUD 预览开关（钮面文字随开关态变）
   ├─ Button_WriteBack          回写工具入口（跳编辑器窗口——菜单路径单源由守卫用例钉住）
   └─ Txt_Tools                 窄幅读数（HUD 态 + 最近操作；全量读数在面板状态行）
```

- **「回写」工具（工具条常驻入口 + 菜单 `LiteGame/快捷/Prefab 回写`）**：Play 中对选中 prefab 实例做**逐属性 override 回写**——枚举属性清单勾选确认 → 逐属性直写 prefab 资产（不需退 Play）。裁决（2026-10-11）：**验收＝Game 视图、编辑＝Inspector（Play 模式选实例改属性）、固化＝Play 内勾选直写**；**不做整场景运行时保存**（真装配链的运行时生成物不得烤进场景资产——视觉单一来源/脏 diff/不代存三重违反）。
  - **来源两档（实测校准，2026-10-16）**：Unity 只对部分路径产生"非默认记录项"（如 `Image.m_Color`）——那部分标「**显式记录**」默认勾选；`SerializedObject` 直写与 RectTransform 的位置/尺寸/锚点（`IsDefaultOverride` 恒真）分不出"谁改的"，标「**数值差异**」默认不勾、逐条核对「实例值 → 资产值」——**驱动写面由此保持"默认不落盘"**（数据值/动态位置归驱动常量或表，不归 prefab）。
  - **结构面不自动回写**：实例新增组件/新增子对象、指向场景对象的引用、不支持的类型**显式列出且拒绝**（结构面归 Prefab Stage 与 Unity 原生 Overrides）。
  - **不回滚实例 override**：固化后实例上仍留着已等值的记录（视觉无差、复扫不再列为差异）；要实例回归干净态用 Unity 原生 Overrides ▸ Revert。
- **目录行来源 = 真表 `Tbuiform.DataList`**（id/layer/全屏）——五页（11-15）登记后**自动出现、零改码**；行克隆走 `Clone_` 条目模板模式（实例化模板行，不建结构——与列表内核同判）。
- 打开/关闭：`UINavigationController.GoAsync(id)` / `UIService.CloseAsync(id)`；系统面（201/202/203）不做目录开关，走演示组服务面（统一入口纪律：反馈面经 `FeedbackService`）。
- **语言切换行（W2）**：两个按钮经 Lua 桥 `Bridge.Text.SetLocale`（**设置页同一条链**——`LocalizationService.SetLocale` → 页面索引 `ILocaleBound` 自动重刷已绑定文案）；当前语言读回显示在工具条。
- **状态注入（W2）**：**空列表**＝对目标页全部 `VirtualList`/`SimpleListView` 注入零行源；**长文本**＝对目标页全部 TMP 文本追加固定样本（记录原文，可显式还原）；**复位**＝还原文本 + 列表走"关+开"真链路复位。**作用域＝本工作台目录打开过的最近页**——页面实例取用口未对产品开（`UIService` 公开面只有 `IsOpen`；不为 dev 工具新增产品 API），无目标页时状态行显式提示；注入是预览性质，页面重开/数据推送即自然复位。
- **HUD 预览（W2）**：内容服务租约取 `BattleHudView.PrefabLocation` → 裸实例化（**零接线静态画面**：无对局数据源，数值/准心为设计期默认值；缺件只报错不炸面板）；再点关闭＝销毁实例 + 归还租约，随面板销毁归还。HUD 根画布 sortingOrder 0（低于页面层）——**与对局内层级一致**：目检 HUD 需先关页。
- ~~`Button_Progress` 依赖 `LoadingScope.Report`~~——**W1 已接线（2026-10-11）**：`FeedbackService.ReportLoading`/`LoadingScope.Report`（文案+progress01，首写不丢/后写胜出）落地，按钮按 0.25 步进演示；状态行补加载文案回读（`CurrentLoadingMessage`）。配套：Loading 表单模板补视觉（Blocker/Message/ProgressBar_Load + LoadingMask 接线——原模板为空壳）

## 3. 消费计划

| 消费 | 用法 | 批 |
| --- | --- | --- |
| 加载页批A/B 手测 | 演示组：开阻断 → 进度上报 → 收场；Back 取消（编辑器 ESC）目检 | W0/W1 |
| 五页批C 验收 | 目录一键开/关/复用目检（登录/大厅/房间/结算/设置） | 随五页批 |
| 控件与动效迭代 | 打开 Baseline/ListScreen 先例页（204/205/206）看效果 | 常备 |
| 多语言 / 溢出目检 | 工具条语言行切 zh/en 实时看页面文案（含列表行重绑）；文本膨胀/空列表注入看边界态 | W2 |
| HUD 静态目检 | 工具条 HUD 预览开关（无对局数据源——看静态布局/配色/控件位置） | W2 |
| **UI 效果调参**（UiFx/转场） | 真链路触发动效实时看手感 → 回写工具固化——**前提：效果参数数据化/组件化**（代码常量 Inspector 改不到；与《UI效果系统专项设计》"数据化定义"互推：谁要被调谁先数据化） | W2 + 效果数据化批 |

## 4. 与测试面的分工（不重复造）

- 断言、取消/超时/代次、租约与清理对称 → L2 EditMode/PlayMode（夜间门禁）；工作台**不做自动断言、不进 `scripts/gate`**。
- 视觉人工档的现实载体：[UI测试开发专项设计 §4.4](../../测试与性能/UI测试开发专项设计.md) 的 V1/V2 人工确认可用本场景进行；V0 失败诊断截图仍在用例侧，门禁口径不变。

## 5. 边界与防锈

- **dev-only 与发布剥离（W2 已落实）**：不担产品职责（SimSandbox 同款标注）。剥离面三件——①`UIWorkbenchDriver` **整件套三宏**（`UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`，与 `DevHUD.cs` 同款，release 剥净）；②`LiteGameIgnoreRule.DevAssetExclusions` **dev 资产排除**（`Assets/Scenes/UiWorkbench.unity` 不入 Scenes 收集组 ⇒ 不进生产包）；③EditMode 守卫钉"排除路径存在 + 规则真的忽略 + 产品场景不误伤"。**余**：出包流程侧"bundle 清单不含 dev 件"的静态检查（《热更与内容发布专项设计》§304 L0）未实施——已登记审计表。
- **防锈登记**：owner = 客户端 UI 线；失效条件 = ① L2 PlayMode 已覆盖其手测场景，或 ② 制作线出现更顺的页面预览工具；到期未用即退役（防 Boot/GuideScene 式历史场景累积；[代码地图](../../../开发导航/代码地图.md) 已标"历史场景"）。
- **不做整场景运行时保存**：Play 态真装配链的运行时生成物不得烤进场景资产（视觉单一来源/脏 diff/不代存三重违反）；需要的固化走 prefab 回写（W2）——场景与 prefab 的分工：场景不承载可调视觉，prefab 承载。
- 不新增第二个"打开任意页"入口：对局入口＝开发页（测试模式/直连对局）+ `DebugTuner` 直入开关（原 F9/F10 热键已删）；本件只管 UI 页。
- **启动落点与开发入口（2026-10-16 流程整理，取代先前的"不做"评估）**：**一个决策点**（`ProcedureMain.ResolveLanding`）＋**一张入口矩阵**：
  | 形态 | 启动落点 | 登录页 | 对局场景组 |
  |---|---|---|---|
  | 正式（Meta 已配置） | 登录页 | 加载 | 不加载（按对局加载——见下"对局场景组"） |
  | 本地服务器（开发） | **大厅页**（跳过登录；房间列表按服务可用性显文案） | 不加载 | 同上 |
  | **直入训练场**（新开关，**优先级最高**） | 测试模式对局（本地服 Room-Test） | 不加载 | 进入时加载 |
  | **直入工作台**（`WorkbenchMode`） | 工作台（加性场景） | 不加载 | 不加载 |
  | 常态运行中 | 大厅页 →「开发」入口（dev 覆盖件）→ 测试模式 / UI 工作台 | — | 同上 |
  **F9/F10 热键已删除**（入口全走 UI：开发页 + 直入开关；对局内退出走 ESC→设置页→退出对局）。
  **工作台载体＝场景**：`UiWorkbench.unity` 为"相机 + 携带引导件（禁用态）+ 场景入口 + 开关 + 面板预制体实例"——两种进入形态共用同一场景（**直接 Play**：场景入口启用携带引导件走真装配；**Boot 工作台模式加性加载**：宿主已在场，携带引导件保持禁用，不二次装配）。
  - **不做场景名门控**（按活动场景名判断）——那要靠"产品码里的场景名与场景文件一致"，红线 5 违规形态；
  - **走既有 dev 开关面**：字段 + 静态镜像 + `#if` 门禁读（与 `UseLocalServerEnabled` 同款，`DebugTuner` 整件随三宏剥离，release 无此面），各场景的序列化值决定形态——**工作台场景默认开、Boot 默认关**（Boot 行为不变）；
  - **退役面**：`ProcedureMain` 的"跳过对局场景组"读在对局按需加载落地后删除；开关保留为"工作台入口"（开发页「UI 工作台」同一条链）。
  - （形态沿革）早先形态＝"Boot 勾选即在工作场景自建面板预制体"，因两入口各存一份、且无法作为场景加载而改为**场景承载**。
  - **对局场景组＝按对局进出**（已落地）：**加载与卸载都在 `ProcedureBattle`**——`StartGame` 到达后加载（Rig ＋ 本局地图，进度进"正在进入对局…"开场遮罩；**未开局散场不加载**），离场收尾卸载（正常结束/主动离场/失败三出口统一）；场景单源 `BattleScenes`（Rig 常量 ＋ 地图场景**查表行 `scene` 列**——**视觉与判定必须同图**）；`ProcedureMatch` 与 Main 都不加载任何对局场景。
  - **训练场＝第二张地图**（已落地）：场景标记件（`MapCollision`/`spawn_00..15`/`map_bounds`——布点工具版本化 `TrainingGroundLayout`）→ 烘焙 `Luban/Data/Map/*.json`（每图一文件，`tbmap` 双行，行内 `scene` 列）→ 运行期按**房事实行键**（`JoinAck.map_id` → `BattleScenes.ResolveMapId`）选表行与场景。
  - **待落地**：①设置页绑定缺陷修复（`Dropdown_Quality` 类型不符——现状整页打不开，见审计表）②大厅页地图选择控件（见 [开房参数选图与人数](../../../施工进度/玩法规则与开房线.md#ch3-开房参数选图与人数)）：大厅页 `Cot_CreateSetup` 选图＋选人数（票据 v3 盖章 / `JoinAck` 回执 / 客户端定容）。
- **状态注入不作用于"非工作台打开"的页面**（如入口自动开的登录页）——需先在目录开一次页；若要覆盖须给 `UIService` 开只读"取已开 form"口（产品 API，另批裁决）。

## 6. 批次

| 批 | 交付 | 依赖 |
| --- | --- | --- |
| W0 场景+驱动 | 场景（GameEntry+相机+WorkbenchPanel）+ `UIWorkbenchDriver`（TakeContainer 轮询取根、Loaded∧Main 门控置灰、目录行按 Tbuiform 克隆与开关〔系统面 201/202/203 置灰走演示组〕、演出组 Loading/错误/Toast、状态行 0.5s 节流）。实机手测待本机 | — |
| W1 进度演示接线 | `LoadingScope.Report`/`ReportLoading` 落地（FeedbackService，写全量/首写不丢/后写胜出）+ Loading 表单模板补视觉（原空壳）+ 驱动演示按 0.25 步进 + 状态行文案回读 | — |
| W2 工具条与回写（可选，随真实需求） | 语言切换行、状态注入（空列表/长文本/复位）、**prefab 改动回写工具**（Play 内勾选直写、来源两档标注、结构面拒绝）、HUD 预览开关（实例化 `Hud_Battle.prefab`）、发布剥离落实（三宏 + dev 资产排除 + 守卫）、**工作台模式开发开关**（`DebugTuner.WorkbenchMode`：Main 不加载对局场景组 + 面板预制体自建——Boot 勾选即进工作台）。交付与证据见 [施工进度/UI工作台W2.md](../../../施工进度/UI线.md#ch7-UI工作台W2) | 效果参数数据化（回写范围=序列化面——回写工具不依赖它；数据化仍决定"能被调的参数有多少落在序列化面"） |
