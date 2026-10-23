# UI 控件 Lua API 参考

> 状态：现行参考；已有 API 与目标 API 分列
> 版本：2.0
> 更新日期：2026-10-01
> Owner：客户端 UI / Lua
> 权威契约：[UI框架总设计](UI框架总设计.md)；制作要求：[UI制作规范](UI制作规范.md)

## 1. 源码与调用约定

现有入口：[LuaBehaviourAdapter](../../../../Assets/LiteGame/LuaBridge/LuaBehaviourAdapter.cs)、[UIBindIndex](../../../../Assets/LiteClient/UI/Bind/UIBindIndex.cs)、[Bridge.BindGlobals](../../../../Assets/LiteGame/LuaBridge/Bridge.Data.cs)、[Bridge.Ui](../../../../Assets/LiteGame/LuaBridge/Bridge.Data.cs)。

两种入口的调用方式不同，必须按注册方式区分：

```lua
-- Bridge.ui 的方法直接注册 C# 委托，不接收 Lua self，使用点号。
Bridge.ui.Show(formId, data)
Bridge.ui.Close(formId)
local opened = Bridge.ui.IsOpen(formId)

-- self.ui 的 shim 显式接收并忽略第一个参数，使用冒号。
self.ui:SetText("Title", "预览文本")
self.ui:OnButton("BtnClose", function()
    Bridge.ui.Close(formId)
end)
```

上面演示两种入口的参数约定：`Bridge.ui` 的方法用点号调用，`self.ui` 的 shim 用冒号调用。

### 1.1 生命周期：实例与调用契约

`IUIFormLogic` 有 `OnInit/OnShow/OnUpdate/OnPause/OnCover/OnReveal/OnHide` 七个回调；缺少对应 Lua 方法时跳过。适配器即实例工厂：注册表存模块，构造期执行 `module.new()` 得到页面实例，向实例挂 `ui` 门面表，回调显式传 `self`。

```lua
local class = require("Core.class")
local M = class("Inventory")

function M:ctor()
    self._selectedKey = nil
end

function M:OnShow(data)
    self.ui:OnButton("BtnConfirm", function()
        -- 调用受控业务入口；不直接访问 Transport/SimWorld。
    end)
end

return M -- 注册模块；运行时调用 M.new()，每页独立实例。
```

Lua `OnHide` 会解绑全部按钮，复用不重跑 `OnInit`，因此按钮绑定放在 `OnShow`。`events.on` 需页面自行持有注销函数；纳入展示作用域的自动解绑为目标能力，C# 与 Lua 语义将保持一致。

`OnResume`、`OnNavigationResult`、`OnLocaleChanged`、`OnDispose` 为目标可选能力，尚未导出；语言切换/返回结果不得通过重复 OnShow 模拟。

## 2. 已有全局门面

| 入口 | 当前行为 | 限制 |
| --- | --- | --- |
| `Bridge.ui.Show(id, data)` | fire-and-forget 打开，Lua 表包装为 IUIData；失败写日志 | 无 Lua 完成结果；并发/取消由服务层承担 |
| `Bridge.ui.Close(id)` | fire-and-forget 关闭 | 覆盖全部逻辑打开态（Active/Covered/Paused） |
| `Bridge.ui.IsOpen(id)` | Active/Covered/Paused 为 true | 不表示资源已加载完成或页面可交互 |
| `Bridge.ui.GetLogic(id)` | 返回注册表模块 | 遗留诊断口，业务不应借此调用另一页/持有模块；页面实例所有权在适配器 |
| `Bridge.data.GetItem/GetUIForm` | 按行缓存的数据门面 | 本参考不扩充数据协议 |
| `Bridge.content.GetProcessor` | 内容注册查询 | 不应越权访问底层服务 |
| `log.info/warning/error`、`events.on` | 日志与事件桥 | 事件退订遵循现有桥协议 |

目标 Go/Back/Dialog/请求结果尚未接入，不能在业务代码中当成现有方法调用。`self.ui` 只管理本页控件；导航统一从 Bridge.ui 发起，不重复增加第二套导航门面。

## 3. 已有 self.ui 方法

当前 shim 声明 **32 个方法**（批C-④ 前 20 + 批C-⑥ 控件族/列表 12）。所有控件名来自页面 `BindNode.BindName`；模板不预设非空名字。

| 方法 | 当前语义/参数 |
| --- | --- |
| `OnButton(name, fn)` / `OffButton(name)` | 替换式 Button 绑定/移除；C# 回调经 SafeCall |
| `SetText(name, text)` | TMP_Text 优先，兼容 UGUI Text；应绑定实际 Label 节点 |
| `SetTextKey(name, key)` / `SetTextKeyArgs(name, key, a0..a3)` / `SetTextKeyPlural(name, key, count, a0..a3)` | 按 key 写文本并登记控件，语言变更自动刷新；参数走具名 arg0..arg3 |
| `UnbindTextKey(name)` | 解除 key 绑定与语言刷新 |
| `SetVisible(name, visible)` | 目标节点 SetActive |
| `SetInteractable(name, on)` | Selectable 优先，回退 UIWidget.Interactable |
| `SetProgress(name, value01)` | ProgressBar 归一化值 |
| `SetProgressRange(name, cur, max)` | ProgressBar 当前/最大值 |
| `SetHp(name, cur, max)` | HpBar 前条/延迟条与数值 |
| `StartCountdown(name, seconds)` / `StopCountdown(name)` | 启停倒计时；Stop 不触发 OnDone |
| `ShowToast(text)` | 查找 Toast 宿主并追加一条，多条并存、超限最旧淘汰；宿主缺失记日志不抛 |
| `ShowBubble(name, text, duration)` | duration 缺省 1.5s |
| `ShowFlyText(name, text)` | 在目标位置显示飘字 |
| `ShowConfirm(title, message, okText, cancelText, fn)` | 确认弹窗（HUD 批C 退场链首用）：经装配注入的确认缝（生产＝`FeedbackService.ConfirmAsync` → `DialogService`——互斥组/同键合并/有界队列承接）等结果并回 `fn(ok)`；okText/cancelText 空＝保持弹窗原文案；无缝（测试/工具装配）/取消/被外部关闭/异常一律回调 `false`——页面链不悬挂 |
| `Pulse(name, strength, duration)` | shim 缺省 1.2/0.16s；Graphic 透明度呼吸，完成/中断回原透明度 |
| `Flash(name, duration)` | shim 缺省 0.3s；Graphic 颜色高亮回落，完成/中断回原色 |
| `Slide(name, ox, oy, duration)` | offset 缺省 0/0，duration 缺省 0.25s；完成/中断回原位 |
| `SetSlider(name, value01)` / `GetSlider(name)` | G15（批C-⑥）：设值 0~1（程序 Set **不触发**变化回调）；读取回当前值 |
| `OnSliderChanged(name, fn(value))` | G15：**用户拖动**才回调（带值）；同名重复登记 = 替换；`nil` = 仅退订；`UnbindAll` 退订全部 |
| `SetToggle(name, on)` / `GetToggle(name)` | G16（批C-⑥）：同上（`SetIsOnWithoutNotify`） |
| `OnToggleChanged(name, fn(on))` | G16：用户点击才回调（带值） |
| `SetDropdownOptions(name, options)` | G17（批C-⑥）：选项文本由页面给**已本地化**文本（语言切换时页面重设——下拉项是引擎组件，不随 LText 自动刷新）；值保持 |
| `SetDropdown(name, index)` / `GetDropdown(name)` | G17：设值（程序 Set 不触发回调）/ 读当前索引 |
| `OnDropdownChanged(name, fn(index))` | G17：用户选择才回调（带索引） |
| `SetList(name, rows)` | G6（批C-⑥）：行表 `{ { key=..., <子节点名>=值\|{key=..., arg0..arg3} , ... }, ... }`——**整表替换语义**（换数据再调一次；更新/缩容同入口）；行字段按**条目子节点名**寻址（缺节点 = 该字段不显）；行含文案键而语言服务未注入 = 装配期抛；语言切换整表重绑刷新行文案 |
| `OnListClick(name, fn(key, action))` | G6：行点击回传**稳定 key** + 点击的按钮节点名（条目根按钮 = 空串）；可在 `SetList` 前后登记（后登记对已实体化条目补挂） |

实现经 `Action<string, LuaTable>` 派发，不向 Lua 暴露 GetControl。Pulse/Flash 的 Graphic 解析是自身 → Button.targetGraphic → 组件/子级 Graphic；Slide 使用组件 Transform 的 RectTransform。

当前限制：

- **`SetTextKey` / `SetTextKeyArgs` / `SetTextKeyPlural` 已生产可用**（批C-② 装配 `BindLocale`——`UiServiceModule` 建语言服务经 `UIService localeProvider` 注入页面索引；批C-④ 起五页全量在用）。缺语言服务仍按"需先 BindLocale"抛（装配错误 fail-fast）；`SetList` 的行含文案键同样在装配期拦。
- **`Pulse` 全链统一 `0.2`（2026-10-10 裁决）**：`UiFx.Pulse`/`UIBindIndex.Pulse` 默认、`LuaBehaviourAdapter` shim 默认、以及原显式传参的两处调用点（`UIMain.lua`、`UIWidget.PlayPressFx`）全部为 `0.2`——呼吸至 20% 再回。`strength` 语义 = **目标 alpha 倍率**（`baseAlpha × strength`）：`>1` 为提亮方向、不透明对象上基本不可见，不用作默认/常规按压。
- 未命中/类型不符多数路径抛 KeyNotFoundException 或 InvalidOperationException；部分装饰入口采用日志降级，不能统称“所有失败必抛”。具体实现以 UIBindIndex 为准。
- 未知派发方法**开发期告警**（批C-⑥：`Log.Warning` 报 UnknownMethod；release 静默）——不吞拼写错误。
- MarkDriver 当前按控件登记命令式/绑定式所有权；目标逐属性协调，避免动效与数据争写。
- Button 解绑当前使用 RemoveAllListeners；目标只移除框架自有监听。
- OnButton 派发持有 payload 并在点击时取 LuaFunction；目标为展示作用域持有的明确回调句柄，关闭/换表释放，不能无限滞留 Lua 引用。
- 连续 TMP 淡入淡出不要使用当前 Graphic 颜色口，按制作规范增加 CanvasGroup 专用路径。
- `SetAnchoredPosition` / `BindText<T>` / `Has` / `Get<T>` / `MarkDriver` / `ResolveGraphic` / `ResolveRect` / `UnbindAll` 未被 Lua 适配器使用（它们是 C# 侧 `UIBindBase` 与诊断面入口）。

## 4. 控件与 API 对照

| 模板/能力 | 已有可用入口形态 | 待补足 |
| --- | --- | --- |
| StateButton | OnButton、SetInteractable；文本绑定 Label | 统一页面输入锁/焦点，框架自有监听 |
| Dialog | OnButton、SetText、Bridge.ui 开关、**ShowConfirm（退场链——Lua 确认经 DialogService 承载）** | 已由 `DialogService` 承载：类型化结果、取消、互斥组、有界队列 |
| Toast/Bubble/FlyText | ShowToast/ShowBubble/ShowFlyText | 多条并存、超限淘汰与复位；Scope 取消、宿主所有权 |
| ProgressBar/HpBar | SetProgress/SetProgressRange/SetHp | 更新去重、统一时钟、收敛停更 |
| Countdown | StartCountdown/StopCountdown | 显示秒去重、关闭期取消 |
| VirtualList/SimpleList | **`SetList(name, rows)` + `OnListClick(name, fn)`（G6，批C-⑥）**——行字段按条目子节点名、稳定 key 回传、整表替换语义 | 选择态由页面自持（无框架选择 API）；行内多按钮各回自己的节点名 |
| RedDot | 无专用 Lua 入口 | G4 BindRedDot(name, key) |
| StarRating/CountText | CountText 可通过 Label 静态设值 | G8 SetStars；G9 RollCount |
| Stepper | 可操作暴露文本节点 | G13 SetStepper/变化通知 |
| InputField | SetInteractable；SetText 只改命中标签，不等于输入模型赋值 | G14 GetInput/SetInput/提交与变更 |
| Slider/Toggle/Dropdown | **Set/Get/On*Changed（G15～G17，批C-⑥）**：程序 Set 不触发回调、变化通知带值、退订随 `UnbindAll` | 下拉项文本需页面在语言切换时重设（引擎组件不随 LText 刷新） |
| TabGroup/BottomNav | 入口按钮可绑定 | G5 SelectTab 与导航适配 |
| AnimatedImage/AvatarFrame | 可操作暴露节点 | G11 PlayAnim/StopAnim；G12 SetAvatar 与租约 |
| UIEventRelay/GuideHighlight | 可见性 | G18 OnClickArea；G19 GuideTo |
| SafeArea | 挂载并启用 SafeAreaReceiver 后自处理 | 统一根/内容区域装配；不能声称当前装配自动给所有页添加 |
| 单向绑定 | C# UIBindIndex.BindText 已有 | G21 Lua 绑定句柄与释放协议 |

G 编号用于追溯；总设计 U0～U4 决定实施顺序。

## 5. Target：扩展契约

本节为尚未实现的目标入口，名称为设计建议；实施时须同步 C# 接口、shim、生成与测试。

- 导航：`Bridge.ui.Show/Replace/Close/IsOpen`（按 lua_path，批C-④ 落地 `Replace`）；请求标识与完成通知（Success/Blocked/Busy/Cancelled/Failed）**仍为目标**——现为 fire-and-forget + 日志观测。
- 弹窗：类型化等待由 C# 承载，Lua 接受结果通知；确认、取消、ScopeExit 区分，不暴露 Unity 对象。
- 本地化：`Bridge.text.Get/Format` 与 LTextLabel；字符串转义和参数校验在服务收口。
- ~~G6~~ **已实现（批C-⑥）**：`self.ui:SetList(name, rows)`（详见 §3 表）；行有稳定 key、C# 持数据源、滚动绑定不逐帧回 Lua、行交互回传 (key, 按钮名)、整表替换语义（更新/缩容同入口）。选择态由页面自持。
- ~~输入控件~~ **已实现（批C-⑥）**：程序 Set 不触发回调（`Set*WithoutNotify`）、变化通知带值、关闭（UnbindAll）退订、防递归（Set 静默）——见 §3 表。
- 图标/头像：引用内容地址，C# 加载租约并检查展示/绑定代次；Lua 不接收原始 AssetHandle。
- 所有事件绑定返回或内部持有可注销句柄，挂展示作用域；新的 API 不再扩散手动清理责任。
- 效果播放：语义化播放 UI 效果定义（Handle/终态/取消语义；见 [UI 效果系统专项设计](UI效果系统专项设计.md)）；现有 Pulse/Flash/Slide 语义保持、底层换通道实现，不新增并行动效入口。

## 6. 维护与验证

新增受控方法必须同步 shim、Dispatch、UIBindIndex/对应服务、xLua 白名单/生成配置、本文，并提供真 Lua 调用测试。不能只用 C# 反射方法存在作为 Lua API 验收。

必测参数约定（dot/colon）、self 实例隔离、缺方法/缺控件/类型错误、回调异常、重复绑定、关闭重开、同页多次数据变化、异步结果迟到、环境重建。完整实现次序和测试分层见总设计第 12～13 节。
