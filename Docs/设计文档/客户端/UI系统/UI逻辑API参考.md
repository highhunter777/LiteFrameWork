# UI 逻辑 API 参考

> 状态：现行参考（以代码为准）
> 版本：2.0
> 定位：回答“页面逻辑（C#）能调用哪些受控 API、参数与错误语义是什么、列表行怎么传”。本文只做查询，不裁决生命周期/架构。
> 生成：方法名与签名以 `Assets/LiteGame/Editor/UITools/UIApi/UIApiSpec.json` 为单源，由 `python scripts/codegen/toolbox.py ui-api` 生成为 `UIBindIndex.Api.g.cs`（声明）/`UIBindBase.Api.g.cs`（转发）/`UIApiManifest.g.cs`（方法清单与 `ApiVersion`）；本文为手写导读，不随生成器覆盖，改 API 先改 spec 并重跑生成。
> 不做的事：不定义页面业务；不替代[UI 系统总设计](UI系统总设计.md)的状态机/导航契约；不记录施工状态。
> 冲突裁决：与代码不一致以代码为准，并顺手修本文。源码入口：`Packages/com.litegame.framework/Runtime/Services/UI/Bind/Index/UIBindIndex.cs`、`Bind/Index/UIBindIndex.Tabs.cs`、`Bind/UIBindBase.cs`、`Generated/UIBindIndex.Api.g.cs`、`Assets/LiteGame/Modules/UI/Pages/PageLogicBase.cs`、`Assets/LiteGame/Modules/UI/Bind/UIListRowSet.cs`。
> 关联：[UI 系统总设计](UI系统总设计.md) · [UI 制作规范](../../../规范/05-UI制作.md) · [UI 命名与控件词表参考](UI命名与控件词表参考.md) · [说明 08 UI 与页面逻辑层](../../../说明/08-UI与页面逻辑层.md)

## 1. 分层与入口

```text
C# 页面 (PageLogicBase : UIBindBase) ──► UIBindIndex ──► 控件（按 BindName 寻址）
C# 页面导航 (Ctx.Nav / Go / Replace / CloseSelf) ──► UINavigationController ──► UIService
确认缝 (Ctx.Confirm) ──► IConfirmDialog
设置页 (Services.Settings) ──► ISettingsPagePort（ProductSettings 直接实现，住 Settings 域）
```

- 页面逻辑不直接操作 `GameObject`/`Transform`/组件；只经受控 API。
- `BindName` 是唯一寻址名；未命中/类型不符/所有权冲突均抛（Debug 三宏下）。
- 高频路径（HUD）走 `HudModel` + 绑定，不经页面逻辑。运行时没有脚本层，也没有 `self.ui`/`Bridge.ui`/shim/dispatch。

## 2. 生命周期

`PageLogicBase`（`Assets/LiteGame/Modules/UI/Pages/PageLogicBase.cs`）继承 `UIBindBase`，子类覆写：

| 回调 | 时机 | 说明 |
| --- | --- | --- |
| `OnInit(IUIData data)` | 首次打开 | 只一次；绑定索引已建；`Ctx` 可读 |
| `OnShow(IUIData data)` | 首次打开、复用打开、暂停恢复 | Paused→Active 也会调 `OnShow(null)`；不要假设只首次 |
| `OnUpdate(float dt)` | 仅 Active | 默认关闭；需要时限频（建议 10Hz 上限） |
| `OnPause()` | 手动暂停 | — |
| `OnCover()` | 被更高全屏遮盖 | 不替代关闭 |
| `OnReveal()` | 遮盖解除 | — |
| `OnPageHide()` | 关闭（进入 Closing，**解绑前**） | 页面唯一收尾点；只清自有状态 |
| `OnHide()` | 关闭（封口，sealed） | 先 `OnPageHide`，再 `Index.UnbindAll`（按钮/订阅/列表/语言） |

- 按钮绑定放 `OnShow`；池化复用不重跑 `OnInit`，`OnHide` 已统一解绑。
- 页面关闭后旧回调不得再写控件；迟到异步用展示代次/页面标记丢弃。
- 页面内导航用 `Go(id)` / `Replace(id)` / `CloseSelf()`；异常进日志（与 `PageFlow` 同口径），不向上抛。

## 3. 受控 API

> 以下方法全部在 `UIBindBase`（转发生成）与 `UIBindIndex`（实现）上；未列方法不存在。生成声明见 `Generated/UIBindIndex.Api.g.cs`。

### 3.1 文本

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetText(name, value)` | 控件名, 文本 | 数据直写；不随语言刷新 |
| `SetTextKey(name, key, params args)` | 控件名, key, 参数 | 按 key 写；语言切换自动刷新 |
| `SetTextKeyPlural(name, key, count, params args)` | 控件名, key, 复数 count, 参数 | 英文 one/other 显式选择 |
| `UnbindTextKey(name)` | 控件名 | 解除该控件本地化绑定 |

- 未注入语言服务时 `SetTextKey*` 抛（静默显示 key 是隐蔽故障）。
- 同一节点只能有一个文案写者（静态 key 与动态 SetText 互斥；由评审与[规范 06 §3.3](../../../规范/06-UI美术协作与交接.md) 把守）。

### 3.2 显隐/交互/数值

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetVisible(name, visible)` | bool | 直接 `SetActive` |
| `SetInteractable(name, on)` | bool | `Selectable` 优先，回退 `UIWidget.Interactable` |
| `SetProgress(name, value01)` | 0~1 | 进度条 |
| `SetProgress(name, current, max)` | 当前/上限 | 进度条 |
| `SetHp(name, current, max)` | 当前/上限 | 血条（前条瞬时 + 后条滑落） |
| `StartCountdown(name, seconds)` | 秒 | 倒计时启动 |
| `StopCountdown(name)` | — | 停止（不触发完成） |
| `SetAnchoredPosition(name, pos)` | `Vector2` | 写 `RectTransform.anchoredPosition` |

### 3.3 提示/反馈

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `ShowToast(text)` | 文本 | 无 Toast 实例时记日志不抛 |
| `ShowBubble(name, text, duration)` | 控件名/文本/时长 | 挂点旁气泡（时长缺省取 `UIFxDefaults`） |
| `ShowFlyText(name, text)` | 控件名/文本 | 飘字（位置取控件 anchoredPosition） |
| `Services.Confirm`（`PageServices`） | 标题/正文/确认/取消 → `bool` | 确认缝；取消返回 false。`UIBindBase` **不提供** `ShowConfirm` |

### 3.4 输入控件

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetSlider(name, value01)` / `GetSlider(name)` | 控件名 | 滑条（0~1） |
| `OnSliderChanged(name, Action<float>)` | 控件名/回调 | 值变化 |
| `SetToggle(name, on)` / `GetToggle(name)` | 控件名 | 开关 |
| `OnToggleChanged(name, Action<bool>)` | 控件名/回调 | 值变化 |
| `SetDropdownOptions(name, options)` | 控件名/选项表 | 重建选项 |
| `SetDropdownValue(name, index)` / `GetDropdownValue(name)` | 控件名 | 当前项 |
| `OnDropdownChanged(name, Action<int>)` | 控件名/回调 | 值变化 |
| `SetTab(name, index)` / `GetTab(name)` | 控件名 | 页签（`TabGroup`/`ToggleTabGroup`） |
| `OnTabChanged(name, Action<int>)` | 控件名/回调 | 页签切换 |

### 3.5 动效

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `Pulse(name, strength, duration)` | 控件名；缺省取 `UIFxDefaults` | 脉冲；返回是否启动 |
| `Flash(name, duration)` | 控件名；缺省取 `UIFxDefaults` | 闪烁 |
| `Slide(name, offset, duration)` | 控件名/偏移；缺省取 `UIFxDefaults` | 滑动 |

- 动效只写表现；页面关闭/控件销毁时由控件侧 Kill，业务写入冲突由 `UIFx` 统一收口。
- 默认值单源 `UIFxDefaults`，页面与工具都引用它，不另存第二份。

### 3.6 列表

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `SetList(name, IUIListRows rows)` | 控件名/行表 | 整表替换 |
| `OnListClick(name, Action<string,string> handler)` | 控件名/回调 | 行点击（行 key, 行内控件名） |

行表用 `UIListRowSet`（`Assets/LiteGame/Modules/UI/Bind/UIListRowSet.cs`）构造：`Create().Row(key).Text(node, value).KeyText(node, key, args)`；含文案键的行随语言切换整表重绑。

### 3.7 按钮与查找

| 方法 | 参数 | 说明 |
| --- | --- | --- |
| `OnButtonClick(name, Action onClick)` | 控件名/回调 | 页面 API；替换式绑定（重绑先移除旧监听），回调经 `SafeCall` 隔离 |
| `Index.BindButtonClick(name, onClick)` / `Index.UnbindButtonClick(name)` | 控件名/回调 | 索引层实现 / 只解绑该按钮 |
| `Index.UnbindAll()` | — | 解绑全部（`OnHide` 统一调用；不要手工依赖） |
| `Index.Get<T>(name)` / `Index.Has(name)` | 控件名 | 强类型控件/存在性；未命中抛 |

## 4. 导航与确认

| 入口 | 签名 | 说明 |
| --- | --- | --- |
| `Ctx.Nav.GoAsync(id, data)` | `int`, `IUIData` | 压栈打开（保留旧页） |
| `Ctx.Nav.ReplaceAsync(id, data)` | 同上 | 替换当前记录（返回栈不残留） |
| `Ctx.Nav.BackAsync()` | — | 返回（最顶模态→层栈顶） |
| `Go(id, data)` / `Replace(id, data)` | 页面基类便捷口 | fire-and-forget，异常进日志 |
| `CloseSelf()` | — | 经 UI 服务统一关闭 |
| `Ctx.Confirm` | 标题/正文/确认/取消 | 确认缝 |

- 页面 id 用 `PageIds.g.cs` 常量（`PageIds.Page_Login` 等），禁拼字符串；`#uiform` 为唯一数据源。
- 导航唯一写者是 `UINavigationController`；页面不得直调 `UIService.ShowAsync`。

## 5. 页面上下文与数据

- `PageServices`（`Services`）：`Meta`（`IMetaSession`，Meta 域）/`Room`（`IRoomView`，会话域）/`Maps`（`IMapProvider`，选图行走 `Options`）/`Nav`（`UINavigationController`）/`UI`（`UIService`）/`Confirm`（`IConfirmDialog`）/`Settings`（`ISettingsPagePort`，设置域）。装配成功后成员非空；“未配置/未进房”由域对象状态表达。`PageLogicBase` 另给同名便捷口（`Nav`/`Meta`/`Maps`/`Room`/`Settings`/`Confirm`，未注入装配面时抛显性错）。
- 页面数据走强类型 `IUIData`：`ResultPageData`/`SettingsPageData`/`RoomPageData` 等，经 `Go/Replace/ShowAsync` 传入；不传 table/dictionary。
- 设置页只经 `Services.Settings`（`ISettingsPagePort`）读写：28 个原语成员（15 读 + 13 写/动作，写即生效、无 `Apply`），端口住 `Modules/Settings/Abstractions`、由 `ProductSettings` 直接实现；页面不认实现类。无装配面时 `OnShow` 早退、12 个设置控件禁用、关闭可用。

## 6. 列表行协议

| 项 | 协议 |
| --- | --- |
| 行身份 | `Row(key)` 的 key 是列表稳定标识（同表内唯一） |
| 字段寻址 | `Text/KeyText(node, …)` 的 node 是行条目 prefab 内的节点名（`BindName`） |
| 文案 | `KeyText(node, key, args)` 走本地化；语言切换整表重绑 |
| 点击 | `OnListClick(name, (rowKey, node) => …)`；行内按钮事件由行条目自己的逻辑/绑定处理 |
| 复用 | 整表替换（`SetList`）；不做增量 diff，列表虚拟化由控件层负责 |

## 7. 数据与本地化

- 页面数据：`IUIData`，经 `Go/Replace/ShowAsync` 传入。
- 静态文案：`LTextKeyRef`（目标组件）在打开/语言切换时写入；有静态 key 的节点页面逻辑不得再 `SetTextKey/SetText`。
- 动态文案：`SetTextKey(name, key, args)`；复数用 `SetTextKeyPlural(name, key, count, args)`；文本 key 必须存在于 `tbltext`（`TextKeyRefRule`）。
- 语言切换：`UIBindIndex` 的 locale 绑定重放已绑定 key；`UIStaticTextBinder` 重刷静态文案。

## 8. 错误语义

| 现象 | 异常/处置 |
| --- | --- |
| 绑定未命中 | `KeyNotFoundException`（核对 BindName/构建器登记） |
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
| 高频 HUD/每帧数值 | `HudModel` + 绑定；不进页面逻辑 |
| 复杂列表/大量交互 | 页面逻辑（`PageLogicBase` + `UIListRowSet`） |
| 低频业务编排/文案/按钮 | 页面逻辑（C#） |
| 静态文案 | `LTextKeyRef` + key；不写页面逻辑 |
| 需要强类型/服务调用 | `GetControl<T>` / `Ctx.Meta` / `Ctx.Room` / `Ctx.Settings` |
| 需要热更快速迭代 | 改 `LiteGame.Modules.UI` 页逻辑，随 HybridCLR hot 程序集发布（`code/` 段） |

> 页面逻辑只有一种语言（C#）；不存在“两套生成/两套所有权”的混用选项。
