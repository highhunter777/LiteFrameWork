# UI 测试开发专项设计

> 状态：现行专项设计
> 版本：1.0
> 更新日期：2026-09-25
> 归属：[测试开发框架总设计](测试开发框架总设计.md)
> 适用范围：Unity UGUI/TMP 页面、UIService、Lua 页面、UI Prefab、输入与导航、列表、动效及其发布性能。
> 执行入口：逻辑上统一使用 `scripts/test.ps1`；Unity 交互遵循根目录 `UNITY-GUIDE.md`。
> 权威预算：[UI 框架总设计第 12 节](../client/ui/UI框架总设计.md#12-验收预算与观测)。

本文把 UI 的**界面测试、自动化测试和性能测试**收纳到现有测试开发框架，不新建一套脱离 `LiteTesting` 的测试体系。本文定义 UI 测试的边界、目录、元数据、夹具、断言、执行 Profile、性能口径和交付证据；产品 UI 契约仍由 [UI 框架总设计](../client/ui/UI框架总设计.md) 与 [UI 制作规范](../client/ui/UI制作规范.md) 负责。

## 1. 目标与边界

### 1.1 三类测试的职责

| 类型 | 解决的问题 | 主要层级 | 默认环境 | 是否允许坐标耦合 |
| --- | --- | --- | --- | --- |
| UI 界面测试 | 页面结构、资源绑定、布局、状态、内容和控件契约是否正确 | L2 EditMode / PlayMode，视觉验收补充 L4 | Unity Editor、必要时 Player | 不允许 |
| UI 自动化测试 | 用户能否通过真实输入完成稳定、可重复的业务路径 | L2 PlayMode，跨进程或真机路径进入 L3/L4 | Unity Player 或 PlayMode | 不允许，必须使用语义定位 |
| UI 性能测试 | 页面打开、更新、滚动、关闭和长期运行是否满足 CPU/GPU/GC/内存预算 | L2 发现问题，L4 定版 | Player、目标设备、固定构建 | 不适用；必须固定场景与数据 |

三类测试的关系固定为：

```text
界面测试：证明“页面是什么、状态是否正确”
自动化测试：证明“用户如何操作、流程能否完成”
性能测试：证明“在什么预算内完成、长期运行是否稳定”
```

### 1.2 测试边界

- 不把截图存在当作界面正确。结构和行为必须有可断言的语义状态；截图只作为视觉证据和失败诊断。
- 不把固定坐标点击当作自动化。定位必须使用稳定的 `ui-id`、语义角色、注册名称或受控组件路径。
- 不把 Editor 的数字当作真机性能结论。Editor 只用于快速发现趋势；发布门禁必须来自 Player 和目标设备。
- 不用长时间 `WaitForSeconds` 掩盖异步问题。等待必须绑定可观察条件、超时和最后状态。
- 不为追求覆盖率复制一套假的 UI。L2 PlayMode 和 L4 自动化应尽可能使用真实 `UIService`、真实页面生命周期、真实资源加载模式和实际 Lua 消费者。
- 复杂美术质量、品牌视觉、动效审美和像素级差异不伪装成单元测试；进入 L4 视觉验收并保留人工结论。

### 1.3 统一原则

1. **语义优先**：测试按页面状态、控件角色和业务结果断言，不按层级偶然性断言。
2. **真实生命周期**：覆盖 Show、Init、Commit、Close、Dispose、复用、取消、失败和 Shutdown。
3. **数据确定性**：固定种子、固定屏幕参数、固定数据规模和固定资源版本；失败可重放。
4. **作用域隔离**：每个用例拥有自己的场景、页面实例、输入状态、资源租约、订阅和临时文件。
5. **证据完整**：失败必须带测试 ID、种子、设备/构建信息、最后一步、日志、截图或性能产物。
6. **分层门禁**：快速结构检查、用户路径回归和设备性能测量分开执行、分别归因。

## 2. 纳入现有测试开发框架

### 2.1 依赖方向

```text
UI 业务测试程序集
        |
        v
UI Test Adapter / Fixture / Page Object
        |
        +--> LiteTesting.Core
        +--> LiteTesting.Unity.Editor（EditMode）
        +--> Unity Test Framework（EditMode / PlayMode）
        +--> Player Profiler / 目标设备（性能与发布验收）

UI 产品代码 --------------------------------^（不得反向依赖测试代码）
```

`LiteTesting.Core` 继续提供稳定随机、超时缩放、产物路径和 LIFO 清理；UI 测试不得在自己的目录重新实现这些能力。EditMode 继续复用 `LiteTesting.Unity.Editor` 的 `UnityTestScope`；PlayMode 需要提供等价的运行时 Scope，负责场景、GameObject、输入、页面服务和临时资源的关闭顺序。

### 2.2 现状与目标目录

当前项目已有 `Assets/Tests/EditMode` UI 用例、`Assets/LiteTesting/Core`、`Assets/LiteTesting/Editor`、`Assets/LiteTesting/Runtime`（PlayMode 所有权）和 `scripts/l2-unity-gate.ps1`。UI 专项目标目录如下；迁移期间允许存量用例暂留在 `Assets/Tests/EditMode`，但新增用例按目标目录归档。

```text
Assets/
├─ LiteTesting/
│  ├─ Core/                         # Runner 无关能力
│  ├─ Editor/                       # EditMode Scope 与编辑器适配
│  └─ Runtime/                      # 目标：PlayMode Scope、输入与运行时产物
├─ Tests/
│  └─ UI/
│     ├─ EditMode/                  # 资产、Prefab、绑定和纯 UI 契约
│     ├─ PlayMode/                  # 真实页面生命周期与自动化流程
│     ├─ Performance/               # 性能场景、采样器和预算断言
│     ├─ Fixtures/                  # 页面、资源、输入、时钟和数据夹具
│     ├─ Scenarios/                 # 数据驱动流程与规模矩阵
│     └─ Support/                   # Locator、Driver、Evidence、报告适配
```

建议程序集划分：

| 程序集 | 目标 | 允许引用 | 禁止引用 |
| --- | --- | --- | --- |
| `LiteGame.UI.EditModeTests` | 资产与 EditMode 契约 | `LiteTesting.Unity.Editor`、UI 编辑器程序集、测试依赖 | PlayMode 专属驱动、设备 SDK |
| `LiteGame.UI.PlayModeTests` | 生命周期与自动化 | `LiteTesting.Core`、UI Runtime、Unity Test Framework、输入适配 | `UnityEditor`、编辑器资产 API |
| `LiteGame.UI.PerformanceTests` | 性能基线与长稳 | `LiteTesting.Core`、UI Runtime、Profiler 适配 | 与业务无关的全局静态开关 |
| `LiteGame.UI.Support` | 夹具和测试适配 | 测试框架与允许的产品接口 | 生产代码反向引用 |

如果 PlayMode 夹具暂时不能独立成 `Runtime` 适配程序集，必须把运行时 Scope 放在 `Assets/Tests/UI/Support`，不能让 PlayMode 测试引用 `UnityEditor` 或复用仅适用于 Editor 的清理逻辑。

### 2.3 命名约定

| 内容 | 约定 | 示例 |
| --- | --- | --- |
| 界面测试文件 | `UiSurface_<Feature>_<Mode>Tests.cs` | `UiSurface_Inventory_EditModeTests.cs` |
| 自动化文件 | `UiAutomation_<Flow>_PlayModeTests.cs` | `UiAutomation_InventoryEquip_PlayModeTests.cs` |
| 性能文件 | `UiPerformance_<Scenario>_PerformanceTests.cs` | `UiPerformance_InventoryScroll_PerformanceTests.cs` |
| 页面对象 | `UiScreen` / `UiPanel` 后缀 | `InventoryUiScreen` |
| 定位标识 | 稳定、业务无关、全局或页面内唯一 | `inventory.item-list` |
| 夹具 | `UiFixture`、`UiScenario`、`UiDeviceProfile` | `InventoryUiFixture` |
| 产物 | `<run-id>/ui/<test-id>/` | `TestResults/artifacts/ui/...` |

## 3. 元数据与执行分类

UI 测试继续使用现有 `Category`、`Duration`、`Priority`、`Owner` 契约，不新增一套不能被 `scripts/test.ps1` 识别的分类。三类 UI 测试通过 `UIType` 属性区分：

| 元数据 | 界面测试 | 自动化测试 | 性能测试 |
| --- | --- | --- | --- |
| `Category` | `Asset` / `Contract` / `Smoke` | `EndToEnd`；只测服务契约时可用 `Integration` | `Performance` |
| `UIType` | `Surface` | `Automation` | `Performance` |
| `Duration` | `Fast` / `Medium` | `Medium`；长链路用 `LongRunning` | `Medium` / `LongRunning` |
| `Priority` | P0 为页面基础契约，P1 为完整状态覆盖 | P0 为核心闭环，P1 为异常与兼容路径 | P0 为发布预算，P1 为趋势观察 |
| `Owner` | `UI` 或稳定团队名 | `UI` 或稳定团队名 | `UI` 或稳定团队名 |

NUnit 用例的推荐标注形式如下；`UIType` 是扩展属性，不改变已有 Lane 过滤规则：

```csharp
[Category(TestCategory.Contract)]
[Property(UIType, Surface)]
[Property(TestTrait.Duration, TestDuration.Fast)]
[Property(TestTrait.Priority, TestPriority.P0)]
[Property(TestTrait.Owner, UI)]
```

规则：

- 一个用例只保留一个主 `Category`；不要同时把同一用例标成 `EndToEnd` 和 `Performance`。
- 纯资产/Prefab 断言使用 `Asset`；真实生命周期使用 `Contract` 或 `Integration`；完整用户流程使用 `EndToEnd`；任何含性能预算断言的用例使用 `Performance`。
- `Performance` 不进入普通功能回归的成功率统计；性能报告单独保存，避免“功能全绿”掩盖性能退化。
- `Quarantine` 必须写明负责人、原因、创建日期和到期日期；不得用它永久隐藏不稳定 UI 自动化。

## 4. UI 界面测试设计

### 4.1 测试对象

UI 界面测试验证页面的可装配性、可见状态、布局与内容契约。它回答的是“页面呈现出来是否符合定义”，不是“用户完整操作链路是否完成”。

测试对象分为四类：

1. **资产结构**：Prefab、脚本、Canvas、RectTransform、TMP、材质、图集、资源引用和本地化 key。
2. **交互结构**：EventSystem、InputModule、Raycast、模态遮罩、焦点顺序、按钮可交互条件和导航关系。
3. **运行状态**：Show/Close、Visible/Hidden、Covered/Paused、Loading/Ready/Error、实例复用和销毁。
4. **内容适配**：中英文、伪本地化、长文本、空数据、大数据、SafeArea、分辨率/缩放和减少动效模式。

### 4.2 EditMode 用例

EditMode 用例必须能在不启动完整游戏流程时快速定位资源和契约问题。每个规则至少包含一个合法正例和一个违规负例，负例可以使用临时 Prefab 或临时 GameObject 构造，不能修改真实项目资产。

| 检查组 | 必测断言 | 失败证据 |
| --- | --- | --- |
| 绑定完整性 | 必需组件存在、类型正确、引用非空、绑定标识唯一 | Prefab 路径、层级路径、字段名、组件类型 |
| 资源完整性 | Sprite、TMP Font、Material、Atlas、Addressable/资源 key 可解析 | 资产路径、GUID/key、引用链 |
| 层级与 Canvas | Canvas 层级、排序、渲染模式、CanvasGroup、遮罩关系符合页面契约 | Canvas 链、排序值、对象状态 |
| 输入与导航 | 可交互控件有唯一标识；禁用/隐藏节点不拦截输入；焦点顺序完整 | 控件 ID、Selectable 邻接、Raycast 状态 |
| 文本与本地化 | 文本保存 key/参数而非硬编码产品文案；样式、字体 fallback、溢出策略符合档案 | key、语言、样式档、文本长度 |
| 布局与适配 | Anchor、Pivot、SafeArea、最小/最大尺寸和列表模板存在 | 分辨率、RectTransform、越界矩形 |
| 生命周期契约 | 页面声明关闭清理、订阅、资源租约和异步取消责任 | 页面 ID、租约/订阅计数 |

EditMode 结构检查不得替代 PlayMode 行为检查。尤其是依赖 `Awake`、`OnEnable`、真实加载、Lua 回调、转场或输入模块的行为，必须在 PlayMode 覆盖。

### 4.3 PlayMode 界面行为

PlayMode 界面测试使用真实 `UIService`、页面注册表、必要的 Lua 页面和目标加载模式。每个 P0 页面至少覆盖：

- 冷启动首开、热开和关闭后复用；
- 打开中关闭、重复 Show、并发 Show、初始化失败和重试；
- Covered/Paused/Visible 状态切换，模态遮罩和焦点恢复；
- 异步资源取消、迟到回调、展示代次校验和关闭后的零写入；
- 列表 0/1/500/5000 条的首尾滚动、刷新、删除到 0 和复用绑定；
- 中英文、伪本地化、长文本、缺失 key 和字体 fallback；
- DevReload/环境重建/Shutdown 后旧页面、旧 LuaEnv、旧订阅不可访问；
- 真实输入前置条件、按钮结果、错误提示、Loading 和空状态。

状态断言至少包括：

```text
页面状态 = Visible / Hidden / Covered / Paused / Loading / Ready / Error
操作结果 = Success / Rejected / Canceled / Failed
资源状态 = Acquired / Released
订阅状态 = Registered / Unregistered
实例状态 = Created / Reused / Destroyed
```

不要只断言“最终页面可见”。还必须断言回调次数、实例数量、资源租约、订阅、异步任务和输入焦点的对称性。

### 4.4 视觉证据

视觉检查分为三档：

| 档位 | 内容 | 门禁 |
| --- | --- | --- |
| V0 | 用例失败时自动截图、层级转储和状态转储 | L2 必须产出，截图只做诊断 |
| V1 | 固定分辨率/缩放/语言的基准截图，对布局越界、缺图、空白和明显错层做人工或受控差异确认 | Nightly 观察，变化必须评审 |
| V2 | 目标设备视觉回归、SafeArea、字体、色彩、动效和平台输入 | Release/L4，需人工签收 |

默认不把像素级截图差异作为 PR 硬门禁。若页面确需自动视觉门禁，必须固定渲染管线、分辨率、质量档、语言、字体和参考资产，并把差异阈值、批准记录和参考图一起版本化。

## 5. UI 自动化测试设计

### 5.1 分层模型

自动化测试使用 `Scenario -> Screen Object -> Locator/Driver -> UI Runtime` 分层：

```text
UiScenario（Given / When / Then）
        |
        v
UiScreen / UiPanel（页面对象与业务动作）
        |
        +--> UiLocator（语义定位）
        +--> UiDriver（点击、输入、拖拽、导航、提交）
        +--> UiWait（状态等待与超时）
        +--> UiEvidence（截图、日志、步骤轨迹）
```

- `UiScenario` 只表达业务意图，不直接操作 Unity 组件。
- `UiScreen` 封装页面内控件和可复用动作，例如 `OpenInventory`、`EquipFirstItem`、`Confirm`。
- `UiLocator` 只返回当前可见、可交互且符合页面作用域的目标；目标不唯一或不可见时立即失败。
- `UiDriver` 统一处理 Input System、鼠标、键盘、触摸、手柄和无障碍输入映射；测试不直接发送散落的底层事件。
- `UiWait` 等待可观察条件，例如页面状态、按钮可交互、文本 key 已刷新、列表绑定代次完成；每次等待都有超时和诊断。
- `UiEvidence` 为每一步记录动作、目标、前后状态和耗时；失败时补充截图、层级和日志。

### 5.2 语义定位契约

定位优先级固定为：

1. 页面作用域内唯一 `ui-id`；
2. 受控语义角色和注册名称，例如 `PrimaryAction`、`DialogConfirm`；
3. 业务稳定 key，例如条目稳定 ID；
4. 明确声明的层级路径和组件类型；
5. 文本匹配只作兜底，必须指定语言和精确匹配规则。

禁止：

- 依赖兄弟索引、自动生成的实例名、随机对象名或未登记的全局查找；
- 依赖屏幕坐标、颜色采样或“当前第几个按钮”；
- 在一个步骤中同时通过多个模糊条件命中目标；
- 通过提高超时或重复点击掩盖定位不稳定。

每个自动化目标必须有唯一性断言。列表条目必须使用稳定业务 key，并在复用后重新验证绑定代次，防止迟到异步资源写入错误行。

### 5.3 动作与断言

标准动作：

| 动作 | 前置条件 | 完成条件 |
| --- | --- | --- |
| `Open` | 页面服务可用、资源目录就绪 | 页面达到 `Ready` 或明确 `Error` |
| `Click` | 目标可见、可交互、焦点有效 | 业务结果或状态转移发生 |
| `Type` | 输入框聚焦、输入策略明确 | 文本模型和展示文本同步 |
| `Submit` | 当前表单校验通过或明确失败 | 提交结果可观察 |
| `Drag` / `Scroll` | 目标和滚动容器唯一 | 目标位置/可见范围变化 |
| `Navigate` / `Back` | 导航栈状态明确 | 当前页、覆盖层和焦点符合契约 |
| `WaitUntil` | 条件可诊断 | 条件满足或超时失败 |

断言应优先验证业务结果，再验证展示结果，最后验证资源和清理：

```text
业务结果：装备成功、购买被拒绝、确认返回 Cancel
展示结果：按钮状态、提示文本 key、列表数量、模态层级
清理结果：订阅归零、资源租约归零、旧实例不再写入
```

### 5.4 自动化场景模板

每个核心流程按以下结构描述并实现：

```text
场景：背包装备一件物品
Given：已打开大厅，背包数据为 1 件可装备物品
When：进入背包 -> 选择物品 -> 点击装备 -> 确认
Then：装备结果成功，按钮变为已装备，角色数据更新，返回后页面状态正确
And：关闭页面后无遗留订阅、资源租约和输入焦点
Evidence：步骤轨迹、最终截图、失败状态树、测试种子
```

P0 自动化流程建议覆盖：启动到大厅、页面打开/关闭、确认弹窗、背包或列表主流程、错误恢复、返回/替换导航、断线或加载失败提示。P1 再覆盖中英切换、长文本、安全区、输入设备差异、后台恢复和复杂列表。

## 6. UI 性能测试设计

### 6.1 测量对象与场景

性能测试必须先定义场景、设备档和数据规模，不能用“打开某页面”作为不完整的测试名称。

| 场景 | 冷态定义 | 热态定义 | 主要指标 |
| --- | --- | --- | --- |
| 首开 | 进程/资源/页面缓存按场景清空 | 资源已加载、页面关闭后再次打开 | 等待时间、主线程、首个可用帧 |
| 复用 | 首次创建新实例 | 关闭后保留并复用实例 | 实例数、分配、打开耗时 |
| 列表滚动 | 数据与节点未预热 | 反复首尾滚动并 Refresh | 帧时、GC、节点数、绑定耗时 |
| 文本/语言刷新 | 目标页面未完成语言绑定 | 批量文本与语言切换 | Canvas 重建、TMP 顶点、CPU |
| 动效/转场 | 动效资源未预热 | 连续打开/关闭与并发转场 | CPU/GPU、Overdraw、分配 |
| 长稳 | 初始页面干净 | 操作 100 次或按版本时长运行 | 内存斜率、句柄、订阅、资源租约 |

固定场景至少声明：页面、加载模式、数据规模、分辨率、分辨率缩放、质量档、语言、输入设备、构建类型、设备型号、Unity 版本、资源版本和测试种子。

### 6.2 指标与预算

UI 性能指标的预算以 [UI 框架总设计第 12 节](../client/ui/UI框架总设计.md#12-验收预算与观测) 为唯一权威来源。当前首轮目标如下，本文不另行复制第二套数字：

| 指标 | 首轮目标或判定 |
| --- | --- |
| 稳定帧 UI CPU | 参考场景 p95 ≤ 2ms；同时记录 p99 和尖峰 |
| 稳定无变化帧分配 | UI 自有热路径目标为 0 B/帧；输入、打开、文本变更单独统计 |
| 打开主线程工作 | 实例化+初始化目标 ≤ 16ms；结算页总工作可另记 ≤ 32ms，但不能默认把 32ms 当作单帧许可 |
| 冷/热打开体验 | 分开记录资源等待、主线程工作、首个可用帧和转场完成时间 |
| 单页绘制 | 初始参考 ≤ 25 drawcall；结合同时可见页面和 GPU 时间判断 |
| 列表节点 | 节点数随视口容量控制，不得随数据总数线性增长 |
| 内存与租约 | 反复打开/关闭后无持续增长；资源租约、订阅和临时对象回到基线 |
| UI 粒子/特效 | 按质量档和页面预算限制；超限必须有降级或拒绝结果 |

指标采用 p50、p95、p99 和最大值共同报告。没有可信设备基线前，不伪造统一百分比；相同设备、相同构建类型和相同数据规模的版本回归默认以 10% 作为预警线，发布门禁以显式预算为准。

### 6.3 采样协议

1. 先执行功能前置断言，确认页面状态和数据正确；前置失败不得产生“性能通过”。
2. 预热不计入样本，默认至少预热 10 次；正式采样至少 30 次或覆盖完整业务时长，按 3 轮重复。
3. 测量期间关闭 Debug HUD、截图、Console 高频日志和人为暂停；Profiler 采样开销必须记录。
4. 使用 `ProfilerMarker`、`ProfilerRecorder`、`FrameTimingManager` 和平台提供的 GPU/内存工具记录数据；采样 API 封装在 `LiteGame.UI.Support`，不要散落在业务测试中。
5. 采样过程记录当前场景、页面状态、数据规模、GC、实例数量、订阅数、资源租约、Canvas rebuild、drawcall、顶点数和帧时间。
6. 采样结束执行关闭、清理和基线对账；如果内存或对象数未回落，测试必须失败或进入明确的泄漏报告。

### 6.4 性能测试环境

| 环境 | 用途 | 结论级别 |
| --- | --- | --- |
| Unity Editor | 快速发现明显的实例化、循环、分配和节点数量问题 | 诊断，不作为发布结论 |
| Development Player | 验证真实加载、脚本、资源包和页面闭环 | Nightly 趋势和候选版本筛选 |
| Release Player | 去除开发开关后的最终基线 | Release 必须 |
| 目标设备 | 低/中/高档设备、目标分辨率和平台输入 | L4 发布硬门禁 |

同一性能场景不得混用不同质量档、不同资源版本或不同刷新率的结果。设备差异必须在报告中单独分组，不能用平均值掩盖低档设备回归。

## 7. 夹具、隔离与数据治理

### 7.1 `UiFixture` 责任

每个 UI 用例通过 `UiFixture` 获得以下资源，并在 TearDown 按反向顺序释放：

1. 测试场景，以及从**测试专用夹具 prefab 加载**的 Canvas、EventSystem 和输入模块（见 7.2——夹具不拼装交互结构）；
2. 页面注册表、UIService、导航栈、转场和 UI 时钟；
3. 本地化、样式、字体、资源目录和可控的加载器；
4. 固定种子、页面数据、网络/内容假端口和失败注入；
5. Screen Object、Driver、Wait 和 Evidence；
6. Profiler 采样器、临时文件和性能报告。

测试 Scope 必须记录所有运行时对象和订阅。任何用例失败后仍应执行完整清理；清理失败与测试断言失败同等判为失败。

### 7.2 真实依赖与替身

| 场景 | 依赖策略 |
| --- | --- |
| 资产结构 | 临时 GameObject/Prefab 或只读真实资产；禁止覆盖项目资产 |
| 生命周期契约 | 可控 Loader、Clock、Lua 回调和失败注入；保留真实 UIService |
| 自动化闭环 | 使用真实页面、真实输入映射和目标数据流；只替换不可重复的外部服务 |
| 性能基线 | 使用真实资源包、真实构建和目标加载模式；不得使用简化假资源得出发布结论 |
| 异常与取消 | 替身只控制故障时机，不能绕过页面提交、关闭和清理路径 |

所有随机数据来自 `TestRunSettings` 派生种子。列表、文本、语言、资源延迟和输入序列都必须能由测试 ID 加种子复现。

**夹具加载而非构建（2026-09-25 裁决）**：测试夹具同样遵守 [UI 框架总设计第 7 节](../client/ui/UI框架总设计.md)的视觉单一来源——Canvas、EventSystem、输入模块和反馈面**从测试专用夹具 prefab 加载**，不在测试代码里 `new GameObject` / `AddComponent` 拼装。理由不是洁癖：夹具拼装出的结构与生产模板本就会漂移，"测试通过"会脱离真实页面形态，而 UI 越权改造的第一次复发（`UIDemoPage`）正是从夹具式构建起步的。

夹具 prefab 的存放与判定：

- 按第 2.2 节目录放在测试专属位置（`Assets/Tests/UI/Fixtures/`），**不进** `Assets/UI/Screens|Widgets` 的 YooAsset 收集路径，不参与产品打包；
- 需要屏上事件（点击/拖拽/焦点）的 PlayMode 用例才加载含 EventSystem/InputModule 的夹具；不上屏的夹具不带屏上依赖，避免把 PlayMode 专属结构漏进 EditMode；
- 夹具 prefab 的结构本身纳入界面契约用例（第 4.2 节），有正例和违规负例。

**存量例外**：`Assets/Tests/EditMode` 迁移期内用代码构造**非视觉**夹具不判违规（如 `UnityTestScope.CreateGameObject` 造带自定义组件的测试宿主）；一旦某夹具开始拼装视觉或交互结构（Canvas、Graphic、EventSystem、InputModule、Raycaster），即转入夹具 prefab，不得以"测试代码"名义豁免。

**负例不受此限**：第 4.2 节用于触发校验器的**违规负例**（第 7.2 节表格"资产结构"行的临时对象）是喂给规则的输入，不是产品面——仍在代码或临时 Prefab 中构造。判据是"该结构是否会被当成真实页面形态复用"：夹具模拟生产面 → 必须加载 prefab；负例只求触发失败 → 不受限。

### 7.3 互斥与清理

- UI 自动化和性能用例默认串行执行，避免输入、全局 UIService、Profiler 或静态缓存互相污染。
- 测试不得依赖上一个用例留下的场景、页面、语言、质量档、PlayerPrefs、Addressables 缓存或静态事件。
- 页面关闭不等于资源清理完成；必须等待租约、订阅、异步任务和实例状态回到约定终态。
- 用例不能保存、覆盖用户已有未保存场景或真实 Prefab；所有临时资产使用测试专属目录并由 Scope 删除。

## 8. 执行 Profile 与门禁

### 8.1 Profile 矩阵

| Profile | 界面测试 | 自动化测试 | 性能测试 | 失败处理 |
| --- | --- | --- | --- | --- |
| PullRequest | P0 资产/契约与少量 PlayMode Smoke | P0 核心闭环，排除 `LongRunning` | 不做设备硬门禁，可做快速趋势采样 | 不自动重试；失败即阻断相关 Lane |
| Nightly | 全量 EditMode/PlayMode、语言/列表/异常矩阵 | P0/P1 全量，含长链路和环境重建 | Development Player/固定设备全量趋势 | 产出趋势、火焰图和差异报告 |
| Release | 指定 Unity 版本和候选资源全量 | P0/P1 核心路径与目标输入设备 | Release Player、目标设备、长稳和内存硬门禁 | 需要人工审批和完整证据 |

逻辑入口保持不变：

```powershell
powershell -NoProfile -File scripts/test.ps1 -Lane L2 -Profile PullRequest
powershell -NoProfile -File scripts/test.ps1 -Lane All -Profile Nightly
```

现有 `scripts/l2-unity-gate.ps1` **已完成 PlayMode 执行接入（2026-09-25）**：EditMode 与 PlayMode 共用同一异步轮询入口（`--async_tests` + 轮询 `test_status`），各自计数、超时与 Console 检查，Total=0 同判失败；未另起绕过 `scripts/test.ps1` 的本地入口。产物上传与性能采样仍待接入（性能的 Player/真机执行归 Release Pipeline，结果使用相同的 `run-id`、分类和产物目录）。

### 8.2 UI 门禁

以下任一情况判失败：

- UI 程序集未编入、测试数为 0、编译错误、Console 新增 Error、测试超时或 Scope 清理失败；
- 必需页面/控件没有稳定定位标识，定位命中 0 个或多个目标；
- 页面状态、业务结果、输入焦点、资源租约、订阅或异步任务未回到约定终态；
- 真实加载模式下出现丢资源、迟到回调写入、关闭后旧页面访问或列表绑定代次错误；
- 性能样本缺失设备/构建/分辨率/数据规模，或 p95/p99 超出显式预算；
- 重复打开/关闭、滚动或语言切换导致内存、实例、订阅或租约单调增长；
- 视觉差异没有参考图、差异阈值或人工批准记录，却被当作自动通过。

### 8.3 报告与产物

每个 UI 用例至少报告：

```text
testId / UIType / Category / Duration / Priority / Owner
runId / seed / UnityVersion / buildHash / platform / device
resolution / scale / quality / language / inputDevice / dataSize
lastStep / expected / actual / finalUiState / cleanupState
logs / screenshot / hierarchyDump / trace / performanceJson / profilerData
```

性能报告额外包含：样本数、预热次数、p50/p95/p99/max、UI CPU、整帧 CPU/GPU、GC 分配、峰值内存、drawcall、顶点数、Canvas rebuild、实例/租约/订阅前后差值和基线版本。

## 9. 用例清单与完成定义

### 9.1 P0 最小集合

每个进入主流程的页面至少具备以下用例：

| 编号 | 用例 | 类型 | 默认层级 |
| --- | --- | --- | --- |
| UI-001 | Prefab、绑定、资源和本地化契约 | 界面 | L2 EditMode |
| UI-002 | 冷开、热开、关闭和复用 | 界面 | L2 PlayMode |
| UI-003 | Loading、Ready、Error、Cancel 状态 | 界面 | L2 PlayMode |
| UI-004 | 模态、层序、Covered/Paused 和焦点恢复 | 界面/自动化 | L2 PlayMode |
| UI-005 | 一个真实用户闭环 | 自动化 | L2 PlayMode |
| UI-006 | 列表 0/1/大数据、刷新、滚动和复用 | 界面/自动化 | L2 PlayMode |
| UI-007 | 中英/伪本地化、长文本和 SafeArea | 界面/自动化 | Nightly/L4 |
| UI-008 | 反复开关、滚动和语言刷新无持续增长 | 性能 | Nightly/L4 |
| UI-009 | 冷热打开、稳定帧和目标设备预算 | 性能 | L4 Release |
| UI-010 | DevReload/Shutdown 后旧环境零访问 | 界面/自动化 | L2 PlayMode |

### 9.2 UI 功能完成定义

UI 功能只有同时满足以下条件才算完成：

- 至少有一组 EditMode 资产/结构契约和一组 PlayMode 真实生命周期用例；
- P0 用户路径使用语义定位和可观察等待，不使用坐标、固定实例索引或无限超时；
- 正常、空数据、边界、非法输入、加载失败、取消、重试、关闭和环境重建路径都有结果断言；
- 新增页面的资源租约、订阅、实例复用、列表绑定代次和异步回调有对称性断言；
- 涉及性能的变更提供固定设备/构建/数据规模下的性能样本，涉及发布的变更通过 L4 Player/真机预算；
- 失败可以用 `run-id`、测试 ID 和种子复现，产物可下载，清理失败不会被吞掉；
- 文档、用例、页面标识和性能预算同一变更提交，不以“手工验证过”替代自动化证据。

## 10. 落地路线与责任

### 10.1 现有能力承接

- 现有 `Assets/Tests/EditMode` 中的 `UiNavModalEditModeTests`、`UiTransitionEditModeTests`、`UiU0EditModeTests` 和 `UiU1EditModeTests` 继续作为 UI 契约存量，逐步补齐 `UIType`、`Duration`、`Priority` 和 `Owner`。
- UI 生命周期、列表、资源所有权、取消、模态和转场的具体断言以 [UI 框架总设计第 12 节](../client/ui/UI框架总设计.md#12-验收预算与观测) 与现有实现契约为准。
- `LiteTesting.Core`、`UnityTestScope`、`scripts/test.ps1` 和 `scripts/l2-unity-gate.ps1` 是统一入口和清理边界；专项文档不得复制一套并行 Runner。

### 10.2 建设顺序

1. **标签与归档**：为存量 UI 用例补齐元数据。**目录与程序集边界已建立**（`Assets/Tests/UI/PlayMode`，2026-09-25）；存量用例的 `UIType`/`Duration`/`Priority`/`Owner` 补齐待办。
2. **PlayMode 支撑**：运行时 Scope（`LiteTesting.Runtime`）与页面夹具已就位，**P0 页面闭环已接入 L2 同一门禁**（真 Lua 生命周期、暂停/覆盖状态语义、动画资源组合、反馈面，12 例）；余项为语义 Locator、Driver、Wait 和 Evidence 分层。
3. **自动化闭环**：以大厅、确认弹窗、列表/背包和错误恢复为首批流程，建立 Screen Object 和数据驱动 Scenario。
4. **性能采样**：接入统一 Profiler 适配、固定设备档、性能 JSON/CSV 和基线对账；先 Nightly 趋势，再开启 Release 硬门禁。
5. **发布验收**：完善低/中/高档设备矩阵、视觉人工签收、前后台/低内存/热更场景和长稳报告。

### 10.3 责任划分

| 角色 | 责任 |
| --- | --- |
| UI/客户端开发 | 页面标识、生命周期契约、可观测状态、资源所有权和对应测试 |
| QA/质量工程 | 场景矩阵、自动化流程、门禁 Profile、报告与失败归因 |
| 工具/框架 | LiteTesting 适配、Runner、Locator/Driver、Profiler 采样和产物格式 |
| 美术/本地化 | 参考图、字体/样式、语言矩阵、视觉差异签收 |
| 发布/设备工程 | Player 构建、设备池、目标预算、长稳和发布审批 |

任何 UI 规则若没有失败证据、定位信息和至少一个违规负例，不得宣称已纳入自动门禁。
