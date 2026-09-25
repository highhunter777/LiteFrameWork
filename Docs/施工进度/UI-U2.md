# UI-U2 产品闭环：施工进度

> 依据：[《UI框架总设计》](../design/client/ui/UI框架总设计.md) §4.3（单写者导航：队列上限/等待超时/可观测拒绝）、§6.2（模态栈/射线遮蔽/平台返回统一处理）、§9（本地化、文本与可访问性）、§13 U2（产品闭环）；[《动画模块专项设计》](../design/client/animation/动画模块专项设计.md) §14（UiFx 原语中断复位）。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

## 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| U2-① 导航协调者 | `UINavigationController`：单写者串行、队列上限、等待超时、排队期取消、可观测拒绝计数 | **已完成**（2026-09-25） |
| U2-② 模态栈 | `UIService` 模态登记/推导、最顶模态 Back、射线遮蔽 | **已完成**（2026-09-25） |
| U2-③ 真实消费者接线 | `ProcedureMain` 改走导航；`ProcedureBattle` 用模态作游戏输入门 | **已完成**（2026-09-25） |
| U2-④ UiFx 中断复位 | `Pulse`/`Flash`/`Slide` 中断即复位回基线 | **已完成**（2026-09-25） |
| U2-⑤a LText 核心 | `LocalizationCatalog` / `LocalizationService` / `LocalizationTable`：回退链、缺 key 策略、复数、转义、key 校验、覆盖率 | **已完成**（2026-09-25） |
| U2-⑤b 字体/SafeArea | 字体族与 fallback 链、缺字检查、SafeArea 细化 | **未开始** |
| U2-⑤c LTextLabel 与 Bridge | `Bridge.text.Get/Format`、LTextLabel（参数/语言刷新/可取消打字机） | **未开始** |
| U2-⑥ Dialog 结果/队列、Loading/Error 页 | 确认弹窗结果回传、加载/错误页 | **未开始** |
| U2-⑦ 焦点 | 手柄/键盘焦点导航 | **未开始** |

## 施工记录

### 2026-09-25 · 导航与模态交付（含一处真实缺陷修复）

**① 导航协调者**（`Assets/LiteGame/Runtime/Shell/UI/UINavigationController.cs`）

落在 `UIService` 之上的唯一导航入口：页面/流程的 Go/Back 一律经本类排队串行执行。
契约（§4.3）：

- **队列满在创建/入栈/OnShow 之前拒绝**——排队计数不含正在执行的操作；
- **等待超时可观测**：入队与出队两个安全调度点判定（首版不建定时器泵）；
- **已接受的操作必须完成、失败或取消**：排队期取消 = 标记放弃（不执行、不加载）；
- 时间源经 `Func<long> nowMs` 注入（测试用假时钟驱动超时，零真实等待）。

**② 模态栈**（`UIService`）

- 模态**不是独立记账**，从仍逻辑打开的全部页面推导（§6.2 遮盖同款纪律），按画布序取最顶；
- `TryGetBackTarget`：最顶模态优先，无模态时取最高非空层级组栈顶（§6.2 平台返回统一处理）；
- **射线遮蔽**：顶层模态打开期间，视觉上位于其下方的仍打开页面 `blocksRaycasts=false`——
  只写 `blocksRaycasts`，`interactable` 仍归转场锁/暂停的输入协调（U1-③ 职责分离不变）。

**③ 真实消费者接线**

- `ProcedureMain` 改走 `_nav.GoAsync`（产品入口不直调 `ShowAsync`）；
- `ProcedureBattle.IsUiBlocking` 取 `_ui.IsModalOpen`——模态打开 = 游戏意图全零（§6.2 输入协调者的游戏输入面）。

**④ UiFx 中断复位**（`UiFx.cs`）

`Pulse`/`Flash`/`Slide` 的中断收尾必须复位到**完成态即基线**：Pulse 回原透明度、
Flash 回**原色**（原实现回落固定色，属缺陷）、Slide 回原位。

#### ⑤ 排队期取消失效——真实缺陷与根因

**现象**：`导航_排队期取消_出队移除不执行` 稳定失败——`CancelledWhileQueued` 恒为 0，
被取消的排队项**仍会执行加载**。即 §4.3"排队期取消 = 不执行、不加载"**未兑现**。

**取证**（逐一排除）：登记非 default（`Equals(default)` 对已 Dispose 登记不可靠的怀疑已排除）；
代码路径全程无异常；转场策略/加载器变异四种组合全部复现；隔离复刻同样复现（非测试间污染）；
强制重编译后复现（非陈旧代码）。**回调顺序证实为 LIFO**（`after,before`）。

**根因**：`CancellationTokenSource` 取消回调按 **LIFO** 执行；`AttachExternalCancellation`
的登记**晚于** `Abandon`，故先执行。ATI 的续延同步跑完后，`WaitFormAsync` 的 `finally`
执行 `op.Registration.Dispose()`——**把 `Abandon` 的登记一并注销**，轮到它时已被移除，
回调被跳过。

证据：与 `Abandon` 同处注册、完全同形的对照 lambda **正常触发**，而先注册的那个连入口埋点
都不执行；停用等待侧 Dispose 后 `CancelledWhileQueued=1`、`Abandon` 正常进入。

**修复**：登记的**生命周期归 NavOp**，不再由等待侧注销——
`PumpAsync` 消费完该 op（放弃/超时/执行完）才 `Dispose`。
等待侧 `finally` 保留为空并加注释说明为何**刻意不**注销。

**这是一处会真实发生的缺陷**：任何"用户点了按钮又在加载期间反悔"的场景都会触发，
后果是**被取消的页面仍被创建**。

### 2026-09-25 · LText 核心（U2-⑤a）

《UI框架总设计》§9 文本链 `#text.xlsx → 生成 key/语言数据 → LText 服务 → LTextLabel/页面模型`。
设计标注"`Get/Format/Raw` 为目标接口，尚未实现"——本批即该实现。

**三件**（Core，纯规则零 Unity 依赖，L1 全覆盖）：

- `LocalizationCatalog`：key → (locale → 文本) 的不可变目录。查询**带回退信息**——
  `TryGet` 同时返回 `usedFallback`，缺 key 与"回退到源语言"可区分（§9 发布期回退源语言，
  但覆盖率统计必须能看出哪些是回退的）。
- `LocalizationService`（实现 `ILocalizationService`）：当前 locale、语言切换事件、
  缺 key 策略、复数选取、参数转义。
- `LocalizationTable`：表数据装载（经 `IJsonSerializer`——Core 只认接口，Newtonsoft 留 Unity 层，
  沿用既有纪律）+ **覆盖率诊断**（某 locale 相比源语言缺哪些 key）。

**关键契约落点**：

- **缺 key**（§9"开发期报告并显示 `[key]`，发布期先回退源语言，再显示可诊断占位"）：
  目标 locale → 源语言 → 占位；**两条路径都计数**——缺 key 不能因为"显示得像样"被忽略。
- **语言切换**（§9"刷新本地化组件…不重跑 OnShow、不重新订阅按钮、不重发业务请求"）：
  `SetLocale` 只切查询的 locale 并发事件，**不重建目录**；同值不触发事件。
- **参数转义**（§9"玩家/外部文本默认禁富文本"）：`Format` 的参数一律转义 `<`
  （TMP 标签的唯一入口）。**不改 `>`**——单独一个 `>` 不构成标签，改了反而让用户看到怪字符。
- **复数显式选取**（§9"英文 one/other 显式选取"）：key 约定 `.one`/`.other`，
  无 `.one` 时显式回退 `.other`（不猜）；`PluralRule` 只覆盖 zh-CN + en，
  并**公开标注"不承诺加列即可零代码支持"**（§9 原话）。
- **key 命名**（§9"`UI.<页面>.<语义>` / `Common.<语义>`"）：`LTextKey.Validate` 校验前缀白名单 +
  段字符集；表解析支持严格（候选校验，坏 key 整表拒绝）/宽松（运行时，跳过并计数）两档。

**未做**：字体族/fallback 链、缺字与图集容量检查（U2-⑤b）；`LTextLabel` 组件与
`Bridge.text.Get/Format`（U2-⑤c，需 Unity 侧）。**故本次交付的是"文本链的服务层"，
不是"UI 已能显示本地化文本"**。

## 测试面

- `Assets/Tests/EditMode/UiNavModalEditModeTests.cs`（7 例，全替身零真资源）：
  单写者串行、队列满在创建前拒绝、等待超时可观测、**排队期取消不执行**、
  最顶模态优先 Back、模态射线遮蔽与恢复、UiFx 中断复位=基线。
- `Tests/LiteFramework.Core.Tests/Localization/LocalizationTests.cs`（48 例，L1）：
  查询与回退链、缺 key 两档策略与计数、语言切换（事件/同值不触发/空值忽略）、
  参数替换与转义、越界与未闭合占位、复数（en one/other、zh 一律 other、无 one 回退 other、
  规则参数化）、key 命名正反例、表解析（正常/严格拒绝/宽松跳过/畸形）、覆盖率诊断。

## 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **761 通过 / 0 失败**（本批 +48 例） |
| L2 | `powershell -NoProfile -File scripts/l2-unity-gate.ps1` | **通过**——12040 个 .meta GUID 全合法；Unity 编译零错误；EditMode **180/180** |

## 已知边界

- **U2-⑤b/⑤c 与 ⑥⑦ 未交付**（字体与 SafeArea、LTextLabel/Bridge、Dialog 与 Loading/Error 页、焦点）——
  U2 退出条件"大厅、长列表、HUD、确认弹窗四个真实消费者闭环 + 中英/输入/异常用例通过"**远未达到**；
  已交付的是导航、模态、LText 服务层三个接缝；UI 侧消费（LTextLabel）与字体尚未接通。
- **无 PlayMode**：本批为 EditMode 全替身验证；真资源场景下的导航/模态行为未验证（L2 PlayMode lane 未建）。
- **队列上限/超时为候选配置**（8 / 10s），标注"目标设备与真实包验证后调整"——尚无实测依据。
