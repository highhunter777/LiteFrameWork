# UI 运行时专项设计

> 状态：现行专项设计
> 版本：1.0
> 更新日期：2026-09-21
> 适用范围：UI 壳、导航、转场、本地化、热更和流程边界
> 维护责任：客户端 UI
> 类别：**机制/运行方式**——UI 壳怎么跑：更新驱动架构、跳转与转场、动效与特效运行时、本地化、富文本校验、覆盖层补丁、资源与热更、流程边界。制作红线见《UI 制作规范》；工具设计见《UI 编辑器工具专项设计》；批次与缺口见《UI 演进路线图》；历史施工记录见 `Docs/Archive/2026-09-20/`。

## 1. 更新驱动架构（2026-09-17 定案）

解决三件事：壳每帧无脑推 Lua `OnUpdate`、高频元素每帧触发 Canvas 重建、事件化后事件频率上升反而更贵。

**四档分级（每档只有一个驱动者）**：

| 档 | 内容 | 驱动者 | 频率 |
|---|---|---|---|
| L0 静态 | 背板/边框/标题 | 无 | 打开写一次 |
| L1 事件 | 按钮/文本/红点/列表数据 | C# 事件 → 控件 | 到达时 |
| L2 帧驱动 | 血条/飘字/倒计时/进度 | **HudTicker（唯一，纯 C# 不过 Lua）** | 逻辑帧末 flush |
| L3 低频心跳 | Lua 界面逻辑 | Lua `OnUpdate`（门控+分频） | 10Hz |

两条纪律钉死：**L2 永不过 Lua；L3 永不高频。**

数据流（取代"数据到达就写控件"）：`Sim 帧事件/快照差分 →（只传 id+数值）→ HudModel 纯 C# 聚合器（同帧同类合并）→ DirtySet 脏位（零分配）→ HudTicker 每逻辑帧末统一 flush → 控件.Render()`。聚合 + 脏位 + 统一 flush = "事件化不会反而更贵"的前提（8 人一帧 5 次命中同一目标 = 1 次写入）。

五个设计决策：

1. **控件双模**：`AutoTick=true` 默认不变 + 公开 `Tick(dt)`；HUD 组装批量关自驱 → N 个 Update 变 1 个（模板自检 36/36 零回归）。
2. **HudTicker 锚逻辑帧末**（`FrameDriver.onLogicalFrame`/快照到达），不锚 Unity Update——与和解对齐、合并同帧事件、不随渲染帧率漂移。
3. **门控放 adapter、不动 `IUIFormLogic` 契约**：`_onUpdate == null` 早退须在参数构造之前。
4. **`UpdateHz` 是内容层旋钮**（默认接口方法，现有实现零改动）：真价值是防内容侧写 60Hz 重逻辑；分频后 `RaiseUpdate` 不在每帧路径，`SafeCall` 纪律自动满足。
5. **事件过桥两规则**：高频事件永不过 Lua（走 L2）；低频事件只传数值 + 单帧上限（超限记日志丢弃）。批量口子（如 `SetHpBatch`）只在"单帧同类调用 > 10 次"时才加。

契约变更仅 4 处（门控/RaiseUpdate 直调/UpdateHz 默认方法/AutoTick），均不破坏现有面。与《商业级通用客户端框架总设计》§12.2 的衔接：绑定区 = L1（离散低频）；"每帧值"原定的 `BindFrame` 由 **L2 取代**（不走绑定层扛每帧值）；命令式区保持，所有权互斥断言继续生效；禁双向不变。**MVVM 收益在 C# 内部拿（HudModel → 控件），跨语言边界只保留 MVP 的离散命令与离散绑定。**（落地顺序见《UI 演进路线图》性能批）

## 2. 界面跳转与转场

```csharp
UniTask<UIForm> GoAsync(int formId, IUIData data = null);  // = ShowAsync + 记录 caller
UniTask<bool>   BackAsync(IUIData result = null);          // 关"当前可返回界面"，result 回传 caller
```

- `Go` 不销毁当前界面（可见性仍由层级组 + 遮盖重算管）；`Back` 把 `result` 经 **caller 的 `OnShow(data)`** 送回——复用七回调，不新增回调。
- "当前最上层可关闭界面"判定：① 有 `Go` 记录的 caller 且可关 → 关它；② 否则取 BaseDepth 最高组的栈尾；③ 都没有 → 返回 false（Lua 决定兜底）。
- 守卫走既有 `IPopInterceptor.CanClose`（战斗中禁退/二级确认策略位已在）；返回 false 由 Lua 兜底。
- Lua 受控面：`Bridge.ui.Go(id, tbl)` / `Bridge.ui.Back(tbl)`，`self.ui` 同步加两条（13 → **15** 方法，《UI 控件 Lua API 参考》需同步）。
- 不做（判据留档）：历史栈/深链/路由表/`tbuiform` 跳转列——出现"任意界面直达任意界面 + 多步回退"真实需求时，在 `UIStack` 旁加独立返回链（不改 `UIStack` 语义）。

### 2.1 转场状态机与编排（与 Go/Back 同批定案；**已落地 2026-09-19**）

- 第一原则：**转场态 ≠ 生命周期态**——`UIFormState` 七态是逻辑生命周期，转场是表现阶段，禁止把转场塞进 `UIFormState`。
- 形态：`StageMachine<TransitionId, TransitionReq>`（Idle/Out/In 平铺三阶段；`TransitionReq` = readonly struct payload：Mode + Outgoing/Incoming）。不用 HSM（平的阶段，层级/历史/冒泡全是负担）。
- **模式由 Go/Back 推导，不给内容层自由度**：`Go` 同组无全屏 = **Push**（只播 Incoming）；同组已有全屏 = **Replace**（出入场同时，交叉淡入走可选 `IReplaceTransition`，未实现则壳合成 WhenAll）；`Back` = **Pop**（Outgoing 离场、下方露出不重播）。`PlayShow/PlayClose` 策略接口不变。
- 四条硬规则：① 同帧多次 `Go` 只留最后一个（丢弃记 Warning）；② 转场中新请求：目标相同忽略（幂等），否则排队 1 个；③ **交互门由壳统一管**（`blocksRaycasts=false`，不依赖策略）；④ **超时兜底** `MaxDuration`（默认 2s）强制收尾 + `completed=false`（防策略不回调卡死）。
- **落地状态（2026-09-19）**：`Shell/UI/Transition/` 四件（Runner = StageMachine + 待办队列 + 超时兜底 + 交互门）+ `IReplaceTransition` + `UIService` 接线已交付；EditMode 用例 **11 条全绿**。余项：Lua 侧 `begin/end` 完成事件桥随 Go/Back 一起做（当前只到 C# 事件层）。

## 3. 动效与特效运行时

- **播放归属定案（2026-09-19）**：**不建 UI 特效服务**；`UiFx` 原语扩口承载——UI 动效（位移/缩放/淡入出）+ 帧动画播放，调用方 = 控件自身 / Lua 受控 API（whitelist 派发通道）；同屏 ≤3 预算由调用方自持，超限**记日志不静默丢**；**建服务的触发条件** = 需跨界面集中管预算/降级，或按质量档统一跳过。运行时直接播合法（无"先经编辑器"前置；样式编辑器只管资产引用，不是播放许可）。
- **`UiFx` 原语与 G20 动效口**：`Pulse`（透明度呼吸）/ `Flash`（高亮回落）/ `Slide`（偏移滑回）走 whitelist 派发通道，Lua 侧 `self.ui` 三方法（《UI 控件 Lua API 参考》§1）；解析器 `ResolveGraphic/ResolveRect`，未命中即抛。🔴 `Pulse/Flash` 走 `Graphic.DOFade/DOColor`（顶点色）——仅可用于 Image，**禁对 `TMP_Text` 调用**（《UI 制作规范》§3）。
- **与 VFX 服务的分工（三权分立的运行时侧）**：VFX 服务（`LiteSim/View`：`VfxHandle`/`VfxCatalog`/`VfxBudget`/`VfxService`）管**世界空间**特效——挂点跟随实体、同屏预算/降级、到期按世界时钟扫描、`silenceUntilFrame` 防重播，**不并进 UI 动效**；UI 侧特效（a. 特效夹层 / b. Canvas 内嵌）由界面/控件经本节归属播放。转场编排见 §2.1；制作期选择规则见《UI 制作规范》§4。

## 4. 本地化（独立 key 表）

- 表 = `Luban/Data/#text.xlsx`：`key`（`UI.<界面>.<语义>` / `Common.<语义>`，生成常量，拼错编译期挂）+ 一语言一列（列名 = `GameSettings.Language`）+ `note`。产物 `tbtext.bytes` + `tbtext.lua` + key 常量类。
- **语言范围定案（2026-09-20）：当前仅 `zh-CN`（源语/开发基准）+ `en`**；将来加语言 = 表加一列，代码零改动。
- 取用收口三方法：`LText.Get / LText.Format({0} 插值) / LText.Raw(key, fallback)`；Lua 侧 `Bridge.text.Get/Format`。
- **缺 key 绝不抛**：开发期记 Warning 回显 `[key]`；发布期回退 zh-CN 列，再缺回显 key。插值不做 ICU（接强复数语言再上）。
- **英文适配三条**：① 复数启用 `xxx.one`/`xxx.other` 后缀约定（`1 item` / `2 items`）；② 大小写直接存最终显示形态，禁运行时 `ToUpper/ToLower`；③ 中英语序靠 `{0}` 编号占位符在各列模板内各自排列（"击杀 {0}" / "{0} killed"），天然解决。
- **长度适配**：英文平均比中文长 30–50%——灰盒 UI 用英文文案验收（见《UI 制作规范》§7）；溢出策略靠文本档位。
- **prefab 文案校验**：prefab 里 TMP 文本一律不写死文案（写 key 或留空，《UI 制作规范》§2 红线）；落成校验器规则（非空且非合法 key = 违规，进 L1 纪律扫描）→ 文案单一可写源 = 表，改文案即热更。
- 语言切换：写 `GameSettings.Language` → 广播 `LanguageChanged` → `UIService.RefreshAllOpen()`（对 Active 界面重跑 `OnShow(null)`，不换逻辑表）。
- **字体简化（2026-09-20 定案）**：中英一个 `NotoSansSC SDF` 全覆盖（含拉丁字形）——TMP fallback 链与按语言分包**推迟**，真加 CJK 之外的语言（俄/日/韩）再启用。
- **数字/时间**：HUD 数值与倒计时固定 InvariantCulture 格式化（倒计时已固定 mm:ss 自渲染），不随系统地区漂移。
- 判据留档：**不借鉴 GF 本地化模块**（XML 字典 + 各界面手动刷 + key 无编译期校验，均弱于本设计；UI 壳借鉴 GF 借的是实现形态，本地化无形态可搬）。

## 5. 富文本校验与注入

- 白名单与禁用标签清单见《UI 制作规范》§7。
- 校验两处 fail-fast：编辑期扫描器（扫 `#text.xlsx` 全语言列 + 业务文案列，与 DisciplineScanner 同风格）+ 启动期断言（RegistryFiller 同批兜底，进填充报告不阻断启动）。
- 注入顺序（安全）：玩家输入/外部字符串 **先转义再插值**（防注入标签）；作者标签信任但校验。
- 性能：TMP `richText` 默认 true 保持；真开销是每帧写文本（已由 §1 的 L1/L2 治理）。

## 6. 覆盖层补丁（运行时改 → 存盘）

- 流程：运行时改属性 → 收集 `UIPatch` → 存盘 `Assets/UI/Patches/<Form>.uipatch.json`（文本可 diff）→ 编辑器菜单「应用补丁到 Prefab」经 Pipeline 声明式写入 → 补丁归档/删除。
- 边界：**只在既有节点上改属性**（RectTransform/TMP_Text/Image/CanvasGroup 值 + 文本 key），不做结构增删（归编排器）；应用时机 = Instantiate 之后、OnInit 之前；**prefab 是唯一真相**（补丁是暂存态，不是第二真相）。
- 生效范围：只在 Editor 与 dev 调试包（`LITEUI_PATCH` 宏）；生产包不收集不写盘（红线）。真机取回 = 写 persistentDataPath 手动拷回。
- 一键应用冲突策略：补丁旧值与 prefab 现值不一致 → 打印差异要求确认，不静默覆盖。

## 7. 资源与热更

- 目录口径（2026-09-19 `e4c6cb9` 重排后）：UI 资产归 `Assets/UI/Screens/`（界面，`PackSeparately`）+ `Assets/UI/Widgets/`（控件，`PackDirectory`）——同组两个 CollectPath 不得互相包含，界面与控件用不同 PackRule，路径必须是兄弟目录（`Screens/` 子目录存在的原因）。
- 收集组（`BundleCollectorSetting.asset`，经 YooAsset 官方 API 写入，禁手改）：`LiteGameScreens` + `LiteGameWidgets`，tag `ui`。实测：编辑器态采集 43 件 / UI 目录 26 件全带 tag；运行时可加载 **26/26** → PASS。
- 加载路径：`UIDemoPage.LoadTemplate` 真机分支经 `AssetService` 加载（编辑器保留 AssetDatabase 快路径，未命中回落运行时路径）；已 UniTask 化（禁原生协程）。

### 7.1 运行期增量重填（不重建 env）

**交付件**：`ILuaRegistry.Generation`（只作读数不作失效判据）；`LuaComponent.RepreloadAsync` + `ClearRequireCacheByRoots`；`LuaBehaviourAdapter.Logic` + `Release()`（释放逻辑表与七回调的 Lua 引用，`LuaBase.Dispose` 幂等）；`UIForm.NeedsReinit`（换表后补跑 OnInit，否则按钮全不响应）；`UIService.MarkLogicStale/ApplyStaleLogic`；`LuaRegistryRefillService`（编排 + `IModuleStats`）；Editor 菜单 `LiteGame/Lua/Refill Registries %&f`。

**顺序钉死（半更新窗口尽量短）**：

```
① MarkLogicStale（先标后清，窗口内界面用旧表跑完）
② RepreloadAsync（改动的 .lua 进预载缓存，不重建 env）
③ Bridge.Data.ClearLuaCaches()
④ ClearRequireCacheByRoots(UI/Content/Strategies)  ← 不清则 require 命中旧 chunk
⑤ 三注册表 Clear()（Fill 重复抛，清是重填前置；Generation 各前进一位）
⑥ RegistryFiller.FillAll()（与启动期/DevReload 同一条路径）
⑦ ApplyStaleLogic()（池中界面立刻换表；Active 界面保留标记等 Close 时换）
```

**生效语义（最终一致，不假装瞬时）**：Active/Covered/Paused 界面保留旧表跑到关闭；池中界面立刻换；被替换的旧适配器 `Release()` 释放引用。

**关键决策**：旧 LuaTable 随适配器替换时释放（精确到"无人持有"，防 use-after-free；DevReload 走 `env.Dispose` 兜底）；`package.loaded` 按三根前缀清（根集是 `gen_lua_keys.py` 校验过的封闭集；已知边界：跨根依赖仍走缓存）；`Generation` 不作失效判据（逐项 Fill 也递增，失效一律显式 `MarkLogicStale`）；策略热更**未做**（三策略件当前未从注册表注入，无消费点，做了也是空转）。

## 8. 流程与 UI 的边界

- 跨界承载者已定案：payload `ProcedureArgs`（`roomId/frameNo/BattleContext` 以只读字段加，不用 owner 载体/字符串键字典）。
- **"安全窗口"（回主城/战斗结束）= 流程迁移点**：语言切换重刷与 Lua 增量重填的真实触发者都挂在流程迁移上（当前只有 Editor 调试菜单，流程线现状与排期见《UI 演进路线图》流程前置）。
- 边界定案：**流程广播事件 + UI 层订阅**（`MatchStarted/BattleStarted/BattleEnded`），流程零 UI id 依赖；需要等 UI 就绪时由**订阅方 await Go**（带超时），**流程迁移不等表现**（表现不携带判定，与 `PlayTransition*` 容错吞异常同一族纪律）。
- 流程迁移与转场是**两层**：流程迁移（逻辑阶段）→ 广播 → UI 层决定是否 `Go` → 转场事务（表现编排）；不允许流程 `OnEnter` 里长时间 `await` 转场完成。
