# UI 控件 Lua API 参考

> 状态：现行参考
> 版本：1.0
> 更新日期：2026-09-21
> 适用范围：UI 控件白名单、`self.ui` API 和 Lua 调用映射
> 维护责任：客户端 UI
> 与代码 1:1：方法名以 `LuaBehaviourAdapter.UiApiShim/Dispatch` 为准，控件字段以 `WidgetPrefabBuilder` 为准；发现不一致以代码为准并修本表。历史交付与实测记录见 `Docs/Archive/2026-09-20/`。

## 0. 前置

- 逻辑表：`Assets/LiteGame/Lua/UI/<界面名>.lua`（须在 `LuaKeys` 登记路径）；七回调 `OnInit/OnShow/OnUpdate/OnPause/OnCover/OnReveal/OnHide`（缺哪个跳哪个）。
- `self.ui` 在 OnInit 内由壳挂好（OnShow/OnUpdate 直接可用）；控件名来自界面 prefab 的 `BindNode.BindName`（模板不预设，界面作者命名）。
- 未命中/类型不符**当场抛**（fail-fast）；同一控件"数据绑定"与"命令式"驱动互斥（`MarkDriver`，Debug 三宏违例抛）。
- OnHide 时壳自动解绑全部按钮监听；`events.on` 的订阅须按事件桥规则自行注销。
- 全局表：`log.info/warning/error`；`events.on(name, fn)`（返回注销委托）；`Bridge.data.GetItem/GetUIForm`（按行缓存）；`Bridge.ui.Show/Close/IsOpen/GetLogic`（以 `tbuiform` 行 id 驱动）；`Bridge.content.GetProcessor`。
- **开界面写 `Bridge.ui:Show(id, data)`**；`self.ui` 只管当前界面的控件；`data` 传 Lua 表，OnShow 收到同一张表。

## 1. `self.ui` 全集（16 方法）

| 方法 | 语义 |
|---|---|
| `OnButton(name, fn)` / `OffButton(name)` | 点击绑定（替换式）/移除；回调经 SafeCall 隔离 |
| `SetText(name, text)` | TMP 优先回退 UGUI Text |
| `SetVisible(name, visible)` | SetActive 显隐 |
| `SetInteractable(name, on)` | Selectable 优先 → 回退 `UIWidget.Interactable`（G1） |
| `SetProgress(name, v01)` / `SetProgressRange(name, cur, max)` | 进度条归一化 / 当前+上限带数值文本（G7） |
| `SetHp(name, cur, max)` | 血条：瞬时条 + 延迟滑落条（G7） |
| `StartCountdown / StopCountdown` | 倒计时 mm:ss；Stop 不触发 OnDone（G10） |
| `ShowToast(text)` | 轻提示；无 ToastHost 记日志不抛 |
| `ShowBubble(name, text, duration)` | 气泡，重复调用取消上一次；duration 默认 1.5s |
| `ShowFlyText(name, text)` | 飘字，位置取控件 anchoredPosition |
| `Pulse(name, strength, duration)` | 透明度呼吸两次（G20；默认 1.2 / 0.16s） |
| `Flash(name, duration)` | 一次性高亮回落（G20；默认 0.3s） |
| `Slide(name, ox, oy, duration)` | 偏移滑回原位入场（G20；默认 0.25s） |

全部走 whitelist 派发通道（`Action<string, LuaTable>` + shim 表），不暴露 `GetControl`。G20 解析规则：索引存的是 BindNode 检测到的组件，`Pulse/Flash` 解析 `Graphic` 三级回退（自身 → `Button.targetGraphic` → 子级）；`Slide` 取 `transform`（BindNode 不产 RectTransform）；未命中 = 抛。

## 2. 控件 × 用法对照（25 件）

| 模板 | 现有 API | 缺口 |
|---|---|---|
| StateButton | OnButton / SetInteractable / SetText→Label 子节点 | — |
| Dialog | OnButton / SetText→Title,Message / Bridge.ui 开关 | — |
| Toast / Bubble / FlyText | ShowToast / ShowBubble / ShowFlyText | — |
| RedDot | — | **G4** `BindRedDot(name, key)`（计数由 C# RedDotTree 推） |
| TabGroup / BottomNav | OnButton（各页签/入口） | **G5** `SelectTab(name, index)` |
| VirtualList / SimpleList | — | **G6** 列表数据源协议（见下） |
| ProgressBar / HpBar | SetProgress / SetProgressRange / SetHp | — |
| StarRating | — | **G8** `SetStars(name, n)` |
| CountText | SetText（静态） | **G9** `RollCount(name, to)` 滚动 |
| Countdown | Start/StopCountdown | — |
| AnimatedImage | — | **G11** `PlayAnim / StopAnim` |
| AvatarFrame | SetText→LevelBadge | **G12** `SetAvatar(name, address)` |
| Stepper | SetText→Value | **G13** `SetStepper` + 变化回调 |
| InputField | SetInteractable / SetText | **G14** `GetInput` + `OnInputSubmit` |
| Slider / Toggle / Dropdown | SetInteractable | **G15/G16/G17** Set 值 + 变化回调 |
| EventRelay | — | **G18** `OnClickArea(name, fn)` |
| GuideHighlight | SetVisible | **G19** `GuideTo(name, targetName)` |
| SafeArea | 壳自动应用，无需 Lua | — |

## 3. 缺口清单（批⑦/⑧ 已交付 G1/G3/G7/G10/G20；余为待办）

- **P1**：G8 星级、G9 滚动数值、G13 步进、G14 输入取值、G15–G17 输入三件、G4 红点、G6 列表协议、G21 Lua 侧绑定区（`UIBindIndex.BindText` 已存在未挂 `self.ui`，与 G9 同批）。
- **P2**：G5 切页、G11/G12 动图·头像、G18 点击区、G19 引导。
- 统一约定：同一派发通道，失败语义 = 抛；交互回调经 SafeCall；新增方法同步本表 §1/§2。

**G6 协议定案**：首版走方案 A `ui:SetList(name, rows)`（行数据驱动，适配器内部实现 `IVirtualListSource`，字段名→子控件映射走命名约定）——无跨语言每帧回调；回调驱动（方案 B）仅作后备。

## 4. 一致性维护

- 新增受控方法四步：`UiApiShim` 加行 → `Dispatch` 加 case → `UIBindIndex` 收口（含 MarkDriver）→ 本表同步。
- 抽查：模板实例化后调受控方法断言状态变化（菜单化，与 `WidgetPrefabCheck` 同款）。
