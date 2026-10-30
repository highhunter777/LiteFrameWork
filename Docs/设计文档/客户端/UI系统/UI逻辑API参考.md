# UI 逻辑 API 参考

> 状态：现行参考（以代码为准；目标 API 单独标注）
> 版本：1.0
> 日期：2026-10-22
> 定位：回答“页面逻辑（Lua/C#）能调用哪些受控 API、参数与错误语义是什么、列表行怎么传”。本文只做查询，不裁决生命周期/架构。
> 生成：§3 表由 `scripts/codegen/gen-ui-api.py` 从 `Assets/LiteGame/Editor/UiTools/UiApi/UiApiSpec.json` 生成（`<!-- BEGIN/END:ui-api:lua -->` 之间，勿手改；改 API 先改 spec）。
> 不做的事：不定义页面业务；不替代[UI 系统总设计](UI系统总设计.md)的状态机/导航契约；不记录施工状态。
> 冲突裁决：与代码不一致以代码为准，并顺手修本文。源码入口：`Assets/LiteGame/Editor/UiTools/UiApi/UiApiSpec.json`（API 单源）、`Assets/LiteFramework/Runtime/Modules/UI/Bind/UIBindIndex.cs`、`Bind/UIBindBase.cs`、`Assets/LiteGame/Adapters/LuaBridge/LuaBehaviourAdapter.cs`、`LuaBridge/LuaListRows.cs`、`LuaBridge/Bridge.Data.cs`。
> 关联：[UI 系统总设计](UI系统总设计.md) · [UI 制作规范](../../../规范/05-UI制作.md) · [UI 命名与控件词表参考](UI命名与控件词表参考.md)

## 1. 分层与入口

```text
Lua 页面 (self.ui) ──► LuaBehaviourAdapter.Dispatch ──► UIBindIndex ──► 控件
C# 页面 (UIBindBase) ─────────────────────────────────► UIBindIndex ──► 控件
Lua 导航 (Bridge.ui) ─────────────────────────────────► UINavigationController/UIService
```

- 页面逻辑不直接操作 `GameObject`/`Transform`/组件；只经受控 API。
- `BindName` 是唯一寻址名；未命中/类型不符/所有权冲突均抛（Debug 三宏下）。
- 高频路径（HUD）不跨 Lua；Lua 只做低频编排。

## 2. 生命周期

### 2.1 Lua

| 回调 | 时机 | 说明 |
| --- | --- | --- |
| `OnInit(self, formContext, data)` | 首次打开/换表重建 | 只一次；绑定索引已建；`self.ui` 已挂 |
| `OnShow(self, data)` | 首次打开与复用打开、暂停恢复 | 当前实现 Paused→Active 也调 `OnShow(null)`；不要假设只首次 |
| `OnUpdate(self, dt)` | 仅 Active | 默认关闭；需要时声明并限频（建议 10Hz 上限） |
| `OnPause(self)` | 手动暂停 | — |
| `OnCover(self)` | 被更高全屏遮盖 | 不替代 OnHide |
| `OnReveal(self)` | 遮盖解除 | — |
| `OnHide(self)` | 关闭（进入 Closing） | 展示作用域释放；不得再订阅事件 |

- 冒号定义的生命周期显式传 `self`；按钮/回调闭包由适配器持有。
- 按钮绑定放 `OnShow`（`OnHide` 会 `UnbindAll`，池化复用不重跑 `OnInit`）。
- 页面关闭后旧回调不得再写控件；迟到异步用展示代次/页面标记丢弃。

### 2.2 C#

`UIBindBase`（MonoBehaviour，挂 prefab 根）实现 `IUIFormLogic`，子类覆写：

```csharp
protected virtual void OnInit(IUIData data);
protected virtual void OnShow(IUIData data);
protected virtual void OnUpdate(float deltaTime);
protected virtual void OnPause();
protected virtual void OnCover();
protected virtual void OnReveal();
protected virtual void OnHide();
```

- `Form` 属性在 `OnInit` 后可读；`OnInit` 内走 `BindIndexBuilder` 构建索引。
- 页面关闭清理 `Form.Subscriptions`；不得在 Closing/Recycled 再订阅。

## 3. Lua `self.ui` API

> 以下为适配器 shim 的现行方法；未列方法不存在。未知方法在开发期告警（拼写错误不静默）。

<!-- BEGIN:ui-api:lua -->

### 3.1 文本

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetText(name, text)` | 控件名, 文本 | 数据直写；不随语言刷新 |
| `SetTextKey(name, key)` | 控件名, key | 按 key 写；语言切换自动刷新 |
| `SetTextKeyArgs(name, key, a0..a3)` | 控件名, key, a0..a3 | 带参格式化（最多 4 个参数） |
| `SetTextKeyPlural(name, key, count, a0..a3)` | 控件名, key, 复数 count, a0..a3 | 英文 one/other 显式选择 |
| `UnbindTextKey(name)` | 控件名 | 解除该控件本地化绑定 |

- 未注入语言服务时 `SetTextKey*` 抛（静默显示 key 是隐蔽故障）。
- 同一节点只能有一个文案写者（静态 key 与动态 SetText 互斥）。

### 3.2 显隐/交互/数值

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetVisible(name, visible)` | bool | 直接 `SetActive` |
| `SetInteractable(name, on)` | bool | `Selectable` 优先，回退 `UIWidget.Interactable` |
| `SetProgress(name, value)` | 0~1 | 进度条 |
| `SetProgressRange(name, cur, max)` | 当前/上限 | 进度条 |
| `SetHp(name, cur, max)` | 当前/上限 | 血条（前条瞬时 + 后条滑落） |
| `StartCountdown(name, seconds)` | 秒 | 倒计时启动 |
| `StopCountdown(name)` | — | 停止（不触发完成） |

### 3.3 提示/反馈

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `ShowToast(text)` | 文本 | 无 Toast 实例时记日志不抛 |
| `ShowBubble(name, text, duration?)` | 控件名/文本/时长 | 挂点旁气泡 |
| `ShowFlyText(name, text)` | 控件名/文本 | 飘字（位置取控件 anchoredPosition） |
| `ShowConfirm(title, message, okText, cancelText, fn)` | 标题/正文/确认文案/取消文案/回调 | 经确认缝；无服务/取消/异常回调 false |

### 3.4 输入控件

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetSlider(name, value)` | 0~1 | 程序设值，不触发变化回调 |
| `GetSlider(name)` | → number | 当前值 |
| `OnSliderChanged(name, fn(value))` | 回调 `fn(value)` | 用户拖动；重复登记替换 |
| `SetToggle(name, on)` | bool | 程序设值，不触发变化回调 |
| `GetToggle(name)` | → bool | 当前值 |
| `OnToggleChanged(name, fn(on))` | 回调 `fn(on)` | 用户点击；重复登记替换 |
| `SetDropdownOptions(name, options)` | 字符串数组 | 选项文本由页面给已本地化文本；值保持 |
| `SetDropdown(name, index)` | 索引 | 程序设值，不触发变化回调 |
| `GetDropdown(name)` | → number | 当前索引 |
| `OnDropdownChanged(name, fn(index))` | 回调 `fn(index)` | 用户选择；重复登记替换 |
| `SetTab(name, index)` | 页签索引 | 程序设值，不触发变化回调 |
| `GetTab(name)` | → number | 当前页签索引；-1 = 未选中 |
| `OnTabChanged(name, fn(index))` | 回调 `fn(index)` | 用户点击页签；重复登记替换 |

### 3.5 动效

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `Pulse(name, strength?, duration?)` | 强度/时长 | 透明度呼吸两次；缺省来自 `UiFxDefaults` |
| `Flash(name, duration?)` | 时长 | 一次性高亮回落 |
| `Slide(name, ox?, oy?, duration?)` | 偏移/时长 | 从偏移滑回原位 |

- 动效只写表现；页面关闭/隐藏即杀；与业务写入冲突时先 Kill。

### 3.6 列表

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetList(name, rows)` | 行表 | 整表替换；空源过渡 + 换源会重绑已实体化条目 |
| `OnListClick(name, fn(key, action))` | 回调 `fn(key, action)` | 条目根按钮 action 为空串；条目内按钮回节点名 |

### 3.7 按钮绑定

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `OnButton(name, fn)` | 控件名, 回调 | 替换式绑定；框架在 OnHide 统一解绑 |
| `OffButton(name)` | 控件名 | 只解绑该控件 |

- 按钮回调替换式绑定；`UnbindButton` 只解绑该控件，`OnHide` 时框架统一 `UnbindAll`。

<!-- END:ui-api:lua -->

## 4. Lua 导航 `Bridge.ui`

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `Bridge.ui.Show(idOrLuaPath, data?)` | 页面 id/逻辑路径, LuaTable | 打开（Go） |
| `Bridge.ui.Replace(idOrLuaPath, data?)` | 同上 | 替换当前记录（返回栈不残留） |
| `Bridge.ui.Close(idOrLuaPath)` | 同上 | 关闭 |
| `Bridge.ui.IsOpen(idOrLuaPath)` | 同上 | 是否打开 |
| `Bridge.ui.GetLogic(luaPath)` | 逻辑路径 | 取模块/实例逻辑（按注册表） |

- 产品导航必须走 `Bridge.ui`/导航器；不要直接调 `UIService.ShowAsync`（工具/测试除外）。
- 数据用 LuaTable 传给 `OnInit/OnShow`；页面按 `IUIData` 保留字段读取。
- 关闭/替换是异步的；不要假设调用后立即完成，后续动作放在回调/`OnShow`。

## 5. C# API

### 5.1 `UIBindBase`（页面基类）

| 成员 | 说明 |
| --- | --- |
| `Form` | `UIForm` 实例（OnInit 后可用） |
| `GetControl<T>(name)` | 取控件（类型不符抛） |
| `HasControl(name)` | 是否存在 |
| `OnButton(name, onClick)` | 替换式绑按钮；返回注销委托 |
| `SetText(name, value)` | 数据直写 |
| `SetTextKey(name, key, args)` | 按 key 写（语言切换自动刷新） |
| `SetTextKeyPlural(name, key, count, args)` | 复数 |
| `UnbindTextKey(name)` | 解除本地化绑定 |
| `SetVisible(name, visible)` | 显隐 |
| `SetInteractable(name, on)` | 可交互 |
| `SetAnchoredPosition(name, pos)` | 位置（表现驱动） |
| `BindLocale(localization)` | 语言服务注入（`ILocaleBound`；通常由 UIForm 调） |

### 5.2 `UIBindIndex`（受控 API 全集）

- 查询：`Get<T>(name)`、`Has(name)`、`ResolveGraphic(name)`、`ResolveRect(name)`。
- 所有权：`MarkDriver(name, ControlDriver.Bound/Command)`；绑定与命令式互斥。
- 文本：`BindText<T>(name, format)`、`SetText`、`SetTextKey`、`SetTextKeyPlural`、`UnbindTextKey`。
- 按钮：`BindButton`、`UnbindButton`、`UnbindAll`。
- 显隐/交互/位置：`SetVisible`、`SetInteractable`、`SetAnchoredPosition`。
- 数值：`SetProgress`（0~1 / 当前上限）、`SetHp`、`StartCountdown`、`StopCountdown`。
- 提示：`ShowToast`、`ShowBubble`、`ShowFlyText`。
- 输入：`SetSlider/GetSlider/OnSliderChanged`、`SetToggle/GetToggle/OnToggleChanged`、`SetDropdownOptions/SetDropdownValue/GetDropdownValue/OnDropdownChanged`。
- 列表：`SetList(IUIListRows)`、`OnListClick(name, handler)`。
- 动效：`Pulse`、`Flash`、`Slide`。
- 语言：`BindLocale`。
- 清理：`UnbindAll()`（页面 OnHide/展示作用域释放时）。

> C# 页面若需要 `ShowConfirm`，经 `DialogService`/`FeedbackService`；Lua 的 `ShowConfirm` 是适配器提供的确认缝。

## 6. 列表行协议

### 6.1 Lua 行表

```lua
rows[i] = {
  key = "Room-A",                                  -- 稳定 key（行点击回传）
  Txt_RoomName = "Room-A",                         -- 字符串/数字 = 数据直写
  Txt_RoomState = { key = "UI.Lobby.RoomState", arg0 = 2, arg1 = 8 } -- 表 = 本地化文案
}
self.ui:SetList("VirtualList_Rooms", rows)
```

- 字段名 = 条目子节点名；值为字符串/数字 = 数据直写；值为带 `key` 的表 = 本地化文案。
- 参数具名 `arg0..arg3`（定长，最多 4 个）。
- 行 key 不投影为字段；空表/缺 key 的字段不显。
- 数值显示用 InvariantCulture（跨区域小数点不漂移）。

### 6.2 C# 行源

实现 `IUIListRows`：

```csharp
int Count { get; }
string KeyAt(int index);
IReadOnlyList<UIListField> FieldsAt(int index);
```

`UIListField`：

- `new UIListField(node, text)`：数据直写。
- `new UIListField(node, key, args)`：本地化文案（≤4 参数）。

### 6.3 行点击

`OnListClick(name, (key, action) => ...)`：

- `key` = 行稳定 key。
- `action` = 条目根按钮为空串；条目内其余按钮回节点名。
- 重绑时先解绑旧监听；池化复用不留旧回调。

## 7. 数据与本地化

- 页面数据：`IUIData`（C#）/ LuaTable（Lua），经 `ShowAsync`/`GoAsync`/`ReplaceAsync` 传入。
- 文案：`SetTextKey*` 走 LText 服务；语言切换只重写 key 绑定，不重跑 `OnShow`、不重发业务请求。
- 静态文案：`LTextKeyRef`（目标组件）在打开/语言切换时写入；有静态 key 的节点 Lua 不得再 `SetTextKey/SetText`。
- 格式化：`Bridge.text.Get/Format/SetLocale/Locale`；数字/日期按产品 locale 策略。
- 外部文本默认禁富文本；需要时只允许白名单标签。

## 8. 错误语义

| 情形 | 行为 |
| --- | --- |
| 绑定未命中 | `KeyNotFoundException`（核对 BindName/Designer 登记） |
| 类型不符 | `InvalidOperationException`（期望/实得类型） |
| 所有权冲突 | Debug 三宏抛；release 信任 Debug 全绿 |
| `SetTextKey` 未注入语言服务 | `InvalidOperationException`（装配错误） |
| 列表目标无 `VirtualList`/`SimpleListView` | `InvalidOperationException` |
| 列表行含 key 但未注入语言服务 | `InvalidOperationException`（装配错误） |
| 页面关闭后再订阅 | Debug 下抛；release 记错误 |
| 关闭已关闭页面 | `InvalidOperationException`（fail-fast） |
| 打开失败 | `UIOpenException`（LoadFailed/InitFailed/Rejected/Busy/Canceled/Timeout） |

> 页面逻辑不得吞掉打开/关闭异常；UI 流程必须给出可见结果（重试/返回/提示）。

## 9. 选择建议

| 场景 | 建议 |
| --- | --- |
| 高频 HUD/每帧数值 | C# `HudModel` + 绑定；不跨 Lua |
| 复杂列表/大量交互 | C# 页面（`UIBindBase` + 生成类） |
| 低频业务编排/文案/按钮 | Lua 页面 |
| 静态文案 | `LTextKeyRef` + key；不写 Lua |
| 需要强类型/服务调用 | C# + `GetControl<T>` |
| 需要热更快速迭代 | Lua（遵守白名单与限频） |

> 同一页面只选一种主逻辑语言；不混用两套生成与两套所有权。
