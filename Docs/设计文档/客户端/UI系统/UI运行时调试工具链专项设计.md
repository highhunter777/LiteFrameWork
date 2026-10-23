# UI 运行时调试工具链专项设计

> 状态：现行专项设计（目标形态；默认只读 + 编辑器显式写回）
> 版本：1.0
> 日期：2026-10-22
> Owner：客户端工具 / 客户端 UI
> 定位：回答“人在 development Player 或编辑器 Play 里如何 inspect 运行时 UI、把页面带到目标状态、验证可点性与遮挡、把问题以属性级 patch 落回 prefab、留下可复盘证据”。
> 不做的事：不替代 L2/L4 自动化；不做结构级资产编辑（增删节点/组件/资产）；不做第二套资产真相或 patch 格式；不做 release/测试者分发包写回；不做编辑器内布局制作（归编辑器工具链）；不记录施工状态。
> 冲突裁决：写口/投影纪律以[GM 与调试工具分层专项设计](../../架构/GM与调试工具分层专项设计.md#7-统一内核与三投影)为准；生命周期/导航以[UI 系统总设计](UI系统总设计.md)为准；工具形态以[UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md)为准；写回载体与样式规则以[UI 制作规范](UI制作规范.md)与编辑器工具链 §8 为准。
> 关联：[UI 系统总设计](UI系统总设计.md) · [UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md) · [13-调试手册](../../../项目说明/13-调试手册.md)

## 1. 定位与边界

### 1.1 它回答什么

| 问题 | 能力 |
| --- | --- |
| 目标页怎么最快打开？ | 页面目录（按 `tbuiform` 自动发现）+ Go/Back/Replace/Close/Reopen |
| 目标状态怎么复现？ | 状态场景注入（空/大/长文本/错误/加载/伪本地化/语言） |
| 看起来对，能不能点？ | 可点性/遮挡快照：射线首命中、遮挡者、Canvas 排序 |
| 为什么长这样/为什么点不到/这个名字被谁消费？ | 深检：页面/层树、绑定与契约（含消费者）、布局/安全区、文本/本地化、样式、输入/焦点、性能 |
| 运行时问题怎么落回资产？ | 属性级 diff → 预览 → 应用到标准 prefab（仅编辑器）；Player 只采集 patch |
| 怎么留证据？ | 截图 + dump + 现场包 + patch/应用审计，带构建身份与页面上下文 |

### 1.2 它不是自动化

本面是**人在环**的调试投影：人决定打开哪页、注入什么状态、点哪里。自动断言、语义定位、性能基线归测试框架；本面不新增测试 Runner，不把截图或“点过一次”当回归证据。

### 1.3 与相邻面

| 相邻面 | 关系 |
| --- | --- |
| DevHUD | 只读投影；可增加一行“当前页/遮挡数”，不变成写面板 |
| GM 页（编辑 Hub） | 富交互投影；共用读口/命令；编辑器独占动作不搬进 Player |
| 编辑器工具链 | 布局制作/契约/校验；本面在真实 Player 验证运行时形态；深检默认只读，写回是修复路径 |
| 测试框架 | 自动回归/性能；本面用于人工复现、验收、取证，二者共用契约但不互相替代 |
| 命令中心 | 命令执行/权限/审计；本面只提供 UI 命令词表与参数解析 |
| Unity Pipeline | 资产写入经 Unity 序列化/Pipeline；本面不直改 prefab/.meta |

## 2. 总体形态

```text
                UI 调试内核（LiteGame.App，dev 三宏，装配期持有）
       读模型 / 命令集 / 页面目录 / 状态场景 / 深检快照 / 可点性
                    ▲                    ▲                    ▲
      编辑器深检窗口（Play）      F8 运行内面板         文本命令（ui.*）
      LiteGame.Editor            LiteGame.DevTool      LiteGame.DevTool
      （UITK，Hub 入口）          （UITK Overlay）       （解析 → 内核命令）
                    ▲
            DevHUD 状态行（只读）

写回（仅编辑器）：
  实例属性 diff → UiPatch（预览/属性选择/冲突检查）→ 标准 prefab（Undo/审计/归档）
  Player/F8 只采集 patch 文件，不调用 UnityEditor API、不写资产
```

- **内核**负责“有哪些页面、当前什么状态、能执行什么动作”，不画 UI、不写资产；住 `LiteGame.App` dev 区，与 `TestModeOptions`/`DevStartupConfig` 同一装配根。
- **投影**住 `LiteGame.DevTool`（运行内）与 `LiteGame.Editor`（编辑器），单向引用 `LiteGame.App`；内核不反向引用投影。
- 投影取件经 `GameEntry` 公开只读访问器，不反射私有服务、不新建全局单例。
- 面板最终载体（独立 Overlay / 控制台外壳中的模块）归调试路线图 T1 决策点；本文只锁模块契约与交互形态。
- 随开发三宏剥离；release 与测试者分发包零残留。

## 3. 读模型

<a id="ch-read-model"></a>

读模型是**不可变快照 + 明确来源**；投影不自行拼读数、不反射私有字段。

### 3.1 页面与导航

| 段 | 内容 | 来源 |
| --- | --- | --- |
| `pages[]` | id / name / prefab / lua / state / layer / fullScreen / sortingOrder / displayGeneration / 是否复用 / 实例根路径 / 缓存策略 / 租约 | `tbuiform`、`UIForm`、`UIService`、`UIFormStore` 只读投影 |
| `navigation` | 打开栈/当前层、队列深度、executed/rejected/timedOut/cancelled、最近操作 | `UINavigationController` 公开计数 |
| `modal/coverage` | 顶层模态、是否模态、组级遮盖、输入拦截数/拦截者 | `UIModalStack`、`UICoverageResolver`、`IInputService` |
| `focus/input` | `EventSystem.currentSelectedGameObject`、指针首命中、Back 目标、导航候选 | `EventSystem`、`UIService.TryGetBackTarget`、`UIFocusCoordinator` |
| `feedback` | Loading/Error/Confirm/Toast 的 form id、可见性、文案、最近结果 | `FeedbackService` |

### 3.2 绑定与契约

| 段 | 内容 | 来源 |
| --- | --- | --- |
| `bindings[]` | BindName → 组件类型 / 驱动方式（Bound/Command/None）/ 是否被消费 / 缺失/重复 / 来源 A/B / 近 1s 写入计数（dev-only） | `UIBindIndex` 只读视图 + dev 计数 |
| `consumers[]` | Lua/C# 字符串字面量中对 BindName 的引用（file:line） | 编辑器引用扫描（只读） |
| `contract` | 契约是否存在、sourceHash、与 prefab 的漂移、controls 差异 | `UiPageContract` 生成/校验结果 |
| `templates[]` | 页面使用的模板、版本/hash、内部件是否被误暴露 | 契约 + 资源扫描 |

### 3.3 布局、文本、样式

| 段 | 内容 | 来源 |
| --- | --- | --- |
| `layout` | Canvas 根/层、CanvasScaler 参考分辨率/Match、SafeArea 矩形、RectTransform 屏幕矩形、Raycaster/CanvasGroup/RaycastTarget | `UIForm.Root/Canvas`、`CanvasScaler`、`SafeAreaReceiver` |
| `text/locale` | 当前 locale、`ILocaleBound` 页面、缺 key、静态/动态文案归属、字体/缺字 | `ILocalizationService`、`LTextLabel`、契约 |
| `style` | 选中节点的令牌引用/解析值、是否裸值、可写属性白名单 | `UiStyleRef`/`UiTextStyleRef`、`UiStyleResolver`、`Graphic`/`TMP_Text` |

### 3.4 性能与资源

| 段 | 内容 | 来源 |
| --- | --- | --- |
| `performance` | Canvas rebuild/批次、Layout 重建、CanvasRenderer/Graphic 计数、TMP 字体/SDF 状态、绑定写入计数、UI GC 采样 | `ProfilerRecorder`（按 Unity 版本对齐）、dev 计数、场景采样 |
| `resources` | 页面/模板租约、缓存实例、图集/字体来源、bundle 名（可用时） | `UIService`/`UIFormStore`/内容服务只读 |
| `effects` | 活跃 `UiFx` 句柄数/目标、粒子/音效并发 | `UiFx` dev 计数、效果服务只读 |

### 3.5 采集纪律

- 快照按需/低频（≤4Hz；隐藏即停）；列表/字符串复用缓冲。
- 深树/样式/性能只在面板可见时采；不每帧全量扫树。
- 任何段缺数据显式显示“未接入/不可用”，不伪造通过。
- 只读投影不注册输入拦截、不改焦点、不暂停 UI 时钟。

## 4. 命令集

<a id="ch-commands"></a>

### 4.1 运行期命令

命令是运行期操控投影的唯一写入口；面板按钮与文本控制台调用同一张表。执行走 `CommandCenter`（`GmOnly` + 审计 + 可撤销）。

| 调用名 | 参数 | 语义 | 实际写口 |
| --- | --- | --- | --- |
| `ui.list` | `[--open]` | 列页面目录；`--open` 只看打开中 | 读模型 |
| `ui.open` | `<id> [--replace] [--state=<场景>]` | 打开页面 | `UINavigationController.GoAsync/ReplaceAsync` |
| `ui.close` | `<id>` | 关闭页面 | `UIService.CloseAsync(id, User)` |
| `ui.back` | — | 返回（模态优先） | `UINavigationController.BackAsync` |
| `ui.reopen` | `<id> [--state=<场景>]` | 关闭并重开（覆盖冷/热/复用） | `CloseAsync` → `GoAsync` |
| `ui.inject` | `<id> --state=<场景> [--seed=N]` | 关闭后带状态重开 | `UiDebugState` + 导航 |
| `ui.lang` | `<zh\|en>` | 切语言并回显 | `ILocalizationService.SetLocale` |
| `ui.device` | `<profile>` | 切换设备档（窗口态/编辑器） | 预览/调试设备档 |
| `ui.raycast` | `[--page=<id>] [--json]` | 可点性快照 | 读模型 |
| `ui.capture` | `[--name=<名>]` | 截图到调试目录 | `ScreenCapture.CaptureScreenshot` |
| `ui.dump` | `[--name=<名>]` | 页面/层序/焦点/遮挡摘要落 JSON | 读模型 |
| `ui.bundle` | `[--reason=ui-debug]` | 现场包（日志尾+身份） | `DiagnosticsBundleWriter` |

### 4.2 命令约束

- 打开/返回必须走导航单写者；关闭/重开串行，不与转场并发。
- 不直接实例化 prefab、不手建 `UIForm`、不改层级组；页面创建/复用/销毁只走 `UIService`。
- 关闭/重开必须等生命周期终态（`Closing → Recycled/Disposed`），不用固定延时。
- 命令失败显示类型化原因，不自动重试；高频动作（拖动/每帧查询）不命令化。
- 命令进入审计环；只读查询（list/dump/raycast）也记录发现面，便于复盘。

## 5. 状态注入

<a id="ch-injection"></a>

### 5.1 契约

状态场景随打开请求以 `IUIData` 传入，类型为 `UiDebugState`：

```text
Scenario = Default | Empty | Large | LongText | Error | Loading | PseudoLocalized
Seed / Language / PageId
```

- **页面选择加入**：C# 页面在 `OnInit/OnShow` 识别并应用；Lua 页面经 `LuaBehaviourAdapter` 的 `IUIData` 通道接收保留字段（仅 dev 分支）；未接入页面忽略并记录一次 Info，不伪造状态。
- **类型落位**：`UiDebugState` 住 `LiteClient.UI` dev 区，release 剥离；内核构造并传入。
- **不反射页面私有字段**，不替换实例，不绕过生命周期；状态注入只影响页面自己的数据模型与展示。
- **已打开页换场景**：先按关闭语义收尾，再带新状态重新打开；不直接改现有实例数据模型。
- **确定性**：同一 `Scenario + Seed` 得到同一份数据；随机数据来自测试种子。

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

内核不解释视觉表现；页面未实现某场景时回显“未接入”而不是假装成功。首批覆盖大厅/房间/结算等 P0 页面。

## 6. 可点性与遮挡

<a id="ch-raycast"></a>

对每个已打开页面的可见可交互控件（`Selectable` 及派生）生成：

```text
页面 id / 控件路径 / 是否激活 / interactable / blocksRaycasts
屏幕矩形中心 / 射线首命中对象 / 判定（可点 / 被遮挡 / 不可交互 / 不可见）
遮挡者路径（首命中不是自身或祖先时）/ 所在 Canvas 与排序
```

判定口径：控件屏幕矩形中心发一条 `PointerEventData`，`EventSystem.RaycastAll` 后取首命中；首命中是控件自身或其子节点 = 可点，否则 = 被遮挡。`CanvasGroup.interactable=false`、`blocksRaycasts=false`、未激活、尺寸为零单列，不混入“被遮挡”。

- 快照是**可视化辅助**，不是门禁；自动化可点性断言仍在 L2/真机验证执行。
- 可视化开启时 ≤4Hz，只扫当前页面；不派发点击、不改焦点、不拦截产品输入。
- 调试面板自身也必须通过可点性自检。

## 7. 投影

<a id="ch-inspector"></a>

### 7.1 编辑器深检窗口（`ui.runtime-debug`）

- 入口：Hub → UI 运行时调试；仅 Play 可用；非 Play 显示“进入 Play 后可用”，不伪造空快照。
- 渲染：UI Toolkit EditorWindow + dev-only UXML/USS/ThemeStyleSheet；与 F8 共用快照与主题链，不共用绘制代码。
- 布局：左页面/层树；右页签 `概览 / 绑定与契约 / 布局与安全区 / 文本与本地化 / 样式与写回 / 输入与焦点 / 性能 / 反馈面 / 操作记录`；底拾取/定位/复制路径/冻结快照/采集/应用/撤销。
- 深查能力：
  - 页面 → `UIForm` 状态、prefab/lua、显示代次、复用/租约、Canvas/Group/Raycaster、sortingOrder。
  - 绑定 → `UIBindIndex` 条目、驱动方式、来源 A/B、缺失/重复、消费者/引用点；契约漂移与 diff。
  - 布局 → CanvasScaler、SafeArea、选中节点屏幕矩形；Ping 节点/prefab。
  - 文本 → locale、`ILocaleBound`、缺 key、静态/动态归属、字体/缺字。
  - 样式 → 令牌引用/解析值/裸值；可写白名单。
  - 输入 → 当前焦点、指针首命中、模态/拦截、Back 目标。
  - 性能 → Canvas rebuild/批次、Graphic/TMP 计数、GC 采样。
  - 反馈/操作 → `FeedbackService` 与导航计数、最近操作。
- 默认只读；写回是显式模式（§8）。
- 性能：可见时 ≤2–4Hz；隐藏/失焦即停；树/表格折叠或虚拟化。

### 7.2 F8 运行内面板

- 渲染：UI Toolkit Overlay；`sortingOrder=10001`（DevHUD 10000，产品最高 6000 以内）；复用 DevHUD 系统字体候选链。
- 入口：F8 总开关；面板聚焦时 Esc 关闭并恢复原焦点，不触发产品 Back。
- 布局：左页面目录（搜索+打开中标记+layer）；右上状态/层序/模态/焦点；右中动作区（Open/Replace/Back/Close/Reopen、语言、设备档、状态场景、样式试调）；右下可点性摘要与证据按钮。
- 深检：紧凑 Inspect 页签（页面/绑定/布局/输入摘要）；完整树与契约差异引导到编辑器窗口。
- 写回：只采集 Patch（`persistentDataPath/ui-debug/patches/`），不调用 UnityEditor API、不写资产。
- 输入纪律：面板打开时只捕获自己的焦点与指针；关闭恢复 `EventSystem` 原选中对象；不做全屏 raycast 遮挡块。

### 7.3 拾取与高亮

- 拾取：指针位置经 `EventSystem.RaycastAll` 取首命中 → 节点路径/BindName/Canvas 排序/遮挡者；编辑器可 Ping/选中，Player 内绘制屏幕描边。
- 高亮：调试层绘制选中节点屏幕矩形（红/黄描边），Esc 清除；不改产品 Graphic/材质/颜色。
- 不穿透：拾取只读；需要点击验证时走真实指针模拟或命令，不以拾取代替点击。

## 8. 写回与样式试调

<a id="ch-patch"></a>

写回是**修复路径**，不是第二制作面。默认只读；进入写回必须显式选中目标 prefab、显示 old→new diff、逐属性确认。

| 项 | 约定 |
| --- | --- |
| 目标 | `Assets/ShareResource/UI/{Screens,Widgets,Hud}/**` 的 prefab；scene-only 对象拒绝 |
| 粒度 | 属性级（已有组件的序列化字段）；不做节点/组件增删、资产创建/删除 |
| 载体 | `UiPatch`（diff/属性选择/冲突检查/Undo/归档）；不建第二套格式 |
| 通道 | 编辑器 Play：实例 diff → patch → 标准 prefab；Player/F8：只采集 patch 文件，编辑器导入应用 |
| 冲突 | 应用前复核 prefab 当前内容/`sourceHash`；与采集时不一致即中止并显示冲突项 |
| 审计 | 每次 apply 记录 patch、目标、操作者、结果、Undo 标记；不自动保存、不静默成功 |

**样式试调**：

- 选中节点显示令牌引用/解析值/是否裸值；可编辑白名单：`Graphic.color`、`Image.sprite/type/preserveAspect`、`TMP_Text.fontSize/color/alignment/overflow`、`Selectable.colors`。
- 试调只改实例，不写资产；`采集 Patch` 固化 diff，`应用 Patch` 才落盘。
- **token-bound 属性默认写回 token**（显示影响页数）；“内联落盘”是显式降级，必须确认解除引用并登记样式债，patch 记录原 token ID。
- 展示状态（active/alpha/interactable/blocksRaycasts）只预览、不落盘；避免把调试态写成资产默认值。
- RectTransform/布局、结构、资产创建删除不在写回面——归 Prefab Stage。

## 9. 证据

<a id="ch-evidence"></a>

目录：`Application.persistentDataPath/ui-debug/<session>/`

| 产物 | 内容 |
| --- | --- |
| `shot-<page>-<scenario>-<ts>.png` | 截图 |
| `dump-<page>-<ts>.json` | 构建身份（buildHash/configDigest）、会话、页面 id/prefab、状态、层序/模态、焦点、语言/分辨率、可点性摘要、注入场景、深检摘要、写回记录 |
| `patch-<ts>.uipatch.json` | 属性级 diff：目标 prefab/节点/属性/old/new/token 引用变更 |
| `patch-applied-<ts>.json` | 应用审计：patch、目标、操作者、逐属性结果、Undo、冲突/失败原因 |
| `bundle-<ts>/` | 现场包：manifest、log-recent、player-log-tail（`DiagnosticsBundleWriter`） |

- 本地 only，不上传；不含凭据/密钥。
- 写盘失败只 `Log.Warning`，不阻塞操作。
- dump 字段与现场包共用身份/会话/日志尾，不另拼字段表。
- 证据必须带构建身份与页面上下文；截图/“点过一次”不作自动门禁。

## 10. 与 DevHUD / GM / 日志的边界

- DevHUD 只读；本面可增加一行状态（当前页/遮挡数），不把 HUD 变成写面板。
- GM 页是编辑器富交互投影；Player 内只留本面与 DevMenu 的轻量入口。
- 命令统一走命令中心；高频旋钮（滑杆/连续拖动）不进审计环。
- 日志用 `Log` 门面；结构化日志/现场包由诊断设施提供；本面不另造日志格式。
- `DiagTrace` 等非 UI 诊断归调试手册，不塞进 UI 深检。

## 11. 容量、失败与发布

### 11.1 容量

- 页面目录每次打开读一次表，之后按需刷新；打开中状态 0.25–0.5s 轮询，隐藏即停。
- 深检快照只采当前选中页（绑定/布局/文本/输入），可见时 ≤2–4Hz。
- 可点性快照只在点击 Raycast 或可视化开启时执行。
- 性能段按需采样（ProfilerRecorder）；截图/dump/现场包按需。
- 不新增每帧分配；列表/文本复用缓冲；树/表格折叠或虚拟化。

### 11.2 失败语义

| 情形 | 契约 |
| --- | --- |
| 非 Play/内核未装配 | 窗口显示原因；F8 空态，不启动 |
| `tbuiform` 不可用 | 页面目录错误行；命令拒绝 |
| 页面 id 不存在 | 返回“未找到”，不猜测 |
| 打开/关闭失败/超时 | 显示类型化原因；不重试、不静默 |
| 页面未接入状态场景 | 正常打开，回显“场景未接入” |
| 契约未接入 | 显示绑定实况 + “契约未接入”，不冒充通过 |
| 性能 marker 不可用 | 显示“不可用（版本/平台）”，不伪造数字 |
| 快照过期 | 显示采样时间/冻结状态；可手动刷新 |
| 截图/现场包失败 | Warning；其余操作继续 |

### 11.3 发布

- 仅开发三宏内存在；内核住 `LiteGame.App`，投影住 `LiteGame.DevTool`（单向引用 App）；资产不进收集组。
- release 零残留：类型/资产/命令/启动行均不可达。
- 调试工具自身可用 UI Toolkit（dev-only UXML/USS/ThemeStyleSheet），产品 UI 不迁移。
- 写回仅编辑器应用；Player/F8 只采集 patch；不调用 UnityEditor API、不写资产。
- 不做 release/测试者分发包写回；不做远程/多客户端聚合。

## 12. 验收与阶段

<a id="ch-debug-phases"></a>

| 阶段 | 范围 | 退出条件 |
| --- | --- | --- |
| D0 读模型与页面目录 | 页面/导航/模态/焦点、`ui.list/open/back/close/reopen`、dump | 包里能按目录打开/返回/重开；dump 可复盘；L1/L2 绿 |
| D1 状态注入与可点性 | `UiDebugState`、语言/设备档、`ui.raycast`、截图/现场包 | 五类场景可复现；遮挡一眼可见；证据可复查；面板自身可点 |
| D2 编辑器深检 | `IUiRuntimeDiagnostics` 快照、绑定/契约/布局/文本/样式/输入/性能页签 | 深检与 `UIForm`/`UIBindIndex`/`Canvas` 实况一致；默认只读；隐藏即停 |
| D3 写回与样式 | `UiPatch` capture/apply、冲突/Undo/审计、token vs 内联 | 真实页面属性级写回；Undo 复原；冲突中止；Player patch 可在编辑器应用 |
| D4 载体与收口 | 接入控制台/面板外壳；HUD 状态行；release 剥离验证 | 同一命令集在面板/控制台等价；HUD 只读；release 零残留 |

**验收层级**：

- L1：目录投影、命令解析、状态枚举、dump 字段、可点性判定、patch 模型/diff/冲突/白名单（不依赖 Unity 运行时）。
- L2 PlayMode：真实 `UIService`/导航/命令中心下的开关/返回/重开；状态注入；截图/dump/patch；租约/订阅对称；深检一致；F8/Player 不写资产。
- 编辑器人工：Hub → 选页 → 深检页签 → Ping/高亮 → 写回 diff/Undo/冲突；失焦即停。
- 开发包人工：F8 → 打开 → 切场景 → Raycast → Capture/Dump/Patch → 关闭；面板自身可点。
- release：零残留（类型/资产/命令/启动行）。

## 13. 非目标

- 不做 UI 自动化框架、语义 Locator/Driver、性能预算门禁。
- 不做编辑器内页面制作、绑定生成、布局编辑。
- 不迁移产品 UI 到 UI Toolkit；调试工具自身允许。
- 不做结构级资产编辑、第二个资产真相、独立 patch 格式。
- 不做无预览/无确认/无审计的静默写盘；不做 scene-only 对象写回。
- 不做通用 GM/世界查看器/弱网注入（各为独立件）。
- 不做像素级视觉对比；截图只作人工证据与失败诊断。
- 不改变页面状态机、导航单写者与资源收集目录。
