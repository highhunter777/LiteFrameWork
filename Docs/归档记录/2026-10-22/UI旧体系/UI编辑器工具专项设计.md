# UI 编辑器工具专项设计

> 状态：现行专项设计；工具入口、形态与安全边界见 §1.1，渲染/适配静态规则见 §4.1，控件库影响面见 §5，制作主线见 §8，角色化重构目标见 §9，角色/契约/交接见 §10
> 版本：3.1
> 更新日期：2026-10-22
> Owner：客户端工具；运行时/API 契约由客户端 UI 负责
> 依赖：[UI框架总设计](UI框架总设计.md)、[制作规范](UI制作规范.md)、现有 BindNode/LiteCodeGen 与 Unity Pipeline
> 输入：Prefab、绑定标记、样式/文本配置、资源引用；输出：标准资产、生成代码与结构化校验报告

## 1. Current 与工具边界

现行工具包括 [UiStyle/UiStyleTool](../../../../Assets/LiteGame/Editor/Style/UiStyleTool.cs)、视觉构建反例扫描器 [VisualConstructionScanner](../../../../Assets/LiteGame/Editor/VisualConstructionScanner.cs)、**UI 校验核心** [UiValidator](../../../../Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs)，以及 UI 制作主窗口、新建页向导和运行时 [BindIndexBuilder](../../../../Assets/LiteClient/UI/Bind/BindIndexBuilder.cs)、[UIBindBase](../../../../Assets/LiteClient/UI/Bind/UIBindBase.cs)。工具入口与清单见 §1.1；各工具的完成度以施工进度为准，本文只写现行形态与目标契约。

### 1.1 工具链平台接缝（Hub / 单一 CLI / 清单）

UI 工具已纳入 `scripts/gate/toolchain.json` 的 `ui` 域：唯一入口是 `LiteGame/工具链` Hub 与 `python scripts/toolchain.py`，`Docs/工具链/工具索引.md` 是清单的生成投影。平台契约见[开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md)，UI 域五条基线（入口/命名/结果/写盘/文档）见[工具链域形态基线专项设计](../../../设计文档/架构/工具链域形态基线专项设计.md) §3.2。

| 组 | ID | 形态 | 入口 | 写面 |
| --- | --- | --- | --- | --- |
| 制作 | `ui.page-new`、`ui.compose` | 窗口 | `python scripts/toolchain.py open ui.compose`（其余同形） | asset/source/generated |
| 校验 | `ui.validate` | 动作 | `python scripts/toolchain.py open ui.validate` | none |
| 样式 | `ui.style.colors-refresh`、`ui.style.colors-report`、`ui.style.text-apply`、`ui.style.text-report` | 动作 | `python scripts/toolchain.py open ui.style.colors-refresh`（其余同形） | asset / none |
| 绑定 hash | `ui.binding-hash-all`、`ui.binding-hash-selected` | 动作 | `python scripts/toolchain.py open ui.binding-hash-all`（其余同形） | asset（产物 data） |

ID、形态、入口与写面以 `scripts/gate/toolchain.json` 和[工具索引](../../../工具链/工具索引.md)为准；本表只按接缝分组，不复制完整清单。

**接缝规则**：

1. **唯一入口**：不新增顶层菜单、不建第二份索引；制作与新建页都在 Hub `ui` 域内打开。
2. **形态**：制作/新建页保持 `window`——Hub 只负责打开，内容留在原窗口，不重写成 `page`；校验/样式/hash 是 `action`，在 Hub 动作页执行并进统一报告。
3. **写盘与安全**：窗口必须声明 `safety`（`dryRun`/`undo`/`confirmation`）并在关闭时给变更摘要；资产写盘只走 Unity 序列化管线与 `ToolInteractionSession`；动作声明 `outputs`/`safety`，一次性写盘走 `ToolWriteScope`。未声明视为未完成平台接入。
4. **结果**：动作输出 `[ui/<工具>]` + 变更/失败计数；窗口关闭给“本次变更 + 可回滚项”；失败显式，不静默成功。
5. **新增工具**：清单一行 + `EditorToolRegistry` 注册 + `EditorToolCatalogEditModeTests` 对账 + `toolchain.py check`；不新增菜单。
6. **单页预览不是 Hub 工具**：它是 `ui.compose` 的动作与编辑器场景，平台清单不承担其开关；不得做成假的 `window`/`action`。旧工作台场景与面板已退役。

> **2026-10-11 UI 工具链第一步交付**：**校验核心**落地（`Assets/LiteGame/Editor/UiTools/Validation/`）——统一报告模型 `UiReport/UiIssue{ruleId,severity,asset,node,message,fixHint}` + 规则端口 `IUiRule` + 双侧上下文 `UiValidationContext`（源码面 + 资产面）+ 核心 `UiValidator`（菜单 `LiteGame/UI/校验/校验 UI 资产` / CLI `-executeMethod …RunCli` 非零退出 / EditMode 三入口共用）。**首条规则＝渲染/适配族的视觉单一来源**（`Rules/RuntimeVisualRule`——包既有 `VisualConstructionScanner`，源码面）；**其余五族（绑定/资源/文本/行为/性能）在报告显式列为"待接入"，不冒充通过**（§4 明文）。后续步：绑定标记工具 → 样式对账扩 `Screens/` → 校验进 CI → 编排面板 → UIPatch。施工记录见 [UI工具链](../../../施工进度/UI线.md#ch4-UI工具链)。

> **2026-10-11 UI 工具链第二步交付（绑定标记工具·底层两项）**：**①词表单源** `UiTools/Naming/UiNamingWords.cs`（C# 常量表——《命名词表与控件清单》机器可读副本：角色词/控件段/模板段/`Tmp_`/语义词 + §9.2 文法正则单源 + `TryParse`/`IsKnownPrefix`）；**②绑定命名规则** `Rules/BindingNamingRule.cs`（`ui.binding-naming`，资产面）——判据：节点名过 §9.2 文法、前缀段属词表（未登记=Warning）、`_` 私有件（模板内合法/页面节点禁）、根名（`Page_`/`Hud_`/`Tmp_`，存量页面根=Warning 待页面批次）；**UGUI 契约件**（Slider fill/handle、ScrollRect viewport/content、Toggle graphic/target）与**模板实例内部件**按身份豁免。绑定族 `implemented=true`。
> **同步命名整改批**（§9.6"随模板批次整形"）：全仓 UI prefab 命名合规化——**模板/夹具根改文件名** `Tmp_<控件类型>`（27 件 Widgets + 6 件 Screens 夹具，`AssetDatabase.MoveAsset` 保 guid，实例不断链）；**内部件**词缀化（`OkButton→Button_Ok`、`Label→Txt_Label`、`Blocker→_Blocker`）；**克隆条目模板** `_Template/_ItemTemplate→Clone_Template/Clone_Item`（§9.6）；业务节点 `UIMain/BtnClose→Button_Close`（同批同步 `BindName`）。整改后规则 **0 Error**（仅 `UIMain` 根 Warning——§9.6 登记的页面根债，随页面批次）。`UIDemoPage.Templates[]` 随文件名同步。详见施工记录。



> **2026-10-10 代码现状订正**：原清单中的 `WidgetPrefabBuilder`（1477 行）与 `UICollectCheckMenu`、`WidgetDemoMenu` 已随提交 `45aa839`「输入服务余部与场景资源杂项（接管并行线）」**从仓库删除**，不再是"已存在"工具。`Assets/ShareResource/UI/Widgets/` 下 27 件 `Tmp_*` 模板 prefab 作为已产出资产保留；结构/行为断言改由 [VisualConstructionScanner](../../../../Assets/LiteGame/Editor/VisualConstructionScanner.cs) 与 `Assets/Tests/EditMode/VisualSingleSourceEditModeTests.cs` 承担。业务 UI 页面按《UI-U2》裁决走编辑器手工制作，不依赖已删除的确定性构建器。

工具服务标准资产和受控运行时：

- 画布、几何、层级、锚点和撤销使用 Unity Prefab Stage；不造第二套画布或运行时布局格式。
- 生成链沿用 Luban/LiteCodeGen/运行时绑定索引；产物可重建、可对账，不能由编辑器私有格式绕过制作校验。
- Odin 仅作 Editor 呈现适配，运行时标记用自有组件/attribute，不依赖 Sirenix。独立 Validator 是否可用按实际环境确认，校验核心必须能脱离付费面板在 CI 执行。
- Prefab/asset/meta 写入经 Unity/Pipeline API，遵循 `Docs/工具链/Unity操作规范.md`。批量改动有预览、Undo、目标清单与结果报告；不得覆盖用户未保存编辑内容。
- 状态、优先级、所有权、动效播放决定由运行时负责；工具只声明配置、预览和验证。运行时播放不以先经过编辑器面板为许可条件。

## 2. 绑定标记与生成

> 现行编辑面在 §5/§8.2 的 UI 制作主窗口（`UiBindingEditor`）；独立绑定标记窗口已退役。本节保留绑定引擎、命名规则与生成契约。

### 2.1 功能

1. 按页面展示 BindRoot/BindNode 树、绑定类型与组件引用；区分页面和列表项绑定作用域。
2. 类型候选、前缀预设命名（词表下拉 + 仅填语义名，组合 `前缀_语义` 同写节点名与 BindName——《制作规范》§9 单源；预设同时挂层级重命名入口，非绑定节点同享；改名时列出 Lua/C# 字符串字面量引用点辅助同步）、批量重命名、标记复制、SceneView 叠加，操作遵循 Unity Undo。
3. 校验空名/重名、缺组件、重复标记、类型不符、失效引用和疑似未暴露交互节点；空名模板暴露位是允许状态。
4. 生成 Lua 路径 A 的名称/路径元数据与 C# 路径 B 的绑定类；支持多 Prefab 批量，跨文件生成类名冲突与同页 BindName 冲突分别报告。
5. 生成记录源 Prefab/绑定内容的 hash；CI 检查是否需要重新生成，避免提交资产却漏生成物。
6. 词表数据文件化：《制作规范》§9 的类别前缀/类型段/语义词条住单一数据文件，Inspector 预设与校验核心共读，规范与工具同批更新（内容源 = [UI 命名词表与控件清单](UI命名词表与控件清单参考.md)）。

### 2.2 运行时索引与验收

缓存的是绑定元数据/相对路径，键包含 Prefab GUID 和内容版本；不能缓存上一个页面实例的 Component 引用。实例化后解析本实例引用并做一致性校验。

改名/删除标记后重生成无残留；生成结果确定、重复执行不产生 diff；生成索引与运行时扫描对同一实例得到相同名称/类型。类型缺失和重名有负例；路径失效报告资产和节点，不静默映射错控件。

落点优先既有 Editor 程序集；是否拆 asmdef 按依赖边界决定，不以“永不新增程序集”限制测试隔离。

## 3. 样式工具

> 目标形态（token 资产/引用/解析/编辑期应用/可选运行期主题）见[UI 样式系统专项设计](UI样式系统专项设计.md)；本节只保留现有对账工具与迁移边界。

### 3.1 单源与职责

现行颜色 token 住 `Editor/Style/UiStyle.cs`；文字样式作为独立语义档，不复制颜色值。运行时按状态改实例合法，样式对账只读盘上资产。目标 token 源/引用/应用见[UI 样式系统专项设计](UI样式系统专项设计.md)。

目标组成：样式表 → `UiTextStyleRef` 等可枚举标记 → 批处理应用/对账 → 窗口展示。运行时标记无 Odin 依赖；若运行时需要字体缩放、语言字体和主题，使用受控样式解析器，不能因为编辑器样式为默认值就禁止运行时适配。

初始文字角色保留以下基线，可按语言/可访问性扩展声明覆盖：

| 角色 | 字号 | 水平对齐 | 换行 | 初始溢出策略 | 字重 |
| --- | --- | --- | --- | --- | --- |
| Title | 24 | Center | 关 | 非关键标题可 Truncate；关键标题按容器调整 | 常规 |
| Body | 20 | Center | 开 | 仅在自适应/滚动容器内允许扩展 | 常规 |
| Caption | 14 | Left | 开 | 由容器限制或滚动 | 常规 |
| LabelSingle | 20 | Center | 关 | Truncate；关键操作需扩展/换行策略 | 常规 |
| Emphasis | 20 | Center | 开 | 同 Body | 加粗 |

Middle、字距/行距 0 是默认，不是所有语言硬限制。字号阶梯是校验参考，不把 `22` 等数值不看页面就自动改成 24；先明确语义档与视觉差异。AutoSizing 必须声明范围与验证样例，不能任意组件自由启用。

### 3.2 应用与对账

- 颜色可用最近 token 匹配与 1e-4 阈值收敛；文字枚举/字号精确应用。改变 token/档位后重跑应幂等。
- 作用域为 `Assets/ShareResource/UI/Widgets/**` + `Assets/ShareResource/UI/Screens/**`（`UiStyleTool.DefaultScanDirs` 两常量；文字档工具扫描根可注入）。只修改已标记的目标属性；结构、锚点/尺寸、shader 替换是另一项明确操作。
- 未标记组件先报告，按整改清单收口；不能静默跳过却报告全工程已一致。
- 漂移报告区分标记缺失、声明覆盖、属性不符和资源引用失效；运行时实例变化不进入资产漂移。
- 字体检查目标字体族/fallback/语言覆盖，不以“全 UI 唯一字体”为目标。共享材质优先，实例材质要有所有权与预算。
- 帧动画只校验资源引用和帧顺序声明，不重排帧序。

### 3.3 材质、文字特效与候选轴

Shader 做能力校验，不做自动“一键换 shader”。用代表性资产验证裁剪、Mask、alpha 和目标平台；不检查虚构的继承关系。

文字特效在首个真实需求出现时增加共享 TMP 材质预设与有限 TextFx 选择，颜色使用 token；每页非默认预设初始参考不超过 2 种，最终以批次/GPU 预算为准。独立描边色等能力需验证所用 shader，不能仅凭编辑器选项承诺。

尺寸/间距、Image.Type、TMP Sprite Asset 名册与层级深度可逐项加入校验。Image.Type 依据用途：普通图标 Simple、拉伸背景 Sliced、进度 Filled。tween、粒子、Animator 的播放参数不并入文字样式表；可静态验证引用，行为仍归 PlayMode/运行时诊断。

## 4. 校验核心与 CI

同一校验核心供菜单、窗口和自动测试调用，输入资产集合，输出 `{ruleId, severity, asset, node, message, fixHint}`。报告不依赖打开窗口，出错要能让测试非零失败。

| 规则族 | 校验内容 | 层级 |
| --- | --- | --- |
| 绑定 | 命名作用域与文法（[制作规范 §9](UI制作规范.md)）、类型、缺组件、生成物 hash | L2 EditMode |
| 资源 | 缺引用、收集组、图集/字体/sprite 名册、生产包排除调试补丁 | L2 EditMode + Player 构建验证 |
| 文本 | key/占位符/语言列、富文本白名单、fallback 与缺字、样式覆盖 | 表结构可纯数据验证；Prefab/TMP 语义在 L2 |
| 渲染/适配 | 根缩放策略、Raycast 配置、层序声明、布局循环风险 | L2 EditMode；动态效果在 PlayMode |
| 行为 | 复用视觉、输入穿透、焦点、列表重绑、语言变更、取消 | L2 PlayMode |
| 性能 | rebuild/批次/GC/打开分段耗时 | Profiler/Player，不能用静态正则代替 |

### 4.1 渲染/适配族的静态规则（目标）

下列规则补足 §4 表中“根缩放策略、Raycast 配置、布局循环风险”的静态可判部分；均只报告、不改资产。未接入时在报告中显示“待接入”，不冒充通过。

| 规则 ID | 判据 | 默认级别 | 例外 |
|---|---|---|---|
| `ui.render-raycast` | 非交互 Graphic 的 `raycastTarget=true`：不在 `Selectable` 子树内、不是显式阻断面、也不是 `ScrollRect` 必需命中件 | Warning | 模态遮罩、Loading `Blocker`、滚动容器命中面、显式点击热区——登记例外 |
| `ui.render-scaler` | 页面根 `CanvasScaler` 与项目档一致（`ScaleWithScreenSize`、1920×1080、Match 0.5） | Warning | 独立根、产品覆盖——登记例外 |
| `ui.render-layout-depth` | `LayoutGroup`/`ContentSizeFitter` 嵌套超过阈值（默认 3 层） | Warning | 阈值按页面类型配置；不设“Prefab 最多 N 层”硬上限 |
| `ui.render-animator` | `Assets/ShareResource/UI/**` 的 prefab 挂 `Animator` | Warning | 已登记例外（如 3D 预览类 UI） |

- `ui.render-raycast` 不自动关闭 `raycastTarget`；关闭是作者显式动作。阻断/命中面误判比漏报更难恢复。
- `ui.render-scaler` 默认 Warning；是否升为 Error 由规则配置显式开启，不在规则内写死。
- `ui.render-layout-depth` 只告警“级联 Rebuild 风险”，不替代 PlayMode/Profiler 的实测；合法组合按例外表放行。
- `ui.render-animator` 与《UI 制作规范》§5“UI 动效走 UiFx/统一播放入口”同源；Animator 例外必须能指出对应的视觉效果与性能预算。

每条硬规则提供正例与违规负例。需要组件和序列化资产语义的规则不得塞进纯文本 DisciplineScanner。规则严重级别、例外和适用范围与制作规范一致；未实现的规则标识为待接入，不显示为自动通过。

预览应覆盖中英/伪本地化、长文本、安全区、字体缩放和 reduced motion。能在编辑态检出的结构错误提前失败；依赖 Awake/OnEnable 的行为用 PlayMode 证明。

## 5. UI 制作主窗口

**主窗口形态**：一个**宿主窗口**（`OdinEditorWindow`，Hub `LiteGame/工具链` → UI 制作，落 `Assets/LiteGame/Editor/UiTools/Compose/`）＋顶部动作（新建页 / 预览）＋四个分区——**绑定暴露 / 受控属性 / 校验·生成 / 控件库**。四个分区可独立交付；不建四个窗口——四窗口会各自复制"Stage 跟随 / 显示根解析 / 预览作废"上下文（违"消除重复"）。工作台退役后，这里也是唯一的 UI 制作入口。

**依赖边界（Odin 只进 `LiteGame.Editor`）**：呈现层走 Odin——宿主窗口基类 `OdinEditorWindow`；**四个分区＝Odin `TabGroup` 页签**（`[OnInspectorGUI, TabGroup("Compose", <页签名>)]`；目标 prefab 字段与状态行置于页签组之上）；受控属性行由 **Odin 属性树**呈现（`PropertyTree.Create` + 白名单过滤 + `InspectorUtilities` 绘制对，写入/Undo/脏标记交属性树统一处理）；**控件库列表＝inline `OdinMenuTree`**（含 Odin 搜索栏；不用独立 `OdinMenuEditorWindow`——保持单宿主窗口），右侧为选中模板的组件/受控面预览与插入入口。页签体（绑定树/报告）走既有 IMGUI 绘制器，在 Odin 页签内混排。**分区逻辑类与运行时标记零 Sirenix**——`UiComposeTarget`（目标解析单源）、`UiWidgetPropertyCatalog`（白名单发现）、`UiWidgetLibrary`（目录即清单）、`UiWidgetInstaller`（Stage 内插入）均不依赖 Sirenix，可脱离 Odin 编译与测试（"摘除 Odin 只换绘制层"）。Odin 为**阻塞式环境项**（`scripts/gate/restore-check.py` §4、`toolchain.json` `paidOrRestrictedPlugins`），缺失即门禁失败。

**分区职责**

| 分区 | 交付面 |
|---|---|
| **绑定暴露** | **编辑面内嵌**：树数据单源 `UiBindingRename.BuildTree`、绘制单源 `Binding/UiBindTreeView.cs`、编辑面 `Binding/UiBindingEditor`；前缀预设/批量改名/预览/应用/hash 全在本分区完成，不再跳独立窗口 |
| **受控属性** | 白名单＝自有控件组件上的**运行时 attribute** `UiWidgetProperty`（`Assets/LiteClient/UI/UiWidgetPropertyAttribute.cs`，零 Odin、只声明不驱动）。**未标注成员不进枚举、无绘制入口**——"不暴露控件内部实现"是物理边界而非按钮禁用；接线引用字段（如 `HpBar.Slider`）与几何/锚点（归 Prefab Stage 原生编辑）都不在此面 |
| **校验/生成** | 同一校验核心的窗口入口——**窄档**（资产面只喂本 prefab、源码面置空）与**全量档**（`Run()`）；报告列表化，行内"定位节点"经 `UiComposeTarget.FindNodeForReport` 反查（报告 node 含根名、绑定树口径不含根——两口径由该件收口）；**生成**＝`BindGenerator.Generate` 成功后同一步 `UiBindGenerationHash.Record`（窗口内先确认框——生成触发域重载） |
| **控件库** | **目录即清单**（扫 `Assets/ShareResource/UI/Widgets` 的 `Tmp_*`，不另存清单文件；内容源仍为《命名词表与控件清单》§1.3）；列表＝**inline OdinMenuTree 左树 + Odin 搜索栏**，右侧为选中模板的组件/受控面预览；插入**只在 Prefab Stage 内**（`InstantiatePrefab` + Undo，Variant/嵌套作用域全归 Unity 原生），插入后**不改名**、只提示下一步标记/命名（§5 原文：不以面板自动重名规则掩盖绑定冲突） |

**控件库影响面（只读）**：选中 `Tmp_*` 时，按 prefab GUID 扫 `Assets/ShareResource/UI/Screens/**` 与 `Hud/**`，列出引用该模板的页面/节点数量与路径，供改模板前评估影响。影响面只读，不自动更新实例——实例更新走 Unity 原生 Overrides，工具不代写。

**流程**：新建页（§8.3）或打开已有页 → Prefab Stage 原生布局 → 本页标记/命名 → 样式和本地化预览 → 校验/生成 → 布局预览。布局操作仍由 Unity Prefab Stage 提供；产物即标准 prefab（不存在"生成 prefab"这一步）。预算按可验证功能拆分，不以行数或天数估算作为交付承诺。

**本轮不做**：UIPatch 调试补丁（§6，随 U4 消费者）；控件库缩略图（RenderTexture）；第二套画布/几何编辑；控件模板新建/克隆；对象引用类值型受控属性（`Unsupported` 显式列出，不静默跳过）；真链路预览面板（§8.4）。

施工记录见 [UI工具链](../../../施工进度/UI线.md#ch4-UI工具链) 步 6。

## 6. 调试补丁与诊断

UIPatch 的版本、作用域、生产禁用和冲突规则由总设计第 10.3 节定义。工具提供差异预览、旧值/版本冲突报告、应用到标准 Prefab、Undo 和补丁归档；不创建第二套资产真相。调试面的写回（[《开发包 UI 调试面专项设计》](开发包UI调试面专项设计.md) §4.3）与本节的 UIPatch 共用同一 patch 模型；调试面不另造写回格式。

运行时诊断展示实例阶段/展示状态、层序、焦点/模态、操作 requestId、内容版本、缓存/租约/订阅/动画数和最近失败；从诊断跳转到页面定义和 Prefab。数据由服务快照提供，避免窗口自行遍历修改运行时状态。

## 7. 时间轴归属与实施顺序

技能/表现 Timeline 的自定义轨、导出和整数帧校验由 [动作与特效专项设计](../../../设计文档/游戏玩法/动作与特效专项设计.md) 负责，不属 UI 工具范围。UI 转场仍走运行时策略；非对局演出可按该专项使用 Timeline。时间轴编辑器不作为 UI U0/U1 的前置。

唯一排期在总设计第 13 节：基础校验/可观测性随 U0/U1；绑定/样式/预览/CI 随 U2/U3；完整编排器与 UIPatch 随 U4 的真实消费者。需要大批量修改模板时事先核对工作区改动并限定资产清单，不能用工具覆盖其他未完成修改。

验收产物包括生成差异、规则正负例结果、代表性页面 PlayMode 结果和性能记录。每个工具的“完成”要对应消费者和产物，不以菜单存在作为完成证明。

## 8. 制作流水线（单页制作主线）

> 本节裁决 UI 制作的目标形态：**一个窗口、一条主线、一个预览入口**。工具细节（校验规则、样式、绑定 hash）仍按 §1–§6；工作台形态由 §8.4 的单页预览取代。

### 8.1 定位与边界

- UI 制作只有一条主线：**新建/打开页面 → Prefab Stage 布局 → 就地命名/绑定 → 校验/生成 → 预览**。
- **唯一编辑面是 Unity Prefab Stage**：工具窗口不造第二套画布，不接管几何/锚点/层级；资产写入仍走 Unity 序列化管线。
- **唯一入口仍是 Hub `LiteGame/工具链` 的 `ui` 域**；不新增顶层菜单，不建第二份工具索引。
- 工作台不再作为独立工具/场景存在；运行内目检收敛为编辑器单页布局预览；真链路验收走 Boot/联调或 L2。

### 8.2 主窗口（UI 制作）

改造现有 `UiComposeWindow` 为单窗口工作流，四块固定布局：

| 区域 | 内容 |
|---|---|
| 顶部 | **已登记页面选择器**（tbuiform，选页即开 Stage）+ 目标页自动跟随 + 动作：新建页 / 预览 / 校验 / 生成 |
| 左列 | 页面结构树（节点/绑定状态）+ 控件库（插入到选中节点） |
| 右列 | 选中节点的命名/绑定（前缀 + 语义，预览后应用）+ 受控属性 |
| 底部 | 本页校验问题、生成物 hash、最近操作结果 |

- **绑定编辑并入本窗口**：前缀预设、批量勾选/重命名、预览、应用全在同一面；删除独立 `UiBindingWindow` 壳，逻辑复用 `UiBindingRename` / `UiBindTreeView`。
- **插入控件后不自动改名**：焦点落到命名输入，由作者确认名字；避免工具自动重名掩盖绑定冲突。
- **目标页跟随**：Prefab Stage 打开即绑定该资产；无 Stage 时只在首次打开带入 Project 选中，不抢后续选择。
- **页面选择器**：顶部读 tbuiform 行（`UiPageScaffold.ListPages`），选页打开对应 Prefab Stage 并绑定目标——预览不需要先去 Project 搜 prefab；新建页成功后由向导刷新清单。Project/Prefab Stage 手动选择仍可跟随。
- **生成动作**（触发域重载）前显式确认；窗口状态可丢失，生成后重开并回到目标页。
- `ui.binding` 从 Hub 清单退役，`ui.compose` 升级为唯一制作窗口；独立回写窗口同样退役——Play 内调参走 Prefab Stage 与 Unity 原生 Overrides，只有出现真实消费者时才恢复专用回写工具。

### 8.3 新建页

输入：页面名（`Page_<语义>`）、id、layer、full_screen、可选模板。

> **角色边界**：本向导同时写 prefab + Lua + tbuiform，属**工程接入**动作；美术起页只复制 `Screens` 视觉基线并改视觉，不执行登记步骤。角色化拆分（美术只建 prefab / 工程登记）见 §10.1/§10.5。

产物三件，按顺序生成并逐步报告：

1. `Assets/ShareResource/UI/Screens/<Page>.prefab`——**复制**脚手架模板后改根名，产物是自包含 prefab，不得成为编辑器模板的 variant。
2. `Assets/LiteGame/Lua/UI/<Page>.lua`——最小页面骨架（`class` / `_viewName` / `OnShow` / `OnHide`）。
3. `Luban/Data/#uiform.xlsx` 追加一行（`id` / `lua_path` / `prefab` / `layer` / `full_screen`），随后跑 Luban 生成。

**脚手架契约**（模板 `Assets/LiteGame/Editor/UiTools/Templates/Tmp_Page.prefab`，编辑器工具资产，不进收集组）：

- 根：`RectTransform` + `Canvas` + `CanvasScaler`（1920×1080，Match 0.5）+ `GraphicRaycaster` + `CanvasGroup`。
- 子件：`Cot_SafeArea` + `SafeAreaReceiver`；不预置 `BindName` / `BindRoot`。
- 页面根与文件名同文；节点命名过《UI 制作规范》§9 文法（语义段 PascalCase——与 `UiNamingWords.GrammarPattern` 同一单源）。

**表写入安全**：id 查重、prefab/Lua 路径校验、layer/full_screen 校验；写前备份 xlsx（`Temp/uiform-backups/`）；追加后跑 Luban，失败时给出可执行的恢复/回滚指令，不静默成功。表写入由 Python 助手脚本（openpyxl 已在门禁依赖）承担，Unity 窗口只负责动作编排与结果呈现。

**id 规则**：业务页推荐取现有最大业务 id + 1；不复用系统反馈面 201–203 与静态先例 204–206 的 id。

完成后直接打开新页的 Prefab Stage；无 `BindRoot` 时不自动生成绑定代码。

### 8.4 单页预览

目标：替换工作台的运行内目检，以最低成本看**单个页面**。现行形态是**布局档**：

| 档 | 做什么 | 不做什么 |
|---|---|---|
| **布局档** | 直接实例化目标 prefab；保存资产即热重载；看视觉、布局、边界态 | 不跑 Boot/Lua/数据；文案与数据为 prefab 默认值 |

- 入口：UI 制作主窗口的“预览”按钮；目标 prefab 路径由入口写入 `EditorPrefs`。
- 预览场景住 `Assets/LiteGame/Editor/UiTools/Preview/UiPreview.unity`，是编辑器工具资产，不进收集组、不进构建；预览不替代 L2 断言。
- **真链路不另造面板**：需要本地化/真实数据时走 Boot → Preload → Main 的正常启动与联调（或 L2 PlayMode），不在预览工具里复制第二套运行内目录、状态注入与 HUD 预览。
- 退出 Play 自动回到进入预览前的场景；DevHUD 在预览中隐藏，避免遮住页面。

### 8.5 退役面

- 代码/资产：`UIWorkbenchDriver`、`WorkbenchUI.prefab`、`UiWorkbench.unity`、`BuildWorkbenchPanel.cs`、`WorkbenchW2Probe.cs`、`WorkbenchUiAssetEditModeTests` 已退役；工作台相关的开发页按钮、启动开关与请求桥一并移除。
- 回写窗口：`PrefabWriteBackWindow`/`PrefabWriteBackScan`/`PrefabWriteBackApply`、`PrefabWriteBackEditModeTests`、Hub `ui.writeback` 与菜单 `LiteGame/快捷/Prefab 回写` 已退役；Play 内调参走 Prefab Stage 与 Unity 原生 Overrides。
- 入口：工作台不再有启动开关或开发页入口；UI 预览只在编辑器主窗口触发（页面选择器选页后一键预览）。
- 新增工具资产：`UiTools/Page/UiPageScaffold`、`UiTools/Page/UiPageWizardWindow`、`UiTools/Preview/UiPagePreview`、`UiTools/Templates/Tmp_Page.prefab`、`DevTool/UiPreviewDriver`；表行助手 `scripts/codegen/_uiform_row.py`。
- 文档：`08-UI工作台` 改为“UI 制作与预览”；`UI工作台专项设计` 随退役归档；同步 `UI线`、代码地图、修改指南、工具索引、审计表。
- 与并行线（`DebugTuner` → `DevMenuLauncher`）合流后再改入口文件，避免互相覆盖。

### 8.6 验收

- **新建页**：一次动作产出 prefab + Lua + 表行，Luban 通过并打开 Stage；重复 id / 非法名被挡；失败有显式恢复路径。
- **单窗口**：改名/绑定/插入/校验/生成不跳窗；生成后窗口可重开并回到目标页。
- **预览**：布局档保存 prefab 即热重载；退出 Play 回原场景；工作台引用清零。
- **门禁**：Unity 编译 0 错；L1；L2 EditMode 新增守卫（脚手架契约、新建页输出、预览入口）；Doc lane。

## 9. 角色化制作工作台（uGUI/Prefab/BindNode 版）

<a id="ch9-ui-authoring"></a>

> 本节是**目标形态**：吸收参考设计（大型/多端 UI 工具链）的结构原则——角色隔离、接口契约、设计令牌、增量校验、运行时调试——但**不迁移到 UI Toolkit**。所有落点仍是我们现有的 Prefab/Canvas/BindNode/Lua/编辑器管线；工具只围绕 Unity Prefab Stage 做约束、校验和预览。

### 9.1 参考设计的译法

| 参考设计（UI Toolkit） | 本项目等价物 | 裁决 |
|---|---|---|
| UXML/USS/C# 三文件分离 | Prefab（结构+视觉）+ `UiStyle`/`UiTextStyle`/`LTextKeyRef`（样式/文案声明）+ `BindNode`/`BindRoot`/`UIBindBase`/Lua（逻辑） | **不做文件级三轨**；用 `UiPageContract` 做美术→工程的接口边界 |
| UI Builder 可视化画布 | Unity Prefab Stage | 唯一编辑面；不自建第二套画布/布局格式 |
| Variables.uss / Themes | `UiStyle`/`UiTextStyle`（编辑期 token 单源）；多主题确有必要时才引入 `UiThemeAsset` | 先做 token 报告/应用/校验，不先做运行期主题系统 |
| Data Binding / Controller 生成 | `BindGenerator` + `BindRoot`/`UIBindBase` + `UiPageContract` + Lua 页骨架 | 复用现有名字绑定；**不引入 UI Toolkit MVVM/INotifyBindablePropertyChanged** |
| UXML/USS 规范校验 | `UiValidator` + 契约/裸样式/引用规则 + 美术 lane | 复用校验核心，不另造扫描器 |
| PanelSettings 多端锚点 | Canvas 根 + `CanvasScaler` + `SafeAreaReceiver` + 设备档 | 用预览/运行时调试的设备档，不引 PanelSettings |
| Dynamic Atlas / SDF FontAsset | SpriteAtlas（UI 图集）+ TMP SDF 字体 + 现有资源收集/字体规范 | 沿用 uGUI 渲染与资源链，不引 UI Toolkit 渲染管线 |
| Figma Connector | 无 uGUI 等价导入管线 | **非目标**；需要时另立资产导入管线，不混入本工具 |

### 9.2 双 lane 形态

一个 Hub 窗口 `ui.compose`（顶部 **lane 切换** + 页面选择器），Prefab Stage 为共享编辑面；不建第二个制作窗口、不建第二份工具索引。

| lane | 可用面 | 禁止面 |
|---|---|---|
| **美术制作** | 起页（从 `Screens` 视觉基线复制，仅 prefab）、控件库、样式 token、静态文案 `LTextKeyRef` key、美术 lane、布局预览、截图 | 页面登记、Lua/表写、绑定生成、契约外的逻辑接线 |
| **工程接入** | 页面登记（Lua + tbuiform）、`UiPageContract` 生成/评审/漂移、绑定编辑/生成、引用/全量校验、运行时调试 | 未经美术评审改视觉/字号/间距/切图 |

- 角色责任、RACI、交接清单见 §10；本节只裁工具形态，不复制角色表。
- lane 是 UX 分层而非权限系统；同一 owner 仍按“一个 prefab 同一时间一个 owner”执行。
- 页面选择器、目标跟随、Prefab Stage、校验报告、预览为共享面；两 lane 不各存一份上下文。
- **新建页拆分**（目标）：美术 lane 的“起页”只复制视觉基线并改视觉，不写 Lua/tbuiform；工程 lane 的“接入登记”才创建 Lua 骨架 + tbuiform 行 + 跑 Luban。落地前，§8 的 `ui.page-new` 仅由工程执行。

### 9.3 契约优先

- **交接物**：`UiPageContract` 由预制体生成（`BindNode`/`LTextKeyRef`/`UiTextStyleRef`/嵌套 `Tmp_*` + `tbuiform`），JSON schema 与字段模型见 §10.2。美术生成/评审，工程消费；契约 diff 视为 UI API 变更。
- **静态文案**：`LTextKeyRef` 挂文本节点，归美术；动态文案仍归 Lua。新页面默认走 `LTextKeyRef`，不把静态文案写进 Lua 形成双源。
- **绑定暴露**：继续用 `BindNode`/`BindName` + `UIBindIndex`；`BindRoot`/`UIBindBase` 的路径 B 生成保留。契约的 `controls[]` 是绑定生成与引用对账的输入，不另造一份名字表。
- **嵌套与作用域**：`Clone_` 条目模板不暴露；嵌套 `BindRoot` 独立生成；模板内部件只进 `templates`。规则与 schema 单源在契约设计，本工具不复制。
- **目标规则**：`ui.page-contract`（schema/漂移/唯一性）、`ui.page-contract-refs`（Lua/C# 引用对账）、`ui.page-contract-text`（静态/动态单写者）进校验核心与 CI；`ui.contract`（交接页/生成/刷新）与 `ui.art-lane`（美术快速校验）为 Hub 目标入口。

### 9.4 校验与预览

**美术 lane**：只扫本次改动的 prefab，目标 <10s，中文可执行提示；范围＝命名/绑定类型/根 Canvas+CanvasGroup+GraphicRaycaster/SafeArea/图集与九宫格引用/裸颜色与裸字号/`LTextKeyRef` 缺 key/模板引用/§4.1 的渲染/适配静态规则。它不替代全量校验，只做提交前自检。

**工程/全量**：`UiValidator` 全量 + 契约三规则 + 引用对账 + PlayMode；CI 阻断契约漂移、破坏性变更未评审、引用不在契约 `controls` 子集。

**预览**：

- 美术用布局档（当前 `UiPagePreview`）做静态布局/文案/安全区/多语言（文案为 prefab 默认值）；
- 工程/联调用 Boot/PlayMode 或后续单页真链路预览看真实数据与行为；
- 设备档（分辨率/安全区/横竖屏）进预览工具栏，别在多个场景里复制分辨率配置。

### 9.5 工具接缝

| 目标入口 | 形态 | 写面 | 说明 |
|---|---|---|---|
| `ui.compose` | window | asset/source（按 lane） | 唯一制作窗口；顶部 lane + 页面选择器 |
| `ui.page-new` / `ui.page-scaffold`（拆分后） | window | 工程：asset/source/generated；美术：asset | 工程登记 vs 美术起页；落地前仅 `ui.page-new`，按工程接入使用 |
| `ui.contract` | page | asset（契约生成物） | 契约生成/评审/漂移；规则在 `UiValidator` |
| `ui.art-lane` | action | none | 美术增量校验；报告进统一结果区 |
| `ui.validate` / 样式 / hash | action | 现状 | 复用现役工具 |
| `ui.runtime-debug` | window（runtime plane） | asset（属性级 patch；仅编辑器显式写回） | 见[《开发包 UI 调试面专项设计》](开发包UI调试面专项设计.md) |

规则：不新增顶层菜单、不建第二份索引；所有入口经 Hub/CLI 清单、`EditorToolRegistry` 与 `EditorToolCatalogEditModeTests` 对账。目标 ID 在实现批才进 `toolchain.json`；本节不提前伪造清单行。

### 9.6 非目标

- 不迁移**产品 UI** 到 UI Toolkit；不引入 UXML/USS/UI Builder/PanelSettings/Dynamic Atlas/UI Toolkit Data Binding/Figma Connector 作为产品 UI 资产。**运行时调试工具自身允许使用 UI Toolkit**（dev-only，见[调试专项](开发包UI调试面专项设计.md)）。
- 不自建可视化布局编辑器；Prefab Stage 是唯一画布。
- 不恢复专用回写窗口；Play 内调参走 Prefab Stage/原生 Overrides。
- 不把契约生成器、美术 lane 或运行时调试器做成第二套运行时 UI 面板。
- 不把美术 lane 的“增量/快”当作门禁替代；全量校验与 L2/L4 仍在夜间/CI。

### 9.7 阶段与验收

| 阶段 | 范围 | 验收 |
|---|---|---|
| P0 | 双 lane UX + 新建页角色拆分；`UiPageContract`/`LTextKeyRef`/美术 lane；运行时调试 MVP（见调试专项 U0–U2） | 美术能从视觉基线起页、标记 `BindNode`、挂 key、生成契约、跑美术 lane、布局预览；工程能登记/消费契约/生成/全量校验；契约漂移与引用越界 CI 红 |
| P1 | 契约三规则收口；**按需**的单页真链路预览（Boot 单页，不建运行内面板）与设备档；运行时调试深检（绑定/样式来源/焦点/性能） | 运行时调试与真实 `UIService` 状态一致；真链路预览不另造面板；无每帧全树扫描 |
| P2 | token 资产化/多主题（仅真实需求）；多端设备矩阵 | token 单源可审计；多端适配不依赖第二套 UI 资产 |

**总验收**：同一页面，美术与工程各自按 lane 独立完成自己的面，接口只通过契约；工具不制造第二套名字/样式/运行时真相；release 零调试残留。

## 10. 角色、页面契约与交接（现行权威）

<a id="ch10-ui-handoff"></a>

> 本章吸收原《UI美术角色与交接契约》的现行规则；原专项全文归档于[2026-10-22 归档](UI美术角色与交接专项设计.md)。工具形态见 §9，本章裁角色/契约/交接，不重复工具细节。

### 10.1 角色与所有权

| 角色 | 负责 | 不负责 |
|---|---|---|
| UI 美术 | 页面布局、视觉层级、切图/九宫格、静态文案 key、文字/颜色样式、暴露控件标记、`UiPageContract` 生成、美术 lane、视觉验收 | Lua/数据/导航、运行时生命周期、性能预算裁决 |
| UI TA（可兼） | 颜色/文字 token、图集、材质/Shader、动效规范、性能与资源预算 | 业务逻辑、页面数据 |
| 客户端 UI 工程 | 页面数据/逻辑/导航接线、契约消费与评审、绑定引用、行为验收、工具实现 | 视觉细节最终裁决、绕过美术直接改视觉 |

RACI 摘要：布局/视觉/切图/静态文案/视觉验收＝美术 A/R；暴露控件/页面契约＝美术 R、工程 A；数据/逻辑/导航/绑定消费/行为验收＝工程 A/R；颜色/文字样式/图集/Shader/性能预算＝TA A/R。

| 面 | Owner | 说明 |
|---|---|---|
| `Assets/ShareResource/UI/{Screens,Widgets,Icons}` | 美术 | 页面/模板/图标；提交走 Unity Pipeline |
| `Assets/ShareResource/UI/Hud` | 美术 + 工程 | 视觉归美术，接线归工程 |
| `Assets/ShareResource/Fonts`、图集、UI 材质/Shader | TA | 字体 fallback、图集分组、Shader 能力 |
| `Assets/LiteClient/UI/**` | 工程 | 运行时框架与控件 |
| `Assets/LiteGame/Lua/UI/**` | 工程 | 页面逻辑与动态文案 |
| `Assets/LiteGame/Editor/UiTools/**` | 工程 | 制作主窗口、新建页、契约生成与校验工具 |
| `Assets/LiteGame/Editor/UiTools/Contracts/**` | 美术生成、双方评审 | 生成物；禁手改 |
| `tbuiform` / `tbltext` 表源 | 工程/本地化落库 | 美术提意图与 key/文案；id 分配与表源落库归工程 |
| `scripts/gate/toolchain.json` UI 条目 | 工具链平台 + 工程 | 不另建菜单/第二索引 |

纪律：一个 prefab 同一时间一个 owner；视觉改动未经美术评审不合并；契约/绑定改动未经工程评审不合并；美术不直接改 Lua/表源；任何人不得手改 `UiPageContract` 生成物；以“先合再补”替代校验视同未交接。

### 10.2 `UiPageContract`

**定义**：从预制体生成的只读页面接口描述，是美术→工程的唯一交接物；截图只是验收证据，不替代契约。归档前全文的字段模型与规则已收口到本节，机器可读定义仍为 [UIPageContract.schema.json](UIPageContract.schema.json)。

| 项 | 约定 |
|---|---|
| 真相 | Prefab（`BindNode`/`LTextKeyRef`/`UiTextStyleRef`/嵌套 `Tmp_*`）+ `tbuiform` 行 |
| 产物 | `Assets/LiteGame/Editor/UiTools/Contracts/<PageName>.contract.json`；不进运行时/不进包 |
| 写入 | 生成器写；禁手改；编辑器显式触发 |
| 段 | `page`（formId/prefabPath/prefabGuid/luaPath/layer/fullScreen/cacheStrategy）、`controls[]`（bindName/nodePath/type/kind）、`text[]`（nodePath/key）、`styles[]`（nodePath/role）、`templates[]`（模板/路径/bindingHash?）、`sourceHash`/`generator` |
| 生成 | 确定性、深度优先；只收非空 `BindName`；页面内唯一；类型可解析；`Clone_` 不暴露、嵌套 `BindRoot` 独立、模板内部件只进 `templates`；文本 key 必须存在；无时间戳；纯视觉属性不进契约 |
| 消费 | Hub `ui.contract`（目标）、规则 `ui.page-contract`/`ui.page-contract-refs`/`ui.page-contract-text`、CI、工程接线 |
| 版本 | `schemaVersion` 主.次；字段废弃先保留一个次版本；契约 diff＝UI API 变更，破坏性变更双方评审 |
| 现状 | 生成器与三规则为目标；未实现时显式“契约未接入”，新页面不得以“无契约”作为交接样板 |

**交接判据**：契约已生成且 `sourceHash` 与 prefab 一致；美术 lane 全绿；静态文案全部走 `LTextKeyRef` 且无 Lua 双写；`controls[]` 与工程接线一致；页面根 Canvas/CanvasGroup/GraphicRaycaster、SafeArea、图集/模板依赖通过；中/英/长文本/安全区截图齐全；Boot/PlayMode 可开、可点、可返回；视觉/行为双签。

### 10.3 `LTextKeyRef` 与静态文案

- **静态文案**（标题/按钮/标签/说明/空态）归美术，挂 `LTextKeyRef`；**动态文案**（数值/房间号/倒计时/账号）归工程，走 Lua `SetTextKeyArgs`/绑定；混合文案归工程。
- `LTextKeyRef` 字段仅 `Key`（`tbltext` key），不含参数/复数/富文本替换；节点须有 TMP/UGUI Text；一个节点最多一个；可与 `BindNode` 共存，但文案写入只归 `LTextKeyRef`。
- 运行时在页面打开/语言变更时按 key 写入，语言切换不重跑 `OnShow`；缺 key 开发期显示 `[key]`/发布期回退并报诊断；有 `LTextKeyRef` 的节点 Lua 禁止 `SetTextKey`/`SetText` 同一节点（`ui.page-contract-text` 拦截）。
- 编辑器 key 下拉读 `tbltext`，显示中/英预览；缺 key 报错；新 key 由美术提案、工程/本地化同批落库。
- 新页面默认 `LTextKeyRef`；存量页面未迁移继续走 Lua，但不得作为新页面交接样板。

### 10.4 交接流程与度量

```text
美术：起页/布局/切图/命名/标记 BindNode/挂 LTextKeyRef/套样式
  → 生成 UiPageContract
  → 跑美术 lane（命名/绑定/类型/Canvas/Raycaster/九宫格/key）
  → 截图：中/英/长文本/安全区
  → 工程：契约评审 + Lua/数据/导航接线
  → 契约引用对账（Lua/C# 名字必须是 controls 子集）
  → 布局预览（静态）→ Boot/PlayMode（真实链路）：可开、可点、可返回
  → 美术视觉签收 + 工程行为签收 → 合并；契约成为该页基线
```

| 首版目标 | 值 |
|---|---|
| 美术独立完成首版 | ≤1 个工作日（熟手 ≤4 小时） |
| 工程接线 | ≤0.5 个工作日 |
| 静态迭代 | <30s（编辑器布局/文案） |
| 运行时状态迭代 | <2min（Boot/联调或 PlayMode 看到真实状态；布局预览只验静态） |
| 美术改动导致的页面首开失败 | 连续两周 0 起 |
| 契约漂移/手改 | 0 起 |

**门**：契约 diff＝API diff；美术 lane 不绿不交接；截图是证据；一页一 owner；返工按“视觉→美术、接线→工程”分流，不以临时豁免合并。

### 10.5 工具映射与未落地项

- 编辑器 §9 的双 lane、`ui.contract`/`ui.art-lane`/`ui.page-scaffold` 是本章工具面；运行时深检见[UI 运行时调试](开发包UI调试面专项设计.md)。
- 未落地：`UiPageContract` 生成器、`LTextKeyRef` 组件/运行时接线、三条契约规则、`ui.art-lane`、美术起页/工程登记拆分；落地前按本章手工兜底，不得声称交接已完成。
