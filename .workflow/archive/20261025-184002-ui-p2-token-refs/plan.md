# 方案：UI 系统 P2-3 B2：令牌引用组件与应用

> 运行 ui-p2-token-refs · 创建于 2026-10-25T14:18:32+08:00

## 背景与目标

- 现状（P2-3a 之后）：
  - 令牌值已收敛到 `UiStyleTokens.json`（12 色 + 5 文字角色）→ `ui.style.tokens` 确定性生成 `Assets/GameData/Config/UI/UiTheme.asset`（SO，随 Config 收集组进包）；**B1 冻结为纯数据、无运行期消费者**。
  - 编辑器读面 `UiTokenCatalog`（Editor 程序集、每次操作装载、禁静态缓存）；`UiStyle`/`UiTextStyle` 旧名退役为 catalog 派生只读属性。
  - prefab 颜色＝**裸值**：`UiStyleTool.ApplyColors` 用"最近 token 匹配 + 阈值"改写（可重入、改值可传播）；`ui.style.color-drift` 报不命中 token 的颜色（真实面 161 Warning，B4 收敛）。
  - 文本侧已有声明标记 `UiTextStyleRef`（角色枚举，"只声明、不驱动"，工具批量应用/对账；未标记文本 141 Warning）。
  - 组件状态色**自持**：`StateButton`/`TabGroup`/`StarRating` 以序列化 `Color` 字段 + C# 默认值实现（`StateButton.cs:16-19`）。
  - 运行期：无任何令牌解析；Lua 侧无颜色 API（`UIBindIndex` 无 SetColor，Lua 零调用）——B2 不动 Lua 面。
- 要解决的问题（总设计 §6.2「引用与应用」的 B2 缺口）：
  1. prefab 颜色没有**引用组件**——语义（"这是 color.bg.screen"）不可声明、不可校验、主题切换无从谈起；
  2. `UiThemeAsset` 无运行期消费者（红线 10 接线债悬着，B1 以"批次边界"豁免）；
  3. 校验器没有「令牌不存在」判据（P2-3a 遗留点名：`ui.style` 族 Note"令牌不存在待 B2 引用组件批"）；
  4. 状态色映射未裁决（P1 边界收口遗留）。
- 目标（可观察）：
  - prefab 可挂 `UiStyleRef` 声明颜色令牌；编辑期工具按声明应用（幂等、可回退〔口径见设计要点〕、报告）；
  - 运行期页面装配时按声明把令牌现值写到 Graphic（降级安全）；
  - `UiThemeAsset` 有真实运行期消费链（App 装载 → UIForm 消费）；
  - 校验新增引用族规则（未知 token/Error、值漂移/Warning、无目标/Error、widget 状态 token 三态、同节点双写者/Error）；
  - 状态色裁决落文（见候选 D3）。

## 需求澄清与假设

- 需要确认的问题（验收确认阶段一并裁决）：
  - D1 运行期角色：方案 A（纯烘焙，运行期不解析）vs 方案 B（装配期应用，推荐）；
  - D2 工具入口：`ui.style.colors-refresh` 升级为 refs-aware vs 新增独立动作 `ui.style.refs-apply`（推荐并入刷新：一次跑完 refs 应用 + 裸值最近匹配，报告分段）；
  - D3 状态色：豁免 vs 本批令牌化（原推荐豁免）。
- 已确认答复（用户裁决，2026-10-25，AskUserQuestion）：
  - **D1＝方案 B（装配期应用）**；
  - **D2＝并入 `ui.style.colors-refresh`**；
  - **D3＝本批令牌化**（未采纳豁免推荐）——`StateButton`/`TabGroup`/`StarRating` 三个自研部件的多态状态色本批全部走令牌；范围只辖这三个 widget，**uGUI 原生 `Selectable.colors` 不在本批**（实测 `Assets/ShareResource/UI` 内 `m_Colors:` 33 处/17 prefab——Screens+Hud 16、Widgets 17；留 B4 复议，审计登记按可复算扫描落清单）。
- 采用假设：
  - 主题资产与表同在 Config 收集组，运行期装载时序/通道与表一致（YooAsset `AssetService.LoadAssetAsync`；bootstrap 侧一次装载）；
  - B2 消费者只有 color 组（refs）与 typography 组（既有文本工具）——`spacing/radius/elevation/motion/icon` 五组继续空集，"按消费者落地"（P2-3a 冻结口径）；
  - 首版只有 Default 主题；主题切换只留接口（`Apply` 可重入），不做切换 UI（总设计 §6.1）。
- 假设影响与风险：若运行期装载时序与表不一致（如 UI 起得比 Config 早），降级路径（保持烘焙值）兜底；五组留空若被质疑"半成品"，以 P2-3a 冻结口径与审计登记回应。

## 范围与非目标

- 范围内：
  1. `UiStyleRef` 颜色引用组件（LiteClient.UI 运行时程序集）；
  2. 令牌解析核心单源化：解析核心下沉 `LiteClient.UI`（运行时可引用），编辑器 `UiTokenCatalog` 变委托（"单源核心＋委托"模式，编辑器域先例＝P1 遍历统一；跨程序集可行性＝`LiteGame.Editor` 已引用 `LiteClient.UI`）；
  3. 运行期装配应用：`UIForm` 增 `ThemeProvider` 注入缝（`LocaleProvider` 同款 internal Func + SafeCall 降级）+ 装配期应用；
  4. 编辑期 applier：refs 声明应用（幂等、可回退〔口径见设计要点〕、报告），并入 `ui.style.colors-refresh`（D2 推荐）；
  5. 校验规则族：`ui.style.ref`（未知 token/值漂移/无目标/widget 状态 token 三态/双写者）；
  6. 状态色令牌化（D3 用户裁决）：三 widget（`StateButton`/`TabGroup`/`StarRating`）状态色走令牌——映射表（**复用既有令牌优先**，只补真正缺的状态令牌；值取现序列化/C# 默认值量化，≤0.5/255 不可见）、widget 增可选 token ID 字段（空＝未令牌化，行为不变）、装配期解析（见设计要点）、未解析/降级用序列化 fallback 值、迁移工具段并入刷新（最近匹配填 token ID，幂等）；
  7. 试点挂载：≥1 个产品页面/控件模板挂 refs（证真实链路；全量收敛归 B4）——挂载经 applier 工具或 unity-pipeline CLI 完成（红线 4：序列化资产禁手改 YAML）。
- 非目标：
  - 161 处裸颜色/141 处未标记文本的存量收敛（B4）；
  - 主题切换 UI 与多主题资产（只留可重入接口）；
  - spacing/radius/elevation/motion/icon 令牌补值与对应应用器；
  - uGUI 原生 `Selectable.colors`（实测 33 处/17 prefab）令牌化——本批只辖自研三 widget（D3 裁决原文），uGUI 面留 B4 复议并审计登记；
  - 三 widget C# 字段初始化器里的存量默认色值清理（本批不新增同型；存量审计登记，B4 随存量收敛清）；
  - Lua 颜色 API（Lua 侧零需求）；
  - 预览矩阵/设备档（B3）。

## 现状与约束

- 相关代码/模块：
  - 运行时：`Assets/LiteClient/UI/Style/{UiThemeAsset,UiTextStyleRef}.cs`、`UIForm.cs`（PrepareForShow/ApplyLocale 先例）、`Widgets/{StateButton,TabGroup,StarRating}.cs`（状态色现状）；
  - 编辑器：`Assets/LiteGame/Editor/Style/{UiTokenCatalog,UiStyle,UiStyleTool,UiTextStyle,UiTextStyleTool}.cs`、`UiTools/Validation/`（规则注册）、`Toolchain/EditorToolHandlers.cs`（工具面）；
  - 生成物：`Assets/GameData/Config/UI/UiTheme.asset`（禁手改）、`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`；
  - 运行期装载：`LiteClient/Adapters/Content.YooAsset/AssetService.LoadAssetAsync`、`ConfigService`（表快照先例）、App bootstrap（`ContainerModule`/`RegistryFiller` 侧注入点，施工时按实际装配点落）。
- 约束：
  - 红线 6：禁新增 public static 可变状态——解析器/主题持有全部走实例传递或 internal 注入缝（LocaleProvider 同款）；确定性判定路径（伤害/命中）不涉及（纯表现面）。
  - 红线 10：注册进容器的服务必须有生产消费者——本批 UiThemeAsset 消费链 = App 装载 → UIForm 装配应用（真实消费者闭环）。
  - 红线 5：token ID 词表单源（`UiStyle.LegacyTokenIds` 已是名字映射单源；refs 组件的合法 ID 集＝生成物实际令牌集，不抄第二份字面量表）。
  - 生成物纪律：`UiTheme.asset` 只经 `ui.style.tokens` 生成；refs 应用工具写 prefab 不写 SO。
  - 编辑器纪律：`UiTokenCatalog` 禁静态缓存口径保持（委托化后装载语义不变）。
  - `UiStyle`/`UiTextStyle` 只读属性与其调用点（规则/夹具）源兼容——回归必须全绿。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 纯烘焙 | `UiStyleRef` 只作声明标记（UiTextStyleRef 同款）；编辑期工具按声明烘焙值进 prefab；运行期不解析、不应用 | 与文本侧对称；零运行期成本；改动面最小 | `UiThemeAsset` 仍无运行期消费者（红线 10 债未清）；主题切换永远不可行；prefab 值与令牌漂移只靠编辑期对账 | 不推荐：B2 的立项理由（B1 边界点名"运行期消费者"）落空 |
| B 装配期应用（推荐） | `UiStyleRef` 声明 token；解析核心下沉 LiteClient.UI；App 装载 SO 一次 → `UIForm.ThemeProvider` 注入 → 装配期（PrepareForShow）按声明写 Graphic；编辑期 applier 同一应用核心（单源）烘焙/收敛；装载失败降级保持烘焙值 | 运行期消费链闭环；主题切换可演进（Apply 可重入）；prefab 烘焙值＝编辑器预览态 + 运行期降级兜底（双意义）；解析核心单源（编辑器委托运行时） | 每页 spawn 遍历 refs 写值（量级：每页几十个，可忽略）；App 装载时序接入点需按 bootstrap 实况落；改动面中等 | **推荐**：完整落 §6.2"运行期只读解析"，B1 边界债清偿 |
推荐方案与理由：**B**。理由：①B1 冻结口径"B2 起由样式引用/解析消费"只有 B 能兑现；②降级路径（SafeCall + 烘焙值兜底）与既有 UI 纪律（语言注入同款）一致，运行期风险可控；③"单源核心 + 委托"模式在本仓有先例（P1 遍历统一 `BindSurfaceWalk` 下沉＋`UiBindSurfaceWalker` 委托——编辑器域内的单源化）；本批跨到运行时程序集的可行性由引用方向保证（`LiteGame.Editor` 已引用 `LiteClient.UI`，见 `UiTokenCatalog.cs` 的 `using LiteClient.UI`），回归面已知（UiTokenCatalog 调用点全在编辑器侧）。

## 设计要点

- 接口/数据：
  - `UiStyleRef : MonoBehaviour`（LiteClient.UI；`[AddComponentMenu("LiteGame/UI/UiStyleRef 颜色令牌引用")]`，`[DisallowMultipleComponent]`）：
    - `[SerializeField] string colorToken`（冻结令牌 ID，如 `color.bg.screen`；空＝未声明合法、应用跳过）；
    - 目标 v1＝自身 Graphic（Image/TMP/Text 通用 `Graphic.color`）；多目标（Outline/Shadow/填充色）留后续版本扩展，不预造字段；
    - 只声明不驱动：无 Awake/OnEnable 写值逻辑（写值统一走应用核心，单写者）。
  - `UiStyleResolver`（LiteClient.UI，纯类，**解析核心单源**；命名从总设计 §6.1）：`FromAsset(UiThemeAsset)`；`TryColor(id, out Color)` / `TryTypography(id, out UiThemeTypographyEntry)`；`Apply(UiStyleRef, Graphic)`（读声明 → 查令牌 → 写 `Graphic.color`；未知 ID 返回结果枚举供报告/规则复用，不猜默认色——沿 `UiTokenCatalog` 口径）；颜色命中阈值（现 `UiStyleTool.HitEpsilon`＝1e-4）**移驻本类**为单源，工具/`ui.style.ref` 规则/`UiRuleThresholds` 三处引用同一常量。
  - `UiTokenCatalog`（Editor）：变薄委托——装载（AssetDatabase）后内部经 `UiStyleResolver.FromAsset` 构建；公开面（`TryColor/TryTypography/Colors/Typography/Theme/SourceHash`）签名不变，调用点源兼容。
  - `UIForm`：`internal Func<UiThemeAsset> ThemeProvider { get; set; }`（LocaleProvider 同款）。真实接线缝＝`UiServiceModule.BuildLocaleProvider` → `UIService` ctor 形参 → `RunOpenAsync` 单点赋值（池化复用靠 form 实例持有持久生效）——ThemeProvider 按同款扩展（ctor 形参 + RunOpenAsync 赋值），这也是 PlayMode 用例置 null 的唯一注入口；`PrepareForShow` 内一次遍历 Root 下 `UiStyleRef` 应用（页面级一次，逐节点不重装载——与静态文案 binder 同模式）。
  - 编辑器应用核心 `UiStyleRefApplier`（Editor）：遍历扫描目录 prefab → 按 refs 声明经 catalog 应用（幂等、报告：应用/跳过/未知 token 计数）；沿用既有 `PrefabUtility.LoadPrefabContents → Save → Unload` 批量模式（与 `ApplyColorsWith`/`ApplyTextStylesWith` 同型）。**回退口径**：批量 applier 的"可回退"＝幂等重跑零变更 + 变更仅经工具落盘、可由版本管理回退（Unity Undo 记录在该批量模式下不跨资产落盘生效）；总设计 §6.2「可 Undo」措辞本批同批澄清为该口径（交互式单页 Undo 若未来需要，另批再上机制）；`UiStyleTool.ApplyColorsWith` 前置调用（先 refs 精确应用，后裸值最近匹配收敛，报告两段分开）。
  - 状态色令牌化（D3 用户裁决，三 widget）：
    - 令牌映射表（**以 prefab 序列化现值为权威**，复用优先，**不强合并近似值**）——实测去重后：新增 **8 枚** `color.state.*`（值与来源，量化 ≤0.5/255 零可见变化）：
      | 候选 ID | 值 | 来源 |
      |---|---|---|
      | color.state.interact.normal | #4073BF | Tmp_StateButton.prefab:96 NormalColor（0.25,0.45,0.75） |
      | color.state.interact.pressed | #264D8C | Tmp_StateButton.prefab:97 PressedColor（0.15,0.3,0.55） |
      | color.state.interact.disabled | #595959 | Tmp_StateButton.prefab:98 DisabledColor（0.35,0.35,0.35） |
      | color.state.interact.selected | #E6B333 | Tmp_StateButton.prefab:99 / Tmp_TabGroup.prefab:316 选中金（0.9,0.7,0.2，两处同值） |
      | color.state.nav.selected（终名施工冻结） | #F2CC4D | Tmp_BottomNav.prefab:60 选中金（0.95,0.8,0.3——第三枚近金，与上枚 R/G/B 差 12/25/26 每通道，可见，不合并） |
      | color.state.nav.normal（终名施工冻结） | #BFBFC7 | Tmp_BottomNav.prefab:61 TabGroup NormalColor（0.75,0.75,0.78） |
      | color.state.star.on | #FFCC33 | Tmp_StarRating.prefab:439 On（1,0.8,0.2） |
      | color.state.star.off | #4D4D4D | Tmp_StarRating.prefab:440 Off（0.3,0.3,0.3） |
      复用 1 枚：Tmp_TabGroup 常态白＝`color.text.bright`。近金/近灰**合并属可见色变、超出 D3 授权**，本批不合并（若用户后续想收敛色板，另批裁决）；映射表住设计文档供人审；迁移工具不硬编码"部件→ID"字面映射（按现值最近匹配自动填，通用）。
    - widget 字段：各状态色字段旁增伴生 `[SerializeField] string xxxToken`（空＝未令牌化，行为不变）；`RefreshVisual` 取值序＝token 已填且 resolver 可解析 → 令牌现值，否则序列化 Color fallback。
    - 装配注入：`IThemeBound { void OnThemeBound(UiStyleResolver resolver) }`（**组件层级能力接口**；先例＝能力接口 `ILocaleBound`〔其派发面是页面 Logic〕＋层级扫描 `UiStaticTextBinder`/`LTextKeyRef`〔页面级一次遍历〕，本机制取后者形态）；`UIForm.PrepareForShow`（ThemeProvider 非空时）遍历 Root 下 `IThemeBound` 注入 resolver 实例——widget 持实例字段，无静态状态（红线 6）；编辑器域/测试无注入时 fallback 行为不变。**写者裁决**：refs 应用遍历**跳过挂 `IThemeBound` 组件的节点**（widget 是自身 Graphic 的唯一写者——`StateButton.GetComponent<Image>()` 就在自身节点）；`ui.style.ref` 增判据"同 GameObject 上 `UiStyleRef`＋`IThemeBound` 并存＝Error"。
    - 迁移工具段：`ui.style.colors-refresh` 第三段——扫描目录内三 widget 组件：token 为空且 Color 值命中（阈值内）某令牌（含新补状态令牌）→ 写 token ID；幂等；不命中不填（空合法，B4）；报告计数。
    - 校验扩展：`ui.style.ref` 第 4 判据——widget 状态 token 已填但 ID 不在令牌集＝Error；已填且值漂移＝Warning；空＝合法不报（B4 前存量口径，基线 Warning 不涨）；第 5 判据——同节点 `UiStyleRef`＋`IThemeBound` 并存＝Error（双写者，见装配注入裁决）。
- 关键流程：
  - 运行期：bootstrap 装载 `UiTheme.asset`（内容服务，一次）→ 注入 UIForm ThemeProvider → 页面 PrepareForShow：refs 遍历 → resolver.Apply → 覆盖 prefab 烘焙值为令牌现值；
  - 编辑期：`ui.style.colors-refresh`（D2 并入）→ refs 应用（可回退/报告）+ 裸值最近匹配（既有）→ `ui.style.colors-report` 对账新增 refs 维度；
  - 校验：`ui.style.ref` 五判据（组件三判据＋widget 三态＋双写者，见 AC5）。
- 失败与边界处理：
  - 运行期装载失败/资产缺失：ThemeProvider null 或装载异常 → 应用跳过，页面用 prefab 烘焙值，`Log.Warning` 一次（SafeCall 语义，不阻断开页）；
  - 未知 token ID：运行期跳过该节点 + Warning 一次；编辑期规则 Error（见 AC5）——**不猜默认色**；
  - 同节点既挂 ref 又被裸值工具改写：refs 段先应用＝权威写者，裸值段跳过已声明节点（报告计数）；
  - `Tmp_*` 模板内 refs 随实例化生效（克隆体带组件与声明，值在装配期写）。

## 通用性与扩展性

- 根因/共性问题：prefab 颜色语义不可声明（裸值）→ 校验/收敛/主题切换全都缺锚点；解析核心长在 Editor 程序集 → 运行期永远无消费者。
- 复用与扩展点：
  - `UiStyleRef` 字段形状对齐 `UiTextStyleRef`（声明式标记），后续 spacing/radius 引用组件按同模式扩展（新组件新字段，不改本组件）；
  - `UiStyleResolver.Apply` 结果枚举 → 编辑器工具报告、校验规则、运行期 Warning 三方共用（一个判据单源）；
  - ThemeProvider 注入缝 = 主题切换（B 后续）的驱动点：换资产重注入 + 重开页面重应用即可，无新增机制。
  - **已知边界（登记）**：运行时动态克隆体（列表行内 StateButton/挂 ref 节点）不在 `PrepareForShow` 遍历面内——B2 单主题下烘焙值＝令牌现值、无可见差异；主题切换批前需补克隆体注入机制，勿误判已覆盖。
- 避免硬编码/特判的约束：合法 token ID 集＝生成物令牌集（运行时由 resolver 判存在性，编辑器下拉来源 catalog）；不写"ID 以 color. 开头即合法"之类的前缀特判；阈值/扫描目录沿用 `UiStyleTool.DefaultScanDirs` 单源。

## 测试策略

- 测试层级与关键用例：
  - EditMode（新 `UiStyleRefEditModeTests`）：声明/空声明合法；应用写值正确（真 catalog）；未知 ID 跳过且结果枚举正确；无 Graphic 节点应用失败口径；幂等（重复应用零变更）。
  - EditMode（新 `UiThemeResolverEditModeTests`→ 实作名 `UiStyleResolverEditModeTests`，命名从总设计 §6.1）：FromAsset 快照语义（与 UiTokenCatalog 委托后回归——既有 `UiStyleRetirementEditModeTests` 等全绿）；重复 ID/空 ID 行为与 P2-3a 口径一致。
  - EditMode（规则夹具扩展 `UiValidationRulesEditModeTests` 或新套件）：未知 token Error；值漂移 Warning（阈值同源）；无目标 Graphic Error；widget 状态 token 三态判据（未知 Error/漂移 Warning/空合法）；例外登记降级生效（复用 UiRuleExceptions）。
  - EditMode/PlayMode（widget 注入与降级）：`IThemeBound` 注入后 RefreshVisual 取令牌值；无注入/未令牌化＝fallback 值不漂；迁移段幂等（重跑零变更）与不命中不填。
  - EditMode（工具面）：`EditorToolCatalog`/wiring 断言 refs 应用并入刷新动作；真实目录冒烟后不落盘（既有口径）。
  - PlayMode（新 1 例，行为族通道）：真 prefab 挂 ref → 页面打开后颜色＝令牌现值；ThemeProvider 置 null → 保持烘焙值且开页不抛。
- 如何先失败后通过：全部新夹具与规则断言在实现前落盘并跑 `wf verify --red`（断言失败转红；既有套件作回归基线）；PlayMode 用例在组件/注入缝落地后先行写好再实现。

## 验收标准

- [ ] AC1（单源）：令牌解析核心住 `LiteClient.UI`（`UiStyleResolver`，命名从总设计 §6.1），编辑器 `UiTokenCatalog` 为委托；颜色命中阈值单源驻 resolver（工具/规则/`UiRuleThresholds` 引用同一处）；既有编辑器调用点与全部既有 UI 夹具回归全绿（UiTokenCatalog 公开面签名不变）。
- [ ] AC2（组件）：`UiStyleRef` 上任（token ID 声明、空合法、单组件约束、只声明不驱动）；试点≥1 个产品页/控件模板真实挂载（经工具/unity-pipeline，非手改 YAML）。
- [ ] AC3（编辑期应用）：refs 应用并入 `ui.style.colors-refresh`（D2）：按声明写值、幂等（重跑零变更）、报告含 应用/跳过/未知 token 计数；已声明节点不被裸值段改写；回退口径＝幂等重跑＋变更仅经工具落盘（版本管理可回退），总设计 §6.2「可 Undo」措辞同批澄清（依赖 D2 确认）。
- [ ] AC4（运行期消费链）：bootstrap 侧装载 `UiTheme.asset` 一次并经 `UIService` ctor 形参＋`RunOpenAsync` 赋值注入 `UIForm.ThemeProvider`；页面装配后 ref 节点颜色＝令牌现值（PlayMode 真 prefab 证）；装载缺失/异常降级＝保持烘焙值 + Warning 一次、不阻断开页。
- [ ] AC5（校验）：`ui.style.ref` 规则族进 `UiValidator.Rules`（未知 token＝Error、声明值与令牌现值漂移＝Warning〔阈值单源〕、节点无 Graphic＝Error、同节点 `UiStyleRef`＋`IThemeBound` 并存＝Error〔双写者〕）；真实面跑一次 0 新增 Error（试点页干净）。
- [ ] AC6（状态色令牌化·D3 用户裁决）：三 widget（StateButton/TabGroup/StarRating）状态色令牌化落地——①映射表落设计文档：**新增 8 枚** `color.state.*`（逐枚清单含值与来源 file:line，量化 ≤0.5/255 零可见变化；近金/近灰不合并——合并属可见色变，超出 D3 授权，本批不做）、复用 `color.text.bright`；取值权威＝**prefab 序列化现值**（C# 默认值与 prefab 不一致处归⑤存量登记）；`UiStyleTokens.json` 补集后 `ui.style.tokens` 重生成 SO（幂等/漂移规则回归全绿）；②widget 伴生 token 字段 + `IThemeBound` 装配注入（refs 遍历跳过 IThemeBound 节点）+ fallback 降级（夹具/PlayMode 证取值序）；③迁移段并入 `ui.style.colors-refresh`（最近匹配填 token，幂等、不命中不填、报告计数）；④`ui.style.ref` 扩展 widget 判据（未知＝Error/漂移＝Warning/空＝合法）与双写者判据（并入 AC5）；⑤uGUI `Selectable.colors`（实测 33 处/17 prefab）与三 widget C# 默认色存量登记审计、留 B4 复议。
- [ ] AC7（工具/门禁面）：工具登记与索引同步（若动作语义变化则 `toolchain.json`/`Docs/工具链/工具索引.md` 同批）；设计文档回写齐——总设计 §6.1（resolver 命名）/§6.2（ThemeProvider 缝、「可 Undo」澄清）/§8.1（refs-aware 刷新口径、B2 消费者）；`.workflow/config.json` 增本批门禁命令；`toolchain.py check` 0 error；`wf verify` 全绿（L1 + 全部 UI EditMode + PlayMode + doc）。

## 风险与回滚

- 风险：
  - bootstrap 装载时序（UI 起得早于资产可用）→ 降级路径兜底 + 首开页可能短暂用烘焙值（视觉等价，令牌值与烘焙值同源时无差异）；
  - 状态色迁移改 prefab（Widgets 面三 widget 所在模板）→ 迁移段幂等 + 不命中不填（不产生新基线 Warning）+ 变更仅经工具落盘（版本管理可回退）；
  - `UiTokenCatalog` 委托化牵动编辑器调用面 → 公开签名不变 + 全量 UI EditMode 回归钉住；
  - 工作区并行线（TPS/weaknet/build-preflight/docs）未提交——本批改动尽量收敛在 UI 面文件；提交时按批拆分（P2-3a 同法）。
- 监控/发现：`ui.style.token-drift`（既有）+ `ui.style.ref`（新）双规则在门禁持续对账；PlayMode 行为用例钉住降级路径。
- 回滚：组件/规则/工具均为增量（无既有调用点强依赖）；回滚＝摘除 refs 组件消费链（UIForm 注入缝置 null 即回到纯烘焙行为），prefab 值不被破坏。
