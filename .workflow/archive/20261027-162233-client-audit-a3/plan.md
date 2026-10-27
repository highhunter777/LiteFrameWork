# 方案：客户端审计 A3——三大件拆分

> 运行 client-audit-a3 · 创建于 2026-10-27 · 基线 `0837cc0d`
> **基线工作区**：启动时含并行线（服务端审计）文档改动——`Docs/审计/README.md`、`Docs/施工进度/服务端审计线.md`（已修改）与 `Docs/审计/服务端/批次计划.md`（未跟踪）；本批不触碰这些文件，验收按文件集排除。

## 背景与目标

- **现状**：审计后续批次计划的 A3 = 「类设计·高（三大件拆分）」3 条 + 「重复实现」1 条 + 「类设计·低」1 条（`StageMachineCore` 树表示法，A3-3 同修；原列在 A5）（`Docs/审计/客户端/批次计划.md` §3）：
  - `LiteGame/App/Presentation/Battle/BattleViewRig.cs` 1058 行：取件组（8 个 `Acquire*Async`）/ 装配组（Attach + 12 段）/ 帧驱动内嵌视角输入与指针锁定 / 逆序拆除 / 读源适配器；`AuthoritativeCombatFeedback` 被压成嵌套类。
  - `LiteClient/UI/Bind/UIBindIndex.cs` 697 行：控件索引 + 所有权登记 + 文本/本地化 + 输入订阅 + 列表源 + 动效命令，六张域级状态字典挤在一类。
  - `LiteFramework/Scripts/Core/Fsm/HierarchicalStageMachine.cs` 481 行 + 共享内核 `StageMachineCore.cs` 374 行：树的存储/构建（`_composite/_parent/_history/_roots/BuildTree`）住在“共享内核”，但只有层级机消费——内核的职责面被表示法污染。
  - `BattleViewRig` 的 8 个 `Acquire*Async` 同构（try 租约 → 登记 viewScope → catch 降级），只差路径与文案（重复实现登记第 2 条）。
- **要解决的问题**：三个枢纽类职责过载（修改指南 §1.6：单类 >400 行且多职责须先拆）；共享内核持有单一子类才用的表示法；同构取件重复。
- **目标（可观察）**：
  1. 每个子批产出“一类一文件”的协作件，主类只保留编排/门面职责；`BattleViewRig` 不再直接持有取件、HUD 族与反馈族字段，`UIBindIndex` 不再直接持有本地化/输入订阅/列表三域状态，`StageMachineCore` 不再含任何树字段与建树逻辑。
  2. 行为零变化：装配出的驱动集合、tick 顺序、降级日志文案、拆除不变量（消费者先于分发器/视图）逐项保持；公共 API（UIBindIndex/HSM）成员不删不改签名。
  3. 四道门禁全绿：Unity 重编译 0 错、L1（PullRequest）、定向 EditMode、doc-lint。

## 需求澄清与假设

- **需要确认的问题**：无（A3 的范围、条目与退出条件已在批次计划中裁定）。
- **已确认答复**：用户 2026-10-27“按项目交付的流程做客户端审计后续批次计划的 a3”——按 staged-delivery 全流程执行 A3 全部三个子批。
- **采用假设**：
  1. 三个子批在**一个工作区**内依次完成、分别形成可回退的改动集；是否分批提交在收尾时征询用户（仓库纪律：提交先征询）。
  2. “行为零变化”以**可观察行为**为准：驱动集合/顺序、日志文案、降级与异常语义不变；tick/dispose 相位表逐项复现原顺序（域内不重排）。
  3. A3 不触碰共享源集（LiteSim/LiteNet/GameData/Generated），无需 `gen-build-hash`。
  4. **tests-first 门禁**：纯行为保持型重构没有可先红的新行为，但**结构本身就是本批交付物**——以 run-local 结构验收探针（`.workflow/active/client-audit-a3/red-structure-check.py`：断言三组新协作件存在且主类不再持有对应域状态/树字段）作为先行红测：基线必红、施工后必绿；行为回归另由既有套件 + Play 冒烟承担。`.workflow/config.json` 的 `red_commands` 临时改指该探针，归档后恢复原值。
- **假设影响与风险**：若用户要求“逐子批独立提交”，三个子批的文件集需保持互斥——本方案已按此划分（见 §范围）；若用户要求补红测，则需先定义可先红的“新行为”（当前不存在），方案需重开。

## 范围与非目标

- **范围内**：
  - **A3-1** `BattleViewRig` 拆分：新增 `BattleViewAssets`（AssetLoader）、`BattleViewSceneAssembler`（SimView/移动动画/激光/保护壳）、`BattleHudAssembler`（准心/HUD/排行榜/击杀/网络/阵亡）、`BattleFeedbackAssembler`（命中分发/伤害数字/特效/音效/远端火光/权威反馈）、`BattleLookController`（视角增量/瞄准滞回/指针锁定）；`AuthoritativeCombatFeedback` 外提一文件；8 个 `Acquire*Async` 收口为一个泛型取件助手。同步更新 `FeatureDebugTunables`/`LookTuning` 对视角常量的引用点。
  - **A3-2** `UIBindIndex` 拆分：新增 `UIBindLocale`（语言服务/本地化绑定/复数解析）、`UIBindEvents`（输入控件订阅与退订）、`UIBindListSource`（列表源/行投影/行点击）；`UIBindIndex` 保留控件索引、所有权登记与受控 API 门面。
  - **A3-3** 层级树表示法收回：`_composite/_parent/_history/_roots/BuildTree` 从 `StageMachineCore` 移入 `HierarchicalStageMachine`；内核构造只做阶段注册，新增受保护的 `InitializeCore(transitions)` 由两机在各自构造尾段调用（保持“注册 → 建树 → 迁移表校验 → 审计 → OnInit”原序）；`InitializeCore` 带初始化守卫（未初始化即 `Start` → 显性抛，不静默退化为无迁移表）。
  - **回写**：5 条登记删除（类设计高 3 + 重复实现 1 + 类设计低 1）+ 5 行归档追加；批次计划 A3 覆盖数 4→5、A5 10→9；`Docs/施工进度/客户端审计线.md` 新章；修改指南 §1.6 枢纽清单同步。
- **非目标**：A4/A5 的 18+10 条类设计债；`UiTools` 网状依赖；`VfxInstance` 状态袋；`CommandCenter`（A2 遗留裁决未下）；任何行为修复、表现调整、资源改动、协议/表改动；不新增公共 API。

## 现状与约束

- **相关代码/模块**：
  - `BattleViewRig` 仅 `ProcedureBattle` 生产消费（`internal`）；常量 `LookSensitivityRadPerPixel`/`AdsLookSensitivityScale` 被 `FeatureDebugTunables` 与 `LookTuning` 文档引用。
  - `UIBindIndex` 是公共面：`UIBindBase`、`BindIndexBuilder`、`LuaBehaviourAdapter`（shim/Dispatch）、`UIDemoPage`（A6 待退役但当前仍编译）、4 个 EditMode 测试类直接调用；`UIBindIndex.Empty` 静态只读实例。
  - `HierarchicalStageMachine` 被 `PreemptiveHierarchicalStageMachine`、`CombatAnimMachine`、流程层消费；树构建校验有 3 个 L1 用例（`HSM_子态未注册_构造抛`、`HSM_重复父_构造抛`、`HSM_InitialChild不在children_构造抛`），只看异常类型，不依赖校验先后。
  - L1 测试在 `Tests/LiteFramework.Core.Tests/`（xunit，dotnet）；UIBind 与战斗表现在 `Assets/Tests/EditMode/`。
- **约束**：
  - 修改指南 §1.6“外提协作件一类一文件、公共 API 不变、行为零变化”（P4 先例：`UIFormStore`/`UICoverageResolver`/`BattleInputPipeline`）。
  - 禁新增 public static 可变状态、禁硬编码、日志统一 `Log.*`；改动文件不得产生同型新债。
  - Unity 序列化资产零改动；只改 `.cs`。
  - 工作区当前含并行线（服务端审计文档）改动，本批不触碰；Android T1/TPS 肩射相机工作流已跟踪且干净，同样不在本批范围。只对 A3 代码文件集负责。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| **A. 门面 + 内部协作件（按域分组，构造注入）** | 主类保留公共 API 与编排；每个域一个 `internal sealed` 协作件，直接传依赖（无新接口） | 与 P4 先例同口径；改动可逐文件评审；回滚边界清晰；不扩公共面 | 主类仍需保留少量跨域编排（tick/dispose 相位） | **推荐** |
| B. 端口接口化 + DI 注册 | 为每个协作件定义接口并进容器 | 可替身/可测试性更高 | 当前无第二个实现与替身需求；会扩公共面并引入过度抽象（违反“不为假想需求设计”） | 不采用 |
| C. 原地分区（#region/注释分段） | 不拆文件，只做逻辑分区 | 改动最小 | 不解决职责与状态耦合，审计条目不销账 | 不采用 |

推荐 A：A3 是“结构还债”，不是能力扩展；P4 已用同一手法拆了 ProcedureBattle/BattleContext/UIService 并被验证。

## 设计要点

### A3-1 BattleViewRig

**外提件（均 `internal sealed`，同程序集）：**

| 文件 | 职责 | 持有状态 |
|---|---|---|
| `BattleViewAssets.cs` | 全部取件与逐项降级；一个泛型 `AcquireAsync<T>` 收口“租约 → 登记 viewScope → catch 降级” | prefab/激光束实例/HUD 实例与引用件/保护壳租约/飘字底件与四档材质/片段清单/四组音效租约 |
| `BattleViewSceneAssembler.cs` | SimView、移动动画驱动、瞄准激光、出生保护壳；视图实例化（含 SkinnedMesh `updateWhenOffscreen` 修正） | `SimView`、`CharacterLocomotionDriver`、`BattleLaserDriver`、`BattleProtectionFxDriver` |
| `BattleHudAssembler.cs` | 准心/HUD/排行榜/击杀提示/网络质量/死亡复活；菜单钮监听挂摘 | 六个 HUD 族驱动 + HUD 实例销毁职责 |
| `BattleFeedbackAssembler.cs` | 命中分发器 + 伤害数字/枪口火光/命中特效/对局音效/远端火光/权威命中反馈 | `HitFeedbackDispatcher`、`AuthoritativeCombatFeedback`、五个消费者 |
| `BattleLookController.cs` | 视角增量（灵敏度/ADS 收敛）、瞄准滞回、指针锁定、复活复位、相机 `SetAiming`/`SetAimPoint` | `PointerLockService`、`AimHoldGate`、`_lookDeltaOf`、`_aimPointOf`、复活序号 |
| `AuthoritativeCombatFeedback.cs` | 公共战斗事件面 → 命中分发（原嵌套类原样外提） | feed/view/dispatcher |

**主类保留**：作用域与视图根、`Attach`/`Tick`/`TickPreSample`/`Dispose` 编排、输入接线（设备源相机注入、`AttachInput`、`Camera.Reset`）、传送命令登记、`PublicFactState`/`LocalSlot` 读源、编辑器诊断件（爆头可视化/自瞄验证）。

**服务依赖归属**：`content`/`themeProvider` 注入 `BattleViewAssets`；`vfx`/`audio`/`worldClock` 注入 `BattleFeedbackAssembler`；`crosshairFeel` 注入 `BattleHudAssembler`；主类只保留 `input`/`camera`（输入接线）与 `commands`（传送命令）及 `context`/视图根/作用域，**不存**上述域引用。协作件字段名为 `_assets` / `_viewAssembler` / `_hudAssembler` / `_feedbackAssembler` / `_lookController`（与探针禁用的旧域字段名不冲突）。

**顺序不变量（tick/dispose 相位端口显式化）**：

主类按**相位方法**驱动协作件；相位内部顺序固定，逐项复现原代码顺序（不重排）：

| 相位 | 调用 | 覆盖的原步骤（行号＝现 `BattleViewRig.cs`） |
|---|---|---|
| `Tick` ① | `_viewAssembler.TickMovement(elapse)` | 移动动画（433） |
| `Tick` ② | `_feedbackAssembler.TickAuthority()` | 权威命中/击杀反馈（434） |
| `Tick` ③ | `_hudAssembler.Tick(elapse)` | 准心（435）→ HUD（436）→ 排行榜（437）→ 击杀提示（438）→ 网络质量（439）→ 阵亡 HUD（440） |
| `Tick` ④ | `_viewAssembler.TickWorldFx()` | 保护壳（441）→ 瞄准激光（442） |
| `Tick` ⑤ | `_feedbackAssembler.TickVisual()` | 伤害数字（443）→ 远端火光（444） |
| `Tick` ⑥ | `_lookController.Tick(elapse)` | 复活复位（450-454）→ 计算 pointerLocked（457-458）→ 视角增量（464-475）→ `SetAiming`（476）→ `SetAimPoint`（477-481）→ 指针锁定/解锁（485） |
| `Dispose` ① | `_viewAssembler.DisposeMovement()` | 移动动画（537-538） |
| `Dispose` ② | `_hudAssembler.DisposeRankboard()` | 排行榜（539-540） |
| `Dispose` ③ | `_hudAssembler.DisposeKillFeed()` | 击杀提示（541-542） |
| `Dispose` ④ | `_feedbackAssembler.DisposeRemoteFire()` | 远端火光（543-544） |
| `Dispose` ⑤ | `_hudAssembler.DisposeNetworkHud()` | 网络质量（545-546） |
| `Dispose` ⑥ | `_hudAssembler.DisposePanel()` | HUD 面板（547-548） |
| `Dispose` ⑦ | `_hudAssembler.DisposeRespawnHud()` | 阵亡 HUD（549-550） |
| `Dispose` ⑧ | `_hudAssembler.DisposeCrosshair()` | 准心（551-552） |
| `Dispose` ⑨ | `_viewAssembler.DisposeLaser()` | 瞄准激光（553-555） |
| `Dispose` ⑩ | `_viewAssembler.DisposeProtectionFx()` | 保护壳 + 束/壳租约引用清空（556-560） |
| `Dispose` ⑪ | `_hudAssembler.DisposeRoot()` | HUD 根件销毁 + 菜单监听摘除（561-570） |
| `Dispose` ⑫ | `_feedbackAssembler.DisposeDamageNumbers()` | 伤害数字 + 材质引用（572-574） |
| `Dispose` ⑬ | `_feedbackAssembler.DisposeCombatFx()` | 枪口火光 + 命中特效（575-578） |
| `Dispose` ⑭ | `_feedbackAssembler.DisposeAuthority()` | 权威命中/死亡反馈（579-580） |
| `Dispose` ⑮ | `_feedbackAssembler.DisposeAudio()` | 对局音效 + 音效租约表（581-583） |
| `Dispose` ⑯ | 主类 | 传送命令注销（584-585） |
| `Dispose` ⑰ | 主类 | 调试件关闭/还原 → `BattleDiagnostics.Detach`（586-592） |
| `Dispose` ⑱ | `_feedbackAssembler.DisposeDispatcher()` | 命中分发器（593-594） |
| `Dispose` ⑲ | `_lookController.Dispose()` | 视角源/瞄准点摘除（595-596）→ 指针解锁（597）→ 相机 `SetAiming(false)`（598） |
| `Dispose` ⑳ | 主类 | `_context.AttachInput(null)`/置空 → 视图置空 → 视图根销毁 → 租约引用清空 → `_viewScope.Dispose()`（599-612） |

跨域不变量（相位表已逐项满足）：消费者先于分发器、分发器先于视图、视图先于视图根与作用域；HUD 根件销毁在 HUD 驱动之后、视图根之前；指针解锁与相机接管还原在离场完成；`_context.AttachInput(null)` 在 context 置空之前。AC5 以本表逐行对照 diff，不做“域内归并”。

**取件收口**：单个泛型助手 `AcquireAsync<T>(location, degradeReason, use, ct)` 包住“取租约 → 登记 viewScope → 调用 use（实例化/主题注入）→ catch 降级（OCE 透传）”；材质四档（任一失败整组不注入）与音效四组（逐条独立降级）在循环里复用同一助手，失败策略由调用点决定；HUD 失败时的孤儿实例清理在 `use` 之后按返回值判定，日志前缀与文案逐字保持。

**AimHold 常量迁移**：`LookSensitivityRadPerPixel`/`AdsLookSensitivityScale` 移到 `BattleLookController`（单一来源），`FeatureDebugTunables` 与 `LookTuning` 注释同步改指。

### A3-2 UIBindIndex

- `UIBindLocale`：语言服务注入/退订、`_ltext` 绑定表、`SetTextKey`/复数解析、语言变更回调、列表行文案格式化（`Format(field)` 与“未注入”fail-fast 文案原样）。**不反向依赖主类**：暴露 `Bind/Detach/HasLocale/Set/SetPlural/Unbind/Clear/Format/Resolve/RewriteAll(Action<string,string> write)` 与 `Changed` 事件；`RewriteAll` 在内部枚举 `_ltext` 并逐项 `write(name, Resolve(binding))`，主类订阅 `Changed` 后调 `RewriteAll(SetText)` + `_listBinder.RebindVisible()`（避免把私有绑定表外泄）。
- `UIBindEvents`：`_controlUnsub` 登记表与三个 `On*Changed`（Slider/Toggle/Dropdown）；构造注入 **`UIBindIndex` 引用**（统一经 `Get<T>`/`ResolveDropdown`），保持 `UIDropdownWrap` 优先语义；`ClearAll()` 供 `UnbindAll`。
- `UIBindListSource`：`_listSources`/`_listClick`/`EmptyRows`/`BindListSource`（行投影、行点击、窗口重绑、池化解绑原样）。构造注入 **`UIBindIndex` 引用 + `UIBindLocale`**；`WriteListItem` 移入本件（用 `locale.Format` + 主类 `RevealOrSetText`），`HandlerFor` 与 `_listClick`、条目钩子 `_clicks` 一并归本件；主类 `TryGetList`/`RevealOrSetText`/`ResolveDropdown` 改 `internal`（同程序集，非公共面）。
- 构造序（主类构造体内）：`_localeBinder` → `_eventBinder = new UIBindEvents(this)` → `_listBinder = new UIBindListSource(this, _localeBinder)` → 订阅 `_localeBinder.Changed`。三个协作件均为 `internal sealed`，不扩公共面。
- `UIBindIndex` 保留：`_controls`/`_drivers`、`Get/Has/TryGet`、`MarkDriver`、受控 API（文本/可见/交互/进度/血条/倒计时/Toast/气泡/飘字/位置）、动效（Pulse/Flash/Slide + ResolveGraphic/ResolveRect）、`BindLocale`/`SetTextKey*`/`UnbindTextKey`/`SetList`/`OnListClick`/`On*Changed` 门面转发。
- `UnbindAll` 语义不变：按钮监听 → 本地化绑定与退订 → 输入订阅 → 列表条目与源清空。

### A3-3 StageMachineCore / HierarchicalStageMachine

- `StageMachineCore` 构造只做阶段注册；`_transitions`/`_audit` 由 `InitializeCore(transitions)` 赋值（原序：注册 → 建树 → 迁移表校验 → 审计 → `OnInit`）。
- **初始化守卫**：`InitializeCore` 置 `_initialized=true`；两机 `Start` 首行 `EnsureInitialized()`，`Snapshot`/`ExportGraph`（诊断面读 `_audit`）同样先过守卫——漏调即抛 `InvalidOperationException("机器未初始化：子类构造未调用 InitializeCore")`，不会静默退化为“无迁移表”。
- 树字段与 `BuildTree` 全部移入 `HierarchicalStageMachine`（字段初始化在派生类，构造体内先 `BuildTree(composites)` 再 `InitializeCore(transitions)`）。
- `StageMachine` 两个构造改调 `InitializeCore(null/transitions)`；`Preemptive*` 不变。
- `ExportGraphEdges` 层级覆写、`Snapshot`、`Roots`、`ActivePath` 原样使用本类字段。
- **已比较未采用的替代**：(a) 保留 `readonly` + 基类构造内虚调用建树——构造期虚调用反模式；(b) 建树后置到基类 `OnInit` 之后——改变非法配置下 `OnInit` 副作用时序。采用“注册-only 构造 + 守卫版 `InitializeCore`”，并在评审中核对初始化序。

## 通用性与扩展性

- **根因**：不是“文件太长”，而是**状态所有权与职责边界错位**——取件/HUD/反馈/视角各自的状态住在同一类里，任何一处扩展都要穿过整份文件；内核持有单一子类才用的表示法。
- **复用与扩展点**：新增表现件时挂到对应装配器（HUD 族 → `BattleHudAssembler`，反馈族 → `BattleFeedbackAssembler`），不再扩主类；新增绑定域（如新的输入控件族）挂 `UIBindEvents`，新增本地化形态挂 `UIBindLocale`；树语义变化只动层级机。
- **避免硬编码/特判**：泛型取件助手统一降级口径（文案由调用方给，路径仍走既有常量）；不新增分支特判；常量继续单源。

## 测试策略

- **行为保持型重构不适用“先红后绿”**：没有新行为可断言。**先行红测 = 结构验收探针**：`red-structure-check.py` 逐项断言（A3-1 六个新文件存在、`BattleViewRig.cs` 无 `Acquire*Async` 且不再声明 HUD/反馈/视角域字段；A3-2 三个新文件存在、`UIBindIndex.cs` 不再声明本地化/输入订阅/列表域字段；A3-3 `StageMachineCore.cs` 无树字段与 `BuildTree`、`HierarchicalStageMachine.cs` 有之）。探针是**结构性验收**（重构的交付物即结构），不替代行为验证；行为回归由既有用例 + Play 冒烟承担，理由与残余风险在 `test-plan.md` 记录。
- **L1（dotnet，PullRequest）**：FSM 全家（`HierarchicalStageMachineTests`/`PreemptiveHierarchicalStageMachineTests`/`FsmCaptureRestoreTests`/`FsmAuditStatsTests`/`ForceStateWatchdogTests`/`TransitionTableTests`）+ 纪律扫描 + BuildHash 守卫。
- **定向 EditMode**：
  - UIBind 面：`LTextBindEditModeTests`、`LTextTypewriterEditModeTests`、`UiControlListProtocolEditModeTests`、`LTextWiringEditModeTests`。
  - 战斗表现面：`BattleHudMenuEditModeTests`、`BattleHudEditModeTests`、`BattleCrosshairEditModeTests`、`BattleRankboardEditModeTests`、`BattleKillFeedEditModeTests`、`BattleNetworkHudEditModeTests`、`BattleRespawnHudEditModeTests`、`BattleProtectionFxEditModeTests`、`BattleLaserEditModeTests`、`BattleDamageNumberEditModeTests`、`HitFeedbackDispatcherEditModeTests`、`BattleCombatFxEditModeTests`、`BattleAudioDriverEditModeTests`、`BattleRemoteFireEditModeTests`、`SimViewEditModeTests`、`TpsBattleRigEditModeTests`、`TpsPointerLockEditModeTests`、`TpsCameraLookEditModeTests`、`TpsAimPointEditModeTests`、`TpsAimPitchEditModeTests`、`TpsCrosshairCenterEditModeTests`、`PresentationLifecycleEditModeTests`、`PresentationOwnershipEditModeTests`（存量红项先做 pristine 对照，只认本批面）。
- **编译**：`unity command recompile` + `recompile_status`（`failed=false`）+ `console` 无新 error。
- **Play 冒烟（必跑，含 Dispose 正证据）**：`Boot.unity` 自带 `DevStartupConfig.DirectToTraining: 1`（启动即直入本地测试模式对局，`Room-Test`）；编辑器现场 `list_open_scenes` 已核：唯一打开场景 = `Assets/Scenes/Boot.unity`、`isActive=true`、`isDirty=false`（无需切场景、无覆盖用户未保存内容风险）。步骤：
  1. 冒烟前核 `editor_status`（`playMode=stopped`、`compiling=false`）与 `list_open_scenes`；
  2. `unity command editor_play` → 轮询 `console` 等 `context-created` 与 `view-attached prefab=`；
  3. `capture_game_view --source screen --save_path .workflow/active/client-audit-a3/evidence/a3-play.png` 留帧证（对局画面已出，rig 装配完成）；
  4. 等待 ≥ 3s（覆盖多次 Tick）→ `unity command eval --code "LiteGame.TestModeRuntime.RequestExit();"`（**GM 面板「退出本地测试模式」按钮的同一请求桥**，驱动产品正常离场链：`Leave → Ended → finally → DetachView → rig.Dispose`）；
  5. 轮询 `console` 等 `ended reason=Leave`；`find_gameobjects --name "[BattleView]"` 与 `--name "Hud_Battle"` 均无命中（**Dispose 已真实销毁 rig 自有对象**的正证据）；`console` 无新增 error/NRE；
  6. `unity command editor_stop` 退出 Play，`editor_status` 回到 `stopped`。
  **证据边界**：步骤 4 走 GM 请求桥而非 UI 点击——本批验证的是生命周期拆除，不声称 UI 可点性；`editor_stop` 的宿主关闭收尾另作兜底。残余风险（未驱动 ESC→设置页入口）在进度卷如实标注。
- **全量验证**：`wf verify`（L1/doc/restore/l0/toolchain/Doc lane/既有 EditMode 套件）。**该固定集不含** 27 个战斗表现/绑定定向类，故另跑定向命令并以 `evidence/targeted-*.txt` 留输出，与 `verify.json` 并列作为收口证据。
- **逐子批验证**：A3-1/A3-2/A3-3 各自完成后先跑定向面（战斗表现面 / UIBind 面 / L1 FSM 面）再进入下一子批，最后跑全量与 Play 冒烟；每子批完成即记录 `git status --short` 快照（`evidence/a3-N-status.txt`），用于 AC7 文件集互斥核对。
- **探针转绿**：全部施工完成后直跑 `python .workflow/active/client-audit-a3/red-structure-check.py`，要求 `exit=0`，输出存 `evidence/structure-green.txt`（`wf verify` 不执行 red_commands，故单独留证）。

## 验收标准

- [ ] AC1：三个子批全部落地，主类职责收敛——`BattleViewRig` 不再持有取件/HUD/反馈/视角域字段与实现；`UIBindIndex` 不再持有本地化/输入订阅/列表域状态字典；`StageMachineCore` 无树字段与 `BuildTree`；外提件一类一文件。
- [ ] AC2：公共 API 不破——`UIBindIndex` 与 `HierarchicalStageMachine` 的 public 成员集合不减少、签名不变（`git diff` 逐项核对）；`BattleViewRig` 的 internal 构造/`LoadAsync`/`Attach`/`Tick`/`TickPreSample`/`Dispose` 签名不变。`StageMachineCore` 的 protected 面变化仅限“树字段删除 + 构造签名收敛 + 新增 `InitializeCore` + `_transitions/_audit` 由 readonly 改为构造尾段赋值（仍 protected、无公共读写面）”——全仓 `: StageMachineCore` 派生类仅 `StageMachine`/`HierarchicalStageMachine`（grep 证据入卷），无仓外扩展承诺。
- [ ] AC3：Unity 重编译 0 错（`recompile_status failed=false`，console 无本批 error）。
- [ ] AC4：L1（PullRequest）全量 PASS；定向 EditMode 全部通过。若出现失败项，须以本批基线 pristine 对照证明失败名单与基线一致且不落本批改动面，否则不得收口。
- [ ] AC5：行为零变化——tick 相位表与拆除跨域不变量逐项保持（评审对照方案表）；降级日志文案与异常类型不变；Play 冒烟日志显示 `context-created` + `view-attached`，全程无新增 error/NRE，截图存证。
- [ ] AC6：审计回写完成——5 条登记删除（类设计高 3 + 重复实现 1 + 类设计低 1）、归档各追加 1 行；批次计划 A3 覆盖 4→5、A5 覆盖 10→9（A5 清单删去 “StageMachineCore 树表示法”，A3 清单补该条；覆盖合计 122 不变，**未结清 127→122**，差额仍为 5 条跳过项）、A3 状态更新；客户端审计线新增一章；`doc-lint` 0 violation / 0 warning。
- [ ] AC7：**代码文件集**按子批互斥，可独立回退（每子批完成记录 `git status --short`，并计算相对上一份快照的**差集**（新增+修改）→ `evidence/a3-N-diff.txt`；三份差集无交集；并行线文档改动与 `.workflow/` 不计入；文档与批次台账集中收尾，不纳入子批回退边界）。

## 风险与回滚

- **风险 1：拆除/tick 顺序被无意改变**（表现件在视图之后拆除、指针锁定未解、相机接管残留）。
  - 监控：评审按 §设计要点相位表逐项对 diff；Play 冒烟覆盖真实装配/关闭；定向 EditMode 中 `TpsPointerLock`/`TpsCameraLook`/`PresentationLifecycle`/`PresentationOwnership` 覆盖生命周期语义。
- **风险 2：`UIBindIndex` 门面转发漏项**（Lua shim/Dispatch 调用到空实现）。
  - 监控：`UiControlListProtocolEditModeTests`（真 Lua 全链）+ `LTextBind`/`LTextTypewriter`；评审对照 public 成员逐一确认转发。
- **风险 3：FSM 构造校验顺序或字段初始化时序变化**（`_transitions/_audit` 由构造体后置赋值）。
  - 监控：`EnsureInitialized` 守卫 + L1 FSM 全套（含构造抛用例、恢复/审计/看门狗）；评审核对“注册 → 建树 → 校验 → 审计 → OnInit”原序。
- **风险 4：新增文件 .meta 未随 Unity 导入生成**。
  - 监控：重编译后检查 `.meta` 是否生成并随提交；不手写 meta。
- **风险 5：Play 冒烟受编辑器现场状态影响**（当前打开场景/编译中断）。
  - 监控：冒烟前 `editor_status` + `recompile_status` 均绿；冒烟日志以本次会话 cursor 取增量，不把历史日志归因本批。
- **回滚**：按子批代码文件集回退（A3-1：`BattleViewRig.cs` + 6 新增 + `FeatureDebugTunables.cs`/`LookTuning.cs` 引用点；A3-2：`UIBindIndex.cs` + 3 新增；A3-3：`StageMachineCore.cs` + `HierarchicalStageMachine.cs` + `StageMachine.cs`），新增件连同 Unity 生成的 `.meta` 一并删除；文档回写单独回退。
