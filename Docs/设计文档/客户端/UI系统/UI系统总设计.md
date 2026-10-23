# UI 系统总设计（uGUI × 大型/多端）

> 状态：现行领域总设计（目标契约；保留基座以代码为准）
> 版本：1.0
> 日期：2026-10-22
> 定位：回答“大型/多端项目里，UI 资产、页面、运行时、工具与协作如何分层并互相约束”。本文是 UI 领域的唯一架构入口；运行时与绑定的**现行能力**以代码为准，本文只裁决目标契约与边界。
> 不做的事：不替代 Prefab Stage 做布局编辑；不定义具体页面的业务逻辑；不定义 GM/网络/世界 VFX；不把目标能力描述成当前能力；不记录施工状态（归 `Docs/施工进度/`）。
> 冲突裁决：UI 运行时状态机、导航单写者、资源租约、绑定所有权以 `Assets/LiteClient/UI/**`、`Assets/LiteCodeGen/**` 的现行代码为准；工具形态以[UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md)为准；美术/工程接口以[UI 美术协作与交接规范](UI美术协作与交接规范.md)为准；制作硬规则以[UI 制作规范](UI制作规范.md)为准。
> 关联：[UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md) · [UI 运行时调试工具链专项设计](UI运行时调试工具链专项设计.md) · [UI 美术协作与交接规范](UI美术协作与交接规范.md) · [UI 制作规范](UI制作规范.md) · [UI 命名与控件词表参考](UI命名与控件词表参考.md) · [UI 逻辑 API 参考](UI逻辑API参考.md)

## 1. 定位与裁决

### 1.1 本文回答什么

- 一个页面从**资产**到**运行时**再到**验收**，经过哪些层、每层的唯一 Owner 是谁。
- 哪些能力是**保留基座**（运行时框架 + 绑定工具），哪些是**重构面**（编辑器工具链、调试面、样式/效果组织、协作流程、文档）。
- 多端（PC Windows 横屏 + iOS/Android 横屏）如何共用一套 prefab、令牌和组件，而不是复制第二套 UI。
- 页面、HUD、系统反馈面、世界空间 UI 的边界与排序。
- 大型项目必须有的质量门槛：契约、校验、预览、运行时诊断、性能预算与设备矩阵。

### 1.2 保留基座与重构范围

**保留基座（不改公共契约，只允许增量扩展）**：

| 基座 | 代码入口 | 保留理由 |
| --- | --- | --- |
| UI 服务与页面实例 | `Assets/LiteClient/UI/UIService.cs`、`UIForm.cs`、`UIFormStore.cs`、`UIStack.cs`、`UILayerGroup.cs` | 已具备打开合流、取消、缓存、租约、遮盖、状态机与失败回滚 |
| 导航/模态/焦点/输入 | `UINavigationController.cs`、`UIModalStack.cs`、`UIFocusCoordinator.cs`、`Input/UiInput.cs` | 单写者、Back 优先级、模态射线与焦点已收口 |
| 转场与动效原语 | `Transition/UITransitionRunner.cs`、`Anim/UiAnimationClock.cs`、`Anim/UiFx.cs` | 状态机 + 超时 + 时钟 + 唯一 DOTween 入口 |
| 绑定与生成 | `Assets/LiteCodeGen/**`、`Bind/UIBindIndex.cs`、`Bind/UIBindBase.cs`、`Bind/BindIndexBuilder.cs` | 名字绑定、路径 A/B、受控 API、所有权互斥 |
| 控件与列表 | `Widgets/**`、`VirtualList.cs`、`SimpleListView.cs` | 既有控件模板与窗口复用内核 |
| Lua 桥接 | `LuaUI.cs`、`LuaBridge/LuaBehaviourAdapter.cs` | 页面逻辑、白名单、环境重建 |
| 本地化 | `ILocaleBound`、`LTextLabel`、`LText` 服务 | 文案 key、语言切换刷新 |

**重构面（可以彻底重设）**：

- 编辑器工具链：制作工作台、起页/登记、绑定编辑与生成入口、样式工具、校验核心、预览、资源检查。
- 运行时调试工具链：读模型、页面目录、状态注入、可点性、深检、写回、证据。
- 视觉组织：设计令牌、样式引用、组件状态、图集/字体策略的**设计与工具**（运行期 `UiFx` 等保留）。
- 美术/工程协作：角色、所有权、页面契约、交接门、度量。
- 文档体系：旧 UI 设计文档已归档；本套文档为现行权威。

> **扩展原则**：新增能力以“新增程序集/新增组件/新增工具”为主；修改保留基座只能是为修复缺陷或明确记录在本文的增量接缝。任何改变 `UIService`/`UIForm`/`UIBindIndex` 公共语义的改动，必须先改本文并走评审。

### 1.3 关键裁决

| # | 裁决 | 依据 |
|---:|---|---|
| 1 | 产品 UI 继续使用 **uGUI + TMP**，不迁移 UI Toolkit | 现有控件、遮罩、图集、IL2CPP 与制作流程已围绕 uGUI；迁移不带来业务收益 |
| 2 | **Prefab 是视觉唯一真相**；运行时代码不构建视觉结构 | 运行时补建 Canvas/Graphic/Text 会绕过统一缩放、排序与制作校验；唯一例外是引导期错误页（见 §6.6） |
| 3 | **页面契约**是美术→工程的唯一接口，契约 diff 视为 UI API 变更 | 截图只是验收证据，不能替代字段级接口 |
| 4 | **Screen UI**走 `UIService` 导航；**Battle HUD**走对局表现装配，不冒充页面 | 两者的生命周期、数据频率、销毁时机不同 |
| 5 | 页面与 HUD **共用**组件模板、令牌、绑定工具与调试工具 | 大型项目的复用单位是组件与规则，不是整页复制 |
| 6 | 一套 prefab 覆盖 PC 与移动横屏；差异用**设备档/令牌/变体**表达 | 禁止为每个平台复制一套页面资产 |
| 7 | 数据流单向：UseCase/ViewModel → 页面逻辑 → 绑定索引 → 控件 | 不引入跨语言双向 MVVM；同一属性只有一个写者 |
| 8 | 高频数据在 C# 聚合并按脏位刷新；Lua 只做低频编排 | 避免每帧跨语言与逐条事件过 Lua |
| 9 | 视觉样式以**令牌引用**为主；裸颜色/裸字号必须登记例外 | 多端、主题与美术协作都要求单源 |
| 10 | 工具只做**约束、生成、校验、预览**；运行时播放不以打开工具为条件 | 工具是质量杠杆，不是运行期依赖 |
| 11 | 运行时调试默认**只读**；写回是编辑器显式模式，Player 只采集 patch | 防止调试态写成资产默认值 |
| 12 | 旧体系文档全部归档；本套文档是唯一现行入口 | 避免两套契约并存 |

### 1.4 术语

| 术语 | 含义 |
| --- | --- |
| **Screen UI** | 由 `UIService` 管理、有 `tbuiform` 行、可导航/缓存的页面与系统反馈面 |
| **Battle HUD** | 对局表现域内的常驻信息层，由对局视图装配，不走页面导航 |
| **World UI** | 世界空间的血条/名牌/伤害数字等，属表现/VFX 域；只共享字体/令牌，不纳入页面体系 |
| **Widget** | 可复用控件模板 `Tmp_*`，自带视觉、交互与声明式能力，不含业务逻辑 |
| **Screen Template** | 页面级组合模板，如列表页基线、弹窗基线、加载页基线 |
| **Page Contract** | 从 prefab 生成的只读接口描述，供工程接线、校验与 CI diff |
| **Widget Spec** | 控件模板的能力声明：插槽、属性、状态、事件、约束、Owner |
| **Design Token** | 颜色、字体、字号、间距、圆角、阴影、动效时长等设计变量 |
| **Device Profile** | 分辨率、DPI、安全区、宽高比、性能档的组合 |
| **Lane** | 工作台中的角色视图（美术 / TA / 工程），共享同一编辑面与上下文 |
| **基座** | 本文 §1.2 保留的运行时与绑定能力 |

## 2. 目标与非目标

### 2.1 目标

1. **可协作**：美术能独立完成页面视觉与交接物；工程只消费契约与绑定，不反复猜节点名。
2. **可扩展**：新增页面/控件/主题/端，优先通过模板、令牌、契约与工具扩展，而不是复制与特判。
3. **可验证**：每个页面有结构校验、契约对账、布局/本地化预览、运行时诊断与性能预算。
4. **多端一致**：PC 与移动横屏共用 prefab；安全区、DPI、超宽屏、输入方式由统一适配层处理。
5. **可运维**：内容热更、Lua 环境重建、缓存与租约、发布门禁有明确契约。
6. **可诊断**：真包内能回答“页面为什么长这样、为什么点不到、这个名字被谁消费、性能花在哪”。

### 2.2 非目标

- 不替换 uGUI、不自研 UI 渲染器或 batcher。
- 不引入 UI Toolkit 作为产品 UI 资产格式（调试工具自身可用，见调试专项）。
- 不构建跨语言双向 MVVM、运行时反射式数据绑定或任意 prefab 热插拔。
- 不把 Prefab Stage 替换成自研可视化画布。
- 不为假想需求预建主题市场、动画时间轴或 Figma 导入；出现真实消费者再立专项。
- 不让工具成为运行期依赖；不把编辑器专用类型/资产带入 release。

## 3. 架构分层与边界

### 3.1 六层模型

```text
L0 Foundation   设计令牌 / 字体 / 图集 / 图标 / 九宫格 / 动效曲线
L1 Component    Tmp_* 控件模板 + WidgetSpec（插槽/属性/状态/事件）
L2 Composition  Tmp_* 页面模板 + Page_*/Hud_* 业务组合
L3 Runtime      UIService / UIForm / 导航 / 层序 / 转场 / 缓存 / 绑定 / 列表
L4 Tooling      编辑器工作台 / 校验 / 契约 / 预览 / 运行时调试
L5 Process      角色 / Lane / 交接 / 评审 / 度量 / CI
```

依赖方向只能自上而下：**组合依赖组件，组件依赖基座；工具读取资产与运行时，不被运行时依赖**。反向依赖（组件引用具体页面、运行时引用编辑器程序集、release 引用调试类型）一律禁止。

### 3.2 三条产品轨

| 轨 | 载体 | 生命周期 | 数据频率 | Owner |
| --- | --- | --- | --- | --- |
| Screen UI | `UIService` + `tbuiform` | 打开/关闭/缓存/销毁 | 低频交互 + 离散事件 | 工程（接线），美术（视觉） |
| Battle HUD | 对局视图装配（`BattleHudView`/Rig） | 随对局视图创建/销毁 | 高频快照/脏位 | 工程（接线），美术+TA（视觉） |
| World UI | 表现/VFX 系统 | 随实体/特效 | 渲染帧 | 表现工程 |

三者共享：控件模板、令牌、字体、绑定工具、校验规则、调试读模型。三者不共享：页面目录、导航栈、缓存策略、生命周期回调。

### 3.3 职责矩阵

| 面 | 设计 | 资产 | 运行时 | 工具 |
| --- | --- | --- | --- | --- |
| 令牌/字体/图集 | TA | TA | 消费 | TA + 工程 |
| 控件模板 | TA + 美术 | 美术 | 消费 | TA + 工程 |
| 页面模板 | 美术 + 工程 | 美术 | 消费 | 工程 |
| 业务页面 | 美术（视觉）/工程（逻辑） | 美术 | 工程 | 双方 |
| 页面契约 | 工程 | 生成物 | 消费 | 工程 |
| 导航/缓存/转场 | 工程 | — | 工程 | 工程 |
| 校验规则 | 工程 + TA | — | — | 工程 |
| 运行时调试 | 工程 | dev-only | 读运行时 | 工程 |

### 3.4 目录蓝图

```text
Assets/ShareResource/UI/
  Screens/        Page_*.prefab（业务页）、Tmp_*（页面模板/系统面）
  Widgets/        Tmp_*.prefab（控件模板）
  Hud/            Hud_*.prefab（战斗 HUD 组合）
  Icons/          图标源/图集输入
  Patches/        仅编辑器/dev 的 UIPatch 输出（不进包）
Assets/LiteClient/UI/        运行时基座（不改结构）
Assets/LiteGame/App/Ui/      页面流程适配（PageFlow/ResultRanking 等）
Assets/LiteGame/Lua/UI/      Lua 页面逻辑
Assets/LiteGame/Editor/UiTools/  编辑器工具链（可重构）
Assets/LiteGame/DevTool/     运行内只读 HUD/菜单（dev-only）
Docs/设计文档/客户端/UI/      本套设计文档
```

> 资源入库/收集规则仍按 `AGENTS.md`「资源规范」与[资源收集目录专项设计](../内容/资源收集目录专项设计.md)；UI 文档不复制那份规则。

## 4. 保留基座

### 4.1 运行时基座

<a id="ch-runtime-base"></a>

**UIService / UIForm**：

- 页面实例状态：`Loading / Active / Covered / Paused / Closing / Recycled / Disposed`。
- 首次打开：加载 → 实例化 → `PrepareForShow` → `OnInit` → `OnShow` → 转场 → `Active`。
- 复用：`Recycled → PrepareForShow → OnShow → Active`，不重跑 `OnInit`（换表重建除外）。
- 打开合流：同页并发 `ShowAsync` 共享一次工作；数据不同返回 `Busy`；调用方取消只取消本人等待。
- 关闭：`CloseAsync(reason)`；系统原因跳过出栈拦截与离场；`CloseAllOpen` 覆盖 Covered/Paused；`ShutdownAsync` 幂等。
- 展示作用域：每次打开重建 `ClientScope`，关闭时取消异步、逆序释放订阅；`DisplayGeneration` 用于迟到结果丢弃。
- 输入与遮盖：`CanvasGroup` 的 `alpha/interactable/blocksRaycasts` 由实例与协调者共同决定；页面不得自行假设 `interactable=true` 就是可点。
- 静态文案接缝（增量）：`PrepareForShow` 惰性创建 `UiStaticTextBinder`（扫描页面根下 `LTextKeyRef`，无标记时零订阅）；语言服务经同一 `LocaleProvider` 注入，一次驱动 `ILocaleBound` 与静态文案；`EnterClosing` 退订、`DestroyInstance` 释放——池化复用不得继续被旧语言事件刷新。

**UINavigationController**：

- 产品导航唯一入口；Go/Replace/Back 串行排队；队列容量与超时可观测。
- Back 优先级：Loading 阻断拦截 → 最顶模态 → 最高非空层栈顶 → 页面关闭守卫 → 平台退出。
- `UIService.ShowAsync` 仍可被工具/测试直接调用；产品代码不得绕过导航器做同帧连续开关。

**UIModalStack / UICoverageResolver / UIFocusCoordinator**：

- 模态有真实射线遮罩；模态登记与页面开关解耦，登记幂等。
- 遮盖从“仍逻辑打开且有遮盖能力”的页面推导，不只看 Active。
- 焦点保存/恢复、首个可交互件聚焦、平台返回轮询由协调者负责；页面不得抢焦点。

**UITransitionRunner / UiAnimationClock / UiFx**：

- 转场是表现阶段，不决定页面是否存在；结果区分 Completed/Skipped/TimedOut/Cancelled/Failed。
- 输入锁在转场接受时生效，恢复由协调者计算，不无条件写 `blocksRaycasts=true`。
- UI 动效只走 `UiFx`（唯一 DOTween 入口）与 `UIClock`；转场策略是 `UiFx` 的组合消费者。

**资源与缓存**：

- `IUIPrefabLease` 是硬边界；生产用 `ContentPrefabLease`，测试/工具用 `Unowned`。
- 缓存策略：Resident / LRU / DestroyOnClose；Resident 也计入预算；只淘汰未打开、未被操作引用的实例。
- 缓存键至少包含 formId 与内容版本；异步图标/头像使用临时租约 + 代次。

### 4.2 绑定基座

<a id="ch-binding-base"></a>

- **路径 A（运行时标记）**：`BindNode.BindName` → `BindIndexBuilder.Build` → `UIBindIndex`，按名字取控件。
- **路径 B（生成类）**：`BindRoot` + `BindNode` → `BindGenerator` 生成 `Xxx.cs` / `Xxx.Designer.cs`；Designer 调 `RegisterControl`，`UIBindBase` 优先使用登记字典。
- **所有权互斥**：同一控件只能被数据绑定或命令式之一驱动；Debug 三宏下违例当场抛。
- **生命周期**：按钮、输入控件、列表行、本地化 key 的订阅随 `UnbindAll`/展示作用域清理；池化复用不得留旧回调。
- **受控 API**：`SetText/SetTextKey/SetVisible/SetInteractable/SetProgress/SetHp/StartCountdown/SetSlider/SetToggle/SetDropdown/SetList/OnListClick/UiFx` 等；详见[UI 逻辑 API 参考](UI逻辑API参考.md)。
- **绑定面遍历单源**：编辑器侧 `UiBindSurfaceWalker` 一次深度优先给出「本页/本模板」归属（绑定根、`Clone_` 子树、嵌套模板实例、模板内部件）；绑定 hash、页面契约生成与校验共用同一遍历，禁止各写一份。

**生成物纪律**：生成文件禁手改；源 prefab 的绑定面 hash 与生成物清单由工具记录；CI 复算，漏生成即红。

### 4.3 扩展原则

1. 新控件优先做成 `Tmp_*` + 可选组件，不把业务逻辑塞进模板。
2. 新运行时能力优先做**独立服务/组件**，通过构造注入接入 `UIService`，不把分支塞进 `UIForm`。
3. 新工具优先做 Hub 工具 + CLI + 规则，不新增顶层菜单或第二份索引。
4. 任何破坏公共契约的改动，先在本文写“现行 → 目标 → 迁移”，再实施。

## 5. 运行时模型

### 5.1 页面目录与身份

<a id="ch-page-catalog"></a>

- **运行时目录**：`Luban/Data/#uiform.xlsx` → `UIFormInfo`（`id`/`lua_path`/`prefab`/`layer`/`full_screen`）。运行时只认这五列，新增列不进运行时。
- **页面 ID 段**（推荐）：

| 段 | 用途 |
| --- | --- |
| 1–99 | 常驻/框架页（登录、大厅、房间、结算、设置、战绩；历史样例 `UIMain` 已于 2026-10-23 退役，id 1 不再使用） |
| 100–199 | 战斗/对局相关页面（非 HUD） |
| 200–299 | 系统反馈面（Dialog/Loading/Toast/Error） |
| 300–399 | 活动/商店/背包等业务扩展 |
| 900–999 | 开发/调试专用页（不得进 release 目录） |

- **页面清单（页面关系）**：登录 → 大厅 → 房间 → 对局 → 结算 → 大厅；设置可从大厅/对局返回；战绩从大厅进入；加载/Toast/Dialog 是系统面，不占业务导航历史。
- **编辑器清单**：`UiPageManifest`（工具维护的注册表，不进运行时）记录页面 Owner、契约路径、设计稿链接、设备档、状态（五态词表与迁移表见[工具链专项设计 §8.5](UI编辑器工具链专项设计.md#ch-page-manifest)）。运行时不得读取该清单。
- **命名**：`Page_<Semantic>`、`Hud_<Semantic>`、`Tmp_<Widget>`；详见[UI 制作规范 §命名](UI制作规范.md#ch-naming)。

### 5.2 生命周期与状态机

<a id="ch-lifecycle"></a>

| 状态 | 含义 | 允许操作 | 回调 |
| --- | --- | --- | --- |
| `Loading` | 资源加载/实例化中 | 可取消 | — |
| `Active` | 展示中、可交互 | Close/Pause/覆盖重算 | `OnShow`（首次与复用） |
| `Covered` | 被更高全屏页遮盖，仍逻辑打开 | Close/Pause | `OnCover` |
| `Paused` | 手动暂停 | Close/Resume | `OnPause` |
| `Closing` | 关闭收尾/离场中 | 幂等 Close | `OnHide` |
| `Recycled` | 池中缓存，未显示 | Show 复用/Destroy | — |
| `Disposed` | 已销毁终态 | 不可复用 | — |

**契约要点**：

- `OnInit` 只在首次打开（或逻辑换表重建）调用；`OnShow` 在首次打开与复用打开时调用。
- 当前实现中 `EnterActiveFromPaused` 调用的是 `OnShow(null)`，不是独立的 `OnResume`。页面逻辑必须把 `OnShow` 视为“重新显示”而非“只首次”；需要区分首次/恢复时用页面自己的标记。
- `OnCover`/`OnReveal` 只表达遮盖状态，不替代 `OnHide`/`OnShow`。
- `OnHide` 之后展示作用域释放；页面不得在 `Closing/Recycled` 再订阅事件（`SubscriptionBag` 会护栏）。
- 初始化失败由 `UIService` 回滚；页面不得以“空逻辑 + Active”伪装成功。

### 5.3 打开/关闭事务

- **打开**：校验请求 → 取/建实例 → 持租约 → `OnInit`（首次）→ 复位视觉 → 入栈/排序 → 遮盖/模态重算 → `OnShow` → 入场转场 → 交付。
- **关闭**：出栈拦截（仅用户语义）→ 离场转场（系统语义跳过）→ `OnHide` → 释放展示作用域/订阅 → 落池 → 遮盖/模态重算 → 缓存记账/淘汰。
- **同组全屏替换**：新页初始化可提交后，旧页在转场收尾关闭；打开失败恢复旧页。
- **重入**：生命周期回调内发起的新操作进入服务队列/安全调度点，不在枚举集合时直接改集合。

### 5.4 导航与返回

- `GoAsync` 保留旧页（覆盖）；`ReplaceAsync` 关闭当前记录；`BackAsync` 返回上一条有效记录。
- 显示栈决定层序，导航记录决定返回历史；两者不能混用。
- 业务页面不得自己维护返回栈；页面内的多步状态用页面模型/子状态表达。
- 返回结果通过导航记录或页面模型恢复，不调用 `OnShow` 代替“返回结果”。

### 5.5 层级、排序、遮盖、模态、焦点与输入

<a id="ch-layers"></a>

**Screen UI 语义层**（`tbuiform.layer`）：

> 下表是**现行基座**的组基深（迁移起点），不是目标带；目标带见下方统一排序带。

| 索引 | 层 | 典型内容 | 排序带 |
| ---: | --- | --- | --- |
| 0 | Bottom | 常驻框架、登录、大厅、房间、对局入口 | 100–199 |
| 1 | Window | 主功能页、设置、战绩、商店 | 200–299 |
| 2 | Top | 详情、浮层、非模态面板 | 300–399 |
| 3 | System | Dialog、Loading、Toast、Error | 400–499 |

- 组内“开序即深序”，`UILayerGroup.DepthStride=100`；容量不足拒绝，不复用 order。
- **HUD 排序带**独立于 Screen UI：目标 HUD 1000–1999，位于世界之上、Screen UI 之下；System 反馈（5000–5999）盖住 HUD，见 §8.4 的最终带表。
- **统一排序带（目标）**：World 0–999；HUD 1000–1999；Screen UI Bottom/Window/Top 2000–4999；System 反馈 5000–5999；DevHUD/调试 10000+。现行 `UIService` 的组基深是 100/200/300/400（构造时固定）；目标带需在保留基座的层策略/构造接缝上**增量扩展**（例如为组增加 base offset），不在本文假装已生效。
- **遮盖**：全屏页只遮盖严格更低层；同组全屏互斥；Covered/Paused 仍可关闭；关闭最高全屏时先确定下一遮盖源。
- **模态**：模态有真实射线遮罩；`TopModalId` 决定 Back 优先；模态登记与页面状态解耦。
- **焦点**：页面打开聚焦首个可交互件；关闭恢复保存焦点；键盘/手柄/平台返回统一由 `UIFocusCoordinator` 处理。
- **输入**：`UiInput.EnsureEventSystem` 幂等建位；页面不得自建 EventSystem；自定义控件必须遵守上层输入锁，不能只信自身 `interactable`。

### 5.6 转场

- Push/Pop/Replace 由导航操作决定；动画策略不决定页面是否存在。
- 转场可取消、可超时（默认 2s）、可降级为立即完成；迟到回调受代次保护。
- 输入锁在转场接受时生效；转场结束由协调者恢复输入，不直接写 `blocksRaycasts=true`。
- 转场参数（时长/曲线/位移）来自令牌与 `UiFxDefaults`；页面不得写魔法数。

### 5.7 缓存与资源租约

- 默认单实例/页；多实例页用独立 `instanceId`，不覆盖 formId 字典。
- 缓存预算默认 16；Resident 计入总预算；低内存/账号切换/内容版本切换/Scope 退出触发清理。
- Prefab 租约从加载持有到最后一个实例销毁；缓存实例也算使用者。
- 异步图标/头像：临时租约 + 展示代次，替换时对称释放；列表复用先解绑旧租约再绑新数据。

### 5.8 数据流与更新分级

<a id="ch-data-flow"></a>

```text
UseCase / Service / Sim Snapshot
        │  (低频事件 / 高频快照)
        ▼
ViewModel / HudModel (C#)  ──高频脏位──►  绑定索引 / 控件
        │  (低频命令)
        ▼
Page Logic (C# UIBindBase 或 Lua)
        │
        └──►  UIBindIndex (SetText/SetList/SetHp/...)
```

| 数据类 | Owner | 刷新时机 |
| --- | --- | --- |
| 静态 | 页面初始化数据 | `OnInit`/内容版本变化 |
| 离散事件 | C# 页面模型或 Lua | 值变化后刷新；重复值跳过 |
| 高频 HUD | C# `HudModel` 聚合 | 逻辑输入/快照提交后记脏位，渲染帧 flush 一次 |
| 视觉连续 | C# ticker + `UIClock` | 渲染帧；血条插值/倒计时/飘字 |
| Lua 低频编排 | Lua 页面 | 默认 `OnUpdate` 关闭；需要时 10Hz 上限 |

- 同一属性只有一个写者；绑定与命令式互斥。
- 列表用 `SetList` 整表替换 + 稳定 key；禁止每个滚动帧跨 Lua `Bind`。
- 页面关闭时取消展示期异步；迟到结果用 `DisplayGeneration` 丢弃。

### 5.9 绑定与控件驱动

- `BindNode` 只挂在**暴露给逻辑**的节点；模板内部件不暴露（`Clone_` 条目不进索引）。
- `BindName` 页面内唯一；默认与节点名同文（前缀_语义），作为契约 `controls[]` 的 API 名。允许为 API 稳定性显式指定独立名，但契约/引用必须使用 `BindName`，不得随视觉改名漂移。
- 路径 A 用于 Lua/轻量页；路径 B 用于 C#/高频/复杂页。选择规则见[UI 编辑器工具链专项设计 §绑定](UI编辑器工具链专项设计.md#ch-binding)。
- 生成类只由生成器写；手写逻辑在 partial 主文件；生成物 hash 与清单由工具维护。
- 控件内部状态（Slider/Toggle/Dropdown）用 `Set*WithoutNotify` 程序写入；用户操作才触发回调。

### 5.10 列表与异步资源

- `VirtualList` 用于大列表（阈值建议 20 条起），`SimpleListView` 只用于小列表。
- 节点数 = 可见容量 + overscan；固定尺寸单轴优先；Grid/变高按真实消费者扩展。
- 行绑定：按子节点名寻址；重绑先解绑旧回调/租约，再写新数据；异步头像核对稳定 key 与代次。
- 滚动监听一次绑定、对称解绑；重复 Refresh 不增长监听。

### 5.11 错误、反馈与降级

<a id="ch-feedback"></a>

- **错误契约**：已关闭服务再调用抛 `ObjectDisposedException`；程序员错误 fail-fast；内容缺失 `Log.Warning` + 默认值。
- **反馈面统一**：Dialog/Loading/Toast/Error 全部归 System 层，由 `FeedbackService` 驱动；不得在服务内自建 Canvas/Toast，与模板并存。
- **Toast 多条并存**：按序堆叠、各自计时、上限淘汰；承载控件负责排布，服务不逐条摆位。
- **Loading**：可由启动预载/场景加载/进房等待复用；必须支持不定态、阶段文案、取消与失败重试。
- **错误页**：引导期资源不可用时允许代码构建兜底页；常规流程可达的界面必须走模板（§6.6）。
- **失败结果**：打开失败区分 LoadFailed/InitFailed/Rejected/Busy/Canceled/Timeout；调用方必须处理，不得吞掉。

### 5.12 热更与环境重建

- 正式热更遵循[热更与内容发布专项设计](../内容/热更与内容发布专项设计.md)；UI 只消费内容服务的结果。
- Lua 环境重建前必须 `UIService.DropAllLogic()`（释放旧 Lua 引用），再重建 env；池中页面复用前重新解析并补跑 `OnInit`。
- 内容版本切换时清缓存实例或按版本键隔离；不得复用旧 env 的对象。
- 调试补丁只走 `UiPatch`，不形成第二套资产真相。

## 6. 视觉与样式体系

<a id="ch-style"></a>

### 6.1 设计令牌

令牌分七组，全部有稳定 ID：

| 组 | 示例 | 用途 |
| --- | --- | --- |
| Color | `color.bg.screen`、`color.text.primary`、`color.brand.accent` | 背景/文字/描边/状态 |
| Typography | `font.body`、`font.title`、`font.caption`、`font.numeric` | 字体族/字号/字重/行距 |
| Spacing | `space.4`、`space.8`、`space.16`、`space.24` | 间距/内边距 |
| Radius | `radius.s`、`radius.m`、`radius.pill` | 圆角 |
| Elevation | `elev.1`、`elev.2` | 阴影/描边层级 |
| Motion | `motion.fast`、`motion.normal`、`motion.slow`、`ease.standard` | 时长/曲线 |
| Icon | `icon.size.s`、`icon.size.m`、`icon.size.l` | 图标尺寸/描边 |

- 令牌源为 JSON（设计态），生成 `UiThemeAsset`（Unity 读面）；运行时通过 `UiStyleResolver` 解析，不读 JSON。
- 令牌 ID 用点分层级；改名需提供迁移映射与旧 ID 别名，至少保留一个次版本。
- 主题/变体只在真实需求出现时增加（如高对比、活动主题）；首版只有 Default。

### 6.2 引用与应用

- prefab 上的颜色/字号/间距优先挂**引用组件**（如 `UiStyleRef`/`UiTextStyleRef`），而不是裸值。
- 编辑期 Applier 按令牌批量应用；应用幂等、可 Undo、有报告。
- 运行期只读解析；主题切换由 `UiThemeBinder`（可选）驱动，不允许页面自行改全局材质/颜色。
- 状态色（Normal/Hover/Pressed/Disabled/Selected）由组件 `Selectable.colors` 或样式状态表定义，不在业务代码里写 RGB。
- 裸值必须能被校验器识别并登记例外（Owner/原因/到期），不得静默通过。

### 6.3 字体与文本

- 统一 TMP SDF；中文主字体为 NotoSansSC SDF（已入库），fallback 链按语言配置。
- 字号来自 Typography 令牌；正文/标题/说明/按钮/数字分角色，不按页面随手改字号。
- 静态文案走 `LTextKeyRef`（目标组件）或美术在 prefab 上挂 key；动态文案走绑定 API；同一节点只能有一个文案写者。
- key 命名：`UI.<页面>.<语义>` / `Common.<语义>`；占位符按编号/语义校验；英文 one/other 显式选择。
- 玩家/外部文本默认禁富文本；作者文本只允许白名单标签。
- 字体缩放/多语言长文本必须进入预览矩阵；按钮关键信息不得依赖截断。

### 6.4 图标、图集与九宫格

- 图标进 `Icons` + SpriteAtlas；按模块/页面加载边界分组，不做一个全 UI 大图集。
- 普通图标 `Image.Type=Simple`；可拉伸背景 `Sliced`；进度 `Filled`；九宫格边界由美术定义并进校验。
- 图集分组遵循“同时可见 + 同时加载”原则；跨页共享的控件模板放共享组，页面独有素材放页面组。
- 第三方图标/素材必须复制进 `ShareResource` 后再引用；禁止直接引用第三方包路径。

### 6.5 组件状态与变体

- 每个控件模板声明状态：Normal/Hover/Pressed/Disabled/Selected/Loading/Empty/Error（按需）。
- 状态用 Animator 之外的显式组件/`UiFx`/`Selectable` 驱动；UI prefab 默认禁用 Animator（例外登记）。
- 变体（Variant）只用于同一控件的尺寸/风格差异；业务页面不得成为模板的 Variant（避免跨 owner 继承）。
- 模板插槽用命名容器（`Cot_Header`/`Cot_Body`/`Cot_Footer`），插槽内容由页面填充；模板不读取业务数据。

### 6.6 视觉单一来源与例外

- 用户可见 UI 视觉一律来自 `Assets/ShareResource/UI/{Screens,Widgets,Hud}` 的 prefab。
- 运行时不得 `new GameObject` 造 Canvas/Graphic/Text/Button，不得 `AddComponent` 挂视觉组件。
- 层容器节点（`UIRoot`/层节点）与输入底座（`EventSystem`）不属于视觉结构，允许运行时建位。
- **唯一例外**：引导期错误页，资源系统不可用时可用代码构建；必须满足：
  1. 仅初始化不变量失败路径；
  2. 有冒烟标记/构建证据；
  3. 不得被常规流程复用。
- 测试夹具从测试专用 prefab 加载，不拼装视觉。

## 7. 多端适配与输入

<a id="ch-adaptation"></a>

### 7.1 平台矩阵

| 端 | 形态 | 首版要求 |
| --- | --- | --- |
| PC Windows | 横屏，可窗口缩放 | 1920×1080 基准；16:9/16:10/21:9；鼠标+键盘 |
| iOS | 横屏，刘海/灵动岛/安全区 | 安全区、DPI 分档、触控热区 ≥44pt |
| Android | 横屏，挖孔/虚拟导航条 | 安全区、分辨率碎片、低端机性能档 |
| 主机/手柄 | 预留 | 焦点导航、Submit/Cancel、10-foot 可读性（不纳入首版验收） |

### 7.2 Canvas 与缩放

- 根 Canvas：`ScreenSpaceOverlay` + `CanvasScaler(ScaleWithScreenSize)`，参考 1920×1080，`match=0.5`。
- 页面根必须自带 Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup（见制作规范）；运行时不得补建。
- 子 Canvas 只用于高频重绘区/独立遮罩/独立排序；子 Canvas 继承根缩放，不重复配置 CanvasScaler。
- 移动端 DPI 高：以逻辑分辨率 + 令牌字号为准，不按物理像素写死；最小可读字号由设备档校验。
- 超宽屏：背景铺满、内容区用安全容器限制；不把内容拉伸到全屏宽度。

### 7.3 安全区与屏幕形态

- `SafeAreaReceiver` 作用于可交互内容容器；背景可铺满。
- 安全区在分辨率/方向/刘海变化时重算；页面不得在 `OnShow` 只算一次。
- 设备档至少覆盖：16:9、19.5:9、20:9、4:3、16:10、超宽 21:9；横屏为主，竖屏预留。
- 预览工具提供设备档切换（见工具链专项）；真机验收必须用真实包。

### 7.4 输入抽象

- 统一走 Input System；`UiInput` 建 EventSystem/InputSystemUIInputModule。
- 鼠标/触控：点击热区不小于令牌最小值；禁止 hover-only 操作；长按/拖拽有替代路径。
- 键盘/手柄：焦点可见、导航顺序明确、Submit/Cancel 映射统一；模态拦截 Back。
- 输入锁：模态/转场/暂停由协调者计算；页面只读 `interactable/blocksRaycasts` 结果，不自行改写全局输入。
- 调试面板自身也必须可点（可点性自检）。

### 7.5 可访问性

- 字体缩放档（100%/115%/130%）至少覆盖关键页面；布局用容器/换行而非固定宽。
- 减少动效：尊重质量档/系统设置；转场降级为立即完成，动效不阻塞操作。
- 对比度：文字/背景达到项目基线；状态不能只靠颜色区分（图标/文案/形状辅助）。
- 触控热区、焦点描边、屏幕阅读（平台能力）作为目标端验收项；不在首版承诺全平台读屏。

### 7.6 平台差异

- 纹理压缩/图集变体按平台生成；不为平台复制 prefab。
- Shader 能力（遮罩/裁剪/透明度）在目标管线验证；不假设“UI/Default 继承”字符串。
- 移动端内存/发热：UI 图集与字体 atlas 有预算；低端档降级粒子/阴影/动效。
- PC 窗口缩放：布局不跳变；最小窗口尺寸有下限。

## 8. 战斗 HUD

<a id="ch-hud"></a>

### 8.1 定位

战斗 HUD 是**对局表现域**的常驻信息层，不是 `UIService` 页面：

- 不占 `tbuiform` 行，不走 Go/Back，不进页面缓存。
- 由对局视图装配（`BattleHudView`/Rig）创建与销毁；随对局结束/退出清理。
- 与 Screen UI 共用控件模板、令牌、字体、绑定工具、调试读模型。
- 世界空间 UI（伤害数字/名牌）归表现/VFX，不纳入 HUD 页面体系。

### 8.2 装配与生命周期

- HUD prefab 住 `Assets/ShareResource/UI/Hud/Hud_*.prefab`，根名 `Hud_<Semantic>`。
- 对局视图装配时实例化并解析序列化引用；禁止字符串路径 `Find`。
- 对局结束/退出时停止 ticker、解绑事件、释放租约；不得留到下一局。
- HUD 的 Canvas 与排序带独立定义（目标 1000–1999），不参与 `UIService` 层组。

### 8.3 数据流与刷新

- 高频数据由 C# `HudModel` 聚合：血量/弹药/冷却/击杀/网络状态；按脏位在渲染帧 flush。
- 每帧只更新变化项；稳定帧零分配、零字符串拼接。
- Lua 不参与高频 HUD；需要脚本化时只做低频编排（击杀播报、提示）。
- 数据来源是快照/逻辑帧，不读 UI 文本决定玩法。

### 8.4 层级与排序

**目标排序带**：

| 带 | 范围 | 内容 |
| --- | ---: | --- |
| World | 0–999 | 世界/角色/场景 |
| HUD | 1000–1999 | 战斗 HUD、准心、血条、击杀播报 |
| Screen Overlay | 2000–4999 | 页面/面板 |
| System Feedback | 5000–5999 | Loading/Toast/Dialog/Error |
| Debug | 10000+ | DevHUD/调试 Overlay（dev-only） |

> 现行 `UIService` 基深 100/200/300/400 与 HUD 的 0 排序是迁移起点；目标带通过层策略与 HUD 根 Canvas 配置落地，不在本文假装已经生效。

### 8.5 与页面/控件的复用边界

- 血条/准心/播报等做成 `Tmp_*` 控件模板，HUD 组合使用；页面也可复用同一模板。
- HUD 组合不放进 `Screens`，页面不直接实例化 HUD prefab。
- HUD 的交互（设置、地图、退出）走页面/系统面，不在 HUD 内自建导航。
- HUD 的视觉改动遵守同一套令牌/校验/契约；HUD 的契约是 `HudSpec`（控件暴露 + 数据源），不是页面导航契约。

### 8.6 性能预算

- HUD 稳定帧 CPU/GC/DrawCall 独立记账；血条/数字收敛后停止写入。
- 粒子/描边/材质实例数量有上限；低端档降级。
- HUD 诊断接入运行时调试读模型（活跃句柄、刷新计数、Canvas rebuild）。

## 9. 动效与效果

<a id="ch-motion"></a>

### 9.1 分层

| 层 | 载体 | 例子 |
| --- | --- | --- |
| 转场 | `UITransitionRunner` + 策略 | 页面 Push/Pop/Replace |
| 控件动效 | `UiFx` | 按钮脉冲、图标闪烁、面板滑入 |
| UI 粒子 | 模板化粒子/效果宿主 | 奖励爆发、升级光效 |
| UI 音效 | `AudioService.Group.Ui` | 点击、确认、错误 |
| 世界 VFX | 表现/VFX 域 | 命中、爆炸、场景特效 |

### 9.2 播放契约

- 所有 UI 动效走 `UiFx`；不直接 `DOTween`，不挂 Animator（例外登记）。
- 动效只写表现属性（透明度/位置/缩放/颜色），不携带判定，不改业务数据。
- 动效绑定展示作用域：页面关闭/隐藏即杀；迟到回调用代次丢弃。
- 同一属性同一时刻只有一个动效写者；业务写入与动效冲突时显式协调（先 Kill 再写）。

### 9.3 定义与预算

- 时长/曲线来自 Motion 令牌；默认值集中在 `UiFxDefaults`，Lua shim 与 C# 同源。
- 效果定义声明：目标、通道、时长、优先级、上限、降级策略。
- 同时可见粒子系统数、UI 音效并发数有预算；低端档降级为静态/无粒子。
- 减少动效模式：转场立即完成，控件动效跳过或缩短。

### 9.4 生命周期

- 页面转场与控件动效都使用 `UIClock` 语义；暂停时一致暂停/恢复。
- 关闭页面先停止动效再复位视觉；不得让动效把已关闭页面写回可见。
- 调试工具可暂停 UI 时钟、查看活跃句柄数；release 剥离。

## 10. 内容、资源与发布

<a id="ch-content"></a>

### 10.1 收集与依赖

- YooAsset 收集组按 `Screens`/`Widgets`/`Hud`/`Icons`/`Fonts` 划分；收集路径不互相包含。
- 页面 prefab 依赖控件模板与图集；模板改动触发依赖页面的内容版本变化。
- 页面不得引用场景对象；不通过 `Resources.Load`/字符串路径加载 UI 资源。
- 第三方资源先复制进 `ShareResource` 再引用；付费资源不入库（按 `AGENTS.md`）。

### 10.2 图集、字体、变体

- 图集按加载边界分组；页面/模块共享的控件素材放共享组。
- 字体 atlas 与 fallback 链按语言配置；缺字有审计与降级。
- 平台变体（压缩/分辨率）用 YooAsset 变体或构建参数表达，不复制 prefab。

### 10.3 表与清单

- 运行时页面目录：`tbuiform` 五列，不改运行时不加列。
- 编辑器页面清单：`UiPageManifest`（工具生成）记录 Owner/契约/设计稿/设备档/状态；不进包。
- 控件清单：`UiWidgetManifest`（工具生成）从 `Tmp_*` + WidgetSpec 汇总；供工作台/校验/文档使用。
- 表数据与代码生成物分别走 Luban/生成器；UI 文档不复制表结构。

### 10.4 热更与版本

- UI prefab/图集/字体作为内容包发布；脚本/Lua 按热更专项的窗口激活。
- 页面契约 `sourceHash` 与绑定生成物 hash 进入 CI；内容包与代码版本不匹配时拒绝进入。
- 热更失败保留已发布版本；环境重建清旧实例引用。

### 10.5 发布门禁

- release 不包含：编辑器工具、调试读模型、F8 面板、dev-only UITK 资产、UIPatch 目录。
- 构建产物检查：无调试资源、无 `[UiDebug]` 启动行、无 dev 命令可达。
- 启动冒烟覆盖：登录→大厅→房间→对局→结算；关键页面可开、可点、可返回。

## 11. 性能、质量与观测

<a id="ch-quality"></a>

### 11.1 预算

| 指标 | 目标/约束 |
| --- | --- |
| 稳定帧 UI CPU | 参考场景 p95 ≤2ms；记录 p99 与尖峰 |
| 稳定无变化帧分配 | UI 热路径 0 B/帧 |
| 打开主线程工作 | 单帧实例化+初始化目标 ≤16ms；结算页可另记 ≤32ms，但不默认允许卡顿 |
| 单页 DrawCall | 初始参考 ≤25；结合同屏页面总量验收 |
| Overdraw | 热点截图/GPU 指标确认；不设固定层数上限 |
| 列表 | 节点随视口容量；HardCap 不作为虚拟化替代 |
| HUD | 稳定帧刷新只碰变化项；粒子/音效并发有上限 |
| 内存 | 图集/字体/缓存实例有预算；缓存淘汰可观测 |

### 11.2 测试分层

| 层 | 范围 | 判定 |
| --- | --- | --- |
| L1 纯逻辑 | 请求合并、队列拒绝、导航历史、深度分配、缓存淘汰、代次/取消、契约 diff、patch 模型 | 一请求一终态；排序不重叠；所有权对称 |
| L2 EditMode | prefab 根契约、命名、绑定、资源引用、本地化 key、令牌、契约、hash | 正例绿 + 违规负例红；报告可定位资产/节点 |
| L2 PlayMode | 真 `UIService` + 真 Lua/C# 页面开关复用、取消、三层遮盖、模态输入、列表滚动、语言切换、DevReload、HUD 刷新 | 可见可点且数据正确；无迟到写入；清理后订阅/对象归零 |
| L4 Player/真机 | 真实 AB/内容模式、低内存、前后台、热更失败恢复、长文本/安全区、长时操作、性能 | 达到预算并有构建/日志/Profiler 证据 |

### 11.3 设备矩阵

- PC：1920×1080、2560×1440、超宽 3440×1440、窗口 1280×720。
- iOS：刘海/灵动岛/无刘海各一档；低端/高端各一档。
- Android：16:9/19.5:9/20:9；低端/中端/高端各一档。
- 每档记录：分辨率、DPI、安全区、质量档、构建类型、页面/数据规模。

### 11.4 观测点

- ProfilerMarker：Show/Init/Commit/Close/Dispose、Lua 回调、转场、列表 Bind/滚动、HUD flush。
- 计数：打开/加载/缓存数、租约/订阅/动画数、队列长度/拒绝/超时、列表节点数、内容版本、最近失败。
- UI 调试读模型按需采样（≤4Hz），隐藏即停；不做每帧全树扫描。
- 真机证据：截图/dump/现场包/patch，带构建身份与页面上下文。

### 11.5 视觉回归

- 预览工具生成中/英/长文本/安全区/设备档截图矩阵；关键页面建立基线。
- 视觉 diff 只作人工评审辅助，不把像素差异当唯一门禁；结构/契约/可点性由自动门禁负责。
- 截图必须来自真实 prefab/真实包，不用拼装夹具冒充。

### 11.6 CI 门禁

- 结构：根 Canvas/CanvasGroup/GraphicRaycaster/SafeArea、命名文法、绑定 hash、契约漂移。
- 资源：缺引用、收集组、图集/字体/sprite 名册、第三方直引、debug 资产入包。
- 文本：key 存在、占位符、语言列、富文本白名单、缺字。
- 性能：静态规则（raycastTarget、布局深度、Animator、图集依赖）；Player 性能另测。
- 工具：`toolchain.py check`、工具清单/文档/测试对账。

## 12. 实施路线

> 以下为设计与施工的阶段划分，不记录完成状态；状态归 `Docs/施工进度/`。

| 阶段 | 目标 | 退出条件 |
| --- | --- | --- |
| P0 冻结与基线 | 旧文档归档；UI 资产/代码/页面清单盘点；基线性能与设备矩阵 | 新文档索引可用；旧链接全部指向归档/新文档；基线证据可复查 |
| P1 契约与校验 | `WidgetSpec`、`UiPageManifest`、Page Contract、校验核心、命名/绑定 hash | 美术可生成契约；工程可消费；CI 能红；正负例齐 |
| P2 工作台与起页 | 单工作台 + 美术/工程 Lane；起页/登记拆分；绑定编辑；样式令牌；布局预览 | 美术独立起页并交接；工程登记/接线；工具入口全部走 Hub/CLI |
| P3 运行时调试 | 读模型 + 页面命令 + 状态注入 + 可点性 + 编辑器深检 + F8 | 真包内可回答页面/绑定/布局/输入/性能问题；release 零残留 |
| P4 迁移与多端 | 现有 7 页 + HUD 迁移；安全区/设备档；性能预算；视觉基线 | PC + 移动横屏验收；关键页面无首开失败；预算有证据 |
| P5 生产闭环 | 热更/环境重建/回滚；CI 全量；度量与复盘 | 内容更新可回滚；门禁稳定；交接度量达标 |

**风险与对策**：

- 旧页面与旧工具并存导致双源：以页面清单冻结旧入口，新页面必须走契约；旧页面迁移列为明确批次。
- Odin/UITK 等编辑器依赖：工具分层，逻辑类不依赖付费面板；CI 可脱离面板运行。
- 多端布局碎片：先做设备档与安全区，不在每个页面写平台分支。
- 性能回归：预算进入 CI/Player 证据，不靠 Editor 数字。

## 13. 文档职责边界

| 内容 | 归属 |
| --- | --- |
| 架构分层、运行时契约、多端适配、HUD 边界、性能/质量总则 | 本文 |
| 工作台、起页/登记、绑定、契约、校验、预览、资源工具 | [UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md) |
| 运行时读模型、命令、注入、深检、写回、证据 | [UI 运行时调试工具链专项设计](UI运行时调试工具链专项设计.md) |
| 角色、所有权、契约交接、评审门、度量 | [UI 美术协作与交接规范](UI美术协作与交接规范.md) |
| 目录、Prefab 根、命名、布局、渲染、文本、动效、提交硬规则 | [UI 制作规范](UI制作规范.md) |
| 命名词表、控件清单、选段指引 | [UI 命名与控件词表参考](UI命名与控件词表参考.md) |
| Lua/C# 受控 API、调用约定、错误语义 | [UI 逻辑 API 参考](UI逻辑API参考.md) |

> 变更先判断职责再落笔；跨文档只引用唯一权威，不复制第二份。设计文档只描述现行形态与目标契约，不记录施工状态。
