# UI 制作规范

> 状态：现行规范（硬规则；违规必须能被工具/门禁发现）
> 版本：1.0
> 日期：2026-10-22
> Owner：客户端 UI / UI TA
> 定位：回答“做 UI 资产时必须遵守什么”。本文只写可执行、可校验的硬规则；架构与协作分别见[UI 系统总设计](UI系统总设计.md)与[UI 美术协作与交接规范](UI美术协作与交接规范.md)。
> 不做的事：不复述工具操作步骤；不定义页面业务；不记录施工状态。
> 冲突裁决：资产目录/收集规则以 `AGENTS.md`「资源规范」与[资源收集目录专项设计](../内容/资源收集目录专项设计.md)为准；运行时契约以[UI 系统总设计](UI系统总设计.md)为准；工具形态以[UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md)为准。
> 关联：[UI 命名与控件词表参考](UI命名与控件词表参考.md) · [UI 逻辑 API 参考](UI逻辑API参考.md)

## 1. 规则等级

| 等级 | 含义 | 违规后果 |
| --- | --- | --- |
| **MUST** | 必须遵守；工具/CI 可判 | 阻断提交/合并 |
| **SHOULD** | 默认遵守；例外需登记 | Warning；进入质量度量 |
| **MAY** | 可选；按页面需求 | 无 |

> 规则 ID 用于校验报告；本文用 `UI-<域>-<序号>` 编号。例外登记在 `UiRuleExceptions`（Owner/原因/到期），不得静默跳过。

## 2. 目录与资产

<a id="ch-directory"></a>

| 内容 | 路径 | 规则 |
| --- | --- | --- |
| 业务页面 | `Assets/ShareResource/UI/Screens/Page_<Semantic>.prefab` | MUST 自包含；不得是编辑器模板 Variant |
| 页面模板/系统面 | `Assets/ShareResource/UI/Screens/Tmp_<Semantic>.prefab` | MUST 只放通用组合；不含业务逻辑 |
| 控件模板 | `Assets/ShareResource/UI/Widgets/Tmp_<Widget>.prefab` | MUST 有 WidgetSpec；不含页面逻辑 |
| 战斗 HUD | `Assets/ShareResource/UI/Hud/Hud_<Semantic>.prefab` | MUST 由对局视图装配；不进 `tbuiform` |
| 图标 | `Assets/ShareResource/UI/Icons/` | MUST 进图集；按模块分组 |
| 字体 | `Assets/ShareResource/Fonts/` | MUST 用 TMP SDF；fallback 链明确 |
| 调试补丁 | `Assets/ShareResource/UI/Patches/` | MUST 仅编辑器/dev；不进包 |
| 契约/清单 | `Assets/LiteGame/Editor/UiTools/` | MUST 生成物；禁手改 |

- **UI-ASSET-01**：产品资产不得直接引用第三方包路径；先复制进 `ShareResource`（或模块内使用资源目录）再引用。
- **UI-ASSET-02**：付费第三方资源不入库；复制件按类型判定（配置/预制体模板/场景三类入库）。
- **UI-ASSET-03**：页面 prefab 不得成为编辑器模板的 Variant（跨 Owner 继承会导致美术改动被模板覆盖）。
- **UI-ASSET-04**：`Patches/`、测试夹具、dev-only 资产不得进收集组/构建。
- **UI-ASSET-05**：资源移动/改名按 `directory-move-sync` 同步收集组、场景、测试、文档与构建器。

## 3. Prefab 根契约

<a id="ch-prefab"></a>

### 3.1 页面根（MUST）

`Page_*` / `Tmp_*`（Screens）根必须同时具备：

| 组件 | 要求 |
| --- | --- |
| `RectTransform` | 锚点 stretch 全屏；`anchoredPosition=0` 基线 |
| `Canvas` | `ScreenSpaceOverlay`；`overrideSorting=true`；sortingOrder 交给层策略 |
| `CanvasScaler` | `ScaleWithScreenSize`；参考 1920×1080；`match=0.5` |
| `GraphicRaycaster` | 页面根一个；不要重复 |
| `CanvasGroup` | alpha=1；interactable/blocksRaycasts 由运行时协调 |
| `SafeAreaReceiver` | 在可交互内容容器上（可与根分离） |

- **UI-PREFAB-01**：缺任一组件 = Error；运行时不得补建。
- **UI-PREFAB-02**：页面根名与文件名一致（`Page_Lobby.prefab` → 根 `Page_Lobby`）。
- **UI-PREFAB-03**：页面根不得挂 `Animator`；动效走 `UiFx`/转场。
- **UI-PREFAB-04**：页面不得引用场景对象；不得通过字符串路径 `Find` 取控件。

### 3.2 控件模板根（MUST）

`Tmp_*`（Widgets）根：

- `RectTransform` + 可选 `CanvasGroup`；不需要独立 Canvas（除非有独立遮罩/排序需求）。
- 不含页面级导航/业务逻辑；只允许控件自身行为组件。
- 声明 WidgetSpec：插槽、可配置属性、状态、事件、尺寸行为。
- 模板内部件默认不挂 `BindName`；页面只操作模板暴露的插槽/属性。

### 3.3 HUD 根（MUST）

- `Hud_*` 根：Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup；排序带按总设计 §8.4。
- 由对局视图装配，不挂 `tbuiform`，不走 `UIService` 导航。
- HUD 内部交互控件若需要点击，必须有明确输入锁/模态兼容；默认 `raycastTarget=false`。

## 4. 命名

<a id="ch-naming"></a>

### 4.1 文法

```text
Root:      Page_<Semantic> | Hud_<Semantic> | Tmp_<Semantic>
Node:      <Role>_<Semantic>[_<Variant>]
Private:   _<Semantic>
Clone:     Clone_<Semantic>
BindName:  默认与节点名同文（页面内唯一；可为 API 稳定性独立命名）
```

- **UI-NAME-01**：只允许 ASCII 字母/数字/下划线；PascalCase 语义段；禁空格、中文、`/`、`.`、`-`。
- **UI-NAME-02**：角色段必须在[词表](UI命名与控件词表参考.md)内；未登记前缀 = Warning（新角色需先登记词表）。
- **UI-NAME-03**：同级节点名唯一；全页 `BindName` 唯一。
- **UI-NAME-04**：私有件用前导 `_`（如 `_Blocker`、`_Handle`）；私有件不得挂 `BindName`。
- **UI-NAME-05**：列表条目模板用 `Clone_<Semantic>`；条目内部件不设 `BindName`。
- **UI-NAME-06**：根名/文件名同文；节点改名时，若 `BindName` 跟随节点名则同批改；若 `BindName` 为 API 稳定性独立命名，则只改节点名并重生成契约，引用不随视觉改名漂移。
- **UI-NAME-07**：禁 `New Game Object`、`Text (TMP)` 等默认名进入提交；导入后立即命名。

### 4.2 角色段（摘要）

| 角色 | 用途 | 示例 |
| --- | --- | --- |
| `Pnl` | 面板/大容器 | `Pnl_Settings` |
| `Cot` | 布局容器/安全容器 | `Cot_SafeArea`、`Cot_Body` |
| `Bg` | 背景 | `Bg_Dim` |
| `Txt` | 文本 | `Txt_Title` |
| `Img`/`Image` | 图片 | `Img_Avatar` |
| `Icon` | 图标 | `Icon_Coin` |
| `Button` | 按钮 | `Button_Ok` |
| `Toggle` | 开关 | `Toggle_Auto` |
| `Slider` | 滑条 | `Slider_Volume` |
| `Input` | 输入框 | `Input_Name` |
| `Dropdown` | 下拉 | `Dropdown_Quality` |
| `Lst` | 列表 | `Lst_Rooms` |
| `Item` | 列表条目根 | `Item_Room` |
| `Slot` | 插槽容器 | `Slot_Content` |
| `Tab` | 页签 | `Tab_Profile` |
| `Grp` | 互斥组 | `Grp_Tabs` |
| `Bar` | 进度/血条 | `Bar_Hp` |
| `Mask` | 遮罩/阻断 | `_Mask` |
| `Safe` | 安全区 | `Safe_Root` |
| `Fx` | 动效挂点 | `Fx_Reward` |
| `Decor` | 纯装饰 | `Decor_Glow` |
| `Clone` | 条目模板 | `Clone_Item` |

> 完整词表、类型段与选段指引见[UI 命名与控件词表参考](UI命名与控件词表参考.md)。

## 5. 布局与适配

<a id="ch-layout"></a>

- **UI-LAYOUT-01**：根 CanvasScaler 参考 1920×1080、Match=0.5；页面不得自带第二套缩放（子 Canvas 继承）。
- **UI-LAYOUT-02**：全屏页面用 stretch 锚点；内容区分层用锚点/边距，不用绝对坐标堆叠。
- **UI-LAYOUT-03**：间距/内边距用 Spacing 令牌（4/8/16/24/32）；不写随机像素。
- **UI-LAYOUT-04**：可交互内容放安全容器；背景可铺满；安全区在分辨率/方向变化时重算。
- **UI-LAYOUT-05**：`LayoutGroup`/`ContentSizeFitter` 嵌套深度 ≤3；超过需登记并说明 Rebuild 风险。
- **UI-LAYOUT-06**：文本容器必须定义换行/溢出策略；关键按钮/结果信息不得依赖截断。
- **UI-LAYOUT-07**：移动触控热区 ≥44pt（逻辑像素按设备档换算）；PC 点击热区 ≥32px。
- **UI-LAYOUT-08**：超宽屏（21:9）内容区有最大宽度/安全边距；背景可裁切，信息不裁切。
- **UI-LAYOUT-09**：页面在 16:9、19.5:9、20:9、4:3、16:10、21:9 预览通过；不接受“只在自己的显示器上看过”。

## 6. 渲染与 Canvas

<a id="ch-render"></a>

- **UI-RENDER-01**：页面根一个 Canvas + 一个 GraphicRaycaster；子 Canvas 只用于高频重绘/独立遮罩/独立排序。
- **UI-RENDER-02**：非交互 Graphic 的 `raycastTarget=false`；只有按钮/输入/阻断面/滚动命中面保留 true。
- **UI-RENDER-03**：不要为每个动态元素建 Canvas；按更新频率/同时可见范围/批次代价划分。
- **UI-RENDER-04**：排序带按总设计 §8.4；不得写私有 sortingOrder 跨越层区间；反馈面不得自建 Canvas。
- **UI-RENDER-05**：图集按“同时可见 + 同时加载”分组；跨页共享控件放共享组；不做全 UI 大图集。
- **UI-RENDER-06**：普通图标 `Simple`；可拉伸背景 `Sliced`；进度 `Filled`；九宫格边界进校验。
- **UI-RENDER-07**：UI prefab 不挂 `Animator`；动效走 `UiFx`/转场（例外需登记视觉效果与预算）。
- **UI-RENDER-08**：Shader 需验证 alpha/Stencil Mask/RectMask2D/裁剪/目标平台；不假设“继承 UI/Default”。
- **UI-RENDER-09**：避免无谓 Overdraw（大块透明叠层、全屏粒子）；热点截图确认。
- **UI-RENDER-10**：运行时不得构建视觉结构（`new GameObject`/`AddComponent` 造 Canvas/Graphic/Text/Button）；唯一例外是引导期错误页。

## 7. 文本、本地化与样式

<a id="ch-text"></a>

- **UI-TEXT-01**：所有文本用 TMP（`TextMeshProUGUI`）；不使用 UGUI `Text` 新建。
- **UI-TEXT-02**：字号/字体/行距来自 Typography 令牌；不得按页面随手写字号。
- **UI-TEXT-03**：静态文案挂 `LTextKeyRef`（只存 `Key`，不带参数/复数）；动态文案走绑定；同一节点只能有一个写者。
- **UI-TEXT-04**：key 命名 `UI.<页面>.<语义>` / `Common.<语义>`；key 必须存在；占位符数量/类型匹配。
- **UI-TEXT-05**：玩家/外部文本默认禁富文本；作者文本只允许白名单标签；`<size>` 限范围。
- **UI-TEXT-06**：正文换行/滚动边界明确；英文/伪本地化长文本预览通过。
- **UI-TEXT-07**：字体 fallback 链覆盖目标语言；缺字审计进校验；共享材质优先，实例材质有 Owner/预算。
- **UI-TEXT-08**：颜色/文字样式优先令牌引用；裸颜色/裸字号 = Warning，需登记例外。
- **UI-TEXT-09**：按钮/标签的 Disabled/Selected 状态色由样式状态表定义，不在 Lua/C# 写 RGB。

## 8. 动效与效果

<a id="ch-motion"></a>

- **UI-MOTION-01**：转场走 `UITransitionRunner`；控件动效走 `UiFx`；不直接 DOTween。
- **UI-MOTION-02**：时长/曲线来自 Motion 令牌；默认值集中在 `UiFxDefaults`；Lua/C# 同源。
- **UI-MOTION-03**：动效只写表现属性，不携带判定，不改业务数据。
- **UI-MOTION-04**：页面关闭/隐藏即杀动效；迟到回调用展示代次丢弃。
- **UI-MOTION-05**：同一属性同一时刻只有一个动效写者；业务写入前先 Kill 冲突动效。
- **UI-MOTION-06**：UI 粒子/音效走模板与 `AudioService.Group.Ui`；并发/数量有预算；低端档降级。
- **UI-MOTION-07**：支持减少动效：转场立即完成，控件动效跳过/缩短；不阻塞操作。
- **UI-MOTION-08**：世界 VFX 不塞进 UI prefab；UI prefab 不挂世界粒子。

## 9. 列表与异步资源

<a id="ch-list"></a>

- **UI-LIST-01**：大列表用 `VirtualList`（建议 ≥20 条）；`SimpleListView` 只用于小列表。
- **UI-LIST-02**：条目模板用 `Clone_<Semantic>`；条目内部件不设 `BindName`；行数据按子节点名/字段协议。
- **UI-LIST-03**：行点击/按钮回调在重绑时先解绑旧监听；池化复用不得留旧回调。
- **UI-LIST-04**：异步头像/图标核对稳定 key + 展示代次；替换时对称释放旧租约。
- **UI-LIST-05**：`SetList` 整表替换；禁止每个滚动帧跨 Lua 调用；滚动监听一次绑定、对称解绑。
- **UI-LIST-06**：空列表/1 条/短列表/大数据/缩容/滚到末尾再缩容都要正确；HardCap 不作为虚拟化替代。

## 10. 交互与可访问性

<a id="ch-interaction"></a>

- **UI-INT-01**：按钮用 `Button` + `targetGraphic`；不要用 Image 挂 EventTrigger 冒充按钮（除明确热区）。
- **UI-INT-02**：Toggle 用 `Toggle` + `ToggleGroup`；Slider/Input/Dropdown 用标准组件或项目包装件。
- **UI-INT-03**：模态有真实射线遮罩；遮罩是显式节点（`_Mask`），不是只关 interactable。
- **UI-INT-04**：焦点可见；键盘/手柄导航顺序明确；Submit/Cancel 统一；模态拦截 Back。
- **UI-INT-05**：禁止 hover-only 操作；触控有等价路径；长按/拖拽有替代。
- **UI-INT-06**：状态不能只靠颜色区分；对比度/字号/触控热区达目标端基线。
- **UI-INT-07**：自定义控件必须遵守上层输入锁；不能只信自身 `interactable`。

## 11. 提交与验证

<a id="ch-submit"></a>

### 11.1 提交前（美术/TA）

- [ ] 根契约组件完整；根名/文件名一致。
- [ ] 命名文法通过；`BindName` 唯一；无默认节点名。
- [ ] 增量校验无 Error；Warning 已解释/登记。
- [ ] 契约已生成且 `sourceHash` 与 prefab 一致。
- [ ] 截图矩阵：中/英/长文本/安全区/设备档。
- [ ] 样式令牌/图集/字体/模板依赖通过。

### 11.2 提交前（工程）

- [ ] 页面登记完成；`tbuiform` 行/Luban 生成通过。
- [ ] 绑定生成/hash 记录完成；生成物与 prefab 一致。
- [ ] 引用全部在契约 `controls[]` 子集内。
- [ ] 全量校验无 Error；PlayMode/Player 可开/可点/可返回。
- [ ] 复用/语言切换/取消/关闭无残留。
- [ ] 性能预算记录（打开/稳定帧/GC/DrawCall）。

### 11.3 禁止提交

- 手改生成物（契约/清单/Designer）。
- 缺 Canvas/CanvasGroup/GraphicRaycaster 的页面。
- 直接引用第三方路径的产品资产。
- dev-only 资产进收集组/构建。
- 用 `new GameObject` 在运行时补视觉结构（除引导期错误页）。
- 以截图/“看过了”替代校验与契约。

### 11.4 门禁

| 门 | 命令/入口 | 判据 |
| --- | --- | --- |
| 增量校验 | `ui.validate-art` | 无 Error；Warning 可见 |
| 全量校验 | `ui.validate` | 无 Error |
| 契约/hash | `ui.contract-diff` / `ui.binding-hash` | 无漂移 |
| 资源/收集 | `ui.assets.*` | 无第三方直引/缺组/debug 入包 |
| Unity 编译 | L2 Unity 门禁 | 0 编译错误 |
| 运行时 | PlayMode/Player | 可开/可点/可返回；无残留 |
| 文档 | Doc lane | 链接/索引有效 |

## 12. 例外登记

- 例外必须包含：规则 ID、资产、节点（可选）、Owner、原因、到期版本/日期。
- 到期未修复自动回到 Warning/Error；不得续期掩盖。
- 例外不适用于“视觉单一来源”“运行时构建视觉”“第三方直引”“生成物手改”四条红线。
- 例外表由工具读取，不进运行时；release 不受影响。
