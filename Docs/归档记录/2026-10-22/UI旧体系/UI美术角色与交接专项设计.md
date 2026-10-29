# UI 美术角色与交接契约

> 状态：现行专项设计；`UiPageContract` 生成器、`LTextKeyRef` 与美术 lane 为目标契约
> 版本：1.0
> 更新日期：2026-10-21
> 适用范围：UI 美术、UI TA、客户端 UI 工程的职责边界；页面契约 `UiPageContract`；静态文案所有权；交接、评审与验收
> Owner：客户端 UI / UI 美术
> 上位：[UI 框架总设计](UI框架总设计.md)
> 关联：[UI 制作规范](UI制作规范.md) · [UI 编辑器工具专项设计](UI编辑器工具专项设计.md) · [UI 控件 Lua API 参考](UI控件LuaAPI参考.md)

## 1. 定位与裁决

本文回答三件事：

1. UI 美术成为独立角色时，**负责什么、不负责什么**；
2. 美术与工程之间用什么接口交接——**页面契约 `UiPageContract`**；
3. **什么算交接完成**，以及如何度量。

本文不回答：UI 运行时生命周期与导航（归[UI 框架总设计](UI框架总设计.md)）、命名与资产硬规则（归[UI 制作规范](UI制作规范.md)）、工具实现细节（归[UI 编辑器工具专项设计](UI编辑器工具专项设计.md)）、工具链平台入口与清单（归[开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md)）、UI 技术栈选型（归 UI 框架总设计 §13.1）、批次进度与证据（归[施工进度/UI线](../../../施工进度/UI线.md)）。

### 1.1 关键裁决

| 裁决 | 内容 |
| --- | --- |
| **角色独立** | 美术不读 C#/Lua，也能完成一页的布局、视觉、静态文案、样式与切图，并自己跑“美术 lane”校验 |
| **交接接口** | 页面契约 `UiPageContract` 由预制体生成，是美术 → 工程的**唯一接口交接物**；截图是验收证据，不替代契约；不靠口头清单或散落的 Lua 字符串 |
| **契约真相** | 预制体是唯一真相；契约是**生成物**，禁手改；契约 diff 视为 UI 的 API 变更 |
| **文案所有权** | 静态文案用 `LTextKeyRef` 挂在文本节点，归美术；动态文案仍归 Lua，归工程；同一节点单写者 |
| **工程边界** | 美术不碰 Lua/数据/导航；工程不替美术改视觉；契约变更双方评审 |
| **验收边界** | 视觉验收归美术，行为验收归工程；两者都过，才算交接完成 |

### 1.2 “独立”的六条验收

角色独立的判据不是“会开 Unity”，而是下面六条全部成立，且不需要工程代劳：

| 能力 | 达标线 |
| --- | --- |
| 起页 | 从模板/基线页复制，独立完成布局、锚点、安全区、九宫格、切图；不写代码 |
| 文案 | 静态文案自己挂 key，能切换中/英/长文本查看；不改 Lua |
| 暴露 | 自己标记 `BindNode` 并生成契约；不手写 C#/Lua 字符串 |
| 样式 | 文字档、颜色、交互状态按 token/档位套用；不逐节点手调 |
| 校验 | 自己跑美术 lane，看得懂错误并修复常见项；不等 60s 全量扫描 |
| 迭代 | 静态布局 <30s 看到；运行时状态 <2min 看到；不等完整启动链 |

> 本文的“美术 lane”指只扫美术改动 prefab、面向美术错误文案的快速校验通道；全量校验仍在夜间门禁。它是美术的提交前自检，不替代 L2/L4 的行为与真机验收。

六条缺一条，角色就仍会退化成“帮工程摆控件的助理”。

## 2. 角色模型

### 2.1 角色定义

| 角色 | 职责 | 不负责 |
| --- | --- | --- |
| **UI 美术（视觉/制作）** | 页面布局、视觉层级、切图与九宫格、静态文案 key、文字/颜色样式、暴露控件标记、页面契约生成、美术 lane、视觉验收 | Lua/数据/导航；运行时生命周期；性能预算裁决 |
| **UI TA（可由美术兼）** | 颜色/文字 token、图集、材质/Shader、动效规范、性能与资源预算；工具反馈 | 业务逻辑；页面数据 |
| **客户端 UI 工程** | 页面数据/逻辑/导航接线、契约消费与评审、绑定引用、行为验收、工具实现 | 视觉细节的最终裁决；绕过美术直接改视觉 |
| **主美/制作人** | 视觉方向、优先级、跨页一致性 | 日常契约字段级决策 |

小型团队可以“UI 美术 + UI TA”一人兼，但**契约的生产者与消费者必须分开**：美术生产，工程消费；不能一个人既改预制体又改 Lua 引用而不经契约对账。

### 2.2 RACI

R = 执行，A = 最终负责，C = 咨询，I = 知会。

| 事项 | UI 美术 | 客户端 UI 工程 | UI TA |
| --- | --- | --- | --- |
| 布局/视觉/切图/九宫格/安全区 | **A/R** | C | C |
| 静态文案 key/中英/长文本 | **A/R** | C | I |
| 暴露控件/页面契约 | R | **A** | I |
| 数据/逻辑/导航/绑定消费 | C | **A/R** | I |
| 颜色/文字样式/图集/Shader | R | C | **A** |
| 动效/粒子/音效表现 | **A/R** | C | C |
| 视觉验收 | **A/R** | C | C |
| 行为验收 | C | **A/R** | I |
| 性能预算 | C | C | **A/R** |

主美/制作人不在日常 RACI 内，按视觉方向、优先级与跨页一致性介入；契约字段级争议由 UI 美术与客户端 UI 工程按上表裁决。

### 2.3 归属边界

| 面 | Owner | 说明 |
| --- | --- | --- |
| `Assets/ShareResource/UI/{Screens,Widgets,Icons}` | UI 美术 | 页面、模板、图标；提交走 Unity Pipeline |
| `Assets/ShareResource/UI/Hud` | UI 美术 + 工程 | HUD 属对局表现域，视觉归美术、接线归工程 |
| `Assets/ShareResource/Fonts`、图集、UI 材质/Shader | UI TA | 字体 fallback、图集分组、Shader 能力验证 |
| `Assets/LiteClient/UI/**` | 客户端 UI 工程 | 运行时框架与控件 |
| `Assets/LiteGame/Lua/UI/**` | 客户端 UI 工程 | 页面逻辑与动态文案 |
| `Assets/LiteGame/Editor/UiTools/**` | 客户端 UI 工程 | 制作主窗口、新建页、契约生成与校验工具 |
| `Assets/LiteGame/Editor/UiTools/Contracts/**` | 美术生成、双方评审 | 生成物；美术不手改（只经生成器刷新），工程不静默改 |
| `tbuiform` / `tbltext` 表源 | 工程/本地化落库 | 美术提页面名/层级/全屏意图与 key/文案；`formId` 分配与表源落库归工程 |
| `scripts/gate/toolchain.json`（`ui.*` 条目） | 工具链平台 + 客户端 UI 工程 | UI 条目由 UI 工程维护、平台校验；新增 UI 工具不另建菜单/第二索引 |

配套纪律：

- 一个 prefab 同一时间只有一个 owner；改动小步提交，避免 prefab 冲突。
- 视觉改动未经美术评审不合并；契约/绑定改动未经工程评审不合并。
- 美术不直接改 `tbuiform`/`tbltext` 的表源文件；key 与文案经契约/表流程落库。
- **`ui.page-new`（新建页向导）同时创建 prefab + Lua + tbuiform 行，属工程接入动作**；美术起页只复制 `Screens` 视觉基线并改视觉，不执行该向导的登记步骤。角色化拆分（美术只建 prefab / 工程登记）是目标项（§7）。

### 2.4 能力门槛

招聘或转岗按“能独立交付一页”筛，不按“会写代码”筛：

- 必须：UGUI/Prefab、锚点与布局、九宫格、图集与切图、TMP、基础本地化、Git/Unity Pipeline、能读校验报告。
- 加分：DOTween/动效、Shader/材质、性能 Profiler、基础 C#（只读不写）。
- 面试题：给一份模板与文案表，独立做一页并通过美术 lane；不要求写 Lua。

### 2.5 禁止事项

- 美术改 Lua/C#、表数据、导航或运行时服务。
- 工程未经美术评审改视觉、字号、间距、颜色、切图。
- 任何人手改 `UiPageContract` 生成物。
- 绕过美术 lane 直接交接；以“先合再补”替代校验。
- 把动态文案塞进 `LTextKeyRef`，或把静态文案继续写在 Lua 里形成双源。

## 3. 页面契约 `UiPageContract`

### 3.1 为什么需要

当前页面接口是**隐式**的：Lua 用字符串引用预制体上的名字——`self.ui:SetTextKey("Txt_Title", ...)`、`self.ui:OnButton("Button_Close", ...)`（例见 [Page_Settings.lua](../../../../Assets/LiteGame/Lua/UI/Page_Settings.lua)）。预制体节点名、`BindNode.BindName`、Lua 字面量三处靠人同步（[UIBindIndex.cs](../../../../Assets/LiteClient/UI/Bind/UIBindIndex.cs)）。

后果是角色不独立：

- 美术不知道哪些控件会被逻辑消费，只能等工程说“这个要暴露”；
- 工程不知道美术改了哪些暴露面，改名/删控件往往到打开页面时才炸；
- 没有一份可 diff 的交接物，评审只能看截图和口头清单。

`UiPageContract` 把这份隐式接口变成**从预制体生成的显式产物**。

### 3.2 定义与存储

`UiPageContract` 是**只读页面接口描述**，包含：页面元数据、暴露控件、静态文案、文字样式、模板依赖、源指纹。

| 项 | 约定 |
| --- | --- |
| 真相 | UI 预制体（`BindNode`/`LTextKeyRef`/`UiTextStyleRef`/嵌套 `Tmp_*`）+ `tbuiform` 行 |
| 产物 | `Assets/LiteGame/Editor/UiTools/Contracts/<PageName>.contract.json` |
| 机器定义 | [UiPageContract.schema.json](UiPageContract.schema.json)（JSON Schema 2020-12） |
| 运行时 | **不进包、不进运行时**；只服务编辑器、校验器与 CI |
| 写入 | 生成器写；禁手改；生成动作在编辑器显式触发 |
| 入库 | 是；可 diff、可评审；契约 diff 视为 UI API 变更 |

命名与存储遵循“生成物集中一处”：不把契约散到 `Screens/` 或 `Widgets/`，避免被 YooAsset 收集组误收。

### 3.3 字段模型

机器可读定义以 [schema](UiPageContract.schema.json) 为准；下表解释语义与来源，不重复类型细节。

| 段 | 字段 | 来源 | 语义 |
| --- | --- | --- | --- |
| 根 | `schemaVersion` | 生成器 | 契约结构版本，`1.x`；主版本不兼容 |
| 根 | `sourceHash` | 生成器 | `schemaVersion` + 页面接口面（`page`/`controls`/`text`/`styles`/`templates`）的规范化 SHA-256；漂移判据 |
| `page` | `formId` | `tbuiform.id` | 页面 id；与导航/表一致 |
| `page` | `prefabPath` / `prefabGuid` | 预制体 | 资产路径与 GUID；GUID 用于改名跟踪 |
| `page` | `luaPath` | `tbuiform.lua_path` | 逻辑表路径；纯展示页为空 |
| `page` | `layer` / `fullScreen` | `tbuiform.layer/full_screen` | 语义层与全屏；与运行时一致 |
| `page` | `cacheStrategy` | `tbuiform`（目标列） | `Lru`/`Resident`/`DestroyOnClose`；缺省 `Lru` |
| `controls[]` | `bindName` | `BindNode.BindName` | 暴露名；页面内唯一；与节点名同文（§9.1） |
| `controls[]` | `nodePath` | 预制体层级 | 相对根的 `/` 路径；根自身不是控件 |
| `controls[]` | `type` | `BindNode.ResolvedTypeName` | 原样记录：组件全名，或 `GameObject`/`Transform` 引用；wrapper 按实际类型 |
| `controls[]` | `kind` | 生成器派生 | `Button`/`Text`/`Image`/`Slider`/`Toggle`/`Dropdown`/`InputField`/`List`/`Other` |
| `text[]` | `nodePath` / `key` | `LTextKeyRef` | 静态文案：文本节点与 `tbltext` key |
| `styles[]` | `nodePath` / `role` | `UiTextStyleRef` | 文字角色档；颜色 token 后续扩展 |
| `templates[]` | `templateName` / `prefabPath` / `bindingHash?` | 嵌套 `Tmp_*` 实例 | 页面用到的模板及可选绑定面 hash |
| — | `generator` | 生成器 | 工具名与版本；便于复盘与兼容 |

### 3.4 生成规则

1. **确定性**：深度优先、同级按层级序；同一输入重复生成必须字节级一致（不含时间戳）。
2. **唯一性**：只收非空 `BindName`（空名只参与代码生成，不进契约）；`bindName` 页面内唯一；重名 = 生成失败，不产出半份契约。
3. **类型可解析**：`type` 为空/`Transform` 兜底时按“未解析”报错；wrapper 类型（如 `UIDropdownWrap`）按实际类型写入，不做二次猜测。
4. **作用域**：`Clone_` 条目模板子树不暴露；嵌套 `BindRoot` 是独立生成单元，不在本页契约展开；模板内部件不进 `controls`/`text`/`styles`，只进 `templates`。
5. **文案 key 必须存在**：`LTextKeyRef.Key` 不在 `tbltext` 中 = 生成失败；不静默产出 `[key]` 契约。例外按《UI 制作规范》§1 的例外表登记。
6. **存量命名**：不合 §9.2 的页面（如 `UIMain` 的 `BtnClose`）生成报错；按 §9.6 整改后再纳入契约，不做静默降级。
7. **无时间戳**：契约不写 `generatedAt`；漂移由 `sourceHash` 判定，避免每次生成产生噪声 diff。
8. **只收接口面**：颜色、尺寸、锚点、贴图、九宫格 border 等**纯视觉属性不进契约**——契约描述“谁被暴露、文案键是什么、用了哪些模板”，不是视觉快照。视觉改动不应触发契约 diff；只有接口面变化（控件/文案/样式角色/模板依赖）才触发。

`sourceHash` 的规范化口径：对 `schemaVersion` + `page` + `controls` + `text` + `styles` + `templates` 按固定字段序序列化（键排序、UTF-8、无空白差异），去掉 `sourceHash` 自身后取 SHA-256；生成器与校验器必须共用同一实现，禁止两套拼接口径。

### 3.5 消费面

| 消费者 | 用途 | 失败语义 |
| --- | --- | --- |
| Hub“交接”页（目标 `ui.contract`） | 展示本页暴露面、缺失项、契约 diff；生成/刷新入口 | 缺契约/漂移 = 阻断交接 |
| 校验器 `ui.page-contract` | schema 校验、`sourceHash` 对账、唯一性/类型/key 存在性 | Error；按 `{ruleId, asset, node, message, fixHint}` 报告 |
| 校验器 `ui.page-contract-refs` | Lua/C# **受控 API 的控件名参数**必须是契约 `controls` 的子集；删/改名必须同批改引用（文案 key 另走 `text[]` 对账） | Error；定位到引用文件与行 |
| 校验器 `ui.page-contract-text` | 节点有 `LTextKeyRef` 时，Lua 不得再 `SetTextKey` 同一节点；反之亦然 | Error；单写者违例 |
| CI | 契约 diff 评审；`sourceHash` 漂移红；破坏性变更需双方签收 | 阻断合并 |
| 编辑器/PlayMode 开发期 | 对 `SetTextKey`/`OnButton` 名字做契约对账（读工程内契约，不随包） | 开发期报错；发布以 CI 为准 |
| 客户端工程 | 按契约接线，不再从 prefab 猜名字 | 缺字段 = 接线阻塞 |

`ui.page-contract*` 为目标准入规则；规则落点、报告模型与三入口（菜单/CLI/EditMode）归[UI 编辑器工具专项设计](UI编辑器工具专项设计.md)。

### 3.6 版本与兼容

- `schemaVersion` 用 `主.次`：主版本不兼容（消费者拒绝未知主版本）；次版本只做向后兼容的字段追加。
- 字段废弃：先保留一个次版本并标 `deprecated`，再在下个主版本删除；生成器与校验器同批升级。
- 契约变更分级：
  - **兼容**：新增控件/文案/模板、类型收窄到更具体类型；可直接合。
  - **破坏**：删/改名 `bindName`、删 key、改 `kind`、改 `nodePath` 语义；必须同批改 Lua/C# 引用并双方评审。
- 存量页面：未生成契约的页面不阻塞运行，但**不得作为新页面的交接样板**；迁移按页面批次逐个纳入。

## 4. 静态文案所有权

### 4.1 静态与动态

| 类型 | 例子 | Owner | 载体 |
| --- | --- | --- | --- |
| 静态文案 | 标题、按钮、标签、说明、空态提示 | UI 美术 | `LTextKeyRef`（预制体） |
| 动态文案 | 击杀数、房间号、版本号、账号、倒计时 | 客户端 UI 工程 | Lua `SetTextKeyArgs` / 绑定 |
| 混合文案 | “共有 3 人”这类带运行时参数的句子 | 工程 | Lua（参数与复数归逻辑） |

判据：**文案内容不随本局数据变化 → 静态；随数据变化 → 动态**。不要把动态文案硬塞进 `LTextKeyRef`。

### 4.2 `LTextKeyRef` 契约

`LTextKeyRef` 是挂在文本节点上的声明组件（与 [UiTextStyleRef.cs](../../../../Assets/LiteClient/UI/Style/UiTextStyleRef.cs) 同型：只声明、不驱动）：

| 项 | 约定 |
| --- | --- |
| 字段 | `Key`（`tbltext` key，`UI.*` / `Common.*`）；不含参数、不含复数、不含富文本替换 |
| 节点 | 必须同时有 `TMP_Text` 或 `UGUI Text`；一个节点最多一个 `LTextKeyRef` |
| 暴露 | 节点可以同时有 `BindNode`（供逻辑改可见性/交互），但文案写入只归 `LTextKeyRef` |
| 运行时 | 页面打开与语言变更时按 key 写入；语言变更**不重跑** `OnShow`、不重建页面；缺 key 开发期显示 `[key]`，发布期回退源语言并报诊断 |
| 单写者 | 有 `LTextKeyRef` 的节点，Lua 不得再 `SetTextKey`/`SetText` 同一节点；校验器 `ui.page-contract-text` 拦截 |
| 编辑器 | key 下拉读 `tbltext`，显示中/英预览；缺 key 报错；提供长文本/伪本地化预览；新 key 由美术提案、工程/本地化同批落库，未落库前标缺 |

运行时接线建议：由 `UIForm` 在 `ApplyLocale` 时扫描本页 `LTextKeyRef`，经 `ILocalizationService` 写文本；语言变更复用同一条链。不要新增第二套“页面级文案服务”。

### 4.3 迁移策略

- 先选一页做样板（建议设置页，静态文案密集），把静态 key 从 Lua 搬到 `LTextKeyRef`；动态 key 留在 Lua。
- 同一节点同一批完成“删 Lua 写入 + 加组件”，不允许双源过渡超过一个批次。
- 存量页面按页面批次迁移；未迁移页面继续走 Lua，但新页面必须以 `LTextKeyRef` 为默认。
- 迁移后用契约 `text[]` 对账：Lua 里不应再出现该页静态 key 的 `SetTextKey`。

### 4.4 例外

- 需要运行时参数、复数、富文本片段替换的文案 → 动态，走 Lua。
- 需要按状态改文案（如“已装备/装备”）→ 由逻辑写 key 或写文本，不归静态。
- 图片内文字、Logo、艺术字 → 不走 `LTextKeyRef`；按图片资产与本地化图片规范处理（后续扩展）。

## 5. 交接流程

### 5.1 主流程

```text
美术：起页/布局/切图/命名/标记 BindNode/挂 LTextKeyRef/套样式
  → 生成 UiPageContract
  → 跑美术 lane（命名/绑定/类型/Canvas/Raycaster/九宫格/key）
  → 截图：中/英/长文本/安全区
  → 工程：契约评审 + Lua/数据/导航接线
  → 校验器：契约引用对账（Lua/C# 名字必须是契约子集）
  → 布局预览（静态）→ Boot/PlayMode（真实链路）：页面可开、可点、可返回
  → 美术视觉签收 + 工程行为签收
  → 合并；契约成为该页基线
```

### 5.2 交接清单

| # | 检查项 | Owner | 证据 |
| --- | --- | --- | --- |
| 1 | 契约已生成、`sourceHash` 与预制体一致 | 美术 | 契约文件 + 生成日志 |
| 2 | 美术 lane 全绿（命名/绑定/类型/Canvas/Raycaster/九宫格/key） | 美术 | 校验报告 |
| 3 | 静态文案全部走 `LTextKeyRef`，无 Lua 双写 | 美术 + 工程 | `ui.page-contract-text` 报告 |
| 4 | 暴露控件与工程接线清单一致 | 美术 + 工程 | 契约 `controls[]` diff |
| 5 | 页面根有 Canvas/CanvasGroup/GraphicRaycaster，射线策略符合规范 | 美术 | 校验报告 + prefab 检查 |
| 6 | 九宫格 border、图集/模板依赖已声明 | 美术 + TA | 资产检查 |
| 7 | 中/英/长文本/安全区截图齐全 | 美术 | 截图附件 |
| 8 | Boot/PlayMode 可开页、无绑定/加载错误；关键交互可点（布局预览只验静态） | 工程 | Boot/PlayMode 记录 / 用例 |
| 9 | 契约破坏性变更已双方评审 | 双方 | PR 评审记录 |
| 10 | 视觉签收 + 行为签收 | 双方 | 交接记录 |
| 11 | 新增/变更工具已进 `toolchain.json`，从 Hub/CLI 可达；未登记不建第二菜单 | 客户端 UI 工程 | `toolchain.py check` + [工具索引](../../../说明/工具索引.md) |

### 5.3 评审门

- **契约 diff = API diff**：删/改名控件、删 key、改类型，必须双方评审；禁止“先合再补”。
- **美术 lane 必须绿**：不绿不交接；不把“已知问题”带进工程接线。
- **截图是证据**：没有中/英/长文本/安全区截图，不算完成视觉自检。
- **一页一 owner**：同一 prefab 同时只有一个 owner；改动小步提交，减少 prefab 冲突。
- **禁止手改契约**：漂移由生成器修复；手改视为绕过门禁。

### 5.4 返工与变更

- 交接后发现视觉问题 → 美术改预制体 → 重新生成契约 → 重跑 lane；若 `controls`/`text` 变化，按契约变更流程重评。
- 交接后发现接线问题 → 工程改 Lua/数据；不得反向要求美术改视觉来“适配”逻辑。
- 破坏性契约变更（删/改名/改类型）必须同批改引用；CI 红时不允许以“临时豁免”合入，除非按《UI 制作规范》§1 登记例外并给出失效条件。

## 6. 验收与度量

### 6.1 角色验收

以“一页标准页”为验收单位：15–30 个节点、5–10 个暴露控件、10–20 条静态文案、含至少一个列表/弹窗/输入控件中的两类。

| 项 | 首版目标 |
| --- | --- |
| 美术独立完成首版 | ≤1 个工作日（熟手 ≤4 小时） |
| 工程接线 | ≤0.5 个工作日 |
| 静态迭代循环 | <30s（编辑器内看到布局/文案变化） |
| 运行时状态迭代 | <2min（Boot/联调或 PlayMode 看到真实状态；布局预览只验静态） |
| 因美术改动导致的页面首开失败 | 连续两周 0 起 |
| 契约漂移/手改 | 0 起 |

目标值随真实项目调整；调整必须记录在本文版本元数据与[施工进度/UI线](../../../施工进度/UI线.md)，不在本文写批次状态。

### 6.2 度量口径

| 指标 | 定义 | 用途 |
| --- | --- | --- |
| 独立完成时间 | 从拿到需求到提交交接清单的工时 | 角色是否成立 |
| 工程接线时间 | 从契约冻结到页面可联调的工时 | 接口是否清晰 |
| 页面首开成功率 | 真包/真机打开成功次数 ÷ 尝试次数 | 绑定/资源是否可靠 |
| 美术 lane 误报率 | 被人工判为误报的规则数 ÷ 总报告数 | 校验是否可用 |
| 契约漂移 | 预制体改了但契约未重生成的次数 | 流程是否被执行 |
| 返工率 | 交接后因契约/视觉问题退回的次数 ÷ 交接次数 | 交接质量 |
| 迭代循环 | 改一处视觉到看到结果的时间 | 工具链效率 |

反指标：不以“面板数量、规则数量、文档页数”作为角色成熟度证据。

## 7. 目标能力与依赖

| 能力 | 目标 | 依赖 | 优先级 |
| --- | --- | --- | --- |
| 页面契约生成/校验 | 一页一份 JSON；`sourceHash` 漂移门禁；契约引用对账 | 生成器、schema、`ui.contract` 页、`ui.page-contract*` 规则 | P0 |
| `LTextKeyRef` + key picker | 静态文案归美术；中/英/长文本预览 | 文本表、`ILocalizationService`、编辑器 picker | P0 |
| 美术 lane | 只扫改动 prefab、<10s、中文提示、常见项一键修 | 校验核心、`ui.art-lane` 动作、增量扫描、自动修复 | P0 |
| 工具链平台接入 | UI 工具全部经 Hub/CLI 清单可达；窗口声明 `safety`/变更摘要；动作走统一报告 | `toolchain.json`、`toolchain.py`、`EditorToolCatalogEditModeTests` | P0 |
| 模板缩略图 | 控件库可看图选件 | `PreviewRenderUtility`/RenderTexture、缓存 | P1 |
| ~~工作台可回写实例~~ | 专用回写窗口已退役；Play 内调参走 Prefab Stage/Unity 原生 Overrides | — | — |
| 角色化起页/接入拆分 | 美术起页只建 prefab；工程写 Lua/tbuiform 并登记 | `ui.page-new` 双入口或角色门控 | P0 |
| 切图/图集管线 | 导入预设、九宫格 border 校验、图集分组 | TextureImporter 预设、SpriteAtlas、收集组 | P1 |
| 动效 binder | 美术可声明式挂动效，不写 C#/Lua | 效果系统 E1、模板 prefab | P1 |

这些能力落地后，本文的六条验收才具备稳定支撑；在此之前，角色可以起步，但会保留人工兜底。

## 8. 与现有文档的边界

| 主题 | 权威 |
| --- | --- |
| 运行时生命周期、导航、层级、输入锁 | [UI 框架总设计](UI框架总设计.md) |
| 命名文法、目录、Prefab/资产硬规则 | [UI 制作规范](UI制作规范.md) |
| 绑定工具、样式工具、校验核心、编排面板 | [UI 编辑器工具专项设计](UI编辑器工具专项设计.md) |
| 样式 token/主题/引用/应用 | [UI 样式系统专项设计](UI样式系统专项设计.md) |
| 工具入口、清单、Hub/CLI、生成索引 | [开发工具链平台专项设计](../../../设计文档/架构/开发工具链平台专项设计.md) |
| UI 域形态（入口/命名/结果/写盘/文档五条基线） | [工具链域形态基线专项设计](../../../设计文档/架构/工具链域形态基线专项设计.md) |
| UI 制作主窗口、新建页、布局预览 | [UI 编辑器工具专项设计 §8](UI编辑器工具专项设计.md#8-制作流水线单页制作主线) |
| UI 美术/工程双 lane、契约优先、美术 lane | [UI 编辑器工具专项设计 §9](UI编辑器工具专项设计.md#ch9-ui-authoring) |
| UI 运行时调试（深检/页面目录/可点性/证据） | [开发包 UI 调试面专项设计](开发包UI调试面专项设计.md) |
| Lua 受控 API 语义 | [UI 控件 Lua API 参考](UI控件LuaAPI参考.md) |
| 页面契约字段与交接规则 | **本文 + [schema](UiPageContract.schema.json)** |
| 批次进度与证据 | [施工进度/UI线](../../../施工进度/UI线.md) |

冲突时以代码、配置和可复现测试为准；本文随契约实现同步修订。本文不复制其他文档的规则正文，只引用唯一权威。

## 9. 工具链平台接缝

UI 工具已纳入 `scripts/gate/toolchain.json` 的 `ui` 域：Hub（`LiteGame/工具链`）与 `python scripts/toolchain.py` 读同一份清单，`Docs/工具链/工具索引.md` 是清单的生成投影。UI 域形态（入口/命名/结果/写盘/文档五条）以[工具链域形态基线专项设计](../../../设计文档/架构/工具链域形态基线专项设计.md) §3.2 为准；本文只裁角色、页面契约与交接，不另建入口。

### 9.1 入口与权威

UI 工具按两组接入：制作/新建页（`window`）、校验/样式/hash（`action`）。**ID、形态、入口、写面与安全声明**以 `scripts/gate/toolchain.json`、[工具索引](../../../说明/工具索引.md) 与 [UI 编辑器工具专项设计 §1.1](UI编辑器工具专项设计.md) 为准；本文不复制完整表。

美术侧只从 Hub 打开工具（`python scripts/toolchain.py open <id>` 为深链），不直接跑编辑器脚本、不新增菜单。

### 9.2 角色侧规则

1. **交接页**：目标 `ui.contract`（`page`）；契约展示/生成/校验/diff 在 Hub 内完成，不单开窗口。
2. **美术 lane**：目标 `ui.art-lane`（`action`）；只扫改动 prefab，报告进统一结果区。
3. **窗口**：`ui.compose`/`ui.page-new` 保持外部窗口；美术不要求工程重写成页件；关闭后看变更摘要与可回滚项。
4. **运行时调试**：工程用 `ui.runtime-debug`（编辑器 Play 深检）与 F8 运行内面板（见[开发包 UI 调试面专项设计](开发包UI调试面专项设计.md)）；美术只读查看视觉/安全区/可点性，不改运行时/资产。
5. **工作台（已退役）**：不再有运行内 dev 场景/开发页入口；布局预览是编辑器场景（布局档），真链路走 Boot/联调或 PlayMode。
6. **新增工具**：按 [UI 编辑器工具专项设计 §1.1](UI编辑器工具专项设计.md) 的清单 + handler + 对账流程；不建第二菜单。

### 9.3 目标工具

| 目标 | 形态 | 登记要求 |
| --- | --- | --- |
| `ui.contract`：交接页（契约展示/生成/校验/diff） | `page` | `plane=editor`、`purpose=dev`、`writes=generated`、`outputs=Assets/LiteGame/Editor/UiTools/Contracts/`、`safety` 按 §3.2 声明；有页件与 handler 才登记 |
| `ui.art-lane`：美术改动集快速校验 | `action` | `plane=editor`、`purpose=dev`、`writes=none`；报告进统一结果区 |
| `ui.page-scaffold`：美术起页（仅 prefab） | `window` | `plane=editor`、`purpose=dev`、`writes=asset`；与工程 `ui.page-new` 登记分离 |
| `ui.runtime-debug`：编辑器 Play 深检 | `window` | `plane=runtime`、`requires=play/player`、`writes=none`；深检只读，F8 面板同内核 |

登记前先用现有 `ui.validate`/`ui.compose` 承载，不建空壳工具或临时菜单。`ui.contract`/`ui.art-lane` 是本文的目标 ID；实际 id 以清单为准，改名须同批改本文与工具索引。

### 9.4 验证入口

```powershell
python scripts/toolchain.py check          # 清单/注册面/域规则/文档新鲜度
python scripts/toolchain.py docs --check   # 工具索引与清单一致
python scripts/toolchain.py open ui.compose
python scripts/gate/test.py --lane Doc     # 含工具索引检查
```

`EditorToolCatalogEditModeTests` 对账 handler 1:1；UI 工具改动必须同时过 `check` 与 `docs --check`，否则视为清单漂移。

## 附录 A：契约示例

以下为**示意**，节点路径与字段值不是资产快照；生成器输出以实际预制体为准。

```json
{
  "schemaVersion": "1.0",
  "generator": {
    "name": "UiPageContractExporter",
    "version": "0.1"
  },
  "page": {
    "formId": 15,
    "prefabPath": "Assets/ShareResource/UI/Screens/Page_Settings.prefab",
    "prefabGuid": "0123456789abcdef0123456789abcdef",
    "luaPath": "UI.Page_Settings",
    "layer": 1,
    "fullScreen": true,
    "cacheStrategy": "Lru"
  },
  "controls": [
    {
      "bindName": "Button_Close",
      "nodePath": "Cot_Header/Button_Close",
      "type": "UnityEngine.UI.Button",
      "kind": "Button"
    },
    {
      "bindName": "Dropdown_Quality",
      "nodePath": "Cot_Body/Dropdown_Quality",
      "type": "LiteGame.UI.UIDropdownWrap",
      "kind": "Dropdown"
    },
    {
      "bindName": "Slider_Volume",
      "nodePath": "Cot_Body/Slider_Volume",
      "type": "UnityEngine.UI.Slider",
      "kind": "Slider"
    },
    {
      "bindName": "Toggle_Sound",
      "nodePath": "Cot_Body/Toggle_Sound",
      "type": "UnityEngine.UI.Toggle",
      "kind": "Toggle"
    }
  ],
  "text": [
    { "nodePath": "Cot_Header/Txt_Title", "key": "Common.Settings" },
    { "nodePath": "Cot_Body/Txt_Volume", "key": "UI.Settings.Volume" },
    { "nodePath": "Cot_Body/Txt_Quality", "key": "UI.Settings.Quality" }
  ],
  "styles": [
    { "nodePath": "Cot_Header/Txt_Title", "role": "Title" },
    { "nodePath": "Cot_Body/Txt_Volume", "role": "Body" }
  ],
  "templates": [
    {
      "templateName": "Tmp_StateButton",
      "prefabPath": "Assets/ShareResource/UI/Widgets/Tmp_StateButton.prefab"
    }
  ],
  "sourceHash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
}
```

### A.1 `kind` 映射

| `kind` | 典型类型 |
| --- | --- |
| `Button` | `UnityEngine.UI.Button`、`StateButton` 模板 |
| `Text` | `TMPro.TMP_Text`、`UnityEngine.UI.Text` |
| `Image` | `UnityEngine.UI.Image`、`RawImage` |
| `Slider` | `UnityEngine.UI.Slider`、`UISliderWrap` |
| `Toggle` | `UnityEngine.UI.Toggle`、`UIToggleWrap` |
| `Dropdown` | `UnityEngine.UI.Dropdown`、`UIDropdownWrap`、`TMP_Dropdown` |
| `InputField` | `UnityEngine.UI.InputField`、`TMP_InputField`、`UIInputFieldWrap` |
| `List` | `VirtualList`、`SimpleListView` |
| `Other` | 其他可暴露组件 |

映射由生成器维护；新增 wrapper 必须同时更新映射与 `BindNode` 类型解析，不允许“落到 Image 也算过”。

## 附录 B：生成器接口（目标）

生成器是编辑器工具，不进入运行时；建议入口：

```csharp
// 目标接口，非现行 API
UiPageContract Generate(GameObject prefabRoot, cfg.uiform row);
UiPageContractValidation Validate(string contractPath, GameObject prefabRoot);
```

- `Generate`：只读预制体，产出契约对象；序列化由调用方统一写盘。
- `Validate`：schema + `sourceHash` + 唯一性/类型/key 存在性；返回结构化问题，不抛穿。
- 生成与校验共用同一套遍历/序列化实现，禁止两套口径。

工具落点归[UI 编辑器工具专项设计](UI编辑器工具专项设计.md)；本文只定义契约与交接语义。
