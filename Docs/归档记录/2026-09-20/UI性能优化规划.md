# UI 性能优化规划

> 制订日期：2026-09-17。定位：**UI 壳与控件库的性能治理施工图**——基于当前代码实测出的热点清单（每条带证据）＋分批落地顺序＋预算红线。
> 压力基准（2026-09-17 用户定案）：**M11 demo 规模**——8 人房 HUD（血条/护盾条/击杀条/飘字）＋结算页，**移动端 60fps**。
> 起步策略（2026-09-17 用户定案）：**直接改已确认热点**，测量与护栏后置为批④。
> 范围：`Assets/LiteGame/Scripts/Runtime/Shell/UI/**` ＋ `Assets/LiteGame/UI/Widgets/**`（prefab/构建器）＋ 相关 Editor 校验。**不碰 `LiteSim.Core`**。
> 相关文档：《动效设计方案》§4（性能红线，本规划承接其落地）、《UI控件库Prefab落地规划》§3（批⑧ 剩余缺口）、《编辑器设计规划》§2.1（校验面板）。

---

## 1. 现状盘点（证据表）

全部为本次读码实测，非推断。

| # | 事实 | 证据 |
|---|---|---|
| 1 | 每界面一个独立 Canvas（`AddComponent<Canvas>` + `overrideSorting`） | `Shell/UI/UIForm.cs:28-31` |
| 2 | 运行时**无 CanvasScaler**（仅编辑态示例有） | `UIService.cs:44-53` 建 `[UIRoot]` 无 Canvas/Scaler；`Editor/WidgetDemoMenu.cs:17-21` 是唯一出处 |
| 3 | **零图集**：无 `.spriteatlas`、无 `packingTag` | 全仓 `find Assets -name "*.spriteatlas*"` = 0；`grep packingTag Assets/LiteGame` = 0 |
| 4 | 关闭界面不销毁，`SetActive(false)` 落池（按 formId 常驻） | `UIService.cs:109-144`、`UIForm.cs:117-121` |
| 5 | 每帧遍历全部持有界面，仅 Active 转发 `OnUpdate` | `UIService.cs:263-267`；驱动 `GameEntry.cs:144` |
| 6 | Lua 回调走 `params object[]` → 每帧每界面一次数组分配 + float 装箱 | `LuaBehaviourAdapter.cs:90`（`Call(_onUpdate, "OnUpdate", deltaTime)`）、`:137-141` |
| 7 | `VirtualList.Refresh` 每次重复 `AddListener` | `Widgets/VirtualList.cs:64-65` |
| 8 | `VirtualList` 池**只增不毁**（无收缩/Trim） | `Widgets/VirtualList.cs:43-48` |
| 9 | `VirtualList` 裁剪靠 `SetActive` 开关，且滚动中每次事件重算 | `Widgets/VirtualList.cs:69-91`；脏标在 `:65` |
| 10 | `Countdown` 每帧无条件写 `Label.text`（mm:ss 每秒才变） | `Widgets/Countdown.cs:47-50,52-57` |
| 11 | `HpBar` 每帧 Lerp，收敛后仍每帧跑 | `Widgets/HpBar.cs:33-38` |
| 12 | `FlyTextPool` 每条飘字一个 `UniTask.NextFrame` 循环 + **每帧改 `TMP_Text.color`** | `Widgets/FlyTextPool.cs:41-67`（`:57-59` 改色 = 顶点 mesh 标脏） |
| 13 | `BindIndexBuilder` 每次 OnInit 全树 `GetComponentsInChildren`（含分配） | `Bind/BindIndexBuilder.cs:16`；调用点 `LuaBehaviourAdapter.cs:74` |
| 14 | `raycastTarget` 仅零星关闭，未成规范 | `Editor/WidgetPrefabBuilder.cs:113,130,254,271,529,743` |
| 15 | 红点已是事件驱动、零轮询（**已达标，无需优化**） | `Widgets/RedDot.cs:37-40`、`RedDotRegistry.cs` |
| 16 | 全工程 0 处 LayoutGroup / ContentSizeFitter（**无此开销**） | 控件库与 prefab 构建器均手动 `anchoredPosition` 排布 |
| 17 | 动效统一 DOTween（自更新，非 MonoBehaviour.Update），无原生协程 | `Anim/UiFx.cs`、`Strategies.cs:42-79` |
| 18 | 有 `IModuleStats` 契约与 `UIService.Snapshot`（无 ProfilerMarker） | `UIService.cs:269-290`、`LiteFramework` `IModuleStats.cs` |

**结论**：瓶颈不在"Update 数量"或"字典遍历"这类常见嫌疑（当前规模下可忽略），而在三处：**① 高频元素每帧触发 Canvas 重建（飘字/倒计时）② 缺失渲染面基础设施（图集/CanvasScaler/分层）③ 少量确定的泄漏与每帧分配**。

---

## 2. 预算与红线（M11 验收口径）

移动端中端机、8 人房满场战斗 30s 采样的预算：

| 指标 | 红线 | 采集方式 |
|---|---|---|
| UI 主线程耗时 | **≤ 2.0 ms/帧** | Profiler 抽样（批④ 记档） |
| UI drawcall（单页） | **≤ 25** | Frame Debugger / Profiler |
| overdraw | **≤ 2 层**（全屏动效叠加） | 承接《动效设计方案》§4 |
| 稳定帧 GC.Alloc（UI 路径） | **0 B/帧**（不含加载） | Profiler GC Alloc 列 |
| 界面打开（含实例化+OnInit） | ≤ 16 ms；结算页 ≤ 32 ms | 计时直读 |
| 控件模板自检 | **31/31 PASS 不许回归** | 菜单 `LiteGame/UI/校验控件模板` |
| L1 | 190 用例全绿不许回归 | `dotnet test Tests/Tests.slnx` |

---

## 3. 优化清单（按批次）

### 批① 热点修复（真 bug 与每帧浪费，立即可做，不依赖 M11）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| A1 | `VirtualList.Refresh` 每次都 `AddListener`（证据 #7）→ 监听累积，一次滚动触发 N 次 `ApplyCulling`，且界面复用后旧委托仍在 | `Awake`/`OnEnable` 一次性挂 + 幂等守卫；或挂前 `RemoveAllListeners` | 反复 Refresh 20 次后滚动一次，`ApplyCulling` 调用计数 = 1（加测试钩子断言） |
| A2 | 池只增不毁（证据 #8）→ 数据从 500 条降到 10 条后仍留 500 个实例常驻 | Refresh 时按 `n` 收缩（销毁富余实例），保留高水位上限；与"池的持有纪律"一致（池归控件自持） | 大数据量→小数据量切换后 `RealizedCount` 与池容量同步下降；池内实例数 ≤ n |
| A3 | 裁剪用 `SetActive`（证据 #9）→ 滚动中反复启用/禁用 Graphic，每次进出视口都吃一次 Canvas 重建 | 改为**窗口复用**：固定 N 个实例，滚出视口的实例直接换绑新索引（不 SetActive）；或至少把"逐项扫描"降为"按滚动增量算首末索引" | 滚动 500 条（视口容 8）时 `SetActive` 调用次数 ≤ 视口容量×2；`Bind(index, rt)` 语义不变 |
| A4 | `Countdown` 每帧写 `text`（证据 #10）→ TMP mesh 每秒 60 次重建，实际每秒才变 1 次 | 先算 `(int)totalSeconds`，与上帧相同即 return（避免插值字符串本身也每帧分配） | 倒计时运行 3s，`Render` 写文本次数 ≤ 4 |
| A5 | `HpBar` 收敛后仍每帧 Lerp（证据 #11） | 收敛判据（差值 < ε）即早退 + 自禁用 `enabled=false`，下次 Set 值再打开 | 静止血条 60 帧内 `Update` 自禁用 |
| A6 | `FlyTextPool` 每条飘字：一个 async 循环 + **每帧改 `TMP_Text.color`**（证据 #12）→ 顶点 mesh 每帧标脏，同屏 16 条 = 每帧 16 次重建 | ① 淡出改 `CanvasGroup.alpha`（不重建 mesh）② 改**单 ticker 集中驱动**：一个 Update 遍历活跃表推进全部飘字，替代 N 个 async 循环（红线：不用原生协程）③ 位置仍走 `anchoredPosition` | 同屏 16 条飘字稳定帧 `Canvas.SendWillRenderCanvases` 次数 ≤ 3；飘字生命周期与池归还语义不变（保留批⑦ 的销毁期守卫） |
| A7 | Lua 回调 `params object[]`（证据 #6）→ 每帧每界面一次数组分配 + float 装箱 | `OnUpdate` 走专用非分配路径（预置参数数组复用；其余回调低频保持现状） | Profiler 稳定帧 UI 路径 GC.Alloc = 0 B；每帧 Lua 回调无新数组（分配计数器断言） |
| A8 | `UIForm.RaiseUpdate` 每帧构造插值字符串 `$"UIForm[{Id}].OnUpdate"` + 捕获 `deltaTime` 的闭包（`UIForm.cs:124-127`）→ 每 Active 界面每帧两处分配，且**与 `SafeCall` 自订纪律矛盾**（`SafeCall.cs:8-10`："仅用于低频入口；每帧热路径不走 try/catch"）。注意：**Lua 侧没写 OnUpdate 也照分**（`Call` 只在 `fn == null` 时跳过，参数数组已在调用点构造） | 热路径直调 + `where` 字符串按 formId 预缓存；门控 `_hasOnUpdate`（Lua 未实现即不进）。**必须与 A7 同批**——只做门控只省 Lua 调用、不省分配 | 无 `OnUpdate` 的界面稳定帧 UI 路径 GC.Alloc = 0 B |

**A1–A3 是一组**（虚拟列表改造必须一次做完，避免"改一半语义漂移"）；**A4–A6 是一组**（高频元素去重建）；**A7 独立**。

### 批② 渲染面基础设施（M11 真机前置，必须与图集同批做）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| B1 | **无 CanvasScaler**（证据 #2）→ 真机多分辨率下 UI 尺寸失控 | `[UIRoot]` 挂 `Canvas` + `CanvasScaler`（`ScaleWithScreenSize`，参考 1920×1080，`MatchWidthOrHeight=0.5`），或按分辨率策略注入；每界面 Canvas 保留（隔离 rebuild 的既有红利） | 三档分辨率（16:9 / 18:9 / 4:3）下 HUD 与结算页布局不溢出、SafeArea 正确 |
| B2 | **零图集**（证据 #3）→ 跨纹理打断合批，HUD 图标 + 结算页图标 drawcall 高 | 建 `UI.spriteatlas`（收集 `Assets/LiteGame/UI/**` 与 `Art/UITextures`）；构建器给 Sprite 节点打 `packingTag`；与 YooAsset 收集组（tag `ui`）对齐 | 单页 drawcall ≤ 25；构建器重建 25 件模板后自检 31/31 不回退 |
| B3 | 每界面独立 Canvas（证据 #1）在 HUD 场景会失控（每个条目一个 Canvas = drawcall 断点爆炸） | **HUD 定案**：单 Canvas + **动态/静态分层**——静态层（准星/边框/小地图背景）与动态层（血条/飘字/击杀条）各一个子 Canvas；只有高频变化元素进动态层 | HUD 场景 Canvas 数 ≤ 3；`CanvasScaler` 只挂根 |
| B4 | 《动效设计方案》§4 的"动效元素再拆子 Canvas"**未落地** | 落地为构建器规则 + 校验器断言（动效件必须挂独立子 Canvas 或被列入白名单） | 校验器对 25 件模板跑规则 0 违规 |
| B5 | `raycastTarget` 未成规范（证据 #14）→ `GraphicRaycaster` 每次触摸遍历 canvas 上全部可命中 Graphic | 构建器**默认全关**、仅交互件显式开；校验器断言"非交互 Graphic 的 raycastTarget=true 即违规" | 校验器 0 违规；HUD 触摸帧耗时下降（批④ 记档） |
| B6 | overdraw 红线仅文档（§4） | 编辑器侧人工检查清单 + 全屏转场单 Image 约定（已实现，保持） | 人工过检记录 |

**顺序硬约束**：B2（图集）与 B3（分层 Canvas）必须同批——只分层不打图集 = 白增断点，只打图集不控重建面积 = 收益被 rebuild 吃掉。

### 批③ 数据面（M11 前定案，防返工）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| C1 | 若 HUD 走"每帧 Lua `OnUpdate` 逐字段拉 8 人血条"→ 每次 `Dispatch` 都构造 `LuaTable` payload（`LuaBehaviourAdapter.cs:106-133`），GC 与跨语言开销双高 | **定案：HUD 数据由 C# 直驱**（Sim 帧事件/快照差分 → HUD 组件，跨边界只传 id 与数值）；Lua 只管低频界面逻辑；确需批量时给批量 API（如 `Bridge.ui.SetHpBatch`），**禁逐字段每帧过 Lua**。<br>**两条衔接纪律**：①**UI 改事件驱动不会自动停掉壳每帧推的 `Logic.OnUpdate`**——那条链由 `UIService.Tick` 的固定心跳驱动（与数据流正交），要停必须改调用点（A8 门控 / 按需 tick）并在 Lua 侧不再写轮询；②事件频率上升时（8 人房命中/伤害每秒几十个）**禁逐事件过 Lua 建 payload 表**，否则总开销比轮询更贵——必须 C# 侧聚合后直驱或批量回调。<br>③事件化替代不了"逻辑自身每帧推进"（冷却/超时/累加窗口）：把它挪进 C# 或降频（如 5Hz），不继续 60Hz 过桥 | 8 人房 HUD 稳定帧 Lua 调用次数 = 0（仅事件驱动时）；GC.Alloc = 0 B |
| C2 | `BindIndexBuilder` 每次 OnInit 全树扫描 + 数组分配（证据 #13）→ 结算页这类大页面打开时卡顿 | 按 prefab 资源路径**缓存"名字 → 节点路径"索引**，实例化后只按路径装配；`Editor/BindGen/` 目前为空，可作为生成侧落点 | 同 prefab 二次打开不再触发全树扫描；结算页打开 ≤ 32 ms |
| C3 | 红点/动效已达标（证据 #15/#17） | 保持不变，仅纳入批④ 统计位 | — |

### 批④ 测量与护栏（起步策略为后置，但 M11 前必须补齐）

| 项 | 内容 |
|---|---|
| D1 | `ProfilerMarker` 标注 UI 关键段：`UIService.Show`、`UIService.Tick`、`VirtualList.ApplyCulling`、FlyText ticker、`LuaBehaviourAdapter.OnUpdate` |
| D2 | `IModuleStats` 扩展（挂 DevHUD，禁止每帧分配）：活跃 tween 数、活跃飘字数、列表 realized 数、Canvas 数、UI GC 累计 |
| D3 | 基线记档：Editor + Android 真机，8 人房满场 30s，按 §2 表格采全指标（含改动前后对照） |
| D4 | 校验器规则进 CI：raycastTarget / 子 Canvas / packingTag / CanvasScaler 四项编辑态扫描，违规即红（与既有纪律扫描器同风格） |

---

## 4. 不做清单（明确排除，避免重复讨论）

- **不换 UI 框架、不自研合批**：UGUI + TMP 保持（《自研框架设计方案》§141 控件归壳的定案不变）。理由：①UGUI 已按 material/texture/render order **自动合批**，要超过它只能绕开 Canvas 渲染路径（自搓 mesh + 自定义 shader，或换渲染路径）——那是 UI 框架级重写；②**合批与 rebuild 隔离是反向目标**——Canvas 越多 rebuild 越局部（批② B3 要的），但 drawcall ≥ Canvas 数；自研合批若想跨 Canvas 合并，等于废掉隔离本身；③收益已被更便宜的手段覆盖：图集（同纹理由自动合批生效）＋ 分层 ＋ 构建器按纹理排布 ＋ 用九宫格/单层替多层装饰；④成本面：shader 变体、GLES/Vulkan 平台差异、TMP 自带 SDF atlas、Image.Type/九宫格语义丢失、长期维护——本项目为 demo、控件属壳机制层，不接受框架级重写。
  **触发自研的判据**：图集 ＋ 分层落地后 drawcall 仍超预算 → 先用 Frame Debugger 定位断批原因（多数是渲染顺序/材质穿插，属内容问题、改构建器排布即可）→ 仍不达标才轮到"多图集按页切换"；自研 batcher 是最后手段，需同时满足"重度 UI（数百元素级榜单/战报）＋ 确认 UGUI 为瓶颈 ＋ 有渲染专责"。当前 M11 规模用不到。
- **不优化 `UIService.Tick` 的字典遍历**（证据 #5）：M11 十个界面量级 ≈ 600 次枚举/秒（纳秒级，占 2ms 帧预算不到 0.05%），且 `Dictionary.ValueCollection` 枚举器是 struct、零分配。真正花钱的是它**调起的东西**——每帧每界面的 Lua `OnUpdate`（A7 修的就是它的 `params object[]` 分配+装箱），而不是遍历本身。
  **判据（留了口子）**：批④ 实测若 Tick 在 UI 主线程耗时中占比 > 5%，改法是把 Active 界面单独维护成列表（约 10 行改动、随时可加）；触发条件是界面数上千，本项目不会发生。
- **不使用 LayoutGroup 排布控件模板与列表**（证据 #16，实测全工程 0 处含第三方 prefab/scene）。这不是禁忌，是三条具体判据：①**与池化/裁剪对冲**——LayoutGroup 的契约是"子集合或尺寸一变就重排全部"，而 `VirtualList` 靠 `SetActive` 裁剪 + 手动 `anchoredPosition` + 实例复用换绑（`Widgets/VirtualList.cs:75-91`），挂上后每次进出视口的开关都触发整组重排、并按 active 子集覆盖我算好的窗口位置与滚动范围；②**开销是响应式的**——代价在"标脏 → `CanvasUpdateRegistry` → 重算 + 推父链 + mesh 重建"，而血条/飘字/倒计时恰好每帧在变，正是批① 要消灭的东西；③**模板由构建器确定性生成、结构固定**（`WidgetPrefabBuilder`），排布在编辑态一次算清、运行期成本为 0，用 LayoutGroup 是把"算一次"换成"每次变化重算"。
  **该用的判据（不搞一刀切）**：内容不定长需按内容自适应尺寸（文本长度决定宽高/自动换行/卡片流）＋ 变化频率低（刷新一次变一次而非每帧）＋ 数量小（几十项内），或纯编辑器期试错布局——满足即用，比手算划算。当前 25 件模板与两个列表都不属于该形态；未来背包/商城/聊天面板若命中该形态**可以引入**。
- **不动 DOTween / 不改动效时长**：动效是表现决策，不是性能手段；低端机降级走策略换短时长（《动效设计方案》§4 已有口径）。
- **不碰 `LiteSim.Core`**：UI 优化不得引入 Sim 侧改动或确定性影响。
- **不做通用 UI 数据绑定框架**：不做"覆盖一切的统一绑定层"（ObservableProperty 全家桶 + 双向 + 每帧自动同步）——理由与判据见 §7.7，纪律承接《自研框架设计方案》§4.7（绑定与命令式按控件混合、禁双向、禁每帧绑定）。
- **不启用调度层**：`IUIScheduler` 契约已注册（`ProcedureLaunch.cs`）但**刻意不消费**——一个 HudTicker 就够（§7.3），多一层调度 = 多一层可观测盲区。
- **不做 UI 分帧加载器 / 异步实例化管线**：四条理由——①**加载本身已是异步**（`AssetService.LoadAssetAsync` + YooAsset），真正的同步段是 `Instantiate` + `BindIndexBuilder` 全树扫描 + `OnInit`；②**UGUI 上"分帧"做不到**——`Object.Instantiate` 是单次原生调用、不可中断，能分帧的只有"多个独立对象分批实例化"（= 界面拆块），不是把一次 Instantiate 切帧；③**原子性代价**：分帧期间界面是半成品态 → 七态机要加中间态、要取消语义、要与转场/遮盖重算竞争 → bug 面远大于收益；④**更便宜的替代已够**：对象池复用（关闭只 `SetActive(false)`，二次打开不走 Instantiate）+ 按 prefab 缓存绑定索引（C2）+ "先显骨架后填数据"（体感优于分帧）+ 在途守卫（`_loading`）。
  **判据（留口子）**：批④ 实测单次打开 > 帧预算（16ms）且**池化/索引缓存/子集拆分都试过仍超标** → 届时做的是"**子块异步加载**"（大页面拆成多个经 AssetService 加载的模块、各自 await），**不是**把一次 Instantiate 切帧。

---

## 5. 风险与坑

| 风险 | 说明 | 对策 |
|---|---|---|
| 虚拟列表改造动 Lua 契约 | `IVirtualListSource.Bind(index, rt)` 是 Lua 侧已公开语义（《UI控件Lua用法表》） | A3 只改内部复用策略，**签名与语义不变**；改前跑模板自检 + Lua shim 实测 |
| 图集打包属序列化内容 | 贴图合并会动资源引用 | **必须经 Unity/Pipeline 操作**（`UNITY-GUIDE.md` 红线）；禁手改 `.meta`/`.spriteatlas` 文本 |
| 分层 Canvas 与图集顺序做错 | 只分层 = 白增 drawcall 断点 | B2/B3 同批，用 drawcall 实测对照验收 |
| 池收缩策略 | A2 销毁富余实例可能与"界面关闭不销毁"的既有池化收益冲突（下次打开要重建） | 只收缩"列表内部实例池"，不动 UIForm 的常驻回收；收缩阈值可配 |
| 批量改控件源文件 | 历史事故：机械拆分脚本因同名覆盖三个源文件（《UI控件库Prefab落地规划》§8.1） | 一次一件、改完即跑 31/31 自检；批量前先算文件名冲突 |
| `await` 之后访问 Unity 对象 | 批⑦ 已抓出 `MissingReferenceException` 两例 | FlyText 改造后保留注销复检与销毁期守卫；任何 await 后复检 `this == null` |

---

## 6. 排期挂接

- **批①（A1–A8）**：立即可做，不阻塞联机线（M9/M10 在推进，UI 壳改动与其无交集）。建议一次提交一件，每件附自检。
- **批②（B1–B6）**：挂 **M11 前置**——M11 demo 消费 UI 壳/控件库/表现层，渲染面基础设施不到位则真机验收会返工。B2/B3 同批。
- **批③（C1–C2）**：与批② 同期定案（C1 是架构决策，越晚改越贵）。
- **批④（D1–D4）**：批①② 收口后补齐并记档；D4 进 CI 后成为长期护栏。

---

## 7. 更新架构设计（批①/批③ 的施工图）

> 2026-09-17 定案。解决三件事：壳每帧无脑推 Lua `OnUpdate`（A7/A8）、HUD 高频元素每帧触发 Canvas 重建（A4–A6）、事件化后事件频率上升反而更贵（C1 的反向风险）。

### 7.1 四档分级（每档只有一个驱动者）

| 档 | 内容 | 驱动者 | 路径 | 频率 |
|---|---|---|---|---|
| **L0 静态** | 背板/边框/标题/图标 | 无 | 打开时写一次 | 0 |
| **L1 事件** | 按钮/开关/文本/可见性/红点/列表数据 | C# 事件 → 控件 | `EventBridge` / 直调 | 到达时 |
| **L2 帧驱动（C#）** | 血条/护盾/飘字/倒计时/进度 | **HudTicker（唯一）** | 纯 C#，**不过 Lua** | 每逻辑帧末 flush |
| **L3 低频心跳** | Lua 界面逻辑（累计/超时/冷却） | Lua `OnUpdate`（门控 + 分频） | 过桥 | 10Hz |

两条纪律钉死：**L2 永不过 Lua；L3 永不高频。**

### 7.2 三段式数据流（取代"数据到达就写控件"）

```
Sim 帧事件 / 快照差分          只传 id + 数值
  ↓
HudModel（纯 C# 聚合器）        同帧同类事件在此合并
  ↓ 写脏项（entityId → 字段位）
DirtySet（位图/栈，零分配）
  ↓
HudTicker → 控件.Render()      每逻辑帧末统一 flush 一次
```

**聚合 + 脏位 + 统一 flush** 是这套设计的心脏：8 人房一帧内 5 次命中同一目标 = **1 次血条写入**。这一步是"事件化不会反而更贵"的前提。

### 7.3 五个设计决策

**① 控件双模：自驱默认、集中驱动可选**（不拆现有 `Update`，保模板自检 31/31 与单机场景）

```csharp
public bool AutoTick = true;                        // 默认 true：独立使用行为不变
public void Tick(float dt) { ... }                  // 公开：集中驱动时由 HudTicker 调
private void Update() { if (AutoTick) Tick(Time.unscaledDeltaTime); }
```

HUD 组装时批量 `AutoTick = false` → **N 个 MonoBehaviour `Update` 变 1 个**。

**② HudTicker 锚在逻辑帧末，不锚 Unity Update**：状态同步下 HUD 该显示"本帧权威状态"；锚 `FrameDriver.onLogicalFrame` / 快照到达 → 与和解对齐（不抖）、天然合并同帧多次事件、写入次数**不随渲染帧率漂移**（120Hz 屏不会多刷一倍）。壳的 `UIService.Tick` 仍是 Unity 心跳，只做"把脏项写进控件"。

**③ 门控放 adapter，不动 `IUIFormLogic` 契约**（步 1 零风险）

```csharp
// LuaBehaviourAdapter：早退必须在参数构造之前，否则 params 数组照样分配
public void OnUpdate(float dt)
{
    if (_onUpdate == null) return;
    Call(_onUpdate, "OnUpdate", dt);
}
```

```csharp
// UIForm：去闭包、去插值串（tag 构造期拼一次）
internal void RaiseUpdate(float dt)
{
    try { Logic.OnUpdate(dt); }
    catch (Exception ex) { Log.Error($"[{_tag}] {ex.GetType().Name}:{ex.Message}", "SafeCall"); }
}
```

**诚实校准**：无异常时 try 块接近零成本（栈展开只在抛出时发生），真正的开销是**委托闭包 + 插值字符串 + params 数组**。`SafeCall.cs:8-10` 那条"每帧热路径不走 try/catch"的实质是"别包一层委托"，不是"别用 try"——本设计保留 try、删掉委托；且经 §7.3④ 分频后它已不在每帧路径上。

**④ `UpdateHz` 是内容层旋钮，不是壳的通用优化**：`IUIFormLogic` 加默认接口方法 `int UpdateHz => 60`（保守，现有 C# 实现零改动）；Lua adapter 返回 `_hasOnUpdate ? 10 : 0`（0 = 永不进）；壳按累计器分频。收益诚实标注：省的是"每帧一次早退判断"（纳秒级），**真价值是防止内容侧写出 60Hz 重逻辑**；附带好处 = 分频后 `RaiseUpdate` 不再是每帧路径，`SafeCall` 纪律自动满足（无需改纪律文档）。

**⑤ 事件过桥两条硬规则**：高频事件（伤害数字/命中/位置）**永不过 Lua** → 走 L2；低频事件过 `EventBridge` 但**只传数值 + 单帧上限**（超限记录并丢弃，禁无界）。`self.ui` 保持 13 方法低频；批量口子（`SetHpBatch`）**只在"单帧同类调用 > 10 次"时才加**——判据明确，不预建。

### 7.4 契约变更清单（4 处，均不破坏现有面）

| 变更 | 位置 | 兼容性 |
|---|---|---|
| `_hasOnUpdate` 门控 | `LuaBehaviourAdapter` | 纯内部 |
| `RaiseUpdate` 直调 + 预缓存 tag | `UIForm` | 纯内部 |
| `UpdateHz` 默认接口方法 | `IUIFormLogic` / `UIBindBase` / `NullUIFormLogic` / `LuaBehaviourAdapter` | 默认实现 → 现有实现零改动 |
| 控件 `AutoTick` + 公开 `Tick` | 25 件控件 | 默认 true → 模板自检零回归 |

### 7.5 落地顺序

| 步 | 内容 | 验收 |
|---|---|---|
| 1（半天，零契约变更） | `RaiseUpdate` 直调 + adapter 门控 | 无 `OnUpdate` 的界面稳定帧 UI 路径 GC.Alloc = 0 |
| 2（1 天） | 控件双模（`AutoTick` + 公开 `Tick`） | 模板自检 31/31 不回归 |
| 3（2 天） | `HudModel`（聚合 + 脏位）+ `HudTicker`（逻辑帧末 flush）——**不等 M11**，用 `SimSandbox` 帧事件 + 假 HUD 验证 | 同屏 8 人血条 + 16 飘字：`Canvas.SendWillRenderCanvases` 稳定帧 ≤ 3 |
| 4 | `UpdateHz` 分频 + 过桥护栏（单帧 Lua 调用上限断言） | 断言实测；HUD 稳定帧 Lua 调用 = 0 |
| 5 | 接批② B3 动态层 Canvas | 见 B3 验收 |

### 7.6 与《自研框架设计方案》§4.7 的关系（防两套纪律打架）

§4.7 管"**驱动方式怎么选**"，§7 管"**变化的时候谁驱动**"——同一件事的两面，衔接如下：

| §4.7 | §7 | 说明 |
|---|---|---|
| 绑定区（值变了要刷，`OnShow` 一次声明） | **L1** | 离散值、低频；`UIBindIndex.BindText<T>` + `UIDataBinder<T>`（`Widgets/UIWidget.cs:55`）是现行实现 |
| `BindFrame`（原定"每帧值唯一合法绑定口"） | **L2 取代它** | 本设计**不走绑定层扛每帧值**，改为 C# 内部（HudModel 脏位 → HudTicker）：既不过桥，也不触发 mesh 重建 |
| 命令式区（有事要办） | L1/L2 的写入动作 | 保持；所有权互斥断言（`UIBindIndex.MarkDriver`）继续生效 |
| 禁双向绑定（无例外） | 不变 | 跨语言双向 = 循环触发地狱 |
| "VM 在 Lua、View 在 C#，跨语言是 MVVM 的天花板：**能绑离散，不能绑连续**" | §7 就是这句话的落地 | 连续变化全部下沉到 C# 侧 |

**结论一句话**：**MVVM 的收益在 C# 内部拿（HudModel 变更 → 控件自动刷新），跨语言边界只保留 MVP 的离散命令与离散绑定。**

### 7.7 为什么不做"通用 UI 数据绑定框架"

先澄清：**不是"不做绑定"，是"不做覆盖一切的统一层"**。现行绑定面（`BindText` / `UIDataBinder` / 所有权互斥）不动、继续用。被拒绝的是"ObservableProperty 全家桶 + 双向 + 每帧自动同步"这类通用框架，五条理由：

1. **跨语言天花板**（§4.7 L459）：VM 在 Lua、View 在 C#，任何自动同步都要过桥 → **每帧连续变化一律不许绑定**。通用绑定框架在跨语言场景里天生只能用一半。
2. **双向绑定 = 循环触发地狱**（§4.7 L454，明令无例外）：跨语言下 A 改 B、B 改 A 的复现与定位成本极高。
3. **"按界面二选一"本身就是错的**（§4.7 L409）：一个 `UIBag` 里金币文本适合绑定、虚拟列表适合命令式 → 按控件混用才是正解，统一层反而强迫二选一。
4. **通用层的可观测性更差**：自动同步把"谁改的"从代码可查变成运行时推断；我们的替代品是**所有权互斥断言**（一个控件一种驱动方式，Debug 下当场抛）——比通用绑定更好查。
5. **收益已被更便宜的手段拿走**：每帧值走 L2（C# 内部脏位 + 统一 flush）、离散值走 L1 绑定、复杂控件走命令式。通用绑定层剩余的独有价值（自动推导绑定、跨界面共享 VM）在当前规模**没有需求方**。

**升级判据（留口子）**：出现"同一份 Model 被 5+ 界面按不同格式展示且频繁变"或"纯 C# 界面内需要 View↔VM 双向编辑"（**仅限路径 B `UIBindBase` 的 C# 界面，跨语言边界仍禁双向**）时，在 **C# 界面内部**上 ObservableProperty + Binder（§4.7 已估 ≈250 行），跨语言边界仍只走离散绑定。当前 M11 规模不命中。

**与既有规划的接口**：`《UI控件库Prefab落地规划》` 批⑧（P1/P2 剩余缺口）落地时**必须遵守本规划 B3/B4/B5 三条构建器规则**，避免"先建、后改"的二遍工。
