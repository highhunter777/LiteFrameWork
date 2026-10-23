# UI 运行时调试工具专项设计（uGUI Runtime Inspector）

> 状态：现行专项设计（目标形态；uGUI Runtime Inspector 为目标；默认只读 + 显式写回）
> 版本：2.0
> 更新日期：2026-10-22
> 归属：[GM 与调试工具分层专项设计](../../../设计文档/架构/GM与调试工具分层专项设计.md#7-统一内核与三投影)（统一内核与三投影）
> 定位：回答“人在 development Player / 编辑器 Play 里如何 **inspect 运行时 UI**、把目标 UI 带到目标状态、验证可点性与遮挡、把运行时发现的问题以属性级 patch 落回标准 Prefab、留下可复盘的证据”。本文定义 UI 能力面的读模型、命令集、两个投影（编辑器深检窗口 + F8 运行内面板）以及编辑器写回模式，不定义通用控制台或产品 UI 契约。
> 不做的事：不替代 L2/L4 自动化（归[《UI 测试开发专项设计》](UI测试开发专项设计.md)）；不让 DevHUD 承载写操作（归[《DevHUD 演进路线图》](../../../设计文档/测试与性能/DevHUD演进路线图.md)）；不做编辑器内的布局制作与预览（归[《UI 编辑器工具专项设计》](UI编辑器工具专项设计.md)与[08-UI 制作与预览](../../../项目说明/08-UI工作台.md)）；**产品 UI 不迁移 UI Toolkit，但运行时调试工具自身允许使用 UI Toolkit**；不做结构级资产编辑（增删节点/组件、创建/删除资产——归 Prefab Stage/Unity Pipeline）；不做第二套资产真相或独立 patch 格式；不做 release/测试者分发包写回；不裁决通用 Player 控制台/世界查看器的载体（归[《调试与开发工具链演进路线图》](../../../设计文档/测试与性能/调试与开发工具链演进路线图.md) T1 决策点）。
> 冲突裁决：写口/投影纪律以 GM 分层 §7 为准；UI 生命周期与导航契约以[《UI 框架总设计》](UI框架总设计.md)为准；页面标识/绑定规则以[《UI 制作规范》](UI制作规范.md)、[《UI 编辑器工具专项设计》§9/§10](UI编辑器工具专项设计.md#ch10-ui-handoff)为准；命令总线以[《命令中心专项设计》](../../../设计文档/架构/命令中心专项设计.md)为准；**写回与内联样式**以[《UI 样式系统专项设计》](UI样式系统专项设计.md)的 token 单源与[《UI 编辑器工具专项设计》§6](UI编辑器工具专项设计.md)的 `UiPatch` 为准——token-bound 属性默认写 token，内联落盘需显式降级并登记样式债；资产写入经 Unity 序列化/Pipeline；能力优先级以调试路线图 T0～T3 为准。
> 关联：[13-调试手册](../../../项目说明/13-调试手册.md)、[08-UI 制作与预览](../../../项目说明/08-UI工作台.md)、[14-客户端开发能力地图](../../../项目说明/14-客户端开发能力地图.md)、[施工进度/GM 与调试线](../../../施工进度/GM与调试线.md)。

## 1. 目标与边界

### 1.1 它回答什么

真包里的人工 UI 验证有四个高频问题，本设计分别给出对应能力：

| 问题 | 能力 |
| --- | --- |
| 目标页怎么最快打开？ | 页面目录（按 `tbuiform` 自动发现）＋ Go/Back/Replace/Close/Reopen |
| 目标状态怎么复现？ | 状态场景注入（空/大数据/长文本/错误/伪本地化/语言）——页面声明接入 |
| 看起来对，能不能点？ | 可点性/遮挡可视化：可交互控件的射线命中与遮挡者 |
| **运行时为什么长这样/为什么点不到/谁在消费这个名字？** | **深检**：页面/层树、绑定与契约（含消费者/引用点）、Canvas/SafeArea、文本/本地化、输入/焦点、uGUI 性能、样式引用与订阅计数 |
| 运行时发现的问题怎么落回资产？ | **写回**：属性级 diff → 预览 → 应用到标准 Prefab（编辑器）；Player 只采集 patch 文件，编辑器应用 |
| 运行时怎么快速试调样式？ | **样式编辑面**：token 引用/解析值/可写属性白名单；试调只改实例，落盘走 patch；token-bound 默认写 token，内联落盘显式降级并登记样式债 |
| 怎么留证据？ | 截图＋状态 dump＋现场包＋patch/应用审计，带构建身份与页面上下文 |

### 1.2 它不是自动化

本面是**人在环**的调试投影：人决定打开哪页、注入什么状态、点哪里。自动断言、语义定位、性能基线归[《UI 测试开发专项设计》](UI测试开发专项设计.md)的 L2/L4；本面不新增测试 Runner，也不把截图或“点过一次”当作回归证据。

### 1.3 与相邻面的关系

| 相邻面 | 关系 |
| --- | --- |
| DevHUD | 只读投影。本面只在 HUD 增加一行状态（当前页/遮挡数），**不把 HUD 变成写面板** |
| 编辑器 GM 页（Hub） | 富交互投影。共用读口/写口/命令；编辑器独占动作不搬进 Player |
| UI 制作与布局预览 | 编辑期布局目检；本面在真实 Player 里用真实内容/数据验证运行时形态；深检与制作窗口共用页面/契约/绑定标识。深检默认只读；写回模式是修复路径，不是第二制作面——生产制作仍走 Prefab Stage，结构性改动不进入本面 |
| UI 编辑器工具 §6 `UiPatch` | 写回载体统一为 `UiPatch`（diff/预览/冲突/Undo/归档）；本面只生产/消费 patch，不另造写回格式、不建第二套资产真相 |
| Unity Pipeline | 资产写入经 Unity 序列化/Pipeline（红线 4）；本面不直改 prefab/.meta 文本 |
| UI 测试开发（L2/L4） | 自动回归与性能；本面用于人工复现、验收与取证，二者共用同一 UI 契约但不互相替代 |
| Runtime Pipeline | 机器驱动通道（`simulate_pointer`/`eval` 等）；本面是人手操作面，二者可并存，互不依赖 |

## 2. 总体形态

```text
        UI 调试内核（LiteGame.App，dev 三宏，装配期持有）
      读模型 / 命令集 / 发现表 / 状态场景 / 深检快照
           ↑                    ↑                       ↑
   编辑器深检窗口（Play）   UI 调试面板（F8）        文本控制台（ui.*）
   LiteGame.Editor         LiteGame.DevTool          LiteGame.DevTool
   （UITK 深检，Hub 入口）  （UITK Overlay，可寄宿 T1） （解析调用名 → 内核命令）
           ↑
   DevHUD 状态行（只读）

写回是编辑器投影的独立能力，不属于只读内核：
  实例属性 diff → UiPatch（预览/属性选择/冲突检查）→ 标准 Prefab（Undo/审计/归档）
  Player/F8 只采集 patch 文件，不调用 UnityEditor API、不写资产。
```

- **内核**负责“有哪些页面、当前什么状态、能执行什么动作”，不负责画界面，也不写资产；住 `LiteGame.App` 的 dev 三宏区，与 `TestModeOptions`/`DevStartupConfig` 同一装配根，便于装配期注册；
- **面板**与**文本控制台**是同一内核的两个运行期操控投影：面板按钮和 `ui.open …` 命令走同一个命令实现；投影住 `LiteGame.DevTool`，单向引用 `LiteGame.App`（与 DevHUD 同一依赖方向），内核不反向引用投影；
- **投影取件**：经 `GameEntry` 的公开只读访问器取内核（与 DevHUD 取 `Stats` 同口径），不反射私有服务、不新建全局单例；
- **第三个投影＝编辑器深检窗口**（`LiteGame.Editor`，Hub 目标 `ui.runtime-debug`，仅 Play）：读同一内核/快照，UI Toolkit 呈现页面树与深检页签；默认只读，写回是独立显式模式；不复制页面目录/命令逻辑。它解决编辑器 Play 下的深查；F8 面板解决 Player 内的人工验证；
- **写回通道**：编辑器深检窗口可进入写回模式（属性级 patch）；F8/Player 只能采集 patch 文件。写回不进内核命令集，不复制 `UiPatch` 的 diff/冲突/Undo 逻辑；
- 面板的最终载体（独立 Overlay／T1 控制台外壳中的一个模块）归 T1 决策点；本设计只锁模块契约与交互形态，不锁外壳；
- 随开发三宏剥离，release 与测试者分发包零残留。

## 3. 读模型

读模型是**不可变快照 + 明确来源**，投影不自行拼读数、不反射私有字段。

### 3.1 页面目录

- **来源**：`tbuiform` 表（`id`/`lua_path`/`prefab`/`layer`/`full_screen`），经 `ConfigService.Tables` 读取；不得硬编码页面清单或 ID。
- **打开状态**：`UIService.IsOpen(formId)`、`UIService.TopModalId`、`UIService.IsModalOpen`、`UIService.TryGetBackTarget`；打开中的页面集合经 `UIModalStack.IUIFormQuery.CollectOpen` 获取。
- **页面状态**：`UIForm.State` 的既有枚举（`Loading`/`Active`/`Covered`/`Paused`/`Closing`/`Recycled`/`Disposed`）。未打开是投影层派生值（`Closed`），不新增产品状态。
- **实例上下文**：实例是否复用、`DisplayGeneration`、逻辑是否待换表（`UIService.StaleLogicCount` 只给总数）；按页面能读到的公开口为准。

### 3.2 焦点、输入与层序

- 当前焦点：`EventSystem.current.currentSelectedGameObject`（只读）；
- 输入拦截：`IInputService.BlockerCount` / `BlockedByName`（只读；与 GM 页同口径）；
- 层序：按 `tbuiform.layer` 分组的已打开栈（`IUIFormQuery.StackOf(layer)`）＋模态栈；
- 本面只读这些状态，不注册输入拦截、不改焦点、不暂停 UI 时钟。

### 3.3 环境与场景

- 语言（`ILocalizationService.Locale`）、分辨率、屏幕缩放、UI 时钟暂停位（`DebugRuntimeTuner.UiPaused`）——只读展示；
- 当前注入的调试场景（`UiDebugState`）与其种子；
- 数据规模/文本长度等“页面自己声明的场景参数”，由页面接入后回显，不由内核猜测。

### 3.4 可点性快照

对每个已打开页面的可见可交互控件（`Selectable` 及派生类）生成一条记录：

```text
页面 id / 控件路径 / 是否激活 / interactable / blocksRaycasts
屏幕矩形中心 / 射线首命中对象 / 判定（可点 / 被遮挡 / 不可交互 / 不可见）
遮挡者路径（首命中不是自身或祖先时）/ 所在 Canvas 与排序
```

判定口径：以控件屏幕矩形中心发一条 `PointerEventData`，`EventSystem.RaycastAll` 后取首命中；首命中是控件自身或其子节点 = 可点，否则 = 被遮挡。`CanvasGroup.interactable=false`、`blocksRaycasts=false`、未激活、尺寸为零单列，不混入“被遮挡”。

该快照是**可视化辅助**，不是门禁；自动化的可点性断言仍按[修改指南 §2.3](../../../修改指南.md) 在 L2/真机验证里执行。

### 3.5 深度检查模型（uGUI Runtime Inspector）

页面目录/命令集解决“把页面带到目标状态”；深检解决“页面为什么长这样、名字被谁消费、谁在挡射线、性能花在哪”。两者共用同一读模型，不各扫一遍运行时。

**读口**：在 `LiteClient.UI` 的 dev 区（三宏）定义只读 `IUiRuntimeDiagnostics`，由 `UIService`/`UIForm`/`UIBindIndex`/`FeedbackService` 提供快照；`LiteGame.App` 的调试内核组合成 `UiRuntimeSnapshot`。投影（编辑器窗口/F8 面板）只读快照，不反射私有字段、不遍历私有容器。release 剥离。

| 段 | 内容 | 来源 |
|---|---|---|
| `forms[]` | id / luaPath / prefabPath / state / layer / fullScreen / sortingOrder / displayGeneration / 是否复用 / 实例根路径 | `UIForm.Info/State/Canvas/DisplayGeneration`、`UIService`/`UIFormStore` 只读投影 |
| `navigation` | 打开中栈/当前层、队列深度、executed/rejectedByCapacity/timedOut/cancelled、最近操作 | `UINavigationController` 公开计数 + 一致性快照 |
| `modal/coverage` | 顶层模态 id、是否模态、组级遮盖、输入拦截数/拦截者 | `UIModalStack`、`UICoverageResolver`、`IInputService` 只读 |
| `bindings[]` | 每页 `BindName` → 组件类型 / 驱动方式（Bound/Command/None）/ 是否被消费 / 缺失/重复 / 绑定来源（路径 A/B）/ 近 1s 写入计数（dev-only）/ **消费者与引用点**（Lua/C# 字符串字面量，含 file:line） | `UIBindIndex` 只读视图（新增 `Entries` 只读面，不暴露可变字典）+ dev 计数 + `UiBindingReferenceScan`（只读） |
| `contract` | 契约是否存在、sourceHash、与当前 prefab 的漂移、`controls[]` 差异 | `UiPageContract` 生成/校验结果（目标；未实现时显式“未接入”） |
| `layout` | Canvas 根/层、CanvasScaler 参考分辨率与 Match、SafeArea 矩形、RectTransform 屏幕矩形、Raycaster/CanvasGroup/RaycastTarget；编辑器可选设备档/横竖屏 | `UIForm.Root/Canvas`、`CanvasScaler`、`SafeAreaReceiver`、`EventSystem` |
| `text/locale` | 当前 locale、`ILocaleBound` 页面、缺 key、静态/动态文案归属（`LTextKeyRef` 目标） | `ILocalizationService`、`LTextLabel`/`LText`、契约 `text[]` |
| `style` | 选中节点的 token 引用/解析值、是否裸值、可写属性白名单与当前值 | `UiStyleRef`/`UiTextStyleRef`、`UiStyleResolver`、`Graphic`/`TMP_Text`/`Image`；无引用时显示“未绑定 token” |
| `input/focus` | 当前 `EventSystem.currentSelectedGameObject`、指针下首命中、模态阻断、Back 目标、当前输入设备、Selectable 导航候选 | `EventSystem`、`UIService.TryGetBackTarget`、`UIFocusCoordinator`、uGUI `Navigation` |
| `performance` | Canvas rebuild/批次相关 ProfilerRecorder、Layout 重建耗时（可用时）、CanvasRenderer/Graphic 计数、TMP 字体资产与 SDF 状态、绑定写入计数、订阅/句柄计数、UI GC 采样 | `ProfilerRecorder`（2022.3 实际 marker 对齐）、`UIBindIndex` dev 计数、`SubscriptionBag`（有仪器时；无则显式“未接入”）、场景采样 |
| `feedback` | Loading/Error/Confirm/Toast 的 form id、可见性、文案、最近一次结果 | `FeedbackService` 既有只读口 |
| `effects` | 活跃 `UiFx` 句柄数/目标（可选，先只做计数） | `UiFx` dev 计数（不在产品逻辑加分支） |

`consumers[]`、订阅计数与 `style` 段只读；写回不改变读模型口径。

**采集纪律**：快照按需/低频（≤4Hz；隐藏即停）；列表/字符串复用缓冲，不每帧全量扫树；深树/样式/性能只在面板可见时采；任何段缺数据显式显示“未接入/不可用”，不伪造通过。

## 4. 命令集

### 4.1 运行期命令

命令是运行期操控投影的唯一写入口：面板按钮与文本控制台调用同一张表；DevHUD 不承载任何写动作。执行仍走 `CommandCenter`（`GmOnly` + 审计 + 可撤销）；本表只是发现与参数解析的稳定词表，不另建执行器。

| 调用名 | 参数 | 语义 | 实际写口 |
| --- | --- | --- | --- |
| `ui.list` | `[--open]` | 列出页面目录；`--open` 只看打开中页面 | 读模型 |
| `ui.open` | `<id> [--replace] [--state=<场景>]` | 打开页面；`--replace` 替换当前记录 | `UINavigationController.GoAsync` / `ReplaceAsync` |
| `ui.close` | `<id>` | 关闭指定页面 | `UIService.CloseAsync(id, User)` |
| `ui.back` | — | 返回（最顶模态优先） | `UINavigationController.BackAsync` |
| `ui.reopen` | `<id> [--state=<场景>]` | 关闭并重开，覆盖冷开/热开/复用 | `CloseAsync` → `GoAsync` |
| `ui.lang` | `<zh\|en>` | 切语言并回显 | `ILocalizationService.SetLocale`（页面 Text 订阅 `OnLocaleChanged`） |
| `ui.resolution` | `<宽>x<高>` | 窗口分辨率预设（窗口态生效） | `Screen.SetResolution` |
| `ui.raycast` | `[--page=<id>] [--json]` | 执行可点性快照并输出/可视化 | 读模型 |
| `ui.capture` | `[--name=<名>]` | 截图到 ui-debug 目录 | `ScreenCapture.CaptureScreenshot` |
| `ui.dump` | `[--name=<名>]` | 当前页面/层序/焦点/遮挡摘要落 JSON | 读模型 |
| `ui.bundle` | `[--reason=ui-debug]` | 落现场包（日志尾＋身份） | `DiagnosticsBundleWriter` |

### 4.2 命令约束

- **打开必须走导航单写者**：`ui.open`/`ui.back` 经 `UINavigationController`；`ui.close` 使用 `UIService.CloseAsync`（工具/测试允许的公开口），但内核保证同一时刻只执行一个关闭/重开操作，不与转场并发；
- **不直接实例化 prefab、不手建 `UIForm`、不改层级组**——页面创建/复用/销毁只走 `UIService`；
- 关闭/重开必须等生命周期终态（`Closing`→`Recycled`/`Disposed`）再进入下一步，不用固定延时；
- 命令失败显示 `CommandResult` 的拒绝/异常原因，不自动重试；
- 高频动作（连续拖动、每帧查询）不命令化；本面只收低频离散动作。

### 4.3 写回与内联样式（编辑器投影）

写回是**修复路径**，不是第二制作面。默认只读；进入写回必须显式选中目标 prefab、显示 old→new diff、逐属性确认。

**写回对象与范围**

| 项 | 约定 |
|---|---|
| 目标 | `Assets/ShareResource/UI/{Screens,Widgets,Hud}/**` 的 prefab 资产；scene-only 对象拒绝 |
| 粒度 | 属性级（已有组件的序列化字段）；不做节点/组件增删、资产创建/删除——结构改动走 Prefab Stage/Unity Pipeline |
| 载体 | `UiPatch`（[《UI 编辑器工具专项设计》§6](UI编辑器工具专项设计.md)）：diff/属性选择/冲突检查/Undo/归档；不建第二套 patch 格式 |
| 通道 | 编辑器 Play：实例 diff → `UiPatch` → 标准 Prefab；Player/F8：只采集 patch 文件，编辑器导入应用 |
| 冲突 | 应用前复核目标 prefab 当前内容/`sourceHash`；与采集时不一致即中止并显示冲突项，不覆盖磁盘变更 |
| 审计 | 每次 apply 记录 patch、目标、操作者、结果、Undo 标记；不自动保存、不静默成功 |

**样式编辑面**

- 选中节点显示 token 引用/解析值/是否裸值；可编辑白名单：`Graphic.color`、`Image.sprite`/`Image.type`/`preserveAspect`、`TMP_Text.fontSize`/`color`/`alignment`/`overflow`、`Selectable.colors`。
- 试调只改实例，不写资产；`采集 Patch` 固化 diff，`应用 Patch` 才落盘。
- **token-bound 属性**：默认“写回 token”（显示该 token 的影响页数）；“内联落盘”是显式降级，必须确认解除 token 引用并登记样式债，patch 记录原 token ID。
- **未绑定属性**：允许内联落盘；校验器按裸样式报告，需 token 化或走例外表。
- 展示状态（`active`/`CanvasGroup.alpha`/`interactable`/`blocksRaycasts`）只预览、不落盘；避免把调试态写成资产默认值。
- `RectTransform`/布局、结构、资产创建删除不在样式写回面——归 Prefab Stage。
- 样式写回与《UI 样式系统专项设计》A 形态一致：prefab 仍是视觉真源；写回不得创建运行时主题覆盖或第二份样式表。

**首版边界**

- V1 只写顶层页面 prefab（`Screens/Hud`）的属性；嵌套模板 prefab 的写回需要显式选定模板资产，结构变更仍拒绝。
- 写回能力在 `UiPatch` 未接入时显式“未接入”，不提供临时第二套实现。

## 5. 状态注入

### 5.1 契约

状态场景随打开请求以 `IUIData` 传入，类型为 `UiDebugState`：

```text
Scenario = Default | Empty | Large | LongText | Error | Loading | PseudoLocalized
Seed / Language / PageId
```

- **页面选择加入**：C# 页面逻辑在 `OnInit`/`OnShow` 识别 `UiDebugState` 并应用；Lua 页面经 `LuaBehaviourAdapter` 的既有 `IUIData` 通道接收保留字段（仅开发三宏分支），页面自行读取；未接入的页面忽略该数据并记录一次 Info，不伪造状态。
- **类型落位**：`UiDebugState` 住 `LiteClient.UI` 的 dev 区（与 `IUIData` 同层、release 剥离），LuaBridge 只做转发，不新增 `LiteGame.LuaBridge → LiteGame.App` 的反向依赖；内核（`LiteGame.App`）构造并传入该数据。
- **不反射页面私有字段**，不替换页面实例，不绕过 `OnInit/OnShow/OnHide/Release` 生命周期；状态注入只影响页面自己的数据模型与展示。
- **已打开页面换场景**：先按关闭语义收尾（`CloseAsync`，等待 `Recycled`/`Disposed`），再带新 `UiDebugState` 重新打开；不直接改现有实例的数据模型。关闭被拦截/失败时停止并回显原因。
- **确定性**：同一 `Scenario + Seed` 必须得到同一份数据；随机数据来自 `TestRunSettings` 派生种子（与测试框架同源）。
- **首批覆盖**：2～3 个 P0 页面（大厅/房间/结算优先）；其余页面先具备打开/关闭/可点性，不强制状态注入。

### 5.2 场景语义

| 场景 | 页面应表达 |
| --- | --- |
| `Default` | 正常数据 |
| `Empty` | 0 条列表/空状态 |
| `Large` | 大列表（首尾滚动与复用） |
| `LongText` | 超长标题/说明/昵称 |
| `Error` | 加载失败/错误提示与重试入口 |
| `Loading` | 持续加载态（不定态） |
| `PseudoLocalized` | 伪本地化文本（长度膨胀） |

内核不解释这些值的视觉表现；页面未实现某个场景时回显“未接入”而不是假装成功。

## 6. 双投影与深检面板

### 6.1 编辑器深检窗口（`ui.runtime-debug`）

- **入口**：Hub `LiteGame/工具链` → **UI 运行时调试**（目标 ID `ui.runtime-debug`，`window` / runtime plane / requires play）。非 Play 时只显示“进入 Play 后可用”，不伪造空快照。
- **渲染**：UI Toolkit EditorWindow + dev-only UXML/USS/ThemeStyleSheet（住 `Assets/LiteGame/Editor/UiTools/RuntimeDebug/`，不进收集组/不进包）；与 F8 面板共用快照与主题链，不共用绘制代码。**调试工具自身允许 UI Toolkit；产品 UI 仍是 uGUI**。
- **布局**：
  - 左：页面/层树（打开中、模态、层序、状态徽标）；点击选中快照条目；
  - 右：页签 `概览 / 绑定与契约 / 布局与安全区 / 文本与本地化 / 样式与写回 / 输入与焦点 / 性能 / 反馈面 / 操作记录`；
  - 底：`拾取元素` / `在 Scene 定位` / `复制节点路径` / `复制 BindName` / `冻结快照` / `采集 Patch` / `应用 Patch` / `撤销写回`；
- **深查能力**：
  - 选中页面 → 显示 `UIForm` 状态、prefab/lua、显示代次、复用/租约、Canvas/Group/Raycaster、sortingOrder；
  - 绑定页签 → `UIBindIndex` 条目（BindName/类型/驱动方式/来源 A·B/未命中/重复/消费者与引用点）；若有 `UiPageContract`，显示契约漂移与 `controls[]` 差异，否则显式“契约未接入”；
  - 布局页签 → 根 Canvas/`CanvasScaler`（参考分辨率/Match）/`SafeAreaReceiver` 当前矩形/选中节点屏幕矩形；编辑器内可 Ping 节点与 prefab；
  - 文本页签 → locale、页面 `ILocaleBound`、缺 key、静态/动态归属（`LTextKeyRef` 目标；未实现时显示“未接入”）；
  - 样式页签 → token 引用/解析值/裸值状态；无引用时显示“未绑定 token”；可写属性白名单见 §4.3；
  - 输入页签 → `EventSystem.currentSelectedGameObject`、指针首命中、模态/输入拦截、Back 目标；
  - 性能页签 → uGUI Canvas rebuild/批次相关 ProfilerRecorder、CanvasRenderer/Graphic 计数、TMP 字体/SDF 状态；采样按需，不每帧；
  - 反馈/操作页 → `FeedbackService` 的 Loading/Error/Confirm/Toast 与 `UINavigationController` 计数、最近操作。
- **样式与写回页签**：显示 token 引用/解析值/裸值状态与可编辑白名单；试调只改实例；`采集 Patch` 落文件，`应用 Patch` 仅编辑器，冲突中止；内联落盘需显式确认并登记样式债。
- **默认只读；写回是显式模式**：进入写回前必须选中目标 prefab、显示 old→new diff、逐属性确认；应用经 `UiPatch` + Unity 序列化/Pipeline，带 Undo/审计/归档。结构、布局、资产创建删除不在写回面。
- **性能**：可见时 ≤2–4Hz 拉快照；隐藏/失焦即停；树与字符串缓冲复用，不每帧分配。

### 6.2 F8 运行内面板

- **渲染**：UI Toolkit Overlay，`PanelSettings.sortingOrder = 10001`（DevHUD 10000，产品当前最高 6000）；复用 DevHUD 的系统字体候选链。标准控件需要 dev-only `ThemeStyleSheet`（资产只在 `Assets/LiteGame/DevTool/`，不进收集组、不进包）；主题缺失时显示降级提示，不阻塞游戏。
- **入口**：`F8` 总开关（F1–F7 已被 DevHUD 占用）；面板聚焦时 `Esc` 关闭面板并恢复原焦点，不触发产品 Back。
- **布局**：左侧页面目录（搜索 + 打开中标记 + layer）；右上状态/层序/模态/焦点；右中动作区（Open/Replace/Back/Close/Reopen、语言、分辨率、状态场景、样式试调）；右下可点性摘要与证据按钮（Raycast/Capture/Dump/Bundle/Patch）。
- **深检**：提供紧凑 `Inspect` 页签（页面/绑定/布局/输入摘要），完整树与契约差异引导到编辑器深检窗口；不在 Player 内复制完整编辑器布局。
- **写回**：F8 只采集 Patch（属性 diff 落 `persistentDataPath/ui-debug/patches/`），不调用 UnityEditor API、不写资产；应用在编辑器深检窗口完成。
- **输入纪律**：面板打开时只捕获自己的焦点与指针；关闭时恢复 `EventSystem` 原选中对象；不拦截产品输入（不做全屏 raycast 遮挡块）。
- **自身可点**：面板的按钮同样要通过可点性自检——调试面自己不可点，等于没有调试面。

### 6.3 拾取与高亮

- **拾取**：指针位置经 `EventSystem.RaycastAll` 取首命中 → 显示节点路径/BindName/Canvas 排序/遮挡者；编辑器窗口可 Ping/选中场景对象，Player 内绘制屏幕描边。
- **高亮**：调试层用 UI Toolkit Overlay 绘制选中节点屏幕矩形（红/黄描边），`Esc` 清除；不改产品 Graphic/材质/颜色。
- **不穿透**：拾取只读，不派发点击、不改焦点、不拦截产品输入；需要点击验证时走真实指针模拟或 §4 命令，不以拾取代替点击。
- **自身可点**：深检窗口/F8 面板的控件本身要过可点性自检。

### 6.4 深检页签与快照对应

| 页签 | 主要快照段 | 关键问题 |
|---|---|---|
| 概览 | `forms[]`/`navigation`/`modal/coverage`/`feedback` | 现在开了哪些页、谁盖谁、为什么退不回去 |
| 绑定与契约 | `bindings[]`/`contract` | 这个名字被谁消费、有没有重复/缺失、契约是否漂移 |
| 布局与安全区 | `layout` | Canvas 排序/缩放/SafeArea/屏幕矩形为什么是这样 |
| 文本与本地化 | `text/locale` | 文本来自 key 还是 Lua、缺什么 key、语言是否生效 |
| 样式与写回 | `style`/`bindings[]`/patch 结果 | 这个颜色/字号来自 token 还是裸值、能不能落盘、会改哪个 prefab |
| 输入与焦点 | `input/focus` | 为什么点不到/焦点在哪/Back 会去哪 |
| 性能 | `performance` | Canvas rebuild/批次/Graphic/TMP 字体开销在哪 |
| 反馈面 | `feedback` | Loading/Toast/Error/Confirm 是否有残留或重复 |
| 操作记录 | `navigation`/`feedback` + 内核日志 | 最近打开/关闭/返回/注入的顺序与失败原因 |

### 6.5 失败与空态

| 情形 | 契约 |
| --- | --- |
| 非 Play / 内核未装配 | 深检窗口显示原因；F8 显示空态，不启动 |
| 页面未打开 | 目录可 open；深检页签显示“未打开”，不伪造实例 |
| 契约未接入 | 显示 `BindNode`/`UIBindIndex` 实况 + “契约未接入”，不冒充契约通过 |
| 性能 marker 不可用 | 显示“不可用（Unity 版本/平台）”，不伪造数字 |
| 快照过期 | 显示采样时间/冻结状态；用户可手动刷新 |

## 7. 证据

目录：`Application.persistentDataPath/ui-debug/<session>/`

| 产物 | 内容 |
| --- | --- |
| `shot-<page>-<scenario>-<ts>.png` | `ScreenCapture.CaptureScreenshot` 截图 |
| `dump-<page>-<ts>.json` | 构建身份（buildHash/configDigest）、会话、页面 id/prefab、`UIForm.State`、层序与模态、焦点、语言/分辨率、可点性摘要、注入场景、写回记录（若有）；**深检摘要**：绑定条目/消费者、契约存在与漂移（若接入）、Canvas/SafeArea 矩形、样式引用、性能采样（marker 可用时） |
| `patch-<ts>.uipatch.json` | 属性级 diff：目标 prefab/节点/属性/old/new/token 引用变更；Player 与编辑器共用同一格式 |
| `patch-applied-<ts>.json` | 应用审计：patch、目标 prefab、操作者、逐属性结果、Undo 标记、冲突/失败原因 |
| `bundle-<ts>/` | 现场包：`manifest.json`、`log-recent.txt`、`player-log-tail.txt`（`DiagnosticsBundleWriter`，`reason=ui-debug`） |

- 本地 only，不上传；不含凭据/密钥；
- 写盘失败只 `Log.Warning`，不阻塞操作；
- dump 的字段词汇与现场包共用（身份/会话/日志尾），不另拼第二套字段表。

## 8. 边界与纪律

| # | 纪律 |
| --- | --- |
| 1 | 仅开发三宏内存在；内核住 `LiteGame.App`，投影住 `LiteGame.DevTool`（单向引用 App）；资产不进收集组；release 零残留 |
| 2 | 投影零业务逻辑：不持规则、不写运行态、不直接调产品服务；写只经内核命令 |
| 3 | 打开/返回走 `UINavigationController` 单写者；关闭/重开串行，不并发转场 |
| 4 | 不新增 `public static` 可变状态；不进入判定路径；不读伤害/弹药/命中/checksum 的静态值 |
| 5 | 不反射页面私有字段/私有服务；状态注入只经页面声明的 `IUIData` 通道 |
| 6 | 不直接实例化 prefab、不替换 `UIForm`、不改 Canvas 排序/层级组 |
| 7 | 只读生命周期：不暂停 UI 时钟、不注册输入拦截、不改 `Time.timeScale`（时间缩放归 GM/API） |
| 8 | 隐藏即停读；深检/可点性快照按需或低频（≤4Hz），不做每帧全量扫描 |
| 9 | 不把截图/“点过一次”当自动门禁证据；门禁仍归 L2/L4 |
| 10 | 面板/命令异常不影响产品 UI 与流程；释放时恢复焦点与展示状态 |
| 11 | **默认只读；写回是独立显式模式**：只做属性级 patch；应用前逐项 diff 预览+确认；经 `UiPatch` 与 Unity 序列化/Pipeline；Undo/审计/归档；不做结构增删、资产创建删除、scene-only 对象写回 |
| 12 | 产品 UI 不迁移 UI Toolkit；调试工具自身可用 UI Toolkit（dev-only UXML/USS/ThemeStyleSheet），不进收集组/不进包；官方 UI Toolkit Debugger 只调试调试工具自身面板，不作为 uGUI 产品 Inspector |
| 13 | 拾取/高亮只画调试层，不改产品 Graphic/材质/颜色；不派发点击、不拦截产品输入；样式编辑是独立编辑模式，不混入拾取 |
| 14 | 写回仅编辑器应用；Player/F8 只采集 patch，不调用 UnityEditor API、不写资产；写回能力随开发三宏剥离，release 零残留 |
| 15 | token-bound 属性默认写 token；内联落盘必须显式解除引用并登记样式债，不静默产生裸样式；不创建运行时主题覆盖或第二份样式表 |

### 8.1 失败语义

| 情形 | 契约 |
| --- | --- |
| 内核未装配（非开发包/装配失败） | 面板显示空态与原因，不启动 |
| `tbuiform` 不可用 | 页面目录显示错误行；命令拒绝 |
| 页面 id 不存在 | 命令返回“未找到”，不做猜测 |
| 打开/关闭失败或超时 | 显示类型化原因（`UIOpenFailure`/异常消息）；不重试、不静默 |
| 页面未接入状态场景 | 正常打开，回显“场景未接入” |
| 截图/现场包失败 | Warning；其余操作继续 |
| 射线快照为空 | 显示“无可交互控件”，不当作通过 |

## 9. 容量与性能

- 页面目录每次打开时读一次表，之后按需刷新；打开中状态 0.25～0.5s 轮询，面板隐藏即停；
- **深检快照**只采当前选中页面（绑定/布局/文本/输入），可见时 ≤2–4Hz；编辑器窗口失焦/隐藏、F8 关闭即停；
- 可点性快照只在点击“Raycast”或打开可视化时执行；可视化开启时 ≤4Hz，只扫当前页面；
- **性能段按需采样**：ProfilerRecorder 只在性能页签可见时读；CanvasRenderer/Graphic 计数按选中页面扫描，不每帧全树遍历；
- 截图/dump/现场包按需执行，不常驻；
- 不新增每帧分配；列表/文本复用缓冲（与 DevHUD 同纪律）；深检窗口的树/表格用虚拟化或折叠，避免打开即全量构建；
- 面板自身开销计入 DevHUD 性能段可见范围，不隐藏。

## 10. 验收层级

| 层级 | 验证内容 | 退出条件 |
| --- | --- | --- |
| L1 | 内核纯逻辑：目录投影、命令表/参数解析、状态场景枚举、dump 字段、可点性判定（假 `Selectable`/射线结果）；patch 模型/diff/冲突/属性白名单/样式降级判定 | 不依赖 Unity 运行时即可跑；违规负例有失败证据 |
| L2 PlayMode | 真实 `UIService`/`UINavigationController`/`CommandCenter` 下的打开/返回/替换/关闭/重开；状态注入接入页；截图/dump/patch 文件；租约/订阅/实例对称；命令审计；**深检快照与 `UIForm`/`UIBindIndex`/`Canvas` 实况一致**；F8/Player 采集 patch 不写资产 | 每例清理后实例/租约/订阅回基线；失败可按测试 ID 复现；patch 格式可被编辑器读取 |
| 编辑器深检窗口 | Play 中 Hub `ui.runtime-debug` → 选页 → 绑定/布局/输入/性能/样式页签读数；Ping/高亮；写回模式：改属性 → diff → 逐属性应用 → prefab diff → Undo 复原；磁盘变更冲突中止；token-bound 内联需确认并登记样式债；失焦即停 | 真实操作路径；快照与运行时一致；写回只产生属性级 diff；关闭后无残留监听/分配；无结构变更 |
| 开发包人工验证 | F8 → 打开页面 → 切场景 → Raycast → Capture/Dump/Patch → 关闭；面板自身可点；Inspect 页签与编辑器深检同源；采集的 patch 在编辑器可预览/应用 | 真实操作路径（红线 13）；结论只覆盖实际走到的路径；附截图、dump 与 patch 审计 |
| release | 调试类型/资产零残留（含 dev-only UITK UXML/USS/ThemeStyleSheet）；写回代码/补丁目录不可达；命令不可达；启动无 `[UiDebug]` 行 | 产物检查 + 启动冒烟 |
| Doc | 本文入索引；链接/锚有效 | `python scripts/gate/test.py --lane Doc` |

## 11. 阶段建议

阶段是能力顺序与退出条件，不是施工状态；完成证据归 `Docs/施工进度/`。

| 阶段 | 范围 | 退出条件 |
| --- | --- | --- |
| U0 读模型与页面目录 | 目录、打开中状态、层序/模态/焦点、`ui.list/open/back/close/reopen`、dump | 人在包里能按目录打开/返回/重开任意页面；dump 可复盘；L1/L2 绿 |
| U1 状态注入 | `UiDebugState`、语言/分辨率、首批 2～3 页接入 | 空/大/长文本/错误/加载五类场景可复现；未接入页明确回显；无生命周期绕过 |
| U2 可点性与证据 | `ui.raycast` 可视化、截图、现场包、面板布局完善 | 每个 P0 页面能一眼看出可点/遮挡；证据文件可复查；面板自身可点 |
| **U3 深检（uGUI Runtime Inspector）** | `IUiRuntimeDiagnostics` 快照；编辑器深检窗口（Hub `ui.runtime-debug`）；绑定/契约（含消费者/引用点）、布局/安全区、文本/本地化、样式、输入/焦点、性能/订阅页签；拾取/高亮 | 深检与 `UIForm`/`UIBindIndex`/`Canvas` 实况一致；默认只读零资产写；隐藏即停；L1/L2 + 编辑器人工验证 |
| U4 载体接入 | 接入 T1 运行期操控载体（控制台/面板外壳）；HUD 状态行 | 同一命令集在面板与控制台等价；HUD 仍只读 |
| U5 写回与内联样式 | `UiPatch` capture/apply；编辑器写回模式；F8/Player patch 采集；token 写回 vs 内联落盘；冲突/Undo/审计/归档 | 真实页面属性级写回；Undo 复原；磁盘变更冲突中止；Player patch 可在编辑器应用；内联样式债可被校验器报出；结构变更被拒绝 |

## 12. 非目标

- 不做 UI 自动化框架、语义 Locator/Driver、性能预算门禁（归 UI 测试开发专项）；
- 不做编辑器内的页面制作、绑定生成、布局编辑（归 UI 编辑器工具 §8/§9）；
- 不迁移产品 UI 到 UI Toolkit；**运行时调试工具自身允许使用 UI Toolkit**（EditorWindow/F8 的 UXML/USS/ThemeStyleSheet 为 dev-only，不进收集组/不进包）；官方 UI Toolkit Debugger 只用于调试工具自身的 UITK 面板，不作为 uGUI 产品 Inspector；PanelSettings/Dynamic Atlas 不作为 uGUI 产品性能指标；
- 不做结构级资产编辑（增删节点/组件、创建/删除资产）与布局编辑——归 Prefab Stage/Unity Pipeline；写回只做属性级 patch；
- 不做第二套资产真相或独立写回格式——写回统一 `UiPatch`，只写标准 Prefab；
- 不做无预览、无确认、无审计的静默写盘；不做 release/测试者分发包写回；不做 scene-only 对象的资产写回；
- 不做通用 GM/世界查看器/弱网注入（各为独立件，见调试路线图 T1/T2）；
- 不进入 release、测试者分发包或远端；不做多客户端聚合/远程调试；
- 不做像素级视觉对比；截图只作人工证据与失败诊断；
- 不改变产品 UI 契约、页面状态机、导航单写者与资源收集目录。

## 13. 关联

- [GM 与调试工具分层专项设计 §7](../../../设计文档/架构/GM与调试工具分层专项设计.md#7-统一内核与三投影)：内核/投影职责与写口纪律。
- [UI 测试开发专项设计](UI测试开发专项设计.md)：L2/L4 自动化、语义定位、性能门禁。
- [DevHUD 演进路线图](../../../设计文档/测试与性能/DevHUD演进路线图.md)：只读投影边界与 UI Toolkit 约束。
- [调试与开发工具链演进路线图](../../../设计文档/测试与性能/调试与开发工具链演进路线图.md)：T1 运行期操控载体的能力与决策点。
- [UI 框架总设计](UI框架总设计.md)：生命周期、导航单写者、资源与状态契约。
- [UI 编辑器工具专项设计 §8/§9](UI编辑器工具专项设计.md#ch9-ui-authoring)：制作主线、双 lane 与契约优先；深检窗口与制作窗口共用页面/契约标识。
- [UI 编辑器工具专项设计 §6](UI编辑器工具专项设计.md)：`UiPatch` 写回载体（diff/预览/冲突/Undo/归档）；调试写回不另造格式。
- [UI 编辑器工具专项设计 §10](UI编辑器工具专项设计.md#ch10-ui-handoff)：`UiPageContract`/`LTextKeyRef`/角色边界（原美术交接专项已吸收）；深检默认只读契约状态，写回不替代交接评审。
- [UI 样式系统专项设计](UI样式系统专项设计.md)：token 单源；token-bound 属性默认写 token，内联落盘需显式降级并登记样式债。
- [13-调试手册](../../../项目说明/13-调试手册.md)：现役调试面、热键与已知不足。
- [施工进度/GM 与调试线](../../../施工进度/GM与调试线.md)：阶段证据（本文不记状态）。
