# UI 命名与控件词表参考

> 状态：现行参考（人类可读投影；机器单源以工具词表为准）
> 版本：1.0
> 日期：2026-10-22
> 定位：回答“节点该叫什么、前缀用哪个、控件模板有哪些、选段怎么选”。本文只做查询与速查，不裁决生命周期/协作。
> 不做的事：不定义页面业务；不替代[UI 制作规范](UI制作规范.md)的硬规则；不记录施工状态。
> 冲突裁决：命名硬规则以[UI 制作规范 §命名](UI制作规范.md#ch-naming)为准；机器可读词表（工具 Inspector 预设/校验）是单一实现源，本文必须与它保持一致，由 `ui.validate` 的 `ui.naming-words` 规则对账。
> 关联：[UI 制作规范](UI制作规范.md) · [UI 编辑器工具链专项设计](UI编辑器工具链专项设计.md)

## 1. 名字模型

```text
页面/模板根：  Page_<Semantic> | Hud_<Semantic> | Tmp_<Semantic>
普通节点：    <Role>_<Semantic>[_<Variant>]
私有节点：    _<Semantic>
条目模板：    Clone_<Semantic>
绑定名：      BindName 默认 = 节点名（页面内唯一；可为 API 稳定性独立命名）
```

| 规则 | 说明 |
| --- | --- |
| 字符集 | ASCII 字母/数字/下划线；PascalCase 语义段 |
| 分隔 | `_` 只做段分隔；语义段内不再加下划线 |
| 禁项 | 空格、中文、`/`、`.`、`-`、默认名 `GameObject`/`Text (TMP)` |
| 唯一 | 同级节点名唯一；全页 `BindName` 唯一 |
| 根名 | 与 prefab 文件名一致 |
| 长度 | 建议 ≤48 字符；`BindName` ≤64 |

## 2. 角色词表

### 2.1 结构/展示

| 前缀 | 全称 | 用途 | 示例 |
| --- | --- | --- | --- |
| `Pnl` | Panel | 面板/大容器 | `Pnl_Settings` |
| `Cot` | Container | 布局容器/安全容器 | `Cot_SafeArea`、`Cot_Body` |
| `Grp` | Group | 互斥/逻辑分组 | `Grp_Tabs` |
| `Bg` | Background | 背景 | `Bg_Dim` |
| `Img` | Image | 图片 | `Img_Avatar` |
| `Icon` | Icon | 图标 | `Icon_Coin` |
| `Txt` | Text | 文本 | `Txt_Title` |
| `Bar` | Bar | 进度/血条/分隔 | `Bar_Hp` |
| `Mask` | Mask | 遮罩/阻断（通常私有） | `_Mask` |
| `Safe` | SafeArea | 安全区容器 | `Safe_Root` |
| `Decor` | Decoration | 纯装饰 | `Decor_Glow` |
| `Fx` | Effect | 动效挂点 | `Fx_Reward` |

### 2.2 交互控件

| 前缀 | 对应组件 | 示例 |
| --- | --- | --- |
| `Button` | `Button` | `Button_Ok` |
| `Toggle` | `Toggle` | `Toggle_Auto` |
| `Slider` | `Slider` | `Slider_Volume` |
| `Input` | `TMP_InputField`/`InputField` | `Input_Name` |
| `Dropdown` | `Dropdown`/`UIDropdownWrap` | `Dropdown_Quality` |
| `Scroll` | `ScrollRect` | `Scroll_List` |
| `Tab` | 页签按钮 | `Tab_Profile` |
| `Stepper` | 步进器 | `Stepper_Count` |
| `Star` | 星级 | `Star_Rating` |

### 2.3 列表/条目

| 前缀 | 用途 | 示例 |
| --- | --- | --- |
| `Lst` | 列表容器/列表控件 | `Lst_Rooms`、`VirtualList_Rooms` |
| `Item` | 列表条目根（非模板） | `Item_Room` |
| `Clone` | 条目模板 | `Clone_Item` |
| `Slot` | 插槽容器 | `Slot_Content` |

> 列表控件节点当前实际命名为 `VirtualList_Rooms`/`SimpleList_*`；`VirtualList`/`SimpleList` 作为**控件类型段**保留，允许作为前缀。新增列表统一 `Lst_<Semantic>`，存量按迁移批次整改。

### 2.4 根前缀

| 前缀 | 用途 | 路径 |
| --- | --- | --- |
| `Page` | 业务页面 | `Screens/Page_*.prefab` |
| `Hud` | 战斗 HUD | `Hud/Hud_*.prefab` |
| `Tmp` | 控件/页面模板 | `Widgets/Tmp_*.prefab`、`Screens/Tmp_*.prefab` |

### 2.5 私有件与克隆模板

- `_<Semantic>`：模板内部件/实现细节，不暴露、不挂 `BindName`。
- `Clone_<Semantic>`：列表条目模板；不暴露；条目内部件按子节点名寻址。
- 例：`_Blocker`、`_Handle`、`_Fill`、`Clone_Item`、`Clone_Tab`。

## 3. 控件模板清单

> 以 `Assets/ShareResource/UI/Widgets/Tmp_*.prefab` 与 `UiWidgetManifest` 为准；新增模板先登记词表/清单再使用。

| 模板 | 用途 | 关键能力 |
| --- | --- | --- |
| `Tmp_AnimatedImage` | 帧动画图片 | 帧序列/循环 |
| `Tmp_AvatarFrame` | 头像框 | 头像/边框/状态 |
| `Tmp_Baseline` | 控件基线 | 通用容器/样式基线 |
| `Tmp_BottomNav` | 底部导航 | 页签/选中态 |
| `Tmp_Bubble` | 气泡提示 | 挂点/时长 |
| `Tmp_CountText` | 数值文本 | 数字格式化 |
| `Tmp_Countdown` | 倒计时 | 开始/停止/完成 |
| `Tmp_Dialog` | 弹窗 | 标题/内容/确认取消 |
| `Tmp_Dropdown` | 下拉 | 选项/选中 |
| `Tmp_EventRelay` | 事件转发 | 点击/拖拽转发 |
| `Tmp_FlyText` | 飘字 | 池/飘字 |
| `Tmp_GuideHighlight` | 引导高亮 | 遮罩/挖洞/目标 |
| `Tmp_HpBar` | 血条 | 当前/上限/延迟条 |
| `Tmp_InputField` | 输入框 | 文本/占位/校验 |
| `Tmp_KillFeedRow` | 击杀播报行 | 双方/图标/文本 |
| `Tmp_Loading` | 加载遮罩 | 不定态/进度/阻断 |
| `Tmp_ProgressBar` | 进度条 | 0~1/当前上限 |
| `Tmp_RankCell` | 排行条目 | 名次/头像/分数 |
| `Tmp_RedDot` | 红点 | 计数/开关 |
| `Tmp_RotatingImage` | 旋转图片 | 速度/方向 |
| `Tmp_SafeArea` | 安全区 | 四边适配 |
| `Tmp_SimpleList` | 小列表 | 少量条目 |
| `Tmp_Slider` | 滑条 | 0~1/变化回调 |
| `Tmp_StarRating` | 星级评分 | 星级/只读 |
| `Tmp_StateButton` | 状态按钮 | Normal/Pressed/Disabled/Selected |
| `Tmp_Stepper` | 步进器 | 加减/范围 |
| `Tmp_TabGroup` | 页签组 | 互斥/选中 |
| `Tmp_Toast` | 轻提示 | 文本/时长/堆叠 |
| `Tmp_Toggle` | 开关 | 开/关/变化回调 |
| `Tmp_VirtualList` | 虚拟列表 | 窗口复用/滚动 |

### 3.1 页面模板/系统面

| 模板 | 用途 |
| --- | --- |
| `Tmp_BaselineA` | 全屏页面基线 A |
| `Tmp_BaselineB` | 全屏页面基线 B |
| `Tmp_ListScreen` | 列表页基线 |
| `Tmp_Dialog` | 系统确认弹窗 |
| `Tmp_Loading` | 通用加载页 |
| `Tmp_Toast` | 系统 Toast |

## 4. 选段指引

| 你要做 | 用哪个 |
| --- | --- |
| 页面大区块 | `Pnl_` / `Cot_` |
| 可滚动内容区 | `Scroll_`/`Lst_` |
| 单项数据行 | `Item_`；模板用 `Clone_` |
| 按钮/开关/滑条/输入/下拉 | 对应控件前缀 |
| 静态标题/说明/按钮文字 | `Txt_` |
| 图标/头像/背景/装饰 | `Icon_`/`Img_`/`Bg_`/`Decor_` |
| 需要工程操作的节点 | 挂 `BindNode`，`BindName` 同名 |
| 模板内部实现件 | `_` 私有；不挂 `BindName` |
| 列表条目内部件 | 子节点名（不加 `BindName`） |

## 5. 语义词参考

| 域 | 常用词 |
| --- | --- |
| 通用 | Title、Subtitle、Body、Caption、Label、Value、Status、Hint、Icon、Avatar、Background、Content、Footer、Header |
| 操作 | Ok、Cancel、Close、Back、Confirm、Retry、Apply、Reset、Next、Prev、Start、Exit |
| 页面 | Login、Lobby、Room、Result、Settings、Records、Shop、Inventory、Profile |
| 列表 | Rooms、Players、Items、Rewards、Records、Rank、KillFeed |
| 战斗 | Hp、Ammo、Skill、Cooldown、Crosshair、KillFeed、Minimap、Timer、Score |
| 状态 | Normal、Hover、Pressed、Disabled、Selected、Loading、Empty、Error |

> 选词优先“业务语义”而不是“控件外观”：`Button_Start` 优于 `Button_Green`；`Txt_PlayerName` 优于 `Txt_Label2`。

## 6. 硬约束速查

- `BindName` 默认等于节点名；独立命名时以 `BindName` 为准，节点改名不牵动引用。
- `_` 私有件不得进契约/运行时索引。
- `Clone_` 条目不暴露；模板内部件不进契约 `controls[]`。
- 根名与文件名一致；`Tmp_` 只用于模板，业务页面去掉 `Tmp_`。
- 未登记前缀 = Warning；新前缀先登记词表。
- 默认名/中文/空格/点号 = Error。
- 同级重名/全页 BindName 重名 = Error。
