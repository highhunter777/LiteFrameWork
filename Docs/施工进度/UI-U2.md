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
| U2-⑤c LText 控件接入 | `UIBindIndex` 按 key 设文本 + 语言变更自动刷新；Lua 门面 `SetTextKey/SetTextKeyArgs/SetTextKeyPlural/UnbindTextKey` | **已完成**（2026-09-25） |
| U2-⑤d 打字机 | LTextLabel 可取消打字机（§9 末条） | **未开始** |
| U2-⑥a 弹窗形态裁决 | 用户 2026-09-25 裁决：弹窗分两类（独立 Canvas 类走服务 / 页面独有类页面自理）；新建页面级 `Screens/Dialog.prefab` | **已完成**（prefab 已探针验证可打开） |
| U2-⑥b Dialog 服务 | `DialogService`（结果/队列/优先级/互斥组/取消）+ `UIDialog` 等待式 + `UIService` 两接缝 | **已完成**（2026-09-25 接手交付，11/11；"Pending 之谜"已破案，见下） |
| U2-⑥c Loading/Error 页 | 加载/错误页统一入口 | **已完成**（2026-09-25：FeedbackService——Loading 嵌套阻断 + Back 首位拦截取消、Error/重试经 DialogService、Toast 计时面；空状态随大厅消费者落地）。**视觉已归位**：三面改走 System 层模板 form，见「反馈面归位」 |
| U2-⑦ 焦点 | 手柄/键盘焦点导航 | **未开始** |
| U2-⑧ per-form 缓存策略列（U1 余项，§5.2） | `UICacheStrategy`（Lru 默认/Resident 不淘汰/DestroyOnClose 关即销毁）的壳消费面 | **已完成**（2026-09-26，壳面 3 例；tbuiform 表列与投影接线未做，生产页面暂全默认 LRU） |
| U2-⑨ 导航 Replace + 包③覆盖度余部（§6.1/§4.3） | `UINavigationController.ReplaceAsync`（显式替换当前记录：关当前顶再开新页，目标即顶幂等）+ PlayMode 补段（导航 Go/Back/Replace 真资源段、转场输入锁、列表真滚动、DevReload 环境重建、循环计数、诊断关联） | **已完成**（2026-09-26，EditMode +2、PlayMode 10 例；修 UIService 回滚租约泄漏与 VirtualList.Offset 符号两处真缺陷，见记录） |

## 未完成事项

### Dialog 服务：谜团已破案，批次已完成（2026-09-25 接手批）

上一批"探针成功/正式用例 Pending"的隐藏变量已定位并消除，批次按同口径重新交付（11/11 绿）。

**谜底（两层叠加）**：

1. **就绪条件错位（主因）**：`UIService.ShowAsync` 的完成要经过转场 runner（`UITransitionRunner` 由
   `UIService.Tick` 帧末驱动），而 `UIForm` 在转场收尾**之前**就已 Active/IsOpen——
   以"表单已打开"为泵动退出条件会**过早退出**，此后命令面（如弹窗的结果等待）尚未挂上，
   后续断言/点击全部落空。探针类当时"立即 Succeeded"是因为其时序恰好落在同帧同步段。
   **修正**：就绪条件一律泵到**业务事实**（如 `TryGetActiveDialog`——对话框武装完成），不是表单 IsOpen。
2. **UniTask 编辑态无 PlayerLoop**：异步 EditMode 用例的等待必须显式泵
   （`for (...Pending...) { svc.Tick(...); }`，同 UiNavModalEditModeTests 已验证形态）——裸 await 永远 Pending。

**本批交付**：

- `DialogService.cs`（`Shell/UI/`）：`ShowAsync(Request)` → `DialogResult{Ok/Cancel/Closed}`；
  **互斥组**（组内串行，跨组并行）、**优先级**（降序出队、同级 FIFO）、**有界队列**（满则类型化
  `Rejected`）、**合并**（同 `MergeKey` 在途请求共享同一结果任务——"重复断线/错误弹窗不堆叠"）、
  **收口**（被外部关闭 → `Closed`）；实现 `ITickable`（容器注册即驱动）。
  **先关后交付**：结果任务完成时弹窗已离场、组已释放（结果交付在 `CloseAsync` 完成之后）。
- `UIDialog` 等待式：`WaitAsync(title,msg,okText,cancelText)` / `SettleExternally()`（幂等，
  外部收口后按钮回调清空）；按钮文案可配。
- `UIService` 接缝：`FormClosed` 事件（`CloseFormInternal` 单一漏斗——用户/系统/替换路径全通知；
  回调内禁再开关界面的重入约束已注明）。旧"TryGetOpenForm"方案不再需要——`ShowAsync` 返回的
  `UIForm.Root` 即实例入口。
- 装配：`ProcedureLaunch` 构造并注册 `DialogService`（Seal 前唯一受信装配点）。
- 测试：`DialogServiceEditModeTests`（11 例，全替身零真资源）——确认/取消、合并共享结果、
  互斥组串行+跨组并行、优先级+FIFO、队列满拒绝、外部关闭/Shutdown 收口、等待者取消只解除本人、
  UIDialog 等待式幂等、缺组件类型化失败。

**上一批已撤出代码的处置**：全部按新实现重写，未复用其代码；`Unity.TextMeshPro` 测试程序集引用
重新加入（TMP 文本断言需要）。

### 已确证发现（历史保留，对后续有价值）

1. **`UIService.ShowAsync` 会 `Instantiate` prefab**（`UIService.cs:195`）——
   调用方持有的 prefab 原件上的组件**不是**屏幕上那个；要操作实例控件必须经返回的
   `UIForm.Root` 查找（本批 DialogService 即此做法）。
2. **`Assets/UI/Widgets/` 下 24 个 prefab 全部无 Canvas**，只有 `Screens/UIMain.prefab` 有——
   把子控件 prefab 当页面打开会抛 `MissingComponentException: There is no 'Canvas'`。
   这是弹窗分类裁决的直接依据。

**方法论沉淀**：上一批 40+ 轮"改-编译-跑"未中的根因即"未先隔离最小可复现"；本批用独立探针
fixture 复现并二分（泵动条件一改即绿），一轮定位——教训成立，照此执行。


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

### 2026-09-25 · FeedbackService 统一反馈入口（U2-⑥c）

《UI框架总设计》§6.1"Loading、空状态、错误/重试、Toast 有统一入口"——本批交付服务层三面
（**代码创建内置视觉**，资产化归制作线替换同结构 prefab；空状态是页面内组件，随大厅消费者落地）：

- **Loading**（`BeginLoading` → `LoadingScope`）：嵌套计数阻断面，最后一个结束才解除；阻断期
  `LoadingToken` 供被门控操作绑定取消。**Back 链首位**：`UINavigationController` 增
  `BackInterceptor` 接缝（§6.1"Loading 阻断期间按操作取消规则处理返回，不能悄悄穿透到下层"——
  阻断期消费 Back = 取消当前操作，不进模态/页面关闭链；未阻断原样穿透）。
- **错误/重试**（`ShowErrorAsync` → bool 重试）：经 DialogService 落地——可重试双按钮
  （重试/退出）、不可重试单确认；同 MergeKey 重复错误自动合并；结果任务**先关后交付**。
- **Toast**（`ShowToast`）：非阻断短提示，Tick 计时到期消失，新提示替换旧提示（单面 + 重置计时）。

**接缝与装配**：`ProcedureLaunch` 构造注册（`BackInterceptor` 在 nav 上由服务自装/自卸）；
错误弹窗首版复用 Dialog 表行（专用 ErrorPage prefab 归制作线替换，服务只依赖 DialogService 契约）。
测试 7 例（`FeedbackServiceEditModeTests`）——嵌套计数、阻断期 Back 取消+不穿透、非阻断穿透、
错误重试/取消、错误合并、Toast 替换与到期。EditMode 就绪口径沿用 ⑥b 破案结论（泵业务事实，非 IsOpen）。

### 2026-09-25 · LText 控件接入（U2-⑤c）

把 LText 服务层接到 UI——**这是让本地化真正可用的那一步**。

- `UIBindIndex.SetTextKey(name, key, args)` / `SetTextKeyPlural(name, key, count, args)`：
  按 key 写文本，并**登记**该控件（语言变更时重写）。
- `BindLocale(ILocalizationService)`：注入语言服务并订阅变更；**重复注入先退订旧实例**
  （避免装配点重复调用留下多份订阅）。
- `UnbindTextKey(name)` / `UnbindAll()`：解除绑定与订阅——`UnbindAll` 是池化复用的安全垫，
  **旧页不得继续被语言事件刷新**。
- **未注入语言服务时 `SetTextKey` 抛**：静默显示原始 key 会让"忘了注入"变成
  "线上全是 key"的隐蔽故障。

**语言变更刷新面精确**（§9"语言变更刷新本地化组件…不重跑 OnShow、不重新订阅按钮、
不重发业务请求"）：只重写登记过 key 的控件；业务自己写的字面值控件不受影响——
测试专门钉住这一条。

**Lua 面**：`self.ui:SetTextKey / SetTextKeyArgs / SetTextKeyPlural / UnbindTextKey`
（Lua API 参考"本地化：`Bridge.text.Get/Format` 与 LTextLabel"的控件面）。
模板参数走**具名定长键** `arg0..arg3`——xLua 的 `LuaTable.Get` 对嵌套数组的映射语义
不由本项目控制，猜错会静默取到空值；具名键语义确定，且与既有 payload 协议（全具名）一致。

**未做**：打字机（§9 末条"LTextLabel 打字机为表现行为，关闭/语言变化取消旧任务"）——
需与动画/时钟接缝一起做，属 U2-⑤d。

### 2026-09-25 · 反馈面归位（视觉单一来源裁决 + ⑥c 返工）

用户裁决：**所有 UI 不准自建视觉**。本批把 ⑥c 的自建反馈面按新纪律返工，并把规则写入设计文档。

**裁决与规则（设计文档）**：`UI框架总设计` §7 视觉单一来源 + 引导期错误界面**唯一例外**三条判据、
§6.2 反馈面归 System 语义层（废止私有 `sortingOrder=30000`）、§6.1 统一入口=语义统一 +
**Toast 多条并存**契约、§1.1 裁决表两行；`UI制作规范` §2 运行时禁建视觉 + §8 反例扫描进静态检查；
`待办总览` 新增「UI 视觉单一来源」行。

**扫出的存量问题（本次一并修）**：

1. **第二条 Toast 实现**：`Widgets/Toast.prefab` 与 `Toast.cs` 早已存在且是模板 25 件之一，
   `UIBindIndex.ShowToast`/Lua 面在用——⑥c 另建了代码版 Toast。**属复发**（`UIDemoPage` 已走过
   「代码构建 → 模板 prefab 实例化」同一改造）。
2. **`FeedbackService` 默认 `errorFormId = 201` 在生产必炸**：`#uiform` 表原本只有 1 行（id=1），
   `UIFormCatalog.Get(201)` 直接抛 `KeyNotFoundException`；而 `DialogServiceEditModeTests` 用
   `FakeCatalog` 自 Add 201，**替身掩盖了真表缺陷**（11/11 绿测的是假表）。
3. **构建器 ↔ prefab 长期不同步**：HEAD 的 `WidgetPrefabBuilder` 已硬编码 `0.25,0.45,0.75`，
   而提交的 `StateButton.prefab` 是 `UiStyle.Accent` 的 `0.3,0.7,0.95`——"确定性生成"已不成立。
   经裁决**以生成器为准**，19 件模板按构建器重建（含外观/结构差异）。
4. **`UIForm.cs` 隐藏兜底**：prefab 缺 Canvas/CanvasGroup 时 `AddComponent` 静默补齐，把装配错误
   藏到表现层——改**缺失即 fail-fast**（制作规范 §2 本就要求页面根自带）。
5. **buildHash 闭包过宽**：`DATA_TARGETS` 把 UI 表单也算进联机版本哈希——加一行反馈面即令
   **全员拒绝进房**。同 `SKIP_DIRS` 已记录的坑（"给菜单加一行注释 → 全员拒绝进房"）的另一个入口；
   按同一判据排除 tbuiform（生成器与 C# 守卫同步）。

**本批交付**：

- `UIService`：`GroupNames` 增 `System`（第 4 语义层，tbuiform.layer=3）；`ResolveLogic` **空 LuaPath
  显式降级不打 Error**（否则纯展示面凭空踩 UI 门禁"Console 新增 Error 即判失败"）。
- `FeedbackService` 重写：拆掉自建 root/Canvas/Image/Text 与 `OverlaySortingOrder`；三面（Loading
  遮罩 / 错误弹窗 / Toast 承载）均走 `UIService` 打开的 **System 层 form**，只持实例驱动状态；
  构造函数开 `open/close/isOpen` 注入口（测试可脱离真表）。
- `LoadingMask`（新控件）+ `Widgets/Loading.prefab`（新模板，构建器确定性生成）。
- `Toast` 改造：**多条并存**（各自计时独立消失，不替换不重置他人）、纵向堆叠（承载容器排布）、
  数量上限 + 最旧淘汰计数；计时从 `UniTask.Delay` 改 **`Tick` 可泵**（EditMode 无 PlayerLoop，
  旧实现下用例会永久挂起）；`ToastTicker : ITickable` 接 `UiAnimationClock.Delta` 经 UIClock 驱动。
- `#uiform` 表新增 3 行（201 错误弹窗 / 202 Loading / 203 Toast，layer=3，lua_path 空）并重生成。
- **可执行规则载体**：`VisualConstructionScanner`（纯函数扫描器）+ `VisualSingleSourceEditModeTests`
  （5 合成的正/负例 + 真实代码面扫描）——规则不再只写在文档里。

**验证**：L1 **761 通过**；L2 EditMode **217/217**（含新增 17 例）；Unity 编译零错误；
`WidgetPrefabCheck` 新增 4 条断言（Loading 阻断面接线、Toast 多条并存/超限淘汰）全 PASS。

**教训（方法论）**：本批两次踩到同一根因——**替身让真链路缺陷隐形**（FakeCatalog 掩盖表缺行、
既有 11/11 全绿测的是假表）；以及**在未提交工作树里做"还原验证"会覆盖掉自己的改动**
（用备份复原 tbuiform 模拟旧输入时，把三行改动一起还原了，需重新 gen 恢复）。

### 2026-09-26 · per-form 缓存策略列（U2-⑧，U1 余项收口）

U1 交付段预告"per-form 缓存策略列随 U2 表扩展"——本批把 §5.2"缓存策略为 Resident、LRU、DestroyOnClose"的**壳消费面**落地（代码 `e5fd46c`）：

- `UICacheStrategy` 枚举（`UIFormInfo.cs`）：`Lru`（默认——关闭入池，超预算按最近使用序淘汰）/ `Resident`（关闭入池但**不参与淘汰**，主界面/高频页，预算满也保留）/ `DestroyOnClose`（关即销毁——不进池，释放租约与 GameObject，低频/重资源页）。
- `UIService.CloseFormInternal`：DestroyOnClose 分流——不走回收池，直接完整销毁后广播 `FormClosed`（对齐 §4.4 离场语义）。
- `UIService.EvictBeyondBudget`：淘汰候选**排除 Resident**——Resident 仍计入 cached 总数（"Resident 也必须计入总预算"口径不变），超预算时只从 LRU 策略实例中按最近使用序淘汰最旧者。
- 测试：`UICacheStrategyEditModeTests`（3 例）——DestroyOnClose 关即销毁且再 Show 为全新实例、Resident 不参与 LRU 淘汰、超预算淘汰最旧而 Resident 幸存。`ReleaseLayoutEditModeTests` 同批整理注释与分类标注（零行为变化）。

**边界（表列配置接入未做）**：tbuiform 表未扩展该列、`UIFormCatalog` 投影未接线——`UIFormInfo.CacheStrategy` 当前恒为默认 `Lru`，生产页面全部按默认 LRU；`Resident`/`DestroyOnClose` 仅可编程构造（测试即此形态）。列加进表后投影才能读（"壳不碰 Luban 类型、投影在装配层"纪律不变）；主界面声明 Resident、低频页声明 DestroyOnClose 的实际配置随表列扩展批落地。

### 2026-09-26 · 导航 Replace + 包③覆盖度余部（U2-⑨）

《框架先行》包③"仍缺"清单的四段（导航 Go/Back/Replace 真资源段、列表段、DevReload 环境重建段、输入锁恢复）+ 三块准入项证据（循环计数/诊断关联/可扩展接入示例）的 PlayMode 收口。接手并行会话的进行中 WIP 并修绿 4 例失败（用户裁决收口）：

- **`UINavigationController.ReplaceAsync`**（§6.1"显式替换当前记录"）：NavKind 增 Replace——关当前顶页（`CloseReason.Replace` 离场原因）再开新页；目标即当前顶时幂等（不先关后开）；排队/超时/取消语义与 Go 同管线。EditMode +2（替换语义/幂等语义，`UiNavModalEditModeTests`）。
- **PlayMode 10 例**（三组，真资源真转场）：
  - `NavigationPlayModeTests`（4）：Go 前进 Back 返回（跨层覆盖栈——下层保留/复用同实例/重开不加租约）、Replace 替换当前记录（旧页关/底层留/Back 回底）、Replace 幂等、转场输入锁（接受即锁 interactable、blocksRaycasts 全程遮挡、完成按协调者恢复——ObservingTransition 在策略首帧取观测点，窗口确定性）。
  - `ListPlayModeTests`（3）：真 VirtualList（构建器新 ListScreen 页面嵌套模板）500 条滚动首尾往返（节点恒于容量、FirstIndex 前进回退、索引 0 重绑）、重复刷新不重绑不增节点、缩容 500→1→0。
  - `EnvRebuildPlayModeTests`（3）：复刻 §10.2 DevReload 顺序（全关→DropAllLogic→env.Dispose→新 env→复用缓存实例接管新逻辑、旧 env 零访问）、同页开关 10 次（租约恰一份、OnShow 逐次递增、Shutdown 对称释放）、注入 OnInit 失败（类型化异常含 formId/阶段、回滚对称、日志经 LiteFramework.Log 面可读）。
- **两处产品真缺陷修复**：
  1. **UIService 回滚租约泄漏**（§4.4 所有权清理）：`RunOpenAsync` 的 InitFailed 与权威取消两条回滚路径只从 `_leases` 字典摘除、不调 `lease.Release()`——"获取后初始化失败"路径泄漏（此前在途取消用例测的是"未获取就取消"0=0 假对称，未覆盖此路径）。两处均补就地释放。
  2. **VirtualList.Offset() 符号假设**（§8.2）：原 `-anchoredPosition.y` 只匹配手动驱动的负值形态；真 ScrollRect 在当前 Content（top-anchor/pivot）结构下以 **+y** 表示滚过距离（PlayMode 实测 normalizedPosition=0 时 y=+27592）——改 `Mathf.Abs`（窗口计算只消费距离不消费符号，EditMode 替身 -y 与真 ScrollRect +y 等价兼容）。
- **测试侧三处修正**（接手时的 4 例红中）：导航组 Layer 配置（前进页须与底页**跨层**，同层全屏触发组内 Replace 契约——U0 交付语义，非缺陷）；BackAsync 异步任务须等终态；复用租约断言改"重开前后 Acquired 不变"（两页各一份租约，基线 2 非线性 1）；列表首行断言改 `ZeroIndexText`（绑定循环按索引升序，"最后绑定"落在最高索引）；诊断断言通道从 LogAssert 改 LiteFramework.Log 面（helper 未注入时不桥接 Unity 日志——LogAssert 恒等不到，EditMode 先例即 ErrorCount/Recent 口径）。

**「可扩展」接入示例**：ListScreen 页面新增只动构建器（`BuildListScreens` 菜单，确定性生成）与测试目录条目——UIService/导航/控件框架零改动，即包③准入项"新增样例页面走既定扩展点"的载体。

**验证**：L2 EditMode **227/227** + PlayMode **22/22**（全绿）；L1 778 沿用（本批未动 dotnet 侧）。

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
| L2 | `powershell -NoProfile -File scripts/l2-unity-gate.ps1` | **通过**——12042 个 .meta GUID 全合法；Unity 编译零错误；EditMode **189/189**（含新增 `Screens/Dialog.prefab`） |
| L2（反馈面归位批） | `unity command run_tests --mode editor` | EditMode **217/217**（+10 FeedbackService、+7 视觉单一来源）；Unity 编译零错误 |
| L1（反馈面归位批） | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **761 通过 / 0 失败**（含 buildHash 闭包调整后的复算守卫） |
| L2（缓存策略批 U2-⑧，2026-09-26） | `unity command run_tests --mode editor` / `--mode playmode` | EditMode **225/225**（装配批 222 + 本批 3）；PlayMode **12/12**；Unity 编译零错误。L1 778（本批未动 dotnet 侧，沿用两次一致实测） |

## 已知边界

- **U2-⑤b/⑤d 与 ⑦ 未交付**（字体与 SafeArea、打字机、焦点；Dialog/Loading/Error 反馈面 ⑥a/b/c 与缓存策略列 ⑧ 已交付）。
  U2 退出条件"大厅、长列表、HUD、确认弹窗四个真实消费者闭环 + 中英/输入/异常用例通过"**远未达到**；
  已交付的是导航、模态、LText 服务层与控件接入四个接缝；字体族与打字机尚未接通。
- ~~**无 PlayMode**：本批为 EditMode 全替身验证；真资源场景下的导航/模态行为未验证（L2 PlayMode lane 未建）。~~
  **已于 2026-09-25 消解**：`Assets/Tests/UI/PlayMode` 建成并接入同一门禁；**反馈面归位批的 Loading/Toast/错误弹窗已补真 prefab + 真转场 PlayMode 用例**（3 例），
  另覆盖真 Lua 页面生命周期、暂停/覆盖状态语义、动画资源组合。见 [框架先行记录](框架先行.md)。
  **仍未在 PlayMode 覆盖**：本批的导航（Go/Back/Replace）与列表滚动、DevReload 环境重建段。
- **队列上限/超时为候选配置**（8 / 10s），标注"目标设备与真实包验证后调整"——尚无实测依据。
- **Toast 无淡入淡出**：多条并存的堆叠与到期是硬切（`SetActive`），表现层过渡随制作线。
- **19 件模板按构建器重建**：以生成器为准的裁决已执行，但差异（外观/结构）未经视觉评审；
  `WidgetPrefabCheck` 只覆盖可代码驱动的行为面，观感差异需人工过目。
