# UI 扩展能力设计（跳转 / 本地化 / 富文本 / 模板 / 覆盖层）

> 2026-09-17。四块能力的设计定稿，用户已拍板四项口径：**覆盖层补丁**存盘 / **独立 key 表**本地化 / **轻量 Go·Back** 跳转 / **TMP 原生 + 白名单**富文本。
> 定位：**能力设计（能做什么 + 边界在哪）**，不是逐件施工图。实施指导在各自开工时另开文件（沿用"不回写旧文档"纪律）。
> 边界与接口：《编辑器设计规划》（UI 编辑器两阶段：标记工具 → 编排器）、《UI控件库Prefab落地规划》（批⑧ 剩余缺口）、《UI性能优化规划》§7 更新架构与批②③、`自研框架设计方案` §4.7（绑定/命令式混合）。
> **优先级**：四块都不阻塞 M10（联机主线）；跳转 → 本地化 → 富文本 → 覆盖层 → 模板这一顺序按依赖排，**M11 的 HUD 需要"模板 P0 + 本地化"**。

## 0. 现状（实测，设计起点）

| 项 | 现状 |
|---|---|
| 跳转 | 只有 `UIStack`（组内栈 Push/Remove/Top，供 `IPopInterceptor` 出栈拦截）+ `UIService.ShowAsync/CloseAsync/IsOpen`；**无 Go/Back 语义、无参数回传** |
| 本地化 | 只有一个设置项 `GameSettings.Language = Setting<string>("lang","zh-CN")`（`GameSettings.cs:17`）；**无本地化表/系统** |
| 富文本 | **TMP 默认已开**（`richText=true`），工程未显式设置、无封装、无校验 |
| 存盘 | 零通道 |
| 界面表 | `tbuiform`：`id / lua_path / prefab / layer / full_screen`（5 列，可扩列） |

---

## 1. UI 跳转（轻量 Go / Back）

### 1.1 语义定义（只三件事，不多做）

```csharp
// UIService 新增（C# 侧；Lua 经 Bridge.ui 白名单导出）
UniTask<UIForm> GoAsync(int formId, IUIData data = null);   // = ShowAsync + 记录 caller（可回传）
UniTask<bool>    BackAsync(IUIData result = null);          // 关掉"当前可返回界面"，把 result 回传给它
```

- **`Go`** = `ShowAsync` 语义 + **记录 caller**（谁跳的）。不销毁当前界面：可见性仍由层级组 + 遮盖重算决定（既有语义，不改）。
- **`Back`** = 关闭"当前最上层可关闭界面"，并把 `result` 通过 **caller 的 `OnShow(data)`** 送回去 —— **复用七回调，不新增回调**。
- **`Back` 的守卫**：走既有 `IPopInterceptor.CanClose`（战斗中禁退、二级确认等策略位已在）。返回 false 时 `BackAsync` 返回 false，**由 Lua 决定兜底**（如回主菜单/退出）。

**"当前最上层可关闭界面"的判定顺序**（钉死，避免歧义）：
1. 若存在 `Go` 记录的 caller 且其当前可关闭 → 关它（常态：从列表页跳详情页 → Back 回列表页）
2. 否则取**层级组 BaseDepth 最高组**的栈尾（`UIStack.Top`）→ 关它
3. 都没有 → 返回 false（无事可做）

### 1.2 参数与回传

- 参数沿用既有 `IUIData`（C#）/ `LuaUIData(LuaTable)`（Lua），**不新增数据类型**。
- 回传同样走 `OnShow(data)` —— 于是"选人列表 → 回填"这类需求天然成立：调用方 `Go(列表页, 带回调上下文)`，列表页选中后 `Back(选中的 id)`，调用方在 `OnShow` 里收到。
- 调用方在 `OnHide` 时清理自己的等待态（既有纪律：`OnHide` 是唯一收尾点）。

### 1.3 Lua 侧受控面（在现有 `Bridge.ui` 上加两条）

```lua
Bridge.ui.Go(id, tbl)        -- 打开 + 记录 caller
Bridge.ui.Back(tbl)          -- 可选 tbl = 回传结果
-- self.ui 门面同步加 Go/Back（与现有 13 方法并列，不扩其它）
```

`self.ui` 由 13 → **15** 方法（《UI控件Lua用法表》需同步）。

### 1.4 明确不做（留判据）

- **不做历史栈**（可回退多步）、**不做深链**（房间号/活动页直达）、**不做 URL/路由表**。
- **不加 `tbuiform` 跳转列**（跳转目标写在 Lua 调用点即可；数据化跳转的判据 = 策划需要独立改跳转关系而不用发 Lua）。
- 判据留档：出现"从任意界面直达任意界面 + 可回退多步"的真实需求时，再在 `UIStack` 旁加一条**独立返回链**（不改 `UIStack` 语义，避免污染出栈拦截）。

### 1.5 转场状态机与编排（2026-09-17 追加，用户选 **B：引入**）

**为什么必须与 §1 同批**：`Go` 若只是 `Show`，视觉上是"B 淡入叠在 A 上"而非"切换"——跳转的"离开感"完全没有。转场编排是 Go/Back 的配套，不是可选装修。

#### 1.5.1 第一原则：转场态 ≠ 生命周期态

`UIFormState`（`Loading/Active/Covered/Paused/Closing/Recycled`）是**逻辑生命周期**；转场是**表现阶段**。一个界面完全可以是 `Active` 且"正在做入场动画"。**禁止把转场态塞进 `UIFormState`**（会把逻辑迁移与表现耦合，且池化/遮盖语义会被污染）。

```
UIFormState（已有，不动）          转场编排层（新增，壳内）
Loading/Active/Covered/Paused/     StageMachine<TransitionId, TransitionReq>   ← 通用状态机（见 1.5.2）
Closing/Recycled                   + UIService 的待办队列（容量 1，见 1.5.4）
                                   + 模式：Push / Replace / Pop
```

#### 1.5.2 形态：**通用状态机 + payload**（2026-09-17 修订——A 路线后改判，见 1.5.6）

转场编排 = **`StageMachine<TransitionId, TransitionReq>`**（同日在 A 路线落地的通用状态机，`Core/Fsm/StageMachine.cs`）：

```csharp
public enum TransitionId { Idle, Out, In }        // 平铺三维足够；Replace 的"两组并发"在 In 阶段内部编排

// payload：每次事务随参数带，编译期强类型（不再是 owner 字段、也不是与状态机并列的"事务类"）
public readonly struct TransitionReq
{
    public readonly TransitionMode Mode;          // Push / Replace / Pop
    public readonly UIForm Outgoing, Incoming;    // Push 时 Outgoing 可为空
}

// 壳内（UIService）持有机器 + Layer 2 组件（队列/完成/超时，见 1.5.4）
// 阶段逻辑写成 IStage<TransitionId, TransitionReq> 实现：无实例字段，参数全走 payload
```

- **一次转场 = 一次事务**：`Request` 入队（last-wins）→ `Tick` 帧末 `Advance` 应用（"一帧最多一变"由机器保证）。
- **为什么不用 HSM**：转场阶段是**平的**（Idle/Out/In），不需要层级、历史、冒泡——HSM 的三种能力在这里全是负担。
- **`UITransition` 这个类型不再需要**：它原本要承载的数据 = `TransitionReq`（payload），职责已由状态机 + payload 承担。

#### 1.5.3 模式由 Go/Back 推导（钉死，不给内容层自由度）

| API | 模式 | 行为 |
|---|---|---|
| `Go(id)` | **Push**（默认） | 只播 Incoming 入场；Outgoing 保持不动的 Active |
| `Go(id)` 且 **同组内已有 `full_screen=true` 的界面** | **Replace** | Incoming 入场**同时** Outgoing 离场 → 这才是"切换" |
| `Back()` | **Pop** | Outgoing 离场，下方界面**露出**（露出的界面不重播入场；若要重播由策略决定） |

**同组全屏互斥**这条推导是自然规律：两个全屏界面叠着本就无意义（后一个必然遮住前一个，且壳已有 `RecomputeCovering` 在管遮盖逻辑态）。**不引入 `tbuiform` 新列**——与 §1.4"不加跳转列"一致；判据：出现"全屏叠开需要真叠"的需求时再加列。

#### 1.5.4 排队 / 互斥 / 兜底（四条硬规则）

1. **同帧多次 `Go` → 只保留最后一次**，其余丢弃并记 Warning（连点 = 想去最后那个，与直觉一致）。
2. **转场进行中的新请求**：目标 = 当前 Incoming → **忽略**（幂等）；否则**排队 1 个**（后续请求丢弃并计数），当前事务结束后立即执行。
3. **交互门由壳统一管**：事务开始时对参与界面 `CanvasGroup.blocksRaycasts = false`，结束时恢复 —— **不依赖策略自觉**（现在只有 `FadeSlideTransition` 自己关，策略漏了就漏交互，是缺口）。策略自身的设置与之可叠加，幂等安全。
4. **超时兜底**：事务有 `MaxDuration`（默认 2s，进 `SimConfig` 同级的 UI 常量区）。超过则**强制收尾**并广播 `completed=false`（防策略不回调把 UI 卡死——转场状态机最容易被忽略的护栏）。

**四条规则落到哪（载体对应，避免实现时再发明）**：

| 规则 | 载体 |
|---|---|
| ① 同帧 last-wins | **机器自身**（`Request` 的 pending last-wins，payload 同步覆盖）——不需要额外组件 |
| ② 排队 1 个 | **UIService 的待办队列**（Layer 2 `RequestQueue`）：机器在 `Out/In` 时压队，回 `Idle` 时取出并 `Request` |
| ③ 交互门 | **壳在阶段钩子里做**（`Out/In` 的 `OnEnter` 关、`OnLeave` 恢复），不经策略 |
| ④ 超时兜底 | **Layer 2 `Timeout` 组件**：`WhenAny(Completion, Delay(MaxDuration))` → 超时即 `Request(TransitionId.Idle)` + 广播 `completed=false` |

与既有守卫的关系：`_closing`/`_loading`（同界面并发守卫）**保留**，与转场事务**正交**（一个管"同一界面别并发开/关"，一个管"全局表现编排"）。
与《UI性能优化规划》§7 的关系：换表 / 语言重刷若落在转场中 → **挂队列尾**（避免动画期中途换树）。

#### 1.5.5 策略接口演进（策略仍是"表现体"，壳管编排）

- `ITransitionStrategy.PlayShow/PlayClose` **保持不变**（既有实现与默认 `FadeSlideTransition` 零改动）。
- **新增可选接口**（策略按需实现；未实现则壳走默认合成）：

```csharp
public interface IReplaceTransition
{
    UniTask PlayReplace(UIForm outgoing, UIForm incoming);
}
// 默认合成：WhenAll(PlayClose(outgoing), PlayShow(incoming))
// 交叉淡入淡出/共享元素位移这类"必须一起算"的效果，由实现者在此定制
```

这样"策略自由编排一组表现"与"壳统一管编排/互斥/兜底"分开，与《自研框架设计方案》§1.3"壳只做机制、转场是策略"的纪律不冲突，也不与《动效设计方案》§A.2 的策略口重复（§A.2 定义的正是这个表现体层）。

#### 1.5.6 决策变更记录：从"自造事务对象"改判为"用通用状态机"

**第一版结论（2026-09-17 早，基于旧 `Fsm<TOwner>`）**：转场**不用**框架状态机，改自造"事务对象 + `TransitionPhase` 枚举"。理由是三处机制错配（均有代码依据）：
1. **数据无处放**：`FsmState` 明令"无状态单例、禁实例字段"，参数只能进 owner；而转场的 `{Mode, Out, In}` 每次不同 → 塞 owner = 用字段当局部变量；字符串键数据字典那条路你们**已删**。
2. **队列推进撞禁区**：`ChangeState` 在 `_inLeave` 期间直接抛 → "转场结束起下一个"写不了。
3. **异常策略相反**：旧 FSM 不捕获回调异常（脊柱炸响），转场要容错 + 超时。

**改判（同日 A 路线重构之后）**：这三条**全部被通用化化解**，故转场改用 `StageMachine`：

| 旧摩擦 | 通用状态机的解法 |
|---|---|
| ① 数据无处放 | **payload 走钩子参数** `OnEnter(m, in TReq)`——不进 owner、不用字典，编译期强类型 |
| ② 队列推进撞 `OnLeave` 禁区 | **`Request`/`Advance` 两段式**：请求只入队，迁移点在 `Advance`（由壳决定何时推进）→ 队列推进发生在机器**外部**，不进 `OnLeave` |
| ③ 异常策略相反 | **内核不带异常策略**：容错与超时由**驱动层/壳**承担（同一内核算两种用法） |

**为什么不顺手用 HSM**：转场只需要"平的三个阶段"，层级/历史/冒泡在这里没有需求方（HSM 的能力对它是纯负担）。**判据**：若将来出现"多阶段且每阶段要 tick 推进、可中断可续接"的转场（遮罩滑入→替换→滑出这类 mask wipe），再用 HSM 表达——那时它有真实需求方。

**为什么不再自造"事务对象"**：状态机已提供状态维度（可观察/可断言/守卫齐全），而"事务数据"正好是 payload 的用途。两者合起来就是原方案想手搓的东西——**自造的那份是重复机制**（同一条纪律：不为已有能力再造一层）。

#### 1.5.7 验收用例

| 用例 | 期望 |
|---|---|
| 连点 3 次 `Go`（同帧） | 只执行最后一个 + 2 条丢弃 Warning |
| `Go` 到全屏界面（同组已有全屏） | Replace：旧界面播离场、新界面播入场，**不叠** |
| 转场中再 `Go` 另一目标 | 排队 1 个；当前结束后执行；第 3 个请求被丢并计数 |
| 转场中 `Back` | 走同一排队规则；不得出现两个事务并发 |
| 策略故意不回调（注入坏策略） | `MaxDuration` 到点强制收尾 + `completed=false`，UI 不卡死 |
| 转场中禁交互 | 参与界面 `blocksRaycasts=false`；结束恢复；不依赖策略实现 |
| 完成事件 | Lua 收到的 `begin/end` 成对，`mode` 与调用相符 |

> **落地状态（2026-09-19）**：编排层已实现——`Shell/UI/Transition/`（`UITransitionRunner` = `StageMachine<TransitionId,TransitionReq>` + 待办队列 1 + 超时兜底 + 壳统一管交互门）+ `Strategies.cs` 加 `IReplaceTransition` + `UIService` 接线（`ShowAsync` 推导 `Push/Replace`、`CloseAsync` 走 `Pop`、`Tick` 帧末转发）。
> 七条的落点：进 `Assets/Tests/EditMode/UiTransitionEditModeTests.cs`（**11 条，EditMode 全绿**）。**例外**：完成事件当前只到 C# 事件层（`Began`/`Finished`）——Lua 侧 `begin/end` 桥随 §1 的 `Go/Back` 一起做。
> **两点实现说明**：① 规则①（同帧 last-wins）落在 Runner 入口（首个请求立即开始、后续走排队/丢弃分支），而非依赖机器 pending 覆盖；② 超时用 `StageTime` 帧末计时而非 `WhenAny(Delay)`——后者依赖 PlayerLoop，在 EditMode 下无法确定性复现。

---

## 2. 本地化（独立 key 表）

### 2.1 表设计（Luban，唯一真相）

`Luban/Data/#text.xlsx`：

| 列 | 说明 |
|---|---|
| `key` | 语义键，约定 `UI.<界面>.<语义>` / `Common.<语义>`（与 `LuaKeys` 同风格，**生成常量**） |
| `zh-CN` / `en` / `ja` … | 一语言一列（列名 = `GameSettings.Language` 取值，直接对齐） |
| `note` | 备注（给翻译/策划，非运行时） |

产物：`Assets/LiteGame/RawFile/Config/tbtext.bytes`（C# `cfg.TbText`）+ `Assets/LiteGame/Lua/Cfg/tbtext.lua`（Lua）+ 由 key 生成的常量类（`LuaKeys` 同一套生成器扩展，键名拼错在编译期就挂）。

### 2.2 取用面（三条，收口到一处）

```csharp
// C#
LText.Get(string key);                       // 当前语言原文
LText.Format(string key, params object[] a); // 插值（占位 {0}/{1}）
LText.Raw(string key, string fallback);      // 缺 key 时的兜底（开发期记 Warning）
```
```lua
Bridge.text.Get(key) / Bridge.text.Format(key, ...)
```
- **缺 key 行为**：开发期（`UNITY_EDITOR || DEVELOPMENT_BUILD`）记 `Log.Warning` 并回显 `[key]`；发布期回退 `zh-CN` 列，再缺则回显 key。**绝不抛**（文案缺失不该炸界面）。
- **插值不引入 ICU**：`{0}` 风格 + "数量后缀"约定（`xxx.one` / `xxx.other`），不做复数/性别规则（判据：将来接俄语/阿拉伯语这类强复数语言时再上 ICU）。

### 2.3 与 prefab 的关系（硬纪律）

- **prefab 里的 TMP 文本一律不写死文案**：写 key（或留空由逻辑填）。
- **落成一个校验器规则**：构建器产物 + 界面 prefab 扫描 —— 非空且不是合法 key 的文本节点 = 违规（与 `WidgetPrefabCheck` 同风格，进 L1 纪律扫描）。
- 于是"文案"只有一个可写源（表），改文案 = 改表 = 热更，不出包。

### 2.4 语言切换

- 切换点：`GameSettings.Language` 写入（Setting 服务已有）→ **广播 `LanguageChanged` 事件**。
- 生效方式：**重刷已打开界面** —— 复用 §5（增量重填）那套"标记 → 池中立刻/Active 等关闭再换"的机制形态，但动作更轻：**不换逻辑表，只重跑 `OnShow`**（Lua 侧 `OnShow` 里做一次全量取文案即可）。做法：`UIService.RefreshAllOpen()`（内部对 Active 界面 `OnShow(null)`，对池中界面不做事）。
- 未打开界面无需处理（打开时自然取当前语言）。
- 字体：CJK 已用 `NotoSansSC SDF`；多语言靠 **TMP fallback 字体链**（一个 SDF 覆盖不全），语言切换时切 fallback 列表；字体资产随语言包下发（YooAsset，M6 后）。

### 2.5 与富文本的关系

文案里**允许**白名单标签（见 §3）；参数插值**先转义再插值**，防止玩家名/输入内容注入标签。

---

## 3. 富文本（TMP 原生 + 白名单）

### 3.1 白名单（钉死允许集合）

| 允许 | 用途 |
|---|---|
| `<color=#RRGGBB>` / `<color=name>` | 高亮数值、品质色 |
| `<b>` `<i>` `<u>` `<s>` | 强调 |
| `<size=N>` | 局部字号（谨慎：影响布局高度） |
| `<sprite name=…>` | 内联图标（金币/属性图标）——**需要 TMP Sprite Asset**（与批② 的 `SpriteAtlas` 是两种资产，别混） |
| `<link=id>` | 可点击链接（可选，配合 `TMP_TextUtilities` + `onClickLink`） |

**禁止**：`<style>`、`<material>`、`<quad>`、`<font>`、`<gradient>`、未知标签、`<sprite index>`（越界索引会抛）。

### 3.2 校验时机与手段（两处，fail-fast）

1. **编辑期扫描器**（新的纪律规则）：扫 `#text.xlsx` 全部语言列 + 业务表里的文案列 → 发现白名单外标签即失败（构建前拦住，与 `DisciplineScanner` 同风格）。
2. **启动期断言**：`RegistryFiller` 同批对已加载文案做一次白名单校验（表被绕过时兜底），失败项进填充报告（不阻断启动，记 Error）。

### 3.3 注入顺序（安全）

```
玩家输入/外部字符串 → 转义（< > 替换为实体）→ 参数插值 → 白名单校验（仅断言，不改写）→ TMP 渲染
```
- 参数**永远**先转义：玩家名/聊天内容不得注入标签。
- 文案作者的标签**信任但校验**（3.2）。

### 3.4 性能

- 富文本本身不贵（TMP 解析一次生成 mesh）；真正的开销是**每帧写文本**（→ 《UI性能优化规划》§7 已治：值变才写、走 L2 不过 Lua）。
- `richText=false` 仅用于确认纯文本的显示件（跳过解析），**默认保持 TMP 默认 true**。

---

## 4. 覆盖层补丁（运行时改 → 存盘）

### 4.1 形态与流程

```
运行时（Editor 或 真机调试包）改属性
  → 收集为 UIPatch（内存）
    → 存盘：Assets/LiteGame/UI/Patches/<Form>.uipatch.json（文本、可 diff）
      → 编辑器菜单「应用补丁到 Prefab」经 Unity Pipeline 声明式写入 prefab
        → 补丁归档（Patches/_applied/）或删除
```

```jsonc
// <Form>.uipatch.json
{ "formId": 1, "prefab": "Assets/LiteGame/UI/Screens/UIMain.prefab",
  "entries": [
    { "node": "Bg/Title", "kind": "Text",  "prop": "text",  "value": "key:UI.Main.Title" },
    { "node": "Bg/Title", "kind": "RectTransform", "prop": "sizeDelta", "value": "300,60" } ] }
```

- **只在既有节点上改属性**（RectTransform / TMP_Text / Image / CanvasGroup 的值 + 文本 key）；**不做结构增删**（增删走编辑器编排器）。
- 应用时机：`Instantiate` 之后、`OnInit` 之前 → Lua 看到的就是打过补丁的树。
- 存盘开关：**只在 Editor 与 dev 调试包生效**（`LITEUI_PATCH` 宏 / `Debug.isDebugBuild`）；生产包不收集、不写盘（红线）。
- 真机取回路径：写 `Application.persistentDataPath`，再手动拷回（不做网络回传，避免额外通道）。

### 4.2 编辑器"一键应用"（禁手改 prefab）

- 实现：菜单（后续进编排器 UI）→ 读补丁 → 经 Pipeline 的 `save_prefab_contents`（声明式 prefab 编辑）逐条写入 → `AssetDatabase.SaveAssets` + 生成差异报告。
- **冲突策略**：补丁里的旧值与 prefab 现值不一致时**打印差异并要求确认**，不静默覆盖（prefab 可能已被别人改过）。
- 应用后补丁归档，**prefab 成为唯一真相**（补丁是暂存态，不是第二真相——与"SO 影子真相"的老教训同一条纪律）。

### 4.3 明确不做

- 真机直改 prefab（真机无 `PrefabUtility`，物理上不可行）。
- 补丁进热更包（补丁是开发期工具，不是内容）。
- 补丁改结构/组件增删（判据：编排器落地后由编排器承接）。

---

## 5. 常用 UI 应用模板（候选清单 + 落地档位）

**"应用模板"= 控件层（C# 件）之上、可复用的组合件**（prefab + 少量驱动逻辑 + Lua 用法），与控件库区分：控件件 = 通用原件，应用模板 = 带玩法语义的成品件。

| 组 | 模板 | 归属 | 档位 |
|---|---|---|---|
| **文本** | **动态文本**（key + 参数 + 数值/时间格式化 + 语言切换自动刷新 + **打字机模式**）、大数缩写（1.2K/1.5M）、连击计数、称号前后缀拼装 | 新增控件件 `LTextLabel`（封装 TMP + LText + 可选 typewriter） | **P0**（M11 HUD 直接用） |
| **战斗 HUD** | **技能 CD 遮罩**（Radial 扫描 + 秒数 + 可中断）、**击杀播报 Kill Feed**（复用 VirtualList 环形）、**命中标记**、**受击方向指示**、弹药/弹匣、队伍血条条 | 控件件 + 页面组装 | **P0** |
| **列表** | **骨架屏**、**空状态**、加载更多、可循环横滑 Banner、可展开树（任务/装备）、拖拽排序、多选批量栏 | 控件件 `Skeleton` / `EmptyState` + 组装 | P0（骨架/空状态） |
| **弹窗** | Tooltip/长按详情、输入弹窗、二级确认（防误删）、不可取消模态进度、通用错误提示 | 组装（Dialog 已有） | P1 |
| **进度/状态** | Buff/Debuff 图标条（层数 + 剩余时间）、读条施法（可打断）、网络延迟信号 | 控件件 | P1 |
| **货币资源** | 货币栏（多币种 + 变化飘字）、资源不足态（变红 + 缺口数）、价格标签（划线原价） | 组装 | P1 |
| **社交排行** | 排行榜行（徽章 + 头像框 + 我的高亮）、好友条目、聊天气泡、组队头像条 | 组装 | P2 |
| **引导/反馈** | 引导序列遮罩、成就/获得弹窗、点击波纹、错误抖动 | 控件件 + 组装 | P2 |
| **布局容器** | 自适应网格、滚动条美化、返回 + 面包屑（**依赖 §1 Back**） | 组装 | P1 |

**动态文本模板（用户点名的那个）的具体形态**：

```csharp
public sealed class LTextLabel : MonoBehaviour   // 控件层新件
{
    public TMP_Text Label;
    public string Key;                 // 文案 key（prefab 里只放 key，不放文案）
    public bool UseTypewriter;         // 打字机模式（走 UniTask，禁协程）
    public float CharInterval = 0.02f;
    public void SetKey(string key, params object[] args);   // 内部 LText.Format + 白名单校验
    public void SetRaw(string text);                         // 系统注入（自动转义）
    // 语言切换：订阅 LanguageChanged → 重新 Format（无需界面重刷也能生效）
}
```
要点：**key 住在 prefab、文案住在表** → 改文案不出包；语言切换自动刷新；打字机走 UniTask 且与 `silenceUntilFrame`（动效防重播）同族纪律。

---

## 6. 依赖顺序与批次建议

```
① 跳转 Go/Back **+ 转场编排（§1.5）**（~400 行 + Lua 门面 2 条）  无依赖；**两者必须同批**（Go 不带 Replace 就没有"离开感"）
② 本地化（表 + LText + 校验器 + 语言切换）          依赖表链（已通），M11 前必须
③ 富文本白名单（扫描器 + 启动期断言）               依赖 ②（扫的是文案表）
④ 覆盖层补丁（收集/存盘/应用三段）                  独立，可与 ②③ 并行
⑤ 应用模板（P0 先做：动态文本 + 战斗 HUD + 骨架/空状态） 依赖 ①②③
```
- 与主线的接口：**都不阻塞 M10**；M11 开工前需要 ①②⑤(P0)，③④ 可并行跟进。
- 每块的验收：① 跳转用例（Go→Back 回传值、拦截生效、无事可做返回 false）；② 缺 key 不炸 + 语言切换重刷 + 校验器 0 违规；③ 非法标签被扫描器拦住；④ 补丁往返（改→存→应用→prefab 值一致）+ 冲突告警；⑤ 模板自检（沿用 25 件模板那套断言形态，扩到应用模板）。

## 7. 明确不做（判据留档）

| 不做 | 判据（何时再考虑） |
|---|---|
| 历史栈 / 深链 / 路由表 | 出现"任意界面直达任意界面 + 多步回退"的真实需求 |
| ICU 复数/性别 | 接入强复数语言（俄/阿） |
| 运行时机器翻译 / 自动字库子集 | 语言数 > 5 或包体被字体压爆 |
| 补丁改结构 / 补丁进热更 | 编辑器编排器落地后由编排器承接 |
| 双向绑定 / 通用绑定层 | 见《UI性能优化规划》§7.7（跨语言天花板，禁） |

## 8. 与既有文档的接口
- **《编辑器设计规划》§2.2 编排器**：本设计的**补丁应用入口**是它的第一阶段消费者（只做属性覆盖）；**结构增删归编排器**（本设计不碰）。
- **《UI控件库Prefab落地规划》批⑧**：应用模板（§5）是批⑧ 之后的下一个自然批次；模板落地沿用"构建器确定性生成 + 断言"的既有形态。
- **《UI性能优化规划》**：② 语言切换重刷复用 §7 的"标记 → 换/刷"形态；③ 富文本与图集（B2）分开，`<sprite>` 用 TMP Sprite Asset；⑤ 动态文本必须走"值变才写"（§7 L1/L2）。
- **《自研框架设计方案》§4.7**：文案 key 走 L1（离散、绑定/命令式皆可），**富文本/面板不得每帧重写**。

## 9. 流程状态机与 UI 的边界（2026-09-17 追加：§1/§1.5 的前置对账）

流程状态机（**2026-09-17 后 = `StageMachine`**，`Core/Fsm/StageMachine.cs`；旧 `Fsm.cs` 已删，见《通用流程状态机施工图》）的**正确用法就是长生命周期阶段机**（状态单例、数据外置、每帧推进、迁移显式、异常炸响）——§1.5 拒用 FSM 的判据（粒度不匹配）在这里正好成立，两条结论不矛盾。

### 9.1 现状：流程线只建了一半（**M10 四批 / M11 的前置缺口**）

| 阶段 | 设计意图（出处） | 现状 |
|---|---|---|
| `ProcedureLaunch` | 唯一受信装配点（设计方案 §3.3） | ✅ |
| `ProcedurePreload` | YooAsset → 热更 → Lua 预载 → main → 配置 → 注册表填充 | ✅ |
| `ProcedureMain` | 主菜单/大厅 | ⚠️ **空转占位**（无 ChangeState） |
| `ProcedureMatch` | 调 `INetworkService.ConnectAsync`（M0 指导 §620、状态同步 §576） | ❌ **不存在** |
| `ProcedureBattle` | `OnEnter` 创建 `BattleContext`（SimWorld/FrameDriver/SnapshotRing/InputHistory）、挂 SimView（状态同步 §115） | ❌ **不存在** |
| `ProcedureResult` | 销毁 BattleContext、解绑快照流、停 FrameDriver（同 §115） | ❌ **不存在** |

**后果一**：联机线多处设计**已假定**这三阶段存在 → M10 第四批（客户端接缝）与 M11 开工即撞上此前置。
**后果二**：流程间传参的**承载者已换正身**（2026-09-18 对账）——`ProcedureOwner` 随 A 路线退场（2026-09-17），改为 payload `ProcedureArgs`（`readonly struct`，`ProcedureError` 读 `Error`）。`roomId / frameNo / BattleContext` 以**只读字段 + 构造入参**加在此处（`ProcedureArgs.cs` 已留注释位）；**当前只有 `Error` 一个真字段**——即三阶段未建 = 承载者仍空着，只是"往哪儿加"已定（不再新增 owner 载体、不用字符串键字典）。
**后果三**：**"安全窗口"（回主城 / 战斗结束）的落点就是流程迁移点** —— 流程线未建 = 本设计的运行期增量重填（§2.4 语言切换 / 《UI性能优化规划》缺口 2）**当前没有真实触发者**，只有调试菜单。根因在此，不在 UI 层。

### 9.2 边界定案：流程不认识 UI id（推荐）

| 方案 | 评价 |
|---|---|
| 流程显式调 UI（`ProcedureBattle.OnEnter → Bridge.ui.Go(战斗HUD)`） | 清晰但**流程认识 UI id**（机制层被内容渗透），不取 |
| **流程广播事件 + UI 层订阅**（推荐） | 流程只切阶段并广播 `MatchStarted/BattleStarted/BattleEnded`；**UI 层**订阅后自己 `Go/Back`。流程零 UI 依赖，与"框架不可知道业务"同源 |

**但广播有一处必须补**：流程需要等"关键 UI 就绪"（进战斗前 HUD 得起来）——用 **`Go` 的 await 语义**（Go/Back 是 `UniTask`，§1.1）在**订阅方**等待，而不是让流程等。**纪律：流程迁移不等表现**（表现不携带判定，与 `PlayTransition*` 容错吞异常同一族）。若确需流程等待，必须带超时（同 §1.5.4 的 `MaxDuration` 兜底）。

### 9.3 与 §1.5 转场事务的顺序

流程迁移与转场是**两层**：流程迁移（逻辑阶段）→ 广播 → UI 层决定是否 `Go` → 转场事务（表现编排）。**不允许流程 `OnEnter` 里长时间 `await` 转场完成**——会把整条流程链卡在 OnEnter（`Fsm` 的 OnLeave 期间还禁止 `ChangeState`），且违反"表现不阻塞逻辑"。

### 9.4 建议排期

流程线骨架（`Match/Battle/Result` 三件 + `ProcedureArgs` 的 `roomId/frameNo/BattleContext` 字段位 + 安全窗口挂点）应排在 **M10 第四批之前或同期**——它同时是 M10 客户端接缝与 M11 表现层的前置，且能让"运行期重填"第一次拥有真实触发点。

> **2026-09-18 对账**：§9.1 的现状表**仍然成立**——`ProcedureMain` 仍是空转占位（`RunAsync` 里没有任何 `Request`），`ProcedureId` 只有 `Launch/Preload/Main/Error`（**枚举值与 `Match/Battle/Result` 的预留说明已在 `ProcedureId.cs` 注释里**，本轮刻意不加以免出现空阶段）。改动只有一处：owner 载体换成 payload（见后果二）。
