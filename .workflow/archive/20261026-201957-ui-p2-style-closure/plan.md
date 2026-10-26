# 方案：UI 系统 P2-3 B4：存量样式收敛与收口

> 运行 ui-p2-style-closure · 创建于 2026-10-26 · 批次：P2-3 B4
> 设计依据：[UI 系统总设计 §6](../设计文档/客户端/UI系统/UI系统总设计.md#ch-style) · [UI 编辑器工具链专项设计 §8.1](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-style-tools) · [UI 制作规范](../设计文档/客户端/UI系统/UI制作规范.md)
> 前批：[UI系统线 第 11～12 章](../施工进度/UI系统线.md)（B2 令牌引用应用、B3 设备档与预览矩阵）

## 背景与目标

### 现状（2026-10-26 真实 Editor 盘点）

用只读脚本 `AgentScripts/StyleInventory.cs` 经 unity-pipeline 扫描 `Assets/ShareResource/UI/{Screens,Widgets,Hud}` 全部 prefab（46 个）：

| 面 | 实测 | 说明 |
|---|---|---|
| 颜色令牌 | 20 枚（12 基础 + 8 状态） | `UiStyleTokens.json` → `UiTheme.asset` |
| Graphic 颜色 | **309** 处，命中 152 / **不命中 157** | 157 即 `ui.style.color-drift` Warning 主体；44 个不同色值 |
| 文本节点 | 141 个，**全部未挂 `UiTextStyleRef`** | `ui.style.text-drift` 的「未标记」141 Warning |
| 文本精确匹配 | **37 个节点**（Body 35 + Emphasis 2） | 其中 **14 个是嵌套 prefab 实例**（8 个 `Hud_Battle`→`Tmp_RankCell`、4 个 Screens/`Tmp_Dialog`→Widgets/`Tmp_Dialog`、1 个 `Tmp_ListScreen`→`Tmp_VirtualList`、1 个 `Tmp_Toast`→Widgets/`Tmp_Toast`）；其 **7 个源节点（4 个源 prefab）**同在本扫描面且同样精确匹配 → **实际新增 23 个组件即可覆盖 37 节点**（14 个由源模板继承） |
| uGUI Selectable | **46 个**（18 prefab；33 个直挂 + 13 个模板继承） | 33 个序列化 ColorBlock 全部为同一套 uGUI 默认值：Normal `#FFFFFFFF`、Highlighted/Selected `#F5F5F5FF`、Pressed `#C8C8C8FF`、Disabled `#C8C8C880`、multiplier 1、fade 0.1；13 个继承自 Widgets 的 7 个源 Selectable（Tmp_Dropdown×2、Tmp_Dialog×2、Tmp_HpBar/Tmp_Slider/Tmp_Toggle 各 1）；HUD 的 `_Slider` `targetGraphic=null`（无 tint 视觉），HUD 实际只有 `Button_Settings` 1 个有目标按钮 |
| 颜色量化差 | 157 个未命中值中 **154 个**浮点值与 8bit 量化令牌值有差（最大 0.00196/通道） | 补令牌后 `ui.style.colors-refresh` 的裸值段会把这些浮点重写为 8bit 令牌值；**8bit 完全一致、不可见**，但**逐位不变不成立**——零可见变化按 `Color32` 量化判据，不按 float 位判据 |
| 令牌值重值 | `color.state.selectable.normal`=`#FFFFFFFF` 与既有 `color.text.bright` 同值；highlighted/selected 互相同值 | 显式 ID 消费（`UiSelectableTheme` 常量）不受影响；`UiStyleTool.TryNearestMatch` 的 `<=` 取后者必须定义确定性 tie-break，并避免 widget 迁移误填 selectable 令牌 |
| 现有 UiStyleRef | 1 处（`Page_Login/Cot_SafeArea/Txt_Title`） | B2 试点 |
| 现有 UiSelectableTheme | 0 | 本批新建 |

基线校验总量 **Warning 298 = 颜色 157 + 未标记文本 141**（`UiValidationEditModeTests` 真界面校准钉）。B2 登记的 C-1（IThemeBound 祖先子树盲区）、C-4（主题资产 null 告警一次无直接断言）、C-5（三 widget `Pick` 逐字重复）同批收口。

> 盘点证据：`AgentScripts/Artifacts/style-inventory.json`（309/152/157/44、141/37）、`style-nesting.json`（37 中 14 嵌套/4 源 prefab 的 7 源节点）、`style-selectable-nesting.json`（46=33 直挂+13 继承/7 源）、`style-color-precision.json`（157 中 154 有量化差）。四份为只读盘点产物，不入库。

### 目标（可观察）

1. 颜色漂移 157 → **0**：44 个现有色值按语义各建一枚**精确值**令牌（零可见变化），另补 5 枚 Selectable 状态令牌；令牌集 20 → **69**。
2. Selectable 状态色全部令牌化：46 个 ColorTint Selectable **全部覆盖**（33 个直挂组件 + 13 个由 5 个源 prefab 的 7 个源 Selectable 继承）；页面经 `UIForm` 装配注入，HUD 经 `BattleViewRig` 注入；prefab 序列化 ColorBlock 逐位不变，运行时按 8bit 令牌值注入（≤0.5/255，不可见）。
3. 未标记文本 141 → **104**：只给属性与现有 5 个角色档完全一致的 **37 个节点**补 `UiTextStyleRef`（Body 35 + Emphasis 2；实际新增 **23 个组件**，14 个由 4 个源 prefab 的 7 个源节点继承），不新增角色、不改字号、不改文本属性。
4. 三个自研 widget 的 C# 默认 RGB 字面量清零；取值序收成 `UiStyleResolver.Resolve(token, fallback)` 单源。
5. `ui.style.ref` 双写者判据从「同 GameObject」扩到「`IThemeGraphicWriter` 祖先子树」；新增 `ui.style.selectable` 规则；主题资产 null 告警一次有直接断言。
6. 真实面 Warning 基线 298 → **104**（颜色 0 + 未标记文本 104），并在夹具中钉死。
7. 全量门禁 PASS；文档/审计回写完成。

### 非目标

- 主题切换 UI/多主题资产、运行时克隆体主题注入（B2 已登记，主题切换批前补）。
- 剩余 104 个文本节点的角色扩展（新增字号/对齐档）——另开文本样式批。
- 44 个色值的近似归并/视觉调色；本批严格零可见变化。
- 五组空令牌（spacing/radius/elevation/motion/icon）；HUD 其余样式重构；P3 运行时调试、P4 迁移、P5 生产闭环。

## 需求澄清与假设

### 已确认答复（2026-10-26 用户裁决）

| 编号 | 问题 | 裁决 |
|---|---|---|
| D1 | 157 处裸色（44 个不同色值）的口径 | **精确补 44 枚令牌**，零可见变化；不合并近似色 |
| D2 | 141 处未挂文字档的文本 | **只标精确匹配的 37 个**（Body 35 + Emphasis 2），其余留债 |
| D3 | 46 个 uGUI Selectable 的状态色 | **专用组件 `UiSelectableTheme` + 5 枚令牌**，运行时随主题注入 |

### 采用假设与影响

- **A1：现有 44 个色值即视觉基线**。新增令牌只做命名与登记，不改变任何 prefab 颜色；若某色值语义命名不准确，后续可改名（令牌改名需迁移映射，按总设计 §6.1）。测试夹具两枚用 `color.debug.*`，不冒充产品语义。
- **A2：Selectable 的 46 个 ColorBlock（33 直挂 + 13 继承）是 uGUI 默认交互反馈，不是品牌色**。5 枚令牌按状态语义命名（normal/highlighted/pressed/selected/disabled），不与 `color.state.interact.*`（自研 widget 状态）混用。
- **A3：HUD 是生产消费者**。`Hud_Battle` 的 2 个 Selectable 也随主题注入；`_Slider` 来自 `Tmp_HpBar` 且 `targetGraphic=null`（无 tint 视觉，组件继承但无操作），`Button_Settings` 有目标、可观察。把 `UIService` 已装载的 provider 透传给对局视图，失败降级保持烘焙值（与页面同口径）；真 HUD prefab 的 binder 注入用 EditMode 断言（见测试策略），`BattleViewRig` 只加单点调用。
- **A4：C# 默认色字段以 prefab 序列化值为权威**。删除三个 widget 的字段初始化 RGB 后，生产 prefab 已有值不受影响；运行时 `AddComponent` 若无 token/序列化值则保持 Unity 默认（透明），由使用方显式配置——现有生产代码无运行时 AddComponent 点。

## 范围与非目标

### 范围内

1. **令牌补集**：`UiStyleTokens.json` 新增 44 枚精确色 + 5 枚 Selectable 状态色；重生成 `UiTheme.asset`；补 schema/README 说明。
2. **Selectable 令牌化**：
   - 新增运行时组件 `UiSelectableTheme`（`IThemeBound`，5 个令牌 ID 常量，写 `Selectable.colors` 的 5 个颜色，保留 multiplier/fade；仅 ColorTint 生效；未注入/未知令牌保持现值）。
   - 新增编辑器迁移件 `UiSelectableThemeApplier.EnsureAll`（**只补组件、不写颜色**；直挂 Selectable 幂等补组件，跳过嵌套 prefab 实例由源模板继承）；真实面预期 **新增 33 个组件、覆盖 46 个 Selectable（13 个继承自 5 个 Widgets 源 prefab 的 7 个源 Selectable）**。
   - `UiThemeBinder` 单一应用核心：`IThemeBound` 注入 + `UiStyleRef` 应用 + `IThemeGraphicWriter` 祖先子树跳过；`UIForm.ApplyTheme` 改为委托；`UiStyleRefApplier`/`StyleRefRule` 共用同一判据。
   - HUD 注入：`UIService.ThemeAssetProvider` → `ProcedureBattle` → `BattleViewRig` 实例化 HUD 后调用 binder；null 降级。
3. **文字档迁移**：`UiTextStyleTool.MigrateExactRoleRefs`（属性与角色档完全一致才加 `UiTextStyleRef`；**跳过嵌套 prefab 实例**，由源模板继承；扫描面必须含 `Hud`；幂等、报告、不猜语义）；真实面预期 **新增 23 个组件、覆盖 37 个节点**（14 个继承），未标记 141→104；不改任何文本属性。
   - **实例覆盖口（实现期发现的必要边界）**：`Tmp_VirtualList`/`Tmp_SimpleList` 源模板被 4 个列表实例继承，后者的 `Left` 对齐是有意的实例覆盖。给 `UiTextStyleRef` 增 `UseInstanceOverrides`（默认 false），4 个实例置 true：工具不应用/不判 Mismatch，但**仍计未标记债**（104 不变，零 Mismatch）。只加字段与 4 个 override，不改任何属性/视觉。
4. **C# 默认色清理**：`StateButton`/`TabGroup`/`StarRating` 删除 `new Color(...)` 初始化器；新增 `UiStyleResolver.Resolve`；三处 `Pick` 收成一行。
5. **规则收口**：
   - 新增 `IThemeGraphicWriter` 写权接口（`Owns(Graphic)`），三 widget 只认领自身/列表 Graphic；`UiStyleRefApplier`/`UIForm`/`StyleRefRule` 统一按「自身或祖先是该目标的 writer」跳过/报 Error（不按整棵子树认领）。
   - 新增 `ui.style.selectable` 规则（缺组件 Warning / 无 Selectable Error / 未知令牌 Error / ColorBlock 漂移 Warning；仅 ColorTint 适用；`UiSelectableTheme` 不算 Graphic writer，不在 `ui.style.ref` 误报）。
   - 补 `UIForm` 主题资产 null 告警一次的 PlayMode 断言（C-4）。
   - 令牌重值 tie-break：`UiStyleTool.TryNearestMatch` 对同值多令牌给确定性规则（显式 ID 消费者不受影响；widget 迁移不得误填 selectable 令牌），并加夹具。
6. **基线/测试/文档/门禁**：真实面基线 0/104/104；新增/扩展夹具并**加入 `.workflow/config.json` 的 commands 与 red_commands**（否则 `wf verify` 不跑新面）；章节 13、设计 §6.1/§6.2/**§6.5**、工具链 §8.1/**§8.3**、UI 制作规范、审计修复登记、施工进度索引。

### 非目标

见「背景与目标·非目标」。特别地：本批**不做**主题切换，因此不实现克隆体注入；`UiThemeBinder` 只作为后续克隆体/主题切换的既有入口，不新增无消费者的 API。

## 现状与约束

### 相关代码/模块

| 面 | 位置 |
|---|---|
| 令牌源/生成 | `Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`、`UiThemeTokens/Validator/AssetGenerator`、`Assets/GameData/Config/UI/UiTheme.asset` |
| 运行时解析 | `Assets/LiteClient/UI/Style/UiStyleResolver.cs`、`UiStyleRef.cs`、`IThemeBound.cs`、`UIForm.cs` |
| 编辑器应用 | `Assets/LiteGame/Editor/Style/UiStyleTool.cs`、`UiStyleRefApplier.cs`、`UiTextStyleTool.cs`、`UiTokenCatalog.cs` |
| 三 widget | `Assets/LiteClient/UI/Widgets/{StateButton,TabGroup,StarRating}.cs` |
| 校验 | `Assets/LiteGame/Editor/UiTools/Validation/{UiValidator,UiRuleIds}.cs`、`Rules/StyleRefRule.cs`、`Rules/StyleColorDriftRule.cs` |
| HUD | `Assets/LiteClient/UI/UIService.cs`、`Assets/LiteGame/App/Procedure/ProcedureBattle.cs`、`App/Presentation/Battle/BattleViewRig.cs`、`App/Bootstrap/ContainerModule.cs` |
| 夹具 | `Assets/Tests/EditMode/UiTheme*`、`UiStyle*`、`UiWidgetThemeEditModeTests.cs`、`UiValidationEditModeTests.cs`、`Assets/Tests/UI/PlayMode/UiThemeApplyPlayModeTests.cs` |

### 约束

- **红线 4**：所有 prefab 改动经 unity-pipeline（本批用 `run_script` 调用编辑器迁移件），禁手改 YAML。
- **零可见变化（8bit 判据）**：迁移只新增组件、不写任何 Graphic 颜色或 ColorBlock 序列化值 → prefab 颜色/ColorBlock **逐位不变**；运行时 `UiSelectableTheme` 注入按 8bit 令牌值量化，与 prefab 浮点值差 ≤0.5/255（不可见）。**不把 `ui.style.colors-refresh` 裸值段的浮点重写当作失败**：实测 154/157 处浮点与 8bit 令牌值有差（最大 0.00196/通道），重写后 8bit 完全一致。
- **幂等**：`EnsureAll` 与 `MigrateExactRoleRefs` 重跑新增 0；已挂组件不重复；`ui.style.tokens` 二次 `changed=false`。**不要求** `ui.style.colors-refresh` 在补令牌后零变更（裸值段会把浮点收敛到 8bit 令牌值）。
- **嵌套 prefab**：Selectable 与文本都只为直挂节点补组件；嵌套实例由源模板继承，禁止在页面里制造重复 override。7 个 Selectable 源组件（5 个 prefab）与 7 个文本源节点（4 个 prefab）都在 Widgets 扫描面内；实例节点按 `PrefabUtility.IsPartOfPrefabInstance` 跳过并单独计数。
- **无共享源集/表结构变更**：不触碰 `Assets/LiteSim/Core`、`Assets/LiteNet/{Proto,Protocol}`、`Assets/GameData/Generated`、Luban 表源；`gen-build-hash.py` 预期无需重跑。
- **发布剥离**：新增组件为运行时必需，不涉及 dev-only；HUD 注入失败只降级不阻断。
- **单一写者**：三 widget 的 `IThemeGraphicWriter` 自持其子树 Graphic 写权；`UiSelectableTheme` 只写 `Selectable.colors`（状态 tint），不写 `Graphic.color`，因此与写基色的 `UiStyleRef` **可共存**（基色×状态 tint 叠加）；`ui.style.ref` 只对 `IThemeGraphicWriter` 祖先子树报双写者，不把 `UiSelectableTheme` 当 writer。
- **失败写盘口径（如实）**：批量工具按 prefab 逐个 `LoadPrefabContents→SaveAsPrefabAsset`，单个 prefab 失败不保存该 prefab；已保存的前序 prefab 不做跨资产事务回滚（版本管理回退或幂等重跑）。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A1 颜色：精确 44 令牌 | 每个现有色值一枚语义 ID | 零可见变化；Warning 清零；可回退 | 令牌集扩到 69，含少量低频色 | **采纳（D1）** |
| A2 颜色：归并近似色 | ~15 枚令牌 + 归一化 | 令牌集小 | 可见色变，需逐页视觉确认 | 不采纳 |
| A3 颜色：登记例外 | 裸值登记 Owner/到期 | 不动资产 | 157 条例外＝债隐藏，不是收敛 | 不采纳 |
| B1 Selectable：专用组件 | `UiSelectableTheme` + 5 令牌 | 语义清晰；运行时随主题；零可见变化 | 新增组件/33 次挂载 | **采纳（D3）** |
| B2 Selectable：只抽常量 | 校验 ColorBlock，不加运行时组件 | 改动小 | 主题切换不覆盖 uGUI 态色，留二次返工 | 不采纳 |
| C1 文字：精确 37 | 只标与现有档位完全一致者 | 零可见变化；37 个立刻收口 | 剩 104 留债 | **采纳（D2）** |
| C2 文字：扩角色覆盖 141 | 新增 10-20 个角色档 | 未标记清零 | 角色体系膨胀，需设计裁决 | 不采纳 |
| D 三 widget C# 默认色 | 删除初始化器，prefab 权威 | 业务代码零 RGB；测试显式赋值 | AddComponent 默认透明 | 采纳 |
| E Pick 收口 | `UiStyleResolver.Resolve` | 三处重复变一处；语义单源 | 无 | 采纳 |

## 设计要点

### 1. 令牌补集（69 色）

- 新增 44 枚精确值颜色令牌，ID/值/来源见**附录 A**；新增 5 枚 Selectable 状态令牌见**附录 B**。测试夹具两枚用 `color.debug.*` 命名（不冒充产品语义）；**值允许跨语义重值**（如 selectable normal 与 text.bright 同白），ID 必须唯一。
- 所有描述写「语义 + 实测来源（prefab/节点）」，不改任何 prefab 颜色。
- JSON schema 2.0 不变；`ui.style.tokens` 重生成 `UiTheme.asset`（幂等）。
- 令牌 ID 文法：`^(color|font|space|radius|elev|motion|ease|icon)\.[a-z0-9]+(\.[a-z0-9]+)*$`——新增 ID 全部小写点分，无连字符。

### 2. `UiSelectableTheme`（运行时）

```csharp
[DisallowMultipleComponent]   // 不 RequireComponent：由 ui.style.selectable 报"无 Selectable"Error（可测的显式判据）
public sealed class UiSelectableTheme : MonoBehaviour, IThemeBound
{
    public const string NormalToken = "color.state.selectable.normal";
    public const string HighlightedToken = "color.state.selectable.highlighted";
    public const string PressedToken = "color.state.selectable.pressed";
    public const string SelectedToken = "color.state.selectable.selected";
    public const string DisabledToken = "color.state.selectable.disabled";

    public void OnThemeBound(UiStyleResolver resolver) => Apply(resolver);
    public void Apply(UiStyleResolver resolver);   // 仅 ColorTint；5 色逐项 TryColor 判定后一次写 Selectable.colors；保留 multiplier/fade
}
```

- 未知令牌：保留该字段当前值，`Log.Warning` 每组件每令牌一次（校验器应报 Error）。未注入/resolver null/资产缺失：保持 prefab 烘焙值（与页面同口径）。
- `Apply` 只在 `Selectable.transition == ColorTint` 时写；写一次 `ColorBlock`（不逐项赋 `Selectable.colors` 五次），`colorMultiplier/fadeDuration` 原样保留。
- 迁移件 `UiSelectableThemeApplier.EnsureAll(catalog, dirs)`：
  - 遍历扫描目录（Widgets/Screens/Hud）；跳过 `PrefabUtility.IsPartOfPrefabInstance(selectable)` 的嵌套实例；直挂 ColorTint Selectable 无组件则 `AddComponent`。
  - **只补组件、不写颜色**（真实面 prefab 颜色/ColorBlock 逐位不变）；组件无序列化 token 字段（5 个公开常量单源），无需迁移数据。
  - 报告 `Added / Covered / Inherited / Skipped`：真实面预期 **Added=33、Covered=46、Inherited=13（7 个源 Selectable）**；幂等重跑 Added=0。
  - 单 prefab 失败不保存该 prefab；前序已保存者不做跨资产事务回滚（版本管理/幂等重跑）。

### 3. `UiThemeBinder` 单一应用核心

- 新增 `LiteClient.UI.UiThemeBinder.Apply(GameObject root, UiStyleResolver resolver, string label)`：
  - 遍历 `IThemeBound`（含 `UiSelectableTheme`）逐个 `OnThemeBound`（`SafeCall` 隔离单组件失败）。
  - 遍历 `UiStyleRef`；**自身或祖先含 `IThemeGraphicWriter` 则跳过**（不再只看同节点）。
  - 未知令牌 Warning 一次（按 label 汇总）；无目标 Graphic 跳过（规则报 Error）。
  - 单源判据：`public static bool IsGraphicOwnedByWidget(Component c)`（`GetComponentsInParent<IThemeGraphicWriter>(true)`），供 `UIForm`/`UiStyleRefApplier`/`StyleRefRule` 共用。
- `UIForm.ApplyTheme` 改为调用 binder；保留 `_themeDegradedWarned` 一次告警。`UiStyleRefApplier.ApplyRefs` 使用同一 `IsGraphicOwnedByWidget` 判据（单源）。
- HUD：`UIService.ThemeAssetProvider` 暴露 `Func<UiThemeAsset>`；`ProcedureBattle` 可选构造参数 `Func<UiThemeAsset> themeProvider = null` → `BattleViewRig` 在 HUD 实例化后调用 binder；provider/null 资产降级（Warning 一次，HUD 保持烘焙值）。`ContainerModule` 传 `context.Require<UIService>().ThemeAssetProvider`。

### 4. 文字档精确迁移

- `UiTextStyleTool.MigrateExactRoleRefs(IReadOnlyList<string> scanDirs)`：
  - 遍历 TMP_Text/Text；无 `UiTextStyleRef` 且当前属性与某角色档**逐项完全一致**（字号 ±0.01、对齐、换行、溢出、Bold）→ 加组件并设 `Role`。
  - TMP 五项：`fontSize/alignment/enableWordWrapping/overflowMode/Bold`；UGUI 四项：`fontSize(int)/alignment(TextAnchor)/horizontalOverflow(Wrap=换行)/Bold`（垂直溢出不在档位，不比对）。
  - **跳过 `PrefabUtility.IsPartOfPrefabInstance(component)` 的嵌套实例**，由源模板继承；真实面调用必须传 Widgets/Screens/**Hud** 三目录（37 个匹配中 9 个在 Hud）。
  - 多档同时匹配＝歧义，跳过并报告（不猜）；报告须列出命中角色（`MatchRoles`），不匹配＝留债。**只加组件、不改任何文本属性。**
  - 幂等；返回 `(prefab 数, 新增组件数, 覆盖节点数, 跳过数)`。
- 真实面执行结果预期：**新增 23 个组件、覆盖 37 个节点（Body 35 + Emphasis 2；14 个由 4 个源 prefab 的 7 个源节点继承）**，未标记 141→104；不产生 Mismatch；二次重跑新增 0。

### 5. C# 默认色与取值序

- `StateButton`/`TabGroup`/`StarRating` 删除 `new Color(...)` 字段初始化器（prefab 序列化值为权威）；保留字段与 token 字段。
- 新增 `UiStyleResolver.Resolve(string token, Color fallback)`：
  ```csharp
  public Color Resolve(string token, Color fallback)
      => !string.IsNullOrEmpty(token) && TryColor(token, out Color value) ? value : fallback;
  ```
- 三 widget 的 `Pick` 改为 `_theme?.Resolve(token, fallback) ?? fallback`（或等价一行）；行为不变（未注入/空 token/未知令牌 → fallback）。
- 夹具中原依赖 C# 默认色的迁移用例改为显式赋颜色（断言不变）。

### 6. 规则收口

- `IThemeGraphicWriter`（空标记接口）由 `StateButton`/`TabGroup`/`StarRating` 实现；`UiStyleRef` 在「自身或祖先」带该接口时由 `ui.style.ref` 报 **Error**（双写者），并由 applier/UIForm 跳过。`UiSelectableTheme` **不实现**该接口（只写状态 tint，不写 Graphic 基色），因此 ref 与其共存不误报。
- 新增 `ui.style.selectable`（Family 样式）：
  - 每个 `UiSelectableTheme`：无 `Selectable`＝Error；5 个令牌任一未知＝Error；ColorBlock 当前值与令牌现值不命中（`UiStyleResolver.Hits`）＝Warning。
  - 每个 `Transition == ColorTint` 的 `Selectable`：缺 `UiSelectableTheme`＝Warning。
  - 非 ColorTint（None/SpriteSwap/Animation）不适用，跳过。
- 令牌重值 tie-break：`UiStyleTool.TryNearestMatch` 命中多个同值令牌时按 ID `Ordinal` 取最后一个（现状）**明确为契约**；但 `MigrateWidgetTokens` 对 widget 状态色不得填 `color.state.selectable.*`（这些令牌只服务 `Selectable.colors`，由 `UiSelectableTheme` 常量显式消费），通过候选过滤/显式排除实现并加夹具。`ui.style.color-drift` 只判命中/不命中，重值不影响计数。
- `UiValidator.Rules` 注册；`Families` 样式族 Note 更新；`UiRuleIds.StyleSelectable` 新增。
- 真实面预期：颜色漂移 0、selectable 0、文本未标记 104、Error 0。

## 通用性与扩展性

- **根因**：存量裸值不是「漏改一两个颜色」，而是令牌集未覆盖真实视觉面 + uGUI 原生状态色没有主题接缝。本批用「精确令牌 + 单一 binder + 能力标记接口」解决，而不是逐 prefab 特判。
- **复用点**：`UiThemeBinder` 是页面/HUD/未来克隆体的唯一应用入口；`UiSelectableTheme` 是任意 Selectable 的通用状态色接缝；`IThemeGraphicWriter` 让「谁拥有 Graphic 写权」成为可扩展能力，而不是三个类名硬编码。
- **扩展方式**：
  - 新增颜色：JSON 加一枚 + 重生成，不改工具/规则形状。
  - 新增状态型控件：实现 `IThemeBound`（+ 需要时 `IThemeGraphicWriter`），binder 自动注入。
  - 新增文字角色：`UiTextStyle.Presets` 加一行 + 令牌 JSON 加 `font.*`；迁移工具按同一精确匹配口径工作。
- **避免硬编码**：令牌值只住 JSON；组件令牌 ID 是公开常量（5 个，单源）；Selectable 的 multiplier/fade 不复制为常量；HUD 注入走既有 provider 缝，不新建全局静态。
- **不为假想需求设计**：不实现主题切换 UI、不实现克隆体注入、不为 104 个文本预造角色。

## 测试策略

| 层 | 套件/用例 | 关键断言 |
|---|---|---|
| EditMode 令牌 | `UiThemeTokensEditModeTests` 扩展 | 真实源 69 色；44 个新 ID/值逐项；schema 校验通过；重值令牌存在且 ID 唯一 |
| EditMode 状态令牌 | `UiThemeStateTokensEditModeTests` 扩展 | 8 既有 + 5 selectable = 13 枚；SO 与 JSON 一致；与 `color.text.bright` 重值显式登记 |
| EditMode 解析 | `UiStyleResolverEditModeTests` 扩展 | 真实资产 69 色；`Resolve` 已知/空/未知/未注入 fallback |
| EditMode Selectable | 新增 `UiSelectableThemeEditModeTests` | 5 色写入；multiplier/fade 保留；仅 ColorTint；未知令牌保留现值并告警一次；resolver null 不抛；幂等 |
| EditMode 迁移 | 新增 `UiSelectableThemeApplierEditModeTests` | 直挂补组件、嵌套实例跳过、覆盖计数（33/46/13）、幂等、只增组件不改颜色、单 prefab 失败不写该 prefab |
| EditMode Binder | 新增 `UiThemeBinderEditModeTests` | refs 应用、IThemeBound 注入、writer 自身/祖先跳过、UiSelectableTheme 节点上的 ref 可应用、未知令牌告警、null resolver；真 HUD prefab 注入后 Button_Settings ColorBlock 变、_Slider（targetGraphic=null）不抛 |
| EditMode 规则 | 新增 `UiStyleSelectableRuleEditModeTests` + `UiStyleRefRuleEditModeTests` 扩展 | 缺组件 Warning/无 Selectable Error/未知令牌 Error/漂移 Warning/非 ColorTint 跳过；writer 祖先 Error、SelectableTheme 祖先不误报 |
| EditMode 文字 | `UiTextStyleEditModeTests` 扩展 | 精确匹配加 Body/Emphasis；36px 不加；嵌套实例跳过；Hud 目录；UGUI 映射；`UseInstanceOverrides` 保留属性且计未标记债；幂等；只加组件不改属性 |
| EditMode Widget | `UiWidgetThemeEditModeTests` 扩展 | 删除默认色后显式值路径不变；Resolve fallback；迁移用例显式赋色 |
| EditMode 工具 tie-break | `UiStyleRefEditModeTests` 扩展 | 同值多令牌确定性；widget 迁移不填 `color.state.selectable.*` |
| PlayMode 主题 | `UiThemeApplyPlayModeTests` 扩展 | 改 selectable token 后真页面按钮 ColorBlock 变化（`Hits` 判据）；provider null 只告警一次（C-4） |
| 真实面门禁 | `UiValidationEditModeTests` | Error 0；颜色 0；文本 104；Warning 104；selectable 0 |
| 全量 | `wf verify` | 新增夹具已加入 `.workflow/config.json` commands；L1/doc/toolchain/L2/全部 UI EditMode + PlayMode PASS |

**先失败后通过**：先写测试与最小骨架（类型存在、行为未实现），运行 `wf verify --red`；预期断言失败（令牌不存在/组件无行为/规则未注册/基线仍 298/69 色断言失败），不是编译或环境错误；再补令牌、组件、迁移、规则；最后真实面执行迁移并把基线钉到 104。

## 验收标准

- [ ] **AC1 令牌补集**：`UiStyleTokens.json` 颜色 **69 个唯一 ID**（20 + 44 + 5；**值允许按状态语义重值**，显式 ID 消费者不受影响；重值清单在附录 B 登记）；44 枚精确值与附录 A 逐项一致；`ui.style.tokens` 重生成 `UiTheme.asset` 幂等（二次 `changed=False`），`UiThemeTokens/StateTokens/Resolver` 夹具全绿。
- [ ] **AC2 颜色漂移清零**：真实面 `ui.style.color-drift` = 0；`UiValidationEditModeTests` 颜色基线 0。
- [ ] **AC3 Selectable 令牌化**：真实面 **33 个直挂组件 + 13 个模板继承 = 46/46 覆盖**（5 个源 prefab 的 7 个源 Selectable 在 Widgets 扫描面；逐一 `Selectable` 计数 `UiSelectableTheme` 组件数 = 46）；5 枚令牌存在；prefab 序列化 ColorBlock **逐位不变**；PlayMode 改 selectable token 后真页面按钮 ColorBlock 按 `Hits` 变化；**真 HUD prefab 经 binder 注入后 `Button_Settings` ColorBlock 按 `Hits` 变化**（`_Slider` targetGraphic=null，无视觉，不抛）；运行时注入值按 8bit 量化（≤0.5/255，不可见）。
- [ ] **AC4 文字档迁移**：真实面 **新增 23 个 `UiTextStyleRef` 组件、覆盖 37 个节点**（Body 35 + Emphasis 2；14 个由 4 个源 prefab 的 7 个源节点继承）；未标记 141→104（4 个列表实例以 `UseInstanceOverrides` 合法覆盖，仍计未标记债）；**零 Mismatch**；零文本属性写入；迁移幂等（二次新增 0）。
- [ ] **AC5 C# 默认色/取值序**：三 widget 源文件无 `new Color(` 颜色字面量；三处 `Pick` 共用 `UiStyleResolver.Resolve`；fallback 行为夹具覆盖。
- [ ] **AC6 双写者/告警收口**：`ui.style.ref` 对 writer 自身/祖先报 Error、对 `UiSelectableTheme` 节点不误报；applier/UIForm 同判据跳过；`UIForm` 资产 null 告警一次有 PlayMode 断言。
- [ ] **AC7 新规则**：`ui.style.selectable` 注册并在真实面零问题；正负例夹具覆盖；样式族 Note 更新；同值令牌 tie-break 有夹具。
- [ ] **AC8 基线/门禁**：真实面 Warning **104 = 0 色 + 104 文本**（104 = 141 - 37 覆盖；其中 14 个继承、23 个直挂；HUD 9 个匹配中 8 个是 `Tmp_RankCell/Txt_Name` 的 8 份实例，HUD 仅新增 2 个组件：`Tmp_RankCell/Txt_Name` + `Button_Settings/Txt_Settings`）；新增夹具已加入 `.workflow/config.json`（commands + red_commands）并在输出中列出套件名；`wf verify` 全绿；`doc-lint` 0/0；`toolchain.py check` 无新增错误；shared BuildHash 不变（`gen-build-hash.py --check-shared`）。
- [ ] **AC9 文档/审计回写**：UI系统线第 13 章、总设计 §6.1/§6.2/**§6.5**、工具链 §8.1/**§8.3**、UI 制作规范、审计修复登记、施工进度 README 索引同步。

## 风险与回滚

| 风险 | 影响 | 监控/发现 | 回滚 |
|---|---|---|---|
| 44 个语义命名有争议 | 令牌 ID 后续改名成本 | 方案/代码评审逐项看附录 A；真实面颜色基线 0 | 改 ID + 迁移映射（未发布前直接改名） |
| 新增组件改变 prefab 结构 | 视觉/交互回归 | 迁移前后颜色/ColorBlock 快照摘要比对；预览矩阵抽样 `Page_Login`/`Hud_Battle` | git checkout prefab；迁移件幂等可重跑 |
| 嵌套 prefab 重复挂载（文本 14/Selectable 13） | 双重组件/override 冲突 | 迁移件按 `IsPartOfPrefabInstance` 跳过并单独计数；真实面断言 23/33 新增、37/46 覆盖 | 重跑迁移（幂等），必要时清 override |
| 文本迁移扫描漏 Hud | 9 个匹配漏标、基线仍 132 | 真实面调用显式传 Widgets/Screens/Hud；37 覆盖断言 | 补传目录重跑 |
| 同值令牌 tie-break 误填 | widget 迁移把白填成 selectable.normal | tie-break 契约 + 候选过滤夹具；widget 迁移不填 selectable 令牌 | 改 ID/过滤后重跑 |
| colors-refresh 浮点重写 | 误判"逐位不变"失败 | 零可见变化按 `Color32` 判据；不要求 colors-refresh 零变更 | 不做该步或按 8bit 复核 |
| HUD 注入改动构造链 | 编译/装配回归 | 全量 L1 + UI PlayMode；BattleViewRig 相关测试 | 移除可选参数，HUD 回退烘焙值 |
| 文本迁移误标 36px 等 | 视觉变化/规则 Mismatch | 迁移只认精确匹配；真实面 Mismatch 断言 0 | 删除新增 ref 组件（按报告清单） |
| 基线更新掩盖回归 | 后续批次看不到债 | 基线钉写死 104 并附组成；`wf verify` 全量 | 回退基线与对应改动 |
| 新增夹具未进门禁 | AC8 假绿 | config.json commands/red_commands 断言 + `wf verify` 套件清单 | 补命令重跑 |
| 生成物/SO 未重生成 | 运行时缺令牌 | AC1 幂等 + SO 计数断言 | 重跑 `ui.style.tokens` |

---

## 附录 A：44 枚精确颜色令牌（零可见变化）

> 值＝实测 prefab 序列化值（8bit 量化）；来源为代表性节点，完整清单见 `AgentScripts/Artifacts/style-inventory.json`（只读盘点产物，不入库）。

| # | ID | 值 | 语义/来源 |
|---:|---|---|---|
| 1 | `color.bg.screen.opaque` | `#1F1F29FF` | 页面不透明底（`Bg_Lobby/Login/Result/Room/Settings`、`Tmp_BottomNav`） |
| 2 | `color.bg.panel` | `#2B303DFF` | 内容面板底（`Bg_RecordsPanel`/`Bg_ScorePanel`/`Bg_PlayersPanel`/`Bg_OptionsPanel`） |
| 3 | `color.bg.panel.alt` | `#272835FF` | 次级面板底（`Bg_RoomsPanel`/`Bg_RecordsPanel`） |
| 4 | `color.bg.header` | `#6B7A94FF` | 页头底（`Bg_Header`） |
| 5 | `color.bg.dialog` | `#1F1F24F5` | 对话框底（`Dialog_Confirm`/`Tmp_Dialog`） |
| 6 | `color.bg.toast` | `#1A1A1FEB` | Toast 底（`Tmp_Toast/Clone_Template`） |
| 7 | `color.bg.dropdown` | `#67678AFF` | 下拉框底（`Dropdown_*`/`Tmp_Dropdown`） |
| 8 | `color.bg.dropdown.item` | `#292933FF` | 下拉选项底（`_Item`） |
| 9 | `color.bg.dropdown.template` | `#1A1A21FA` | 下拉展开模板底（`Clone_Template`） |
| 10 | `color.bg.control` | `#1F1F26FF` | Slider/Toggle 轨道底（`_Background`） |
| 11 | `color.bg.toggle.on` | `#1E4228FF` | Toggle 开启底（`Page_Settings/_Background`） |
| 12 | `color.bg.toggle.off` | `#262633FF` | Toggle 关闭底（`Tmp_Toggle/_Background`） |
| 13 | `color.bg.input` | `#1A1A21E6` | 输入框底（`Tmp_InputField`） |
| 14 | `color.bg.avatar` | `#4C5973FF` | 头像占位底（`Tmp_AvatarFrame/_Avatar`） |
| 15 | `color.bg.progress.track` | `#14141AD9` | 进度槽底（`Tmp_ProgressBar/_Bg`） |
| 16 | `color.bg.hpbar` | `#050A16CC` | 血条槽底（`Hud_Battle`/`Tmp_HpBar/_Background`） |
| 17 | `color.bg.button.secondary` | `#8D8DA3FF` | 次级按钮底（Settings/Leave/Close） |
| 18 | `color.bg.button.muted` | `#4C4C57FF` | 弱化按钮底（Cancel/Minus/Plus） |
| 19 | `color.bg.button.accent` | `#515193FF` | 强调按钮底（Lobby `Button_Records`） |
| 20 | `color.bg.item.alt` | `#334C73FF` | 列表条目底（各页 `Clone_Item`） |
| 21 | `color.bg.transparent` | `#14141A00` | 全透明容器底（`VirtualList_*`） |
| 22 | `color.bg.placeholder` | `#666673FF` | 图片占位底（`Tmp_AnimatedImage`/`Tmp_RotatingImage`） |
| 23 | `color.debug.baseline` | `#0F0F14EB` | 视觉基线夹具底（`Tmp_Baseline*`；非产品语义，限定 debug 命名） |
| 24 | `color.debug.fixture` | `#1F242EFF` | 测试载体底（`Tmp_UiTestPage`；非产品语义，限定 debug 命名） |
| 25 | `color.text.hud.primary` | `#EBF5FFFF` | HUD 正文/计数/击杀条（31 处） |
| 26 | `color.text.hud.accent` | `#FFB840FF` | HUD 强调数字（击杀/换弹/保护色，12 处） |
| 27 | `color.text.fly` | `#FFE666FF` | 飘字文本（`Tmp_FlyText`） |
| 28 | `color.effect.highlight` | `#FFFFFF26` | 排行/高亮遮罩（`Image_Highlight`，9 处） |
| 29 | `color.effect.vignette` | `#E6F2FF73` | 页面暗角（`Image_Vignette`，6 处） |
| 30 | `color.effect.spinner` | `#FFFFFFE6` | 加载转圈环（`Image_SpinRing`/`RotatingImage_Spin`） |
| 31 | `color.effect.blocker` | `#0000008C` | 加载遮罩（`Bg_Blocker`） |
| 32 | `color.effect.backdrop` | `#FFFFFF0D` | 记录页背景弱高亮（`Image_Backdrop`） |
| 33 | `color.effect.loading.track` | `#00000099` | 加载进度槽（`ProgressBar_Load`） |
| 34 | `color.effect.hpbar.backfill` | `#FFFFFF59` | 血条余量层（`_BackFill`） |
| 35 | `color.effect.hud.weapon` | `#FFFFFF57` | HUD 武器区底（`Bg_Weapon`） |
| 36 | `color.effect.hud.button` | `#FFFFFF40` | HUD 设置按钮底（`Button_Settings`） |
| 37 | `color.effect.avatar.frame` | `#D9B240E6` | 头像框（`Tmp_AvatarFrame/_Frame`） |
| 38 | `color.state.hp.fill` | `#2CC057FF` | 血条填充（`_Fill`） |
| 39 | `color.state.check` | `#59CC73FF` | Toggle 勾选（`Checkmark`） |
| 40 | `color.state.success.bright` | `#4CD973FF` | 加载完成填充（`Tmp_Loading/_Fill`） |
| 41 | `color.state.warn.bright` | `#E63333FF` | 红点（`Tmp_RedDot/_Dot`） |
| 42 | `color.state.danger` | `#E25C50FF` | 危险操作按钮（Settings `Button_Exit`） |
| 43 | `color.state.star.on.deep` | `#D9B240FF` | 星级底图烘焙金（`Tmp_StarRating/_Star0-4`；与 `color.state.star.on` `#FFCC33FF` **不同值**，运行时 widget 用后者） |
| 44 | `color.brand.accent.soft` | `#4CBFF2FF` | 进度填充浅蓝（`Tmp_ProgressBar/_Fill`） |

## 附录 B：5 枚 Selectable 状态令牌

> 33 个直挂 ColorBlock 实测完全一致；值取序列化现值量化，零可见变化。13 个模板继承项由 7 个 Widgets 源 Selectable 继承（Tmp_Dropdown 根+Item ×2、Tmp_Dialog 两按钮 ×2、Tmp_HpBar/Tmp_Slider/Tmp_Toggle 各 1）。
> **重值登记**：normal `#FFFFFFFF` 与既有 `color.text.bright` 同值；highlighted 与 selected 互相同值。显式 ID 消费者（`UiSelectableTheme` 常量）不受影响；最近匹配按 ID `Ordinal` 取后者（契约），widget 迁移不得填入这 5 枚。

| # | ID | 值 | 对应 ColorBlock 字段 |
|---:|---|---|---|
| 45 | `color.state.selectable.normal` | `#FFFFFFFF` | normalColor |
| 46 | `color.state.selectable.highlighted` | `#F5F5F5FF` | highlightedColor |
| 47 | `color.state.selectable.pressed` | `#C8C8C8FF` | pressedColor |
| 48 | `color.state.selectable.selected` | `#F5F5F5FF` | selectedColor |
| 49 | `color.state.selectable.disabled` | `#C8C8C880` | disabledColor（alpha 0.5） |

> 合计：20 既有 + 44 精确 + 5 Selectable = **69 色**。`colorMultiplier=1`、`fadeDuration=0.1` 不是颜色令牌，保持原值不动。
