# UI 控件 Lua API 参考

> 状态：现行参考；已有 API 与目标 API 分列
> 版本：2.0
> 更新日期：2026-09-21
> Owner：客户端 UI / Lua
> 权威契约：[UI框架总设计](UI框架总设计.md)；制作要求：[UI制作规范](UI制作规范.md)
> 注意：当前适配器存在 self 缺失，完整页面调用不可视为已验收；见总设计 UI-01

## 1. 源码与调用约定

现有入口：[LuaBehaviourAdapter](../../../../Assets/LiteGame/Runtime/Shell/UI/LuaBehaviourAdapter.cs)、[UIBindIndex](../../../../Assets/LiteGame/Runtime/Shell/UI/Bind/UIBindIndex.cs)、[LuaComponent.BindBridge](../../../../Assets/LiteGame/Runtime/Shell/Lua/LuaComponent.cs)、[Bridge.Ui](../../../../Assets/LiteGame/Runtime/Shell/Bridge/Bridge.Data.cs)。

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

上面演示参数约定，不表示当前完整打开链已正常。旧稿 `Bridge.ui:Show(id, data)` 多传了一个表参数，废止。

### 1.1 生命周期：现有形态与待修正项

现有 `IUIFormLogic` 有 `OnInit/OnShow/OnUpdate/OnPause/OnCover/OnReveal/OnHide` 七个回调；缺少对应 Lua 方法时跳过。适配器建立绑定索引并向逻辑表挂 `ui`。

当前问题：适配器 `fn.Call(args)` 未传逻辑实例，冒号定义的 `self` 被错位；GameEntry 直接使用 require 返回表，尚无明确实例工厂。目标是注册模块、工厂创建实例、回调显式传 self。不能仅把 Lua 方法全部改成点号绕过实例契约。

修复后的目标写法：

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

return M -- 注册工厂/模块；运行时调用 M.new()，每页独立实例。
```

当前 Lua OnHide 会调用 UnbindAll，复用不会正常重跑 OnInit，因此按钮绑定放在 OnShow。`events.on` 当前仍需页面自行持有注销函数；自动纳入展示作用域是 U1 目标。当前 C# UIBindBase 不具备与 Lua 完全相同的自动解绑，需要同步修正。

OnResume、OnNavigationResult、OnLocaleChanged、OnDispose 为目标可选能力，尚未导出；语言切换/返回结果不得通过重复 OnShow 模拟。

## 2. 已有全局门面

| 入口 | 当前行为 | 限制 |
| --- | --- | --- |
| `Bridge.ui.Show(id, data)` | fire-and-forget 打开，Lua 表包装为 IUIData；失败写日志 | 无 Lua 完成结果；并发/取消由 U1 重构 |
| `Bridge.ui.Close(id)` | fire-and-forget 关闭 | 当前仅 Active；目标覆盖全部逻辑打开态 |
| `Bridge.ui.IsOpen(id)` | Active/Covered/Paused 为 true | 不表示资源已加载完成或页面可交互 |
| `Bridge.ui.GetLogic(id)` | 返回注册表逻辑表 | 遗留诊断口，业务不应借此调用另一页/持有模块；随实例工厂迁移收口 |
| `Bridge.data.GetItem/GetUIForm` | 按行缓存的数据门面 | 本参考不扩充数据协议 |
| `Bridge.content.GetProcessor` | 内容注册查询 | 不应越权访问底层服务 |
| `log.info/warning/error`、`events.on` | 日志与事件桥 | 事件退订遵循现有桥协议 |

目标 Go/Back/Dialog/请求结果尚未接入，不能在业务代码中当成现有方法调用。`self.ui` 只管理本页控件；导航统一从 Bridge.ui 发起，不重复增加第二套导航门面。

## 3. 已有 self.ui 方法

当前 shim 声明 16 个方法。这是静态声明数量，不代表全链路测试通过。所有控件名来自页面 `BindNode.BindName`；模板不预设非空名字。

| 方法 | 当前语义/参数 |
| --- | --- |
| `OnButton(name, fn)` / `OffButton(name)` | 替换式 Button 绑定/移除；C# 回调经 SafeCall |
| `SetText(name, text)` | TMP_Text 优先，兼容 UGUI Text；应绑定实际 Label 节点 |
| `SetVisible(name, visible)` | 目标节点 SetActive |
| `SetInteractable(name, on)` | Selectable 优先，回退 UIWidget.Interactable |
| `SetProgress(name, value01)` | ProgressBar 归一化值 |
| `SetProgressRange(name, cur, max)` | ProgressBar 当前/最大值 |
| `SetHp(name, cur, max)` | HpBar 前条/延迟条与数值 |
| `StartCountdown(name, seconds)` / `StopCountdown(name)` | 启停倒计时；Stop 不触发 OnDone |
| `ShowToast(text)` | 查找 Toast 宿主；缺失按当前实现记录日志 |
| `ShowBubble(name, text, duration)` | duration 缺省 1.5s |
| `ShowFlyText(name, text)` | 在目标位置显示飘字 |
| `Pulse(name, strength, duration)` | shim 缺省 1.2/0.16s；当前是 Graphic 透明度动画，非缩放 |
| `Flash(name, duration)` | shim 缺省 0.3s；当前调用 Graphic 颜色 Tween，不能承诺自动回到原色 |
| `Slide(name, ox, oy, duration)` | offset 缺省 0/0，duration 缺省 0.25s；最终视觉应受复位契约约束 |

实现经 `Action<string, LuaTable>` 派发，不向 Lua 暴露 GetControl。Pulse/Flash 的 Graphic 解析是自身 → Button.targetGraphic → 组件/子级 Graphic；Slide 使用组件 Transform 的 RectTransform。

当前限制：

- 未命中/类型不符多数路径抛 KeyNotFoundException 或 InvalidOperationException；部分装饰入口采用日志降级，不能统称“所有失败必抛”。具体实现以 UIBindIndex 为准。
- 未知派发方法目前静默忽略；目标开发期报告 UnknownMethod，不能在 API 演进中悄悄吞拼写错误。
- MarkDriver 当前按控件登记命令式/绑定式所有权；目标逐属性协调，避免动效与数据争写。
- Button 解绑当前使用 RemoveAllListeners；目标只移除框架自有监听。
- OnButton 派发持有 payload 并在点击时取 LuaFunction；目标为展示作用域持有的明确回调句柄，关闭/换表释放，不能无限滞留 Lua 引用。
- Pulse 的 Lua 默认值与 C# UiFx 默认值不同；Flash/Slide 及取消后的基线必须在 U0/U1 实测并统一。当前 G20 入口存在不代表视觉复位已完成。
- 连续 TMP 淡入淡出不要使用当前 Graphic 颜色口，按制作规范增加 CanvasGroup 专用路径。

## 4. 控件与 API 对照

| 模板/能力 | 已有可用入口形态 | 待补足 |
| --- | --- | --- |
| StateButton | OnButton、SetInteractable；文本绑定 Label | 统一页面输入锁/焦点，框架自有监听 |
| Dialog | OnButton、SetText、Bridge.ui 开关 | 类型化结果、取消、互斥与有界队列 |
| Toast/Bubble/FlyText | ShowToast/ShowBubble/ShowFlyText | Scope 取消、宿主所有权、容量与复位 |
| ProgressBar/HpBar | SetProgress/SetProgressRange/SetHp | 更新去重、统一时钟、收敛停更 |
| Countdown | StartCountdown/StopCountdown | 显示秒去重、关闭期取消 |
| VirtualList/SimpleList | 无 Lua 数据源门面 | G6 SetList；先修 C# 列表内核 |
| RedDot | 无专用 Lua 入口 | G4 BindRedDot(name, key) |
| StarRating/CountText | CountText 可通过 Label 静态设值 | G8 SetStars；G9 RollCount |
| Stepper | 可操作暴露文本节点 | G13 SetStepper/变化通知 |
| InputField | SetInteractable；SetText 只改命中标签，不等于输入模型赋值 | G14 GetInput/SetInput/提交与变更 |
| Slider/Toggle/Dropdown | SetInteractable | G15～G17 设值、读取/变化通知 |
| TabGroup/BottomNav | 入口按钮可绑定 | G5 SelectTab 与导航适配 |
| AnimatedImage/AvatarFrame | 可操作暴露节点 | G11 PlayAnim/StopAnim；G12 SetAvatar 与租约 |
| UIEventRelay/GuideHighlight | 可见性 | G18 OnClickArea；G19 GuideTo |
| SafeArea | 挂载并启用 SafeAreaReceiver 后自处理 | 统一根/内容区域装配；不能声称当前壳自动给所有页添加 |
| 单向绑定 | C# UIBindIndex.BindText 已有 | G21 Lua 绑定句柄与释放协议 |

历史 G 编号只用于追溯；总设计 U0～U4 决定施工顺序。

## 5. Target：扩展契约

所有本节入口尚未实现，名称为设计建议；实施时须同步 C# 接口、shim、生成与测试。

- 导航：`Bridge.ui.Go/Back/Replace` 提供请求标识与完成通知；Lua 无原生 await 不等于只能 fire-and-forget。用受控回调/事件返回 Success/Blocked/Busy/Cancelled/Failed，页面退出后停止交付。
- 弹窗：类型化等待由 C# 承载，Lua 接受结果通知；确认、取消、ScopeExit 区分，不暴露 Unity 对象。
- 本地化：`Bridge.text.Get/Format` 与 LTextLabel；字符串转义和参数校验在服务收口。
- G6：`self.ui:SetList(name, rows)`，数据行有稳定 key；C# 持数据源，滚动绑定不逐帧回 Lua。行交互回传 key/行为。选择、更新和数据缩容均有明确语义。
- 输入控件：程序 Set 默认不触发用户变化回调，需通知时显式指定；回调带值，关闭退订，防递归循环。
- 图标/头像：引用内容地址，C# 加载租约并检查展示/绑定代次；Lua 不接收原始 AssetHandle。
- 所有事件绑定返回或内部持有可注销句柄，挂展示作用域；新的 API 不再扩散手动清理责任。

## 6. 维护与验证

新增受控方法必须同步 shim、Dispatch、UIBindIndex/对应服务、xLua 白名单/生成配置、本文，并提供真 Lua 调用测试。不能只用 C# 反射方法存在作为 Lua API 验收。

必测参数约定（dot/colon）、self 实例隔离、缺方法/缺控件/类型错误、回调异常、重复绑定、关闭重开、同页多次数据变化、异步结果迟到、环境重建。完整实现次序和测试分层见总设计第 12～13 节。
