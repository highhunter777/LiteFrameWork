# UI 制作规范

> 状态：现行规范
> 版本：1.0
> 更新日期：2026-09-21
> 适用范围：UI 资源、Prefab、渲染、动画、文本和性能红线
> 维护责任：客户端 UI
> 类别：**规范/红线**——做 UI 时必守的约定与验收。两级：🔴 硬规则（违规即红，进校验器）／⚪ 建议。机制原理见《UI 运行时专项设计》，工具设计见《UI 编辑器工具专项设计》，批次与缺口见《UI 演进路线图》。

## 1. 渲染层级

- 现状：每界面一个 Canvas（Overlay），组间深度 BaseDepth 步进 100（Bottom=100/Window=200/Top=300）；`[UIRoot]` 无 Canvas/CanvasScaler；零图集。
- 🔴 Overlay 下世界空间粒子/3D **永远在 UI 之下**（渲染顺序物理事实，非配置问题）。
- 目标形态（M11 前，随性能批② B1/B3 落地，见《UI 演进路线图》）：UI 相机（ScreenSpaceCamera，cullingMask 只留 UI 层）+ 根 Canvas（CanvasScaler 1920×1080，MatchWidthOrHeight=0.5）+ 底/内容/顶三层嵌套 Canvas + UI 特效夹层。
- 层序契约：三层界面沿用 100–199 / 200–299 / 300–399；各层 UI 特效夹层 150 / 250 / 350；全屏遮罩/加载层 1000+。
- ⚪ 3D 嵌 UI：RenderTexture 优先（可裁可缩放）；需真实交互（拖拽旋转）才用专用相机 + 世界空间摆位。

## 2. 布局

🔴 硬规则：

| 规则 | 理由 |
|---|---|
| 不用 `LayoutGroup`/`ContentSizeFitter` | 与池化/裁剪对冲、开销响应式；模板与列表一律锚点 + `anchoredPosition` |
| 不用 `Outline` 组件 | 顶点 ×5（BaseMeshEffect 复制四份偏移副本），重建成本最高；描边走 shader 或预烘焙进图 |
| `Shadow` 允许（2026-09-20 解禁） | 顶点 ×2（复制一份偏移副本）；高频重建元素上按比例放大重建成本，知情使用 |

⚪ **Shadow 解禁理由留档**：美术不富裕，"预烘焙描边/阴影进图"路径成本高——Shadow（×2）代价可接受，Outline（×5）依旧禁止。
| `raycastTarget` 默认关，仅交互件开 | Raycaster 每次触摸遍历全 Canvas 可命中 Graphic |
| 动态元素与静态元素分层（动态进子 Canvas） | 压小重建面积 |
| 界面 prefab 根 = 界面本体（自带 Canvas + CanvasGroup） | 既有约定 |
| 文本只放 key 或留空，不放文案 | 本地化硬纪律（细则见 §7 与《UI 运行时专项设计》本地化节） |
| `RectMask2D` 优先于 `Mask` | Mask 多一个 stencil 通道 |

⚪ 建议：参考分辨率 1920×1080 + Match 0.5（横屏）；字号阶梯 12/14/16/20/24/32/40；间距 8 的倍数、安全边距 32；图标 24/32/48/64/96；目录 `Assets/UI/Screens|Widgets|Patches`（**不得新开 UI 根目录**，收集组按路径划分）；prefab 层级 ≤5 层；模板不预设 `BindName`（命名归界面作者，多实例重名炸索引）；全屏界面根挂 `SafeArea`（已交付，不手写适配）。

## 3. 动画

三轨分工（既有定案）：UI 动效/转场 = DOTween（`UiFx`/转场策略）走 `IUIClock`（时停不停）；剧情/技能时序 = 序列执行器走 `IWorldClock`；非对局演出 = Unity Timeline。联机技能表现不跑 `PlayableDirector`。

🔴 硬规则（约束的是**改法**，不是"不准改"——运行时改颜色/透明度合法，只是路径要选对）：

| 规则 | 理由 |
|---|---|
| 淡入淡出只改 `CanvasGroup.alpha`，不改 `TMP_Text.color` | 改顶点色 = 标脏 mesh → Canvas 重建 |
| 位移/缩放只改 `transform`/`anchoredPosition` | 不触发 mesh 重建 |
| 动效元素挂独立子 Canvas | 防整页 rebatch |
| `SetLink(KillOnDisable)` + 关界面必杀 tween | 防 tween 泄漏 |
| 禁原生协程，异步一律 UniTask | 项目红线 |
| 转场期间禁交互由壳统一管 | 不依赖策略自觉 |

实测定案（2026-09-20，Unity 2022.3.55 + TMP 3.0.7，探针 `havePropertiesChanged`）：`FlyTextPool` 淡出改走 `CanvasGroup.alpha`（原每帧写 `TMP_Text.color.a` 违反上表首条）；`_Template` prefab 预先挂好 `CanvasGroup`，运行时**不再 `AddComponent`**；取不到组件**退化为只位移不淡出**，不抛。**一般化**：任何每帧改透明度/颜色的 UI 元素，组件随 prefab 自带。
已知例外：`UiFx.Pulse/Flash` 走 `Graphic.DOFade/DOColor`，仅可用于 Image（只标脏自身三角形）；**禁对 `TMP_Text` 调用**。

⚪ 时长阶梯：快 0.15s（反馈）/标准 0.25s（转场弹窗）/慢 0.35s（全屏）；入场 `OutQuad`/`OutBack`，离场 `InQuad`，禁 `Linear`；列表项错峰 0.02–0.04s 最多前 8 项；动效不阻塞逻辑（`PlayTransition*` 容错吞异常）。

## 4. UI 特效（三载体）

**先定载体，再谈怎么做**——三条载体的归属与"预设"落点各不相同：

| 载体 | 形态 | 例子 | 归属与预设 |
|---|---|---|---|
| ① 动画 | 驱动已有元素（变换/顶点色/图集序列） | DOTween 动效、帧动画 | 本规范 §3；帧动画资产引用由样式编辑器校验 |
| ② 粒子 | **额外的渲染对象** | 升级光效、结算烟花 | 挂载/播放归运行时（`UiFx` 口 + 本节 a/b 方案） |
| ③ 材质/shader | **元素自身的材质状态** | 流光、描边、灰度、进度遮罩 | 预设 = **共享材质预设**（《UI 编辑器工具专项设计》样式编辑器·字体特效轴同形态、同样暂缓） |

- **只有载体②存在"挂哪"**：常驻装饰特效**静态挂合法**——方案 a 本身就是"直接挂"（UI 相机下世界空间 + 夹层 sortingOrder；依赖批② B1/B3 就位，现状 Overlay 下会被 UI 盖死）。静态挂 = 常驻渲染物：同屏 ≤3 预算按常驻占用计；低端机没有运行时切换点（"按质量档统一跳过" = 建服务的触发条件之一）。
- **载体③不产生新对象**（寄生在已有元素上，无挂载问题），只有**断批预算**：每多一种材质变体 = 拆一批（见 §5）。当前全工程 0 自研 UI shader → 通用材质预设与字体特效轴**同为"设计已备、暂缓等触发"**：文本轴触发 = 第一次出现"确实需要描边"的文本；通用材质轴触发 = 第一个自研 UI shader 落地。落地形态 = 共享材质预设资产 + 样式编辑器"选预设"面（校验挂规则 9/10 + S1/S2）；样式编辑器对 shader 只做底线校验，不做"一键换"管理界面（那是鼓励按实例断批）。

**粒子（载体②）第一原则**：🔴 **能不用就不用**（默认否定）。判断顺序：帧动画/顶点色/UV 动画能表达 → 一律不用；必须跟随 UI 或被 Mask 裁剪 → 方案 b；全屏/3D 观感且 UI 相机就位 → 方案 a。"8~16 帧图集能表达 80% 观感"直接走帧动画，不进粒子评审。

| 方案 | 适用 | 代价 |
|---|---|---|
| c. 帧动画替代（默认） | 点击涟漪、图标呼吸、升级闪光 | 观感上限低 |
| b. Canvas 内嵌 ParticleSystem | 必须跟随 UI/被裁剪且帧动画表达不了 | 断批、RectMask2D 不裁粒子、实现成本高 |
| a. 独立特效相机 + 夹层 sortingOrder | 全屏大范围、真 3D 观感，**批② B1/B3 就位后** | 分层排序要设计 |

三权分立（划的是**归属**不是禁令）：VFX 服务管世界空间（不并进 UI 动效）；`UiFx` 管 UI 位移/缩放/淡入出（不是特效播放器）；UI 特效层管 a/b。🔴 UI 特效不携带任何玩法判定（判定在 Sim）。

播放归属与调度（不建服务、经 `UiFx` 口、运行时直接播合法）：**运行时机制见《UI 运行时专项设计》“动效与特效运行时”节**。🔴 a/b 走共享材质资产，禁按实例改粒子材质。

⚪ 粒子材质用 UI 系/Unlit、关 `ZWrite`；同屏粒子系统 ≤3（M11 HUD 预算）；低端机降级 = 换帧动画（c）而不是减时长。

## 5. Shader

🔴 硬规则：UI 件 shader 一律继承 `UI/Default`（保 Mask/RectMask2D/CanvasGroup）；TMP 必用 SDF 变体；`<sprite>` 前必须先建 TMP Sprite Asset（当前为 0）；禁按实例改材质属性（断批）——按实例变化走顶点色/UV 通道或少量共享材质变体。

⚪ 路线：灰度/禁用态 = 顶点色 + 灰化贴图变体；描边 = 预烘焙进图集；流光 = 共享材质 UV 动画（`_Time` 驱动零 CPU）；模糊 = URP Renderer Feature（禁用于列表）；进度/遮罩 = `Image.Type=Filled`；品质色 = 顶点色 + 图集。变体走 `ShaderVariantCollection`，单 shader 关键字 ≤8；自研 UI shader 须过 §1 层序契约。

## 6. 控件模板与 Prefab 约定

- 目录：`Assets/UI/Widgets/`（控件模板，`PackDirectory`）+ `Assets/UI/Screens/`（界面 prefab，`PackSeparately`）；工具在 `Assets/LiteGame/Editor/`（含 `Style/`），运行时在 `Assets/LiteGame/Runtime/Shell/UI/`，字体在 `Assets/Fonts/`。旧 `Assets/LiteGame/UI|Scripts|Fonts` 全部作废。
- 模板结构：`Root(UIWidget) → Bg / Content / Interaction` 三层缺省（特殊控件可加层）；节点名 = 绑定名（PascalCase），非暴露节点前缀 `_`；模板内容自适应 + 锚点居中，实例化方设尺寸；`SafeAreaReceiver` 纳入适配层统一维护。
- 灰盒美术：统一色板（12 token，样式编辑器单源）、九宫格切片、单一占位字体；**禁止业务文案与图标**（文案走表/Lua，占位图仅形态示意）；同族用 prefab variant 派生（多态视觉用子资源状态图，不做多 prefab）。

🔴 模板纪律：

1. **一类一文件（文件名 = 类名）**——Unity 序列化硬约束：文件中非首个 MonoBehaviour 存不进 prefab（保存后变 `<null>`）。新增 MonoBehaviour 必须独占文件；纯类/接口/枚举可共存但不得排在 MonoBehaviour 之前。
2. **模板不预设 `BindName`**：根挂一个空名 BindNode 作"可暴露位"提示，命名归界面作者（多实例重名会让 `BindIndexBuilder` 直接抛）；控件内部接线全落序列化引用。
3. **模板不放 `BindRoot`**（界面级生成物，放模板上与运行时控件类同名冲突且无消费者）。
4. 模板是**壳机制层资产**：不引业务依赖、不含逻辑（交互由控件 C# 类实现）；Lua 只经受控 API 驱动。
5. prefab 变更后**索引/绑定类必须重新生成**（标记工具批量校验进 CI 前置）。
6. **编辑态 `InstantiatePrefab` 不触发 Awake**：依赖 Awake 接线的行为断言必须跑 Play 态（编辑态只查结构）。

工具菜单：`LiteGame/UI/构建控件模板 Prefabs`（确定性重建，可重复执行）/ `LiteGame/UI/校验控件模板`（加载/实例化/驱动/断言，tag `WidgetTemplateCheck`）。

## 7. 文本与富文本纪律

- 文本只放 key 或留空（§2 红线）；key 命名、复数后缀、大小写、语序占位等英文适配细则见《UI 运行时专项设计》本地化节。**灰盒 UI 用英文文案验收**（英文比中文长 30–50%，不用中文验收）；溢出策略靠文本档位（`LabelSingle` Truncate / `Body` Overflow）。
- 富文本白名单（TMP 原生 + 白名单）：允许 `<color>` `<b>` `<i>` `<u>` `<s>` `<size=N>` `<sprite name=…>`（需先建 TMP Sprite Asset，与 SpriteAtlas 是两种资产）`<link=id>`（可选）；禁止 `<style>` `<material>` `<quad>` `<font>` `<gradient>` `<sprite index>` 与未知标签。
- 玩家输入/外部字符串**先转义再插值**（注入防线）；作者标签信任但校验（校验机制见《UI 运行时专项设计》富文本节）。

## 8. 新做 UI 检查清单

- 🔴 根带 Canvas+CanvasGroup；无 LayoutGroup/Fitter/Outline；非交互 `raycastTarget=false`；文本只放 key
- 🔴 高频元素挂子 Canvas；淡出走 `CanvasGroup.alpha`；关界面必杀 tween；禁协程；粒子过三问；特效不带判定
- 🔴 shader 继承 `UI/Default`；TMP 用 SDF；不按实例改材质
- 🔴 模板一类一文件；不预设 BindName、不放 BindRoot；`<sprite>` 前先建资产
- ⚪ 字号/间距/图标落阶梯；层级 ≤5；全屏挂 SafeArea；灰盒用英文验收；Shadow 允许（顶点 ×2，高频重建元素慎用）

## 9. 性能预算红线（M11 验收口径）

| 指标 | 红线 |
|---|---|
| UI 主线程耗时 | ≤ 2.0 ms/帧 |
| 单页 UI drawcall | ≤ 25 |
| overdraw | ≤ 2 层 |
| 稳定帧 UI 路径 GC.Alloc | 0 B/帧 |
| 界面打开（实例化+OnInit） | ≤ 16ms；结算页 ≤ 32ms |
| 控件模板自检 | **36/36** 不许回归 |
| L1 | 全绿不许回归 |

治理批次与测量记档见《UI 演进路线图》。
