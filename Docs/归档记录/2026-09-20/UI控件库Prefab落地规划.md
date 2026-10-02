# UI 控件库 Prefab 模板落地规划

> 定案 2026-09-14（用户）：**UI 控件 prefab 模板要落地**。本规划补齐 M4c 遗留——控件库有 C# 实现，**prefab 资产为 0**（现状核对：23 个 C# 件、0 个 .prefab、Demo 页为代码构建）。
> 关系：清单沿用《M4实施指导》§2.10（20 余件）；绑定约定沿用《自研框架设计方案》§4.6；本规划是《编辑器设计规划》UI 编辑器两阶段的素材与验收对象。

---

## 1. 现状核对（2026-09-14）

| 项 | 状态 |
| --- | --- |
| 控件 C# 实现（`Runtime/Shell/UI/Widgets/`） | ✅ 23 件（UIWidget 基座 / VirtualList / StateButton / TabGroup / Dialog / Toast / Progress / HpBar / StarRating / Stepper / RedDot / SafeAreaReceiver / FlyText / UIDemoPage …） |
| Play 自检 | ✅ 5/5 PASS（UIDataBinder 去重 / StarRating 钳制 / Stepper 钳制 / RedDot 树传播 / Countdown 回零） |
| **prefab 模板** | ❌ **0 个**——全部代码构建 |
| Demo 页 | ⚠️ 代码构建（`UIDemoPage.cs` + `WidgetDemoMenu.cs`），非 prefab 实例 |

§2.10 验收原文要求"**控件自带 prefab 模板** + 每件最小 Play 断言 + 一页 Demo 场景全展（灰盒）"——前两项是本规划的交付缺口。

## 2. 目标形态

**目录约定**：
```
Assets/LiteGame/UI/
├── Widgets/                    # 每控件一个模板 prefab
│   ├── StateButton.prefab
│   ├── Dialog.prefab
│   ├── VirtualList.prefab
│   └── ...（对齐 §2.10 清单）
├── Atlas/                      # 灰盒图集 + 九宫格切片（占位图）
└── Demo/
    └── WidgetsDemo.prefab      # Demo 页（全展各控件，灰盒）
```

**每模板的结构与标记**（绑定双路径 A 的落点）：
- 根节点：挂 `UIWidget` 子类 + `BindRoot`（类名 / 命名空间 / 输出目录）
- 需要暴露的子控件：挂 `BindNode` 标记（名字 = 绑定名，PascalCase；类型 = 子控件组件类型）→ 索引与绑定类**一键生成**（LiteCodeGen §4.6，零手工查找）

**层级与命名约定**：
- 结构：`Root(UIWidget) → Bg / Content / Interaction`；三层缺省，特殊控件可加层
- 节点名 = 绑定名（PascalCase，如 `IconImage` / `CountText`）；非暴露节点前缀 `_`（如 `_Decor`）
- 锚点：模板按"内容自适应 + 锚点居中"设计，实例化方设尺寸；`SafeAreaReceiver` 纳入适配层统一维护

**灰盒美术标准**：
- 统一色板（背景/主色/警告/禁用四色）、九宫格边界切片、单一占位字体——**色板已于 2026-09-17 token 化落地**：`Editor/Style/UiStyle.cs`（12 token：Bg/BgDeep/ItemBg 三级底 + Primary/Accent/Warn/Success 语义色 + Text/TextDim/TextBright 文字 + Raycast/Mark 引导件专用）+ 批量工具 `UiStyleTool`（菜单 `LiteGame/UI/样式工具/`——刷新按 token 收敛/对账报漂移）。**定案：仅编辑器工具语义**——颜色归手作 prefab 序列化数据，工具不进运行时、不挂组件；改 token 值后重跑"刷新"即向存量传播（最近 token 匹配 + 1e-4 阈值，幂等可重入）；手调新色由"对账"报告暴露不擅改。2026-09-17 首轮执行：22 模板 / 68 处离散变体收敛，零漂移基线成立（详见 §9）
- **禁止业务文案与图标**：文案走表/Lua（`Bridge.ui` 受控 API），占位图仅形态示意

**变体策略**：同族用 prefab variant 派生（如 `Dialog` 的确认/警告/输入三 variant；`StateButton` 的多态视觉用子资源状态图，不做多 prefab）

## 3. 落地步骤（六批）

| 批 | 内容 | 验收 |
| --- | --- | --- |
| ① 约定定案 | 目录、命名、层级、灰盒色板与图集、标记规范写入本文档附录 | 约定评审通过（本文件补充） |
| ② 样板 4 件 | `StateButton` / `Dialog` / `VirtualList` / `HpBar`——覆盖交互/弹窗/列表/展示四类 | 四件模板 + 标记完整；索引生成成功；Play 断言通过 |
| ③ 余件补齐 | 对齐 §2.10 清单（TabGroup / BottomNav / Toast / Bubble / ProgressBar 环形直线 / StarRating / CountText / Countdown / AnimatedImage / AvatarFrame / InputField / Slider / Toggle / Dropdown / Stepper / RedDot / FlyText / 引导高亮位 / PageView / SafeAreaReceiver） | 清单对账无缺项 |
| ④ 断言迁移 | 现有 UIDemoPage 断言 → **prefab 实例化断言**（加载模板 → 驱动 → 断言），每件至少一条 | 断言全绿（Play） |
| ⑤ Demo 页改造 | `UIDemoPage.cs` 代码构建 → `WidgetsDemo.prefab` 实例 + 断言；场景/prefab 入库 | Demo 页全展为 prefab 实例，非代码构建 |
| ⑥ 用法表 | 控件 → Lua 受控 API 对照表（内容作者可见） | 表与代码一致（抽查） |

## 4. 与编辑器/绑定工具的关系

- **绑定工具（已有）**：模板的 `BindRoot`/`BindNode` 标记 → 索引/绑定类生成——本规划是它的第一批真实消费者
- **UI 编辑器阶段一（标记工具）**：为模板提供可视化编辑与批量校验（见《编辑器设计规划》§2.1）——模板落地过程即是标记工具的验收场景
- **UI 编辑器阶段二（编排器）**：以本模板库为拖拽素材（见《编辑器设计规划》§2.2）

## 5. 纪律

- 模板是**壳机制层的资产**：Lua 不碰内部实现，只经受控 API 驱动（§1.3 归属纪律）
- 模板**不引业务依赖**（不挂业务脚本、不含业务资源引用——地址由表/代码给）
- 模板**不含逻辑**：交互行为由控件 C# 类实现，模板只提供结构、样式与标记
- prefab 变更后**索引/绑定类必须重新生成**（标记工具批量校验纳入 CI 前置/自检清单）

## 6. 验收（本规划完成判定）

1. §2.10 清单控件**每件一个 prefab 模板**，标记完整、索引可一键生成
2. 每件至少一条 **Play 断言**（prefab 实例化路径）全绿
3. Demo 页全展（**prefab 实例**，不再代码构建）
4. 无业务文案/图标硬编码在模板内
5. 控件 → Lua 受控 API 对照表交付

## 7. 工作量与排期

- **工作量**：手工搭建 20 余件模板（主要成本，灰盒）+ 断言/Demo 改造约 300 行代码
- **排期**：随 M4 遗留清偿 + UI 编辑器阶段一（在 M5 纵向切片与 M11 demo 之间择机；联机 UI 清单（《联机Demo设计》§4）是本规划的消费者之一）
- **依赖**：不阻塞 M5-M10；但 M11 demo 的 UI 验收要求模板就绪

---

## 8. 实施状态与落地实证（2026-09-14）

### 8.1 新纪律：**一类一文件（文件名 = 类名）**——Unity 序列化硬约束

**实测定案**：**文件中非首个 MonoBehaviour 无法序列化进 prefab**（保存后组件变 `<null>` 丢失）。判定实验：

| 类 | 所在文件位置 | prefab 存活 |
| --- | --- | --- |
| `ProgressBar` | Progress.cs **首类** | ✅ 存活 |
| `HpBar` | Progress.cs 第二类 | ❌ 丢失 |
| `Toast` | Dialogs.cs 第二类 | ❌ 丢失 |
| `BottomNav` | TabGroup.cs 第二类 | ❌ 丢失 |
| `StarRating` | Display.cs 首类 | ✅ 存活 |

→ **交付形态改了**：M4c 的"多类单文件"组织（Dialogs/Display/Input/Progress/FlyText/TabGroup 等）已按一类一文件拆分（27 个 MonoBehaviour 全部为各自文件的首类）；文件名同时对齐类名。
**纪律**：新增 MonoBehaviour 必须独占文件且文件名 = 类名；纯类/接口/枚举可与同类共存（但不得排在 MonoBehaviour 之前）。

**事故与恢复记录（如实存档）**：机械拆分脚本因"新文件名与源文件名相同"覆盖了三个源文件（`UIWidget` / `RedDot` / `SafeAreaReceiver` 的类体丢失）。恢复方式：**旧程序集仍在内存**（编译未刷新）→ 反射 dump 三个类的完整成员签名（字段名/方法签名/可见性逐一对齐）→ 照签名重建源码 → 编译零错误 + 类清单与拆分前一致（27 个）+ 序列化探针 6/6 存活。教训：**批量改写源文件前先备份/先算文件名冲突**（工程无 git，删除不可恢复）。

### 8.2 批①②③④ 当前进度

| 批 | 内容 | 状态 |
| --- | --- | --- |
| ① 约定定案 | 目录 `Assets/LiteGame/UI/Widgets/`、三层结构 Root→Bg/Content/Interaction、灰盒色板、命名规范 | ✅ 落在构建器里（确定性生成，可重复执行） |
| ② 样板 4 件 | `StateButton` / `Dialog`(UIDialog) / `VirtualList` / `HpBar` | ✅ 已生成 + 自检 PASS |
| ③ 余件补齐 | **25 件全部生成**（见下清单）——每件 15-25 行构建代码 + 内部接线（Fill/Template/Button/graphic 等） | ✅ **完成** |
| ④ 断言 | **25 件各有最小断言，编辑态与 Play 态均 25/25 PASS**（菜单 `LiteGame/UI/校验控件模板`） | ✅ 完成（UIDemoPage 旧的代码构建断言仍保留，待批⑤一并迁移） |
| ⑤ Demo 页改造 | `UIDemoPage` 代码构建 → **模板 prefab 实例**（3 列网格铺 25 件 + 少量"看得见状态"驱动；模板件断言已迁 `WidgetPrefabCheck`，页面只留模板无关的两条） | ✅ **完成（Play 实测：模板实例化 25/25、PASS 2/2、零错误）** |
| ⑥ 用法表 | 控件 → Lua 受控 API 对照表 | ⏳ 待做 |

**模板清单（25 件）**：StateButton / Dialog / VirtualList / HpBar / Toast / Bubble / FlyText / RedDot / TabGroup / BottomNav / ProgressBar / StarRating / CountText / Countdown / AnimatedImage / AvatarFrame / Stepper / InputField / Slider / Toggle / Dropdown / EventRelay / GuideHighlight / SafeArea / SimpleList

**工具（已交付）**：
- 菜单 `LiteGame/UI/构建控件模板 Prefabs` → 确定性重建全部模板（可重复执行，产物入库）
- 菜单 `LiteGame/UI/校验控件模板` → 模板加载 + 实例化 + 驱动 + 断言（日志 tag `WidgetTemplateCheck`）
- 日志出口说明：Editor 工具在**编辑态**用 `Debug.Log`（`LiteFramework.Log` 的 Unity 实现在启动期安装，编辑态无输出）

**两条实证备忘（避免将来重踩）**：
1. **编辑态 `PrefabUtility.InstantiatePrefab` 不触发 `Awake`** → 依赖 Awake 内部接线的行为断言（如 `TabGroup` 的点击切页）**必须跑在 Play 态**；断言写法 = 编辑态只查结构、Play 态加行为分支（`if (!Application.isPlaying) return true;`）
2. **模板根的空名 BindNode 只是"可暴露位"提示**：界面作者实例化后自行命名；**切勿**给模板子节点预设 BindName（多实例重名 → 界面级索引直接抛）

### 8.3 模板约定的两处细化（与 §2 原文的差异，落地实证）

1. **模板不预设 `BindName`**：多个实例同名会让界面级索引（`BindIndexBuilder`，重名即抛）直接爆炸。改为——模板根挂一个**空名 BindNode** 作为"可暴露位"提示，**命名归界面作者**；控件内部接线全部落在序列化引用上（Fill/Template/Button 等模板已接好）。
2. **模板不放 `BindRoot`**：`BindRoot` 是界面级生成物；放模板上会默认以控件名生成类（与运行时控件类**同名冲突**），且模板级绑定类无消费者。

### 8.4 待办清单（本规划收口前必须完成）

- [x] 批③ 余件模板生成（25 件全部就位）
- [x] 批④ 每件最小断言（编辑态 + Play 态 25/25 PASS）
- [x] 批⑤ Demo 页改 prefab 实例（Play 实测 25/25 实例化 + 2/2 断言；`UIDemoPage` 加 `#if UNITY_EDITOR` 加载，**真机分支已由《UI资源热更缺口收口》补齐**——`LoadTemplate` 改经 `AssetService` 加载，编辑器保留 AssetDatabase 快路径）
- [x] 批⑥ 控件 → Lua 受控 API 用法表（→ **《UI控件Lua用法表.md》**：现有 `self.ui` 13 方法 + 全局表 `Bridge.data/ui/content` 全清单 + 25 件对照 + **缺口清单（G1-G21）+ 优先级**）
- [x] **批⑦ 受控 API 扩展（P0）**：G1 修正（`SetInteractable` 回退 `UIWidget.Interactable`）+ 新增 8 条（`SetProgress`/`SetProgressRange`/`SetHp`/`StartCountdown`/`StopCountdown`/`ShowToast`/`ShowBubble`/`ShowFlyText`）——验证：模板自检 31/31 PASS + Lua shim 实测 5/5 + `Dispatch` 真实路径 4/4；**顺带修 UIBubble 销毁期 `MissingReferenceException`**
- [ ] 批⑧ 剩余缺口（P1/P2：星级/滚动数值/步进/输入三件/红点/列表协议/切页/动图/头像/点击区/引导/动效口/Lua 绑定区）——清单见《UI控件Lua用法表》§3
- [x] **YooAsset 收集组**（**已完成，2026-09-17，见《UI资源热更缺口收口》§2**）：`LiteGameUI`（`Assets/LiteGame/UI/Screens`，`PackSeparately`，tag `ui`）+ `LiteGameWidgets`（`Assets/LiteGame/UI/Widgets`，`PackDirectory`，tag `ui`）；编辑器态采集 43 件 / UI 目录 26 件全带 tag，运行时 26/26 可加载 → PASS
- [ ] 界面级命名约定文档（实例化后如何命名子控件，避免重名——见 §8.3-1 与 §8.2 备忘 2）

---

## 9. 样式 token 与批量工具（2026-09-17 落地）

**定案背景**：模板维护路径改为**手作 prefab**（构建器降级为历史参考——2026-09-17 用户定案）；颜色归 prefab 序列化数据，样式系统取"**仅编辑器工具**"语义（不进运行时、不挂组件、无覆盖冲突）。

**交付件**（`Assets/LiteGame/Scripts/Editor/Style/`）：
- `UiStyle.cs`：12 token 单源（三级底/语义色/文字/引导件专用；值来自构建器 39 处颜色的意图归纳）
- `UiStyleTool.cs`：菜单 `LiteGame/UI/样式工具/`——**刷新**（最近 token 匹配 + 1e-4 阈值收敛，幂等可重入；改 token 值后重跑即向存量传播）与**对账**（漂移报告：手调新色只报不改）

**首轮执行记录**：22 模板 / 68 处离散变体收敛（同意图色值漂移消除）；剩余 6 处纯白补录为 `TextBright` token 后，**零漂移基线成立**（对账通过 + 刷新 0 处幂等）。

**触发性留档（YAGNI）**：运行时主题/SO 换肤、role 标记组件、编排器样式面板——留待《编辑器设计规划》阶段二编排器一并；触发条件 = 第二次出现"要批量改一批颜色"的实际需求。

> **2026-09-19 触发条件成立**：用户定案「升级为样式编辑器，收纳文本样式编辑」→ 设计与批次见 **《样式编辑器设计》**（颜色轴不动，新增文本档位轴 + 标记组件 + 校验纪律 + Odin 窗口）。本文 §9 的 YAGNI 留档据此转入实施（完成后再回填状态）。
