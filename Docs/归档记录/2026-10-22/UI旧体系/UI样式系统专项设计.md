# UI 样式系统专项设计

> 状态：现行专项设计；A（编辑期）落地，B（运行期主题）只做兼容设计
> 版本：1.0
> 更新日期：2026-10-22
> 适用范围：UI 颜色、文字、按钮状态、图标/间距/动效 token；token 源、引用、解析、编辑期应用、校验与迁移
> Owner：UI TA / 客户端 UI
> 上位：[UI 框架总设计](UI框架总设计.md)
> 关联：[UI 制作规范](UI制作规范.md) · [UI 编辑器工具专项设计](UI编辑器工具专项设计.md) · [UI 美术角色与交接契约](UI美术角色与交接专项设计.md) · [开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md) · [样式编辑器设计（归档）](../../../归档记录/2026-09-20/样式编辑器设计.md)
> 裁决：2026-10-22 用户裁决「按 A 落地，token/ref 按 B 兼容设计」

## 1. 定位与裁决

本文回答：UI 的**样式决策**如何成为单源、如何被组件引用、如何应用到 prefab、如何校验、如何迁移，以及未来如何平滑接入运行时主题。

它不回答：UI 运行时生命周期与导航（归[UI 框架总设计](UI框架总设计.md)）、命名与资产硬规则（归[UI 制作规范](UI制作规范.md)）、工具平台入口与清单（归[开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md)）、页面契约与交接（归[UI 美术角色与交接契约](UI美术角色与交接专项设计.md)）。

### 1.1 核心裁决

| 裁决 | 内容 |
| --- | --- |
| **A 落地** | 第一版是**编辑期样式系统**：token → 引用 → 解析 → 应用到 prefab；运行时不按 token 重算 |
| **B 兼容** | token ID、引用组件、解析器从第一天按“可换主题”设计；运行时 `UiThemeService` 作为后续可选层，不改变引用形态 |
| **单源** | token 源是 JSON（schema 校验 + 可 diff）；生成的 `UiThemeAsset` 是 Unity/运行时读面；**生成物禁手改** |
| **引用存 ID** | 组件上只存 token ID，不存 hex/字号；颜色不再靠“最近邻猜” |
| **prefab 是真源（A）** | A 形态下 prefab 的序列化值仍是视觉真源；token 只通过 applier 写入，不在运行时覆盖 |
| **主题是真源（B）** | B 形态下当前主题在运行时覆盖 refs；prefab 值退化为默认/无主题回退 |
| **范围收敛** | v1 只做颜色、文字、按钮状态；图标/间距/动效是 P1；运行时换肤、布局、图集不在 v1 |
| **不做选择器/级联** | UGUI 没有 USS/级联；样式解析只认组件上的 token 引用，不做“按节点名/层级匹配”的第二套规则 |

### 1.2 成功判据

1. 改一个 token，跑一次 applier，所有引用处收敛；不需要逐 prefab 手改。
2. UI prefab 内不再存在“没有 token 引用的可见颜色/文字属性”（例外表登记）。
3. 按钮 normal/highlighted/pressed/disabled 在模板间一致，状态不再各页一套。
4. 迁移批次完成后，视觉截图与迁移前零差异（或差异逐条签收）。
5. B 形态接入时，只替换主题资产与 binder，不改引用组件、不改页面逻辑。

## 2. Current 与 Target

### 2.1 现行形态

| 件 | 现状 | 问题 |
| --- | --- | --- |
| [UiStyle.cs](../../../../Assets/LiteGame/Editor/Style/UiStyle.cs) | 12 个 `static readonly` 颜色 token | 编辑器代码常量；无主题、无数据资产、无预览 |
| [UiStyleTool.cs](../../../../Assets/LiteGame/Editor/Style/UiStyleTool.cs) | 最近邻颜色匹配 + `1e-4` 阈值；apply/report | 无颜色身份；改 token 值后旧值不保证迁移（阈值匹配）；无 dry-run/Undo |
| [UiTextStyle.cs](../../../../Assets/LiteGame/Editor/Style/UiTextStyle.cs) | 5 个文字角色档 | 代码常量；改档位需重编译 |
| [UiTextStyleRef.cs](../../../../Assets/LiteClient/UI/Style/UiTextStyleRef.cs) | 文字角色标记（枚举） | 只有文字有身份；颜色/按钮/图标没有 |
| `UiStyleTool` / `UiTextStyleTool` | 两个批处理 action | 不是系统；没有主题、引用统计、状态模型、预览 |
| 作用域 | Widgets + Screens | HUD、伤害数字等未纳入 |

现行工具是“第一代对账器”，由 2026-09-20 的“仅编辑器、不挂组件、无运行时”裁决演进而来（[归档设计](../../../归档记录/2026-09-20/样式编辑器设计.md)）。它们适合迁移与对账，不足以承载样式系统。

### 2.2 目标形态

```text
UiStyleTokens JSON（设计态唯一源，schema 校验）
  ↓ ui.style.generate
UiThemeAsset（Unity 资产；字体/贴图直引；运行时读面）
  ↓
UiStyleResolver（ID → 值；别名/继承/缺 token 报错）
  ↓
UiColorStyleRef / UiTextStyleRef / UiButtonStyleRef / UiIconStyleRef
  ↓
编辑期 UiStyleApplier（写 prefab；dry-run/diff/Undo）
  或 运行期 UiThemeBinder（可选；打开页/换主题时应用）
  ↓
UiStyleValidator + ui.style 页（token/引用/预览/校验）
```

三层职责：

- **Token 层**：设计决策（颜色/字体/状态/尺寸/动效）的唯一定义。
- **引用层**：组件声明“我用哪个 token”，不存具体值。
- **应用层**：编辑期写 prefab；运行期只在 B 形态覆盖。

## 3. Token 源

### 3.1 JSON 为设计态源，SO 为 Unity 读面

| 项 | 约定 |
| --- | --- |
| 源文件 | `Assets/ShareResource/UI/Themes/<theme>.tokens.json` |
| 机器定义 | [UiStyleTokens.schema.json](UiStyleTokens.schema.json)（JSON Schema 2020-12） |
| 生成物 | `Assets/ShareResource/UI/Themes/Generated/<Theme>.asset`（`UiThemeAsset`） |
| 生成器 | `ui.style.generate`（编辑器 action；JSON → SO；校验资产类型与 token 完整性） |
| 手改 | **禁止**；生成物带 `sourceHash`，与 JSON 不一致即校验红 |
| 运行时 | 只读生成的 `UiThemeAsset`；不加载 JSON |
| 打包 | JSON 是设计态资产，不进包；生成的 SO 进 UI 主题收集组（后续接 `BundleCollectorSetting`） |

为什么不用 SO 直接做源：JSON 可 diff、可 schema 校验、可被设计工具/Agent 读写；SO 负责 Unity 引用与运行时性能。两者职责分开，但只有 JSON 能改。

### 3.2 Token 组

| 组 | 键前缀 | v1 | 说明 |
| --- | --- | --- | --- |
| `colors` | `color.*` | ✅ | 语义色：surface/text/brand/feedback/overlay/guide；值 hex 或 `@color.*` 别名 |
| `typography` | `text.*` | ✅ | 字体资产/字号/行高/字距/字重/对齐/换行/溢出/颜色 |
| `buttons` | `button.*` | ✅ | variant/size → tint + 文字档 + 状态色（normal/highlighted/pressed/disabled/selected） |
| `icons` | `icon.*` | P1 | 图标边长；由控件/布局消费 |
| `spacing` | `space.*` | P1 | 间距刻度；第一版只声明，不自动写 LayoutGroup |
| `motion` | `motion.*` | P1 | 时长/缓动；供 `UiFx`/转场/效果系统消费 |
| 圆角/描边 | — | P1 | UGUI 无原生圆角；圆角是 **9-slice sprite 样式**，不是数值属性；不放进 v1 schema 的数值字段 |

> 《UI 制作规范》§6 允许“声明 min/max 的自动缩放”；v1 schema 只接受 `autoSize=false`，不开放自动缩放 token。等真实需求出现，再以次版本加 `min/max`，不在第一版放宽。

### 3.3 ID 与命名

- 格式：`组.语义[.变体]`，小写点分，如 `color.text.primary`、`text.title`、`button.primary.md`、`icon.md`、`space.4`、`motion.fast`。
- 稳定：ID 一旦发布不改语义；改名 = 新 ID + 旧 ID 别名/弃用，至少保留一个次版本。
- 别名：颜色值支持 `@color.*`；解析器只允许一层别名并检测环。
- 生成常量：`UiStyleIds`（目标）由 JSON 生成，供 C#/工具引用；Lua 仍用字符串 ID，由契约/校验器对账。
- 禁止：把 hex/字号直接写进引用组件；把主题值写进 prefab 作为“第二源”。

### 3.4 主题与变体

- 一个主题一个 JSON；`meta.themeId` 稳定，`meta.version` 语义化。
- 变体用 `meta.extends` 继承基础主题：同 ID 覆盖，新增 ID 追加；解析器先合并再解析。
- 第一版只交付 `default`；暗色/活动主题按真实需求新增，不预建空主题。
- B 形态的主题切换 = 换 `UiThemeAsset` + binder 重应用；A 形态换主题 = 重新 generate + apply + 重打 prefab（成本更高，但无运行时开销）。

## 4. 引用层

### 4.1 组件

| 组件 | 目标 | 字段 | 写入面 |
| --- | --- | --- | --- |
| `UiColorStyleRef` | `Graphic`（Image/RawImage/TMP 等） | `StyleId`（`color.*`） | `Graphic.color` |
| `UiTextStyleRef` | TMP/UGUI 文本 | `StyleId`（`text.*`）；兼容旧 `Role` 枚举 | 字号/行高/字距/对齐/换行/溢出/字重/颜色 |
| `UiButtonStyleRef` | `Selectable` + 背景 `Graphic` + 文字 | `StyleId`（`button.*`） | `Selectable.colors`、`SpriteState`、文字档 |
| `UiIconStyleRef` | `Image`/`RectTransform` | `SizeStyleId`（`icon.*`）、可选 `ColorStyleId` | 尺寸、颜色（P1） |
| `UiSpacingStyleRef` | `LayoutGroup`/显式间距 | `StyleId`（`space.*`） | 间距（P1） |

### 4.2 规则

1. **只存 ID**：引用组件不存 hex、字号、像素值；Inspector 下拉从当前主题读取，显示预览。
2. **单写者**：一个属性只允许一个样式引用；`UiButtonStyleRef` 已覆盖的 `Selectable.colors` 不再由 `UiColorStyleRef` 写。
3. **模板优先**：样式引用住在模板/页面 prefab；实例覆盖是 Unity prefab override，校验器把它标成“显式覆盖”，不静默改写。
4. **嵌套模板**：页面 applier 不深入嵌套模板内部；模板资产独立应用，页面只应用自有节点。
5. **缺 token**：Inspector 立即报错；applier 失败并定位到 `asset + node + StyleId`，不静默用默认值。
6. **Lua 动态色**：只允许经 `Bridge.ui.GetColor(id)` 读 token；禁止在 Lua 写 hex。动态字号/字重仍归静态样式，不在运行时改。

### 4.3 旧组件迁移

- `UiTextStyleRef` 保留类型；新增 `StyleId` 字符串字段。迁移期：`StyleId` 非空优先；为空时按 `Role` 枚举映射到旧 ID，保证旧 prefab 不炸。
- 迁移完成后 `Role` 标记为 `Obsolete`，下一个主版本移除。
- 颜色没有旧引用组件；迁移脚本用“最近邻”**只做一次性赋值**，生成 `UiColorStyleRef`，之后生产路径不再猜色。

## 5. 解析与应用

### 5.1 `UiStyleResolver`

- 输入：`UiThemeAsset` + token ID；输出：颜色/文字档/按钮档/图标尺寸/间距/动效。
- 合并 `extends`；解析 `@color.*` 别名（一层，检测环）；缺 token 抛 `UiStyleException`，带 `themeId + tokenId + 引用位置`。
- 纯数据、无 Unity 副作用；可 EditMode 直测；缓存解析结果。
- 主题版本与 token 版本随 `UiThemeAsset` 走；打开页固定所用版本，不在播放中换底层定义。

### 5.2 编辑期 `UiStyleApplier`

- 扫描 `Assets/ShareResource/UI/{Widgets,Screens}`（v1；HUD/伤害数字按消费者后续纳入）。
- 对每个 prefab：读 refs → resolver 解析 → 只写声明属性 → 保存。
- **dry-run**：输出 `{asset, node, component, property, old, new}` 清单，不写盘；**apply** 才保存。
- **幂等**：值已等于解析结果则零写入；重复 apply 不产生 diff。
- **写入通道**：Prefab Stage 内走 Unity Undo；批量走 `PrefabUtility.LoadPrefabContents` + `SaveAsPrefabAsset`，并接 `ToolWriteScope`（备份/回滚/变更集）；失败不半写。
- **不做结构改动**：不加/删组件、不改层级、不动锚点/尺寸（除非 `UiIconStyleRef` 明确声明尺寸且在范围内）。
- **嵌套 prefab**：只处理自有节点；模板内部件由模板自身应用，避免“页面覆盖模板”造成双重写。
- 应用前可出“影响面”统计：每 token 被多少 prefab/节点引用（`ui.style` 页）。

### 5.3 运行期 `UiThemeBinder`（B，可选）

- `UiThemeService` 持当前 `UiThemeAsset`；`UiThemeBinder` 在 `UIForm` 打开/主题切换时按 refs 应用；不改布局、不每帧写。
- 主题切换只允许在安全窗口（无转场/无加载遮罩）；页面固定主题版本，切换后新开页用新主题。
- 性能约束：`Graphic.color`/TMP 属性写入会标脏 mesh；只在主题切换/打开页发生；TMP 字体资产切换成本更高，需单独验证；不因 binder 创建实例材质。
- B 形态下 prefab 值是“无主题回退”；A 形态下 binder 不存在，禁止两套同时写。
- 运行时只读 token；不允许 Lua 改主题值，只能切主题 id（若产品需要）。

### 5.4 状态应用

- 按钮：`UiButtonStyleRef` 解析到 `ColorBlock`（normal/highlighted/pressed/selected/disabled）与可选 `SpriteState`；文字档由 `textStyle` 解析。
- 文本：`UiTextStyleRef` 写 TMP/UGUI 属性；颜色用 typography 的 `color` 或独立 `UiColorStyleRef`（单写者规则）。
- 第一版不接 `AnimationTriggers`；需要帧动画/材质态时另立 token，不塞进 button。

## 6. 校验

| 规则 ID | 内容 | 级别 |
| --- | --- | --- |
| `ui.style.token` | token 存在、别名无环、资产引用可解析 | Error |
| `ui.style.ref` | 引用组件挂在允许类型上；`StyleId` 前缀与组件匹配；单写者 | Error |
| `ui.style.raw` | 可见 `Graphic`/文本无样式引用（例外表除外） | Error（迁移期 Warning） |
| `ui.style.text` | 文字档必填项齐全；字体/fallback 存在；AutoSizing 关（v1）；字号在阶梯内 | Error |
| `ui.style.button` | 四个必需状态齐全；`textStyle` 存在 | Error |
| `ui.style.theme` | 主题 ID/版本/继承合法；重复 token；覆盖完整性 | Error |
| `ui.style.drift` | JSON `sourceHash` ≠ 生成 SO / 生成物缺失 | Error |
| `ui.style.deprecated` | 使用已弃用 token/别名 | Warning |
| `ui.style.contrast` | 文字/底色对比度（按主题对） | Warning（首版） |

- 规则落 `UiValidator` 的“样式”族；每条配正例/违规负例；负例必须能定位到 `asset + node + StyleId`。
- 快速通道：只扫改动 prefab（美术 lane）；全量扫描夜间。
- 例外按《UI 制作规范》§1 登记，带资产、规则、原因、替代验证、失效条件；禁止无登记抑制。
- 校验器不替代 PlayMode：主题切换/字体 fallback/运行时 binder 的行为仍需 PlayMode 用例。

## 7. 工具链

| 工具 | 形态 | 作用 |
| --- | --- | --- |
| `ui.style` | `page` | token 浏览/色板/文字样例/按钮状态预览；引用统计；主题切换预览；dry-run diff；校验报告 |
| `ui.style.generate` | `action` | JSON → `UiThemeAsset`；校验资产/schema/hash；写生成物 |
| `ui.style.apply` | `action` | 解析 refs → 写 prefab；dry-run/apply/rollback；输出变更集 |
| `ui.style.validate` | `action` | 跑样式规则；CI/Agent 入口 |
| `ui.style.migrate` | `action` | 一次性：旧颜色最近邻 → `UiColorStyleRef`；旧 `Role` → `StyleId`；报告不可自动迁移项 |

- 平台登记：`category=ui`、`plane=editor`、`purpose=dev`；`ui.style` 为 `page`，其余为 `action`；写盘声明 `outputs`/`safety`（dry-run supported、undo file/asset、confirmation required）。
- 现有 `ui.style.colors-refresh`/`colors-report`/`text-apply`/`text-report` 保留为**迁移期工具**；`ui.style` 页与 `apply`/`validate` 上线后，日常入口切到新工具，旧 action 在迁移批次收口后退役。
- 不新增第二菜单/第二索引；Hub `LiteGame/工具链` + `python scripts/toolchain.py` 是唯一入口。

## 8. 迁移批次

> 批次只定义范围、退出条件与回滚；完成状态由施工进度承担，本文不记“已完成”。

### B0 盘点与冻结

- 盘点 12 色 token、5 文字档、30 件模板、6 个页面；统计无 ref 的可见颜色/文本、按钮状态、HUD/伤害数字范围。
- 冻结迁移窗口；截图基线（中/英/长文本/关键页面），作为“零视觉差异”判据。
- 退出：盘点表 + 截图基线 + 例外清单；不改代码。

### B1 Token 源 + 生成器 + 解析器

- 落地 `UiStyleTokens.schema.json`、`default.tokens.json`、`UiThemeAsset` 生成器、`UiStyleResolver`。
- 12 色映射到语义 token；5 文字档映射到 `text.*`；按钮先建 primary/secondary/ghost/danger × md。
- 退出：schema 校验通过；生成 SO；resolver 单测（别名/继承/缺 token/环）；不改 prefab。

### B2 引用层 + Applier（试点）

- 新增 `UiColorStyleRef`/`UiButtonStyleRef`；`UiTextStyleRef` 增 `StyleId`；Inspector 下拉 + 缺 token 报错。
- 选 2 件模板 + 1 个页面试点：挂 refs → dry-run diff → apply → 截图零差异 → Undo 验证。
- 退出：试点零视觉差异；dry-run 清单可复现；失败不半写。

### B3 全量迁移模板与页面

- 30 模板 + 6 页面挂 refs；旧颜色最近邻只用于生成 refs；全量 apply；截图对比。
- 移除生产路径的“最近邻猜色”；`UiStyleTool` 降级为迁移/对账工具。
- 退出：可见属性 ref 覆盖率 100%（例外登记）；零视觉差异或逐条签收；旧工具不再参与日常。

### B4 校验 + 工具链页

- `ui.style.*` 规则 + 负例；`ui.style` 页 + `generate/apply/validate/migrate` actions；美术 lane 快速扫描。
- 退出：负例全红、正例全绿；`toolchain.py check` / `docs --check` 通过；页面可 dry-run/apply/回滚。

### B5 按钮状态 / 图标 / 间距（P1）

- 按钮四态全量迁移；`UiIconStyleRef`、`space.*`；HUD/伤害数字按消费者纳入。
- 退出：状态截图一致；图标尺寸/间距不再散落魔数；HUD 覆盖范围明确。

### B6 运行期主题（B，可选）

- 触发条件：产品出现真实换肤/暗色/活动主题需求，且 A 形态的“重生成 + 重打 prefab”成本不可接受。
- 交付 `UiThemeService` + `UiThemeBinder` + 主题切换安全窗口 + 性能/热更验证。
- 退出：主题切换无每帧写、无材质实例泄漏；页面打开/切换耗时达标；热更主题可回滚；A 形态仍可独立工作。

### 回滚

- B1/B2：删生成物与新增组件，恢复旧工具；prefab 未批量写。
- B3：按批次保留迁移前 prefab 快照/分支；applier 变更集可回滚；截图基线用于验收。
- B6：关闭 binder，回退到 prefab 烘焙值；主题资产可留在包内但不激活。

## 9. 验收与度量

| 项 | 首版目标 |
| --- | --- |
| 单 token 改动传播 | 一次 generate + apply，所有引用处收敛；无手改 |
| 可见属性 ref 覆盖 | 100%（例外表登记） |
| 迁移视觉差异 | 0（或逐条签收） |
| 按钮状态一致性 | 四个必需状态在模板间一致 |
| 中/英/长文本 | 字体 fallback、换行/截断不破 |
| dry-run/回滚 | 每次 apply 有 diff 与回滚路径 |
| B 兼容 | token/ref 不改即可接 `UiThemeBinder` |

度量：裸值违规数、token 覆盖率、apply 耗时、视觉差异条数、回滚次数、B 形态切主题帧耗时（后续）。

## 10. 边界

| 主题 | 权威 |
| --- | --- |
| 运行时生命周期、导航、页面作用域 | [UI 框架总设计](UI框架总设计.md) |
| 命名、目录、Prefab/资产硬规则 | [UI 制作规范](UI制作规范.md) |
| 现有绑定/样式/校验工具行为 | [UI 编辑器工具专项设计](UI编辑器工具专项设计.md) |
| 角色/RACI/页面契约/交接 | [UI 美术角色与交接契约](UI美术角色与交接专项设计.md) |
| 工具入口/清单/Hub/CLI | [开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md) |
| 样式 token/主题/引用/应用 | **本文 + [schema](UiStyleTokens.schema.json)** |
| 历史样式编辑器设计 | [归档设计](../../../归档记录/2026-09-20/样式编辑器设计.md)（只作背景，不参与裁决） |

本文不复制其他文档的规则正文；冲突以代码、配置和可复现测试为准，本文随实现同步修订。

## 附录 A：Token 示例

以下为示意，资产路径/值不是最终主题；生成器输出以 JSON 为准。

```json
{
  "schemaVersion": "1.0",
  "meta": {
    "themeId": "default",
    "themeName": "Default",
    "version": "1.0.0"
  },
  "colors": {
    "color.surface.panel": "#1F1F29F2",
    "color.surface.sunken": "#141419E6",
    "color.surface.item": "#2E2E38FF",
    "color.brand.primary": "#E6BF40FF",
    "color.brand.accent": "#4DB3F2FF",
    "color.feedback.danger": "#E64033FF",
    "color.feedback.success": "#66D966FF",
    "color.text.primary": "#EBEBF0FF",
    "color.text.secondary": "#9999A6FF",
    "color.text.strong": "#FFFFFFFF",
    "color.overlay.hit": "#FFFFFF03",
    "color.guide.mark": "#FFD94D40"
  },
  "typography": {
    "text.title": {
      "fontAsset": "asset:Assets/ShareResource/Fonts/NotoSansSC SDF.asset",
      "fontSize": 24,
      "lineHeight": 1.2,
      "letterSpacing": 0,
      "fontWeight": "Regular",
      "alignment": "Center",
      "wrap": false,
      "overflow": "Truncate",
      "color": "@color.text.primary"
    },
    "text.label": {
      "fontAsset": "asset:Assets/ShareResource/Fonts/NotoSansSC SDF.asset",
      "fontSize": 20,
      "lineHeight": 1.2,
      "letterSpacing": 0,
      "fontWeight": "Regular",
      "alignment": "Center",
      "wrap": false,
      "overflow": "Truncate",
      "color": "@color.text.primary"
    }
  },
  "buttons": {
    "button.primary.md": {
      "textStyle": "text.label",
      "tint": "@color.brand.primary",
      "states": {
        "normal": { "tint": "@color.brand.primary", "text": "@color.text.strong" },
        "highlighted": { "tint": "@color.brand.primary", "text": "@color.text.strong" },
        "pressed": { "tint": "@color.brand.primary", "text": "@color.text.strong" },
        "disabled": { "tint": "@color.text.secondary", "text": "@color.text.primary" }
      }
    }
  },
  "icons": {
    "icon.md": 24
  },
  "spacing": {
    "space.1": 4,
    "space.2": 8,
    "space.4": 16
  },
  "motion": {
    "motion.fast": { "duration": 0.15, "easing": "EaseOut" }
  }
}
```

## 附录 B：现行 token 到目标 ID 的映射

| 现行 | 目标 ID | 备注 |
| --- | --- | --- |
| `Bg` | `color.surface.panel` | 面板/模板底 |
| `BgDeep` | `color.surface.sunken` | 深底（Toast/进度槽） |
| `ItemBg` | `color.surface.item` | 条目/次级底 |
| `Primary` | `color.brand.primary` | 主色金 |
| `Accent` | `color.brand.accent` | 功能蓝 |
| `Warn` | `color.feedback.danger` | 警示红 |
| `Success` | `color.feedback.success` | 确认绿 |
| `Text` | `color.text.primary` | 正文 |
| `TextDim` | `color.text.secondary` | 弱文字 |
| `TextBright` | `color.text.strong` | 亮白 |
| `Raycast` | `color.overlay.hit` | 近透明可点击 |
| `Mark` | `color.guide.mark` | 引导高亮 |
| `Title` | `text.title` | 24/Center/不换行/Truncate |
| `Body` | `text.body` | 20/Center/换行/Overflow |
| `Caption` | `text.caption` | 14/Left/换行/Overflow |
| `LabelSingle` | `text.label` | 20/Center/不换行/Truncate |
| `Emphasis` | `text.emphasis` | 20/Center/换行/Overflow/Bold |

按钮 variant 首版建议：`button.primary.md`、`button.secondary.md`、`button.ghost.md`、`button.danger.md`；尺寸 `sm/md/lg` 由 `text.*` 与间距共同决定，不单独建尺寸 token。

## 附录 C：生成器接口（目标）

```csharp
// 目标接口，非现行 API
UiThemeAsset Generate(UiStyleTokensJson source);          // JSON → SO；校验资产类型/schema/hash
UiStyleValidation Validate(string jsonPath, string soPath); // schema/hash/资产引用
```

- 生成器只读 JSON、只写生成物；不改 prefab。
- `UiThemeAsset` 的 `sourceHash` 覆盖 JSON 规范化内容；`ui.style.drift` 只比 hash，不重新解析整包。
- 生成器与校验器共用同一套解析/校验实现，禁止两套拼接口径。
