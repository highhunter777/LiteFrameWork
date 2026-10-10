# UI 命名词表与控件清单

> 状态：现行清单（2026-10-10，`Assets/UI/Widgets` 实测 27 件核对）
> 定位：《UI制作规范》§9 的**完整枚举面**——供开发速查、Inspector 预设与校验器消费；**规则本体以 §9 为权威**（判据/同文/私有件/模板层等），本清单只枚举与举例
> 维护：词表/控件增改先按 §9 §1 例外表登记，再同步本表；工具"词表数据文件化"以本表为内容源（[工具专项 §2](UI编辑器工具专项设计.md)）

## 1. 第一段总表（节点名前缀段）

### 1.1 角色词表（展示/结构）

| 前缀 | 类别 | 例 |
| --- | --- | --- |
| `Page_` | 页面根（= prefab 文件名） | `Page_Login` |
| `Cot_` | 容器/分组（含列表容器、SafeArea） | `Cot_TradePanel` |
| `Bg_` | 背景/底板 | `Bg_Shop` |
| `Txt_` | 文本 | `Txt_Title` |
| `Image_` | 图像 | `Image_Portrait` |
| `Icon_` | 图标 | `Icon_Coin` |
| `Clone_` | 克隆条目模板（非激活） | `Clone_Item` |

### 1.2 交互控件段（UGUI 全称；与同名模板段共用同一个词，无歧义）

| 段 | 控件 | 例 | 备注 |
| --- | --- | --- | --- |
| `Button_` | Button | `Button_Close` | 无 Button 模板，裸 UGUI |
| `Toggle_` | Toggle | `Toggle_Sound` | 一般经 `Tmp_Toggle` 模板/变体 |
| `Slider_` | Slider | `Slider_Volume` | 经 `Tmp_Slider` |
| `Dropdown_` | Dropdown | `Dropdown_Quality` | 经 `Tmp_Dropdown` |
| `InputField_` | InputField | `InputField_Name` | 经 `Tmp_InputField` |
| `ScrollRect_` | ScrollRect | `ScrollRect_Items` | 备用裸用；列表优先 `VirtualList_`/`SimpleList_` |
| `Scrollbar_` | Scrollbar | `Scrollbar_Vertical` | 备用裸用 |

### 1.3 控件模板段（全部 27 件；业务使用段 = 去 `Tmp_`）

模板本体按 `Tmp_<控件类型>`（特化 `Tmp_<控件类型>_<语义>`）命名，随模板批次整形；下表"使用段"应用于业务变体/页面实例（出处定段）。

| 现模板 | 目标名 | 使用段 | 用途/备注 | 使用例 |
| --- | --- | --- | --- | --- |
| Dialog | `Tmp_Dialog` | `Dialog_` | 标准弹窗（标题/正文/确定/取消） | `Dialog_Confirm` |
| Toast | `Tmp_Toast` | `Toast_` | 轻提示吐司（多条并存、上限淘汰） | `Toast_Warning` |
| Loading | `Tmp_Loading` | `Loading_` | 加载遮罩/指示 | `Loading_Block` |
| FlyText | `Tmp_FlyText` | `FlyText_` | 飘字（伤害/金币；池化） | `FlyText_Gold` |
| Bubble | `Tmp_Bubble` | `Bubble_` | 挂点旁短命气泡提示 | `Bubble_Tip` |
| GuideHighlight | `Tmp_GuideHighlight` | `GuideHighlight_` | 引导高亮框（对齐目标） | `GuideHighlight_First` |
| RedDot | `Tmp_RedDot` | `RedDot_` | 红点（含注册表） | `RedDot_Mail` |
| Countdown | `Tmp_Countdown` | `Countdown_` | 倒计时 | `Countdown_Start` |
| CountText | `Tmp_CountText` | `CountText_` | 计数文本（数字递增） | `CountText_Gold` |
| HpBar | `Tmp_HpBar` | `HpBar_` | 血条 | `HpBar_Player` |
| ProgressBar | `Tmp_ProgressBar` | `ProgressBar_` | 进度条 | `ProgressBar_Task` |
| AvatarFrame | `Tmp_AvatarFrame` | `AvatarFrame_` | 头像框（含等级角标） | `AvatarFrame_Player` |
| AnimatedImage | `Tmp_AnimatedImage` | `AnimatedImage_` | 帧序列动画图 | `AnimatedImage_Banner` |
| Toggle | `Tmp_Toggle` | `Toggle_` | 开关 | `Toggle_Sound` |
| Slider | `Tmp_Slider` | `Slider_` | 滑条 | `Slider_Volume` |
| Dropdown | `Tmp_Dropdown` | `Dropdown_` | 下拉选择 | `Dropdown_Quality` |
| InputField | `Tmp_InputField` | `InputField_` | 输入框 | `InputField_Name` |
| StateButton | `Tmp_StateButton` | `StateButton_` | 状态按钮（选中态） | `StateButton_Item` |
| Stepper | `Tmp_Stepper` | `Stepper_` | 步进器（±数量） | `Stepper_Count` |
| StarRating | `Tmp_StarRating` | `StarRating_` | 星级评分 | `StarRating_Level` |
| TabGroup | `Tmp_TabGroup` | `TabGroup_` | 页签组 | `TabGroup_Settings` |
| BottomNav | `Tmp_BottomNav` | `BottomNav_` | 底部导航 | `BottomNav_Main` |
| VirtualList | `Tmp_VirtualList` | `VirtualList_` | 虚拟列表（窗口复用；含 `Clone_` 条目模板） | `VirtualList_Rooms` |
| SimpleList | `Tmp_SimpleList` | `SimpleList_` | 简单列表（小列表；含 `Clone_` 条目模板） | `SimpleList_Players` |
| SafeArea | `Tmp_SafeArea` | （不占类型段） | 安全区适配容器——结构工具，使用名按容器角色 `Cot_SafeArea` | `Cot_SafeArea` |
| EventRelay | `Tmp_EventRelay` | （不占类型段） | 点击转发辅助（结构工具，随模板内部用） | — |
| Baseline | `Tmp_Baseline` | （不占类型段） | 基准件（BaselineA/B 基准页共用；结构工具） | — |

## 2. 选段指引（放什么 → 用什么段）

| 你要放的节点 | 用段 | 备注 |
| --- | --- | --- |
| 页面根 | `Page_` | 根名=文件名；注册进 tbuiform |
| 任意分组容器 | `Cot_` | 含滚动内容/安全区容器 |
| 背景/底板图 | `Bg_` | 全屏出血件 |
| 装饰文本/标签 | `Txt_` | 文案经 `SetTextKey`（另见总设计 §9） |
| 图像/立绘/通用图 | `Image_` | 无更具体角色时 |
| 小图标 | `Icon_` | — |
| 单击区域 | `Button_` | 来自模板则按出处（如 `StateButton_Item`） |
| 开关/滑条/下拉/输入 | `Toggle_`/`Slider_`/`Dropdown_`/`InputField_` | 经对应模板/变体 |
| 列表（含条目模板） | `VirtualList_`/`SimpleList_` | 条目模板 `Clone_<语义>` 在变体内 |
| 弹窗/吐司/加载/飘字等 | 对应模板段 | 系统面（Loading/Toast/错误弹窗）走 tbuiform 2xx，页面不内嵌 |
| 克隆条目模板 | `Clone_` | 非激活、子树不设 `BindName` |
| 模板内部实现件 | `_` | 不暴露、不被页面引用；页面禁用 `_` |

## 3. 保留形态速查

| 形态 | 用途 | 例 |
| --- | --- | --- |
| `Page_<语义>` | 页面根（= 文件名） | `Page_Login` |
| `Tmp_<控件类型>` / `Tmp_<控件类型>_<语义>` | 模板本体（资产） | `Tmp_Dialog` / `Tmp_Button_HeroLevelUp` |
| `<控件类型>_<语义>` | 模板的业务变体/使用（出处定段） | `Dialog_Trade` / `StateButton_Ok` |
| `<角色词>_<语义>` | 业务节点 | `Cot_TradePanel` / `Txt_Title` |
| `Clone_<语义>` | 克隆条目模板（非激活） | `Clone_RoomItem` |
| `_<名>` | 模板内部实现件（不暴露） | `_Viewport` / `_Bg` |

## 4. 语义词条（高频参考，20 词）

- 动作：`Close`、`Confirm`、`Cancel`、`Back`、`Submit`、`Save`、`Reset`、`Retry`、`Refresh`、`Buy`、`Equip`、`Use`
- 角色：`Title`、`Message`、`Name`、`Value`、`Count`、`Level`、`Time`、`Desc`

规则：同页面同语义同词（`Close` 不混 `Quit`/`Exit`）；词条重复出现即补录（§9 §1）。

## 5. 硬约束速查（摘录；权威见 §9）

- 前缀段只为三类：角色词 / 交互控件名 / 模板名——**全称**，互斥不别名（`Button_` 不写 `Btn_`）。
- 全名恰一个下划线；ASCII；编号 0 起连续升序（`Button_Tab0`）。
- `BindName` 与节点名同文；未暴露=不设；页面业务节点禁 `_`（`_` 仅模板内部件且不暴露）。
- 文案 key（`UI.<页面>.<语义>`/`Common.<语义>`，见总设计 §9）与节点名是**不同命名空间**，互不替代。
- 改名联动：`BindName`、Lua/C# 字符串引用、生成物同批更新（§9.2）。
