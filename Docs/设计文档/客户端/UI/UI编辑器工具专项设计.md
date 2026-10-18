# UI 编辑器工具专项设计

> 状态：现行专项设计；已有菜单与目标工具分列
> 版本：2.1
> 更新日期：2026-10-10
> Owner：客户端工具；运行时/API 契约由客户端 UI 负责
> 依赖：[UI框架总设计](UI框架总设计.md)、[制作规范](UI制作规范.md)、现有 BindNode/LiteCodeGen 与 Unity Pipeline
> 输入：Prefab、绑定标记、样式/文本配置、资源引用；输出：标准资产、生成代码与结构化校验报告

## 1. Current 与工具边界

已存在 [UiStyle/UiStyleTool](../../../../Assets/LiteGame/Editor/Style/UiStyleTool.cs)、视觉构建反例扫描器 [VisualConstructionScanner](../../../../Assets/LiteGame/Editor/VisualConstructionScanner.cs)、**UI 校验核心** [UiValidator](../../../../Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs)，以及运行时 [BindIndexBuilder](../../../../Assets/LiteClient/UI/Bind/BindIndexBuilder.cs)、[UIBindBase](../../../../Assets/LiteClient/UI/Bind/UIBindBase.cs)。完整标记工具、编排器、样式窗口和 CI 规则仍未交付。

> **2026-10-11 UI 工具链第一步交付**：**校验核心**落地（`Assets/LiteGame/Editor/UiTools/Validation/`）——统一报告模型 `UiReport/UiIssue{ruleId,severity,asset,node,message,fixHint}` + 规则端口 `IUiRule` + 双侧上下文 `UiValidationContext`（源码面 + 资产面）+ 核心 `UiValidator`（菜单 `LiteGame/UI/校验/校验 UI 资产` / CLI `-executeMethod …RunCli` 非零退出 / EditMode 三入口共用）。**首条规则＝渲染/适配族的视觉单一来源**（`Rules/RuntimeVisualRule`——包既有 `VisualConstructionScanner`，源码面）；**其余五族（绑定/资源/文本/行为/性能）在报告显式列为"待接入"，不冒充通过**（§4 明文）。后续步：绑定标记工具 → 样式对账扩 `Screens/` → 校验进 CI → 编排面板 → UIPatch。施工记录见 [UI工具链](../../../施工进度/UI线.md#ch4-UI工具链)。

> **2026-10-11 UI 工具链第二步交付（绑定标记工具·底层两项）**：**①词表单源** `UiTools/Naming/UiNamingWords.cs`（C# 常量表——《命名词表与控件清单》机器可读副本：角色词/控件段/模板段/`Tmp_`/语义词 + §9.2 文法正则单源 + `TryParse`/`IsKnownPrefix`）；**②绑定命名规则** `Rules/BindingNamingRule.cs`（`ui.binding-naming`，资产面）——判据：节点名过 §9.2 文法、前缀段属词表（未登记=Warning）、`_` 私有件（模板内合法/页面节点禁）、根名（`Page_`/`Hud_`/`Tmp_`，存量页面根=Warning 待页面批次）；**UGUI 契约件**（Slider fill/handle、ScrollRect viewport/content、Toggle graphic/target）与**模板实例内部件**按身份豁免。绑定族 `implemented=true`。
> **同步命名整改批**（§9.6"随模板批次整形"）：全仓 UI prefab 命名合规化——**模板/夹具根改文件名** `Tmp_<控件类型>`（27 件 Widgets + 6 件 Screens 夹具，`AssetDatabase.MoveAsset` 保 guid，实例不断链）；**内部件**词缀化（`OkButton→Button_Ok`、`Label→Txt_Label`、`Blocker→_Blocker`）；**克隆条目模板** `_Template/_ItemTemplate→Clone_Template/Clone_Item`（§9.6）；业务节点 `UIMain/BtnClose→Button_Close`（同批同步 `BindName`）。整改后规则 **0 Error**（仅 `UIMain` 根 Warning——§9.6 登记的页面根债，随页面批次）。`UIDemoPage.Templates[]` 随文件名同步。详见施工记录。



> **2026-10-16 工作台侧两件**：①**prefab 改动回写**（`LiteGame/Editor/UiTools/WriteBack/`——选中 prefab 实例枚举"实例 vs 源 prefab"逐属性差异〔来源两档：Unity 显式记录 / 数值差异〕，勾选后直写资产、Undo 覆盖、Play 内可用；结构面〔新增组件/子对象〕与场景对象引用显式拒绝；见 [UI工作台设计](UI工作台专项设计.md) §2 与操作手册 [08-UI工作台](../../../项目说明/08-UI工作台.md)）。②工作台工具条（场景内 dev 件，非本专项的工具窗口面）。

> **2026-10-10 代码现状订正**：原清单中的 `WidgetPrefabBuilder`（1477 行）与 `UICollectCheckMenu`、`WidgetDemoMenu` 已随提交 `45aa839`「输入服务余部与场景资源杂项（接管并行线）」**从仓库删除**，不再是"已存在"工具。`Assets/ShareResource/UI/Widgets/` 下 27 件 `Tmp_*` 模板 prefab 作为已产出资产保留；结构/行为断言改由 [VisualConstructionScanner](../../../../Assets/LiteGame/Editor/VisualConstructionScanner.cs) 与 `Assets/Tests/EditMode/VisualSingleSourceEditModeTests.cs` 承担。业务 UI 页面按《UI-U2》裁决走编辑器手工制作，不依赖已删除的确定性构建器。

工具服务标准资产和受控运行时：

- 画布、几何、层级、锚点和撤销使用 Unity Prefab Stage；不造第二套画布或运行时布局格式。
- 生成链沿用 Luban/LiteCodeGen/运行时绑定索引；产物可重建、可对账，不能由编辑器私有格式绕过制作校验。
- Odin 仅作 Editor 呈现适配，运行时标记用自有组件/attribute，不依赖 Sirenix。独立 Validator 是否可用按实际环境确认，校验核心必须能脱离付费面板在 CI 执行。
- Prefab/asset/meta 写入经 Unity/Pipeline API，遵循 `Docs/工具链/Unity操作规范.md`。批量改动有预览、Undo、目标清单与结果报告；不得覆盖用户未保存编辑内容。
- 状态、优先级、所有权、动效播放决定由运行时负责；工具只声明配置、预览和验证。运行时播放不以先经过编辑器面板为许可条件。

## 2. 绑定标记与生成

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

### 3.1 单源与职责

保留 `Editor/Style/UiStyle.cs` 颜色 token；文字样式作为独立语义档，不复制颜色值。运行时按状态改实例合法，样式对账只读盘上资产。

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

每条硬规则提供正例与违规负例。需要组件和序列化资产语义的规则不得塞进纯文本 DisciplineScanner。规则严重级别、例外和适用范围与制作规范一致；未实现的规则标识为待接入，不显示为自动通过。

预览应覆盖中英/伪本地化、长文本、安全区、字体缩放和 reduced motion。能在编辑态检出的结构错误提前失败；依赖 Awake/OnEnable 的行为用 PlayMode 证明。

## 5. 编排面板

**编排面板形态**：一个**宿主窗口**（`OdinEditorWindow`，菜单 `LiteGame/UI/编排/UI 编排面板`，落 `Assets/LiteGame/Editor/UiTools/Compose/`）＋四个分区——**绑定暴露 / 受控属性 / 校验·生成 / 控件库**。原文"四个可独立交付的面板"由**分区级可单交付**满足（未接入分区显式画"未接入"，不冒充通过）；不建四个窗口——四窗口会各自复制"Stage 跟随 / 显示根解析 / 预览作废"上下文（违"消除重复"）。

**依赖边界（Odin 只进 `LiteGame.Editor`）**：呈现层走 Odin——宿主窗口基类 `OdinEditorWindow`；**四个分区＝Odin `TabGroup` 页签**（`[OnInspectorGUI, TabGroup("Compose", <页签名>)]`；目标 prefab 字段与状态行置于页签组之上）；受控属性行由 **Odin 属性树**呈现（`PropertyTree.Create` + 白名单过滤 + `InspectorUtilities` 绘制对，写入/Undo/脏标记交属性树统一处理）；**控件库列表＝inline `OdinMenuTree`**（含 Odin 搜索栏；不用独立 `OdinMenuEditorWindow`——保持单宿主窗口），右侧为选中模板的组件/受控面预览与插入入口。页签体（绑定树/报告）走既有 IMGUI 绘制器，在 Odin 页签内混排。**分区逻辑类与运行时标记零 Sirenix**——`UiComposeTarget`（目标解析单源）、`UiWidgetPropertyCatalog`（白名单发现）、`UiWidgetLibrary`（目录即清单）、`UiWidgetInstaller`（Stage 内插入）均不依赖 Sirenix，可脱离 Odin 编译与测试（"摘除 Odin 只换绘制层"）。Odin 为**阻塞式环境项**（`scripts/gate/restore-check.ps1` §4、`toolchain.json` `paidOrRestrictedPlugins`），缺失即门禁失败。

**分区职责**

| 分区 | 交付面 |
|---|---|
| **绑定暴露** | 内嵌**只读**树（数据单源 `UiBindingRename.BuildTree`、绘制单源 `Binding/UiBindTreeView.cs`——与绑定标记工具同源）；**编辑动作（前缀预设/批量改名/预览）仍跳绑定标记工具窗口**，两处交互不合搬进宿主 |
| **受控属性** | 白名单＝自有控件组件上的**运行时 attribute** `UiWidgetProperty`（`Assets/LiteClient/UI/UiWidgetPropertyAttribute.cs`，零 Odin、只声明不驱动）。**未标注成员不进枚举、无绘制入口**——"不暴露控件内部实现"是物理边界而非按钮禁用；接线引用字段（如 `HpBar.Slider`）与几何/锚点（归 Prefab Stage 原生编辑）都不在此面 |
| **校验/生成** | 同一校验核心的窗口入口——**窄档**（资产面只喂本 prefab、源码面置空）与**全量档**（`Run()`）；报告列表化，行内"定位节点"经 `UiComposeTarget.FindNodeForReport` 反查（报告 node 含根名、绑定树口径不含根——两口径由该件收口）；**生成**＝`BindGenerator.Generate` 成功后同一步 `UiBindGenerationHash.Record`（窗口内先确认框——生成触发域重载） |
| **控件库** | **目录即清单**（扫 `Assets/ShareResource/UI/Widgets` 的 `Tmp_*`，不另存清单文件；内容源仍为《命名词表与控件清单》§1.3）；列表＝**inline OdinMenuTree 左树 + Odin 搜索栏**，右侧为选中模板的组件/受控面预览；插入**只在 Prefab Stage 内**（`InstantiatePrefab` + Undo，Variant/嵌套作用域全归 Unity 原生），插入后**不改名**、只提示下一步标记/命名（§5 原文：不以面板自动重名规则掩盖绑定冲突） |

**流程（沿用）**：打开页面 → 选控件模板 → 原生布局 → 标记/命名 → 样式和本地化预览 → 校验生成 → 真 Lua 页面验证。布局操作仍由 Unity Prefab Stage 提供；产物即标准 prefab（不存在"生成 prefab"这一步）。预算按可验证功能拆分，不以行数或天数估算作为交付承诺。

**本轮不做**：UIPatch 调试补丁（§6，随 U4 消费者）；控件库缩略图（RenderTexture）；第二套画布/几何编辑；模板新建/克隆；对象引用类值型受控属性（`Unsupported` 显式列出，不静默跳过）。

施工记录见 [UI工具链](../../../施工进度/UI线.md#ch4-UI工具链) 步 6。

## 6. 调试补丁与诊断

UIPatch 的版本、作用域、生产禁用和冲突规则由总设计第 10.3 节定义。工具提供差异预览、旧值/版本冲突报告、应用到标准 Prefab、Undo 和补丁归档；不创建第二套资产真相。

运行时诊断展示实例阶段/展示状态、层序、焦点/模态、操作 requestId、内容版本、缓存/租约/订阅/动画数和最近失败；从诊断跳转到页面定义和 Prefab。数据由服务快照提供，避免窗口自行遍历修改运行时状态。

## 7. 时间轴归属与实施顺序

技能/表现 Timeline 的自定义轨、导出和整数帧校验由 [动作与特效专项设计](../../游戏玩法/动作与特效专项设计.md) 负责，不属 UI 工具范围。UI 转场仍走运行时策略；非对局演出可按该专项使用 Timeline。时间轴编辑器不作为 UI U0/U1 的前置。

唯一排期在总设计第 13 节：基础校验/可观测性随 U0/U1；绑定/样式/预览/CI 随 U2/U3；完整编排器与 UIPatch 随 U4 的真实消费者。需要大批量修改模板时事先核对工作区改动并限定资产清单，不能用工具覆盖其他未完成修改。

验收产物包括生成差异、规则正负例结果、代表性页面 PlayMode 结果和性能记录。每个工具的“完成”要对应消费者和产物，不以菜单存在作为完成证明。
