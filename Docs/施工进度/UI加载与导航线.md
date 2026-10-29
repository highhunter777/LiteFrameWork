# UI 加载与导航线（加载页装饰 · 遮罩收口 · 场景组加载链 · 退出卸载）

> **旧体系冻结（2026-10-22）**：本卷记录旧 UI 体系的施工历史，不再对应现行契约；新体系设计见 [UI 系统总设计](../设计文档/客户端/UI系统/UI系统总设计.md)。保留原文作为历史证据，不回填新状态。


> **定位**：加载面与场景生命周期：加载页转圈装饰、加载遮罩孤儿面、对局场景组加载时点、退出卸载路径。

> **章法**：一卷一条线；每章＝原一份批次记录（标题、正文、证据原样并入，仅降一级标题）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。
> 每章开头的引用块是该批的原始状态与证据；本卷只做归并与章节化，未改写任何批次结论。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | 加载页转圈装饰接线（转圈资源查找 + 落地） | `加载页转圈装饰接线.md` |
| 2 | 加载遮罩孤儿面收口（进房快路径卡加载页根修） | `加载遮罩孤儿面收口.md` |
| 3 | 对局场景组加载链修订（进房 → 开局） | `对局场景组加载链修订.md` |
| 4 | 退出卸载修复（宿主关闭路径不再引擎侧卸场景） | `退出卸载修复.md` |
| 5 | 运行阻断修复批（并行回归处置） | `运行阻断修复批.md` |
| 6 | 进房加载面 Back 取消接线（本地服加载页卡住检查） | — |

---

## 第1章 加载页转圈装饰接线（转圈资源查找 + 落地）

<a id="ch1-加载页转圈装饰接线"></a>

> **原文件**：`Docs/施工进度/加载页转圈装饰接线.md`（归并前独立成文，内容整段并入本章）

用户点单（2026-10-17）：加载页的 `AnimatedImage_Spin` 是空帧静态占位 → "**找一下项目有没有转圈装饰资源，用其他形式也是可以的**"。

### 状态

**已完成（附验证证据）**——2026-10-17（已提交 `07a77cb1`）。

### 资源查找结论

| 来源 | 内容 | 判用 |
| --- | --- | --- |
| **`Assets/Synty/InterfaceMilitaryCombatHUD/Sprites/HUD/`**（现役 UI 图源包） | **`LoadingIcon_01` 转圈图标族**：`Ring_Clean`（六边框）/`Arrows_Clean`（内箭）/`Triangles_Clean`/`Full_Clean`（合成）+ 各自 `Underlay` 阴影版 | ✅ **采用**（专为加载转圈画的一族） |
| 同包其他件 | `Reticle_Arc_Medium_01..03`（折角）、`Reticle_Ring_*`/`Ring_*_Clean`（对称环，转起来看不出动）、`Dial_Medium_01_Clean` | ✗（形不成转圈读感） |
| `Assets/Art/Emerald Treasure/**` | `progress_bar_circle_*`（环形进度）、`Progress Bar (Circle).prefab` | ✗（另一套画风的付费 UI 包；与现役 HUD 包混搭不成） |
| `Assets/Art/HONETi/fantasy_gui_4/**` | `GUISpriteRotate.cs`（第三方旋转脚本）、`LoadingSimulation.cs` | ✗（第三方脚本不入产品面） |
| 自有收集目录 | `ShareResource/UI/Icons/Weapons/*`（8 件 HUD 复制件）／`Icons/Loading/*`（本批新增） | 复制件按类型判定：第三方付费件 → 不入 VCS（`.gitignore:245-249`），随打包机本地还原 |

形态选择：**旋转自播**（底环静止 + 内箭自转），不取"帧序列"——与《通用加载页设计》§5「循环装饰不走 `UiEffectBinder`（自播件直接挂 prefab）」同判据，且不用等美术出帧。

### 交付

| 面 | 落点 |
| --- | --- |
| 新件 | `Assets/LiteGame/UI/Widgets/RotatingImage.cs`——`DegreesPerSecond`/`PlayOnEnable` 参数 + `Play/Stop`；`Update` 里 `_angle = Mathf.Repeat(_angle + 度速 × UiAnimationClock.Delta, 360)` 写 `localRotation`（时停/暂停即停；未绑定退化真实帧；取模防长跑精度漂移） |
| 模板 | 模板库第 30 件 `Assets/ShareResource/UI/Widgets/Tmp_RotatingImage.prefab`（同形于 `Tmp_AnimatedImage`：根挂 Image + 自播件；`Tmp_` 根名 = 文件名） |
| 加载页 | `Widgets/Tmp_Loading.prefab`：`AnimatedImage_Spin`（空帧占位）→ **`Image_SpinRing`**（静态六边框底环，96×96，位置/尺寸/白 0.9 透明沿用原槽位）＋ 子件 **`RotatingImage_Spin`**（内箭 72×72 居中自转，180°/s） |
| 词表 | `UiNamingWords.TemplateSegments` ＋ `RotatingImage_`；《UI命名词表与控件清单》§1.3 增行（模板 29→30 件）；`UIDemoPage.Templates` 同批登记（控件库 demo 一页全展） |
| 资源 | `Assets/ShareResource/UI/Icons/Loading/`：`LoadingIcon_01_Ring_Clean`／`LoadingIcon_01_Arrows_Clean` 复制件（`.gitignore` 同族登记——复制件不入库） |
| 顺手修（HEAD 即有红） | `Tmp_Loading/Cot_Content/ProgressBar_Load/Fill` → `_Fill`：`Fill` 无前缀段不合 §9.2 ⇒ `UiValidationEditModeTests.门禁_真实资产面_命名hash表路径零Error` **常红**（真实资产面 Error 1）；引用全是组件引用（`LoadingMask.ProgressFill`/`ProgressBar.Fill`）＋无按名路径覆盖，改名零牵连 |

工具（本批一次性，`AgentScripts/` 不入库）：`AddLoadingSpin.cs`（复制资源＋建模板＋接加载页）· `StripLoadingSpinLegacy.cs`（清底环上遗留的 `AnimatedImage`）· `RenameLoadingFillNode.cs`（`Fill`→`_Fill`）· `PrintUiValidation.cs`（印真实资产面校验报告）· `DevHoldLoading.cs`（dev 按住遮罩目检）。

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误 |
| EditMode | `PresentationOwnershipEditModeTests` **8/8**（新守卫「旋转件_按UIClock步进_暂停不推进」：0.5s×180°/s＝90°、暂停保持角度）· `UiValidationEditModeTests` **14/14**（真实资产面 Error 0 / Warning 1＝UIMain 债）· `UiWidgetLibraryEditModeTests` 6/6 · `VisualSingleSourceEditModeTests` 8/8 · `UiU1` 19/19 · `FeedbackService` 12/12 |
| PlayMode（真资源） | `FeedbackSurfacePlayModeTests` **3/3**（含 `202` 真 `Screens/Tmp_Loading.prefab` 打开/阻断/收口） |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1721** |
| Play 实测（真跑遮罩） | 启动走廊实拍：`Image_SpinRing` 六边框 + 内箭转圈件在场（截图 `AgentScripts/Artifacts/hold_mask_0.png`）；dev 按住遮罩连拍 4 帧，**转圈件区域逐帧变化**（帧间均差≈11、变化像素 450~530，其余静件不动）且角度递进≈90°/帧 ↔ 180°/s 设定；运行期控制台零错误 |
| 写后复核 | 模板/加载页读回：底环 `Image` 单件（遗留 `AnimatedImage` 已清）、内箭 `Image`＋`RotatingImage`（sprite 双 guid 分别落 `Icons/Loading` 复制件）；与写前快照 diff 只在 Spin 子树（其余节点零改动；无按名路径覆盖面） |

### 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| 遮罩按住方式 | 旋转的 4 帧由 **dev 直调** `FeedbackService.BeginLoading` 按住遮罩取（同工作台"加载演示组"口径，不旁路装配）；**进房链本身未重走**——其结构面由 PlayMode 真资源用例与启动走廊覆盖。 |
| 复制件不入库 | `Icons/Loading/*` 与既有 `Icons/Weapons/*` 同规则：第三方付费件复制件不入 VCS，随打包机本地还原（`.gitignore` 同批登记该目录）；干净克隆下引用悬空的**存量同类问题**（12 处第三方原始库直引）已登记审计表，本批未迁。 |
| 12 处直引 | `Hud_Battle`/`Page_*`/`Tmp_Dropdown` 仍直指 `Assets/Synty/**`、`Assets/Art/Crosshair/**`（审计表登记，未修）。 |
| Underlay 层 | 图标族尚有 `Underlay`（阴影）版未用——底环/内箭当前单层；要层次感时补复制件即可（零代码）。 |

---

## 第2章 加载遮罩孤儿面收口（进房快路径卡加载页根修）

<a id="ch2-加载遮罩孤儿面收口"></a>

> **原文件**：`Docs/施工进度/加载遮罩孤儿面收口.md`（归并前独立成文，内容整段并入本章）

用户报告（2026-10-17）："为什么单是应用本地服进去会有加载页卡住" → 诊断结论：**遮罩面留存成孤儿**——阻断设计本身没问题（全屏 `Image` 吃射线 + Back 拦截），问题在**收口路由**：打开结果的交付先于"在途登记"摘除，交付链内紧接着发起的关闭被判成"关闭在途打开"（只取消、不关闭）。

### 状态

**已完成（附验证证据）**——2026-10-17（已提交 `3eb94696`）。

### 诊断（修复前，含探针实证）

| 步 | 事实 | 出处 |
| --- | --- | --- |
| 1 | `BeginLoading` 的开面走 `nav.GoAsync`（异步入队：转场/prefab 加载）——不是同步落地 | `FeedbackService.BeginLoading/OpenLoadingAsync` |
| 2 | 本地服快路径 `Begin→End` 快于开面落地 | 用户实机复现 + 用例复现 |
| 3 | 旧行为：作用域已结束 ⇒ `EndLoading` 无面可收（`_loadingForm` 仍 null），排队中的开面随后落地**无人认领** | 探针 `Visible=False Open=True` |
| 4 | 本轮新增的自收分支进了，但 `CloseAsync` 打到 UIService 的**"在途打开 = 权威取消"分支**（只 `Cancel` 不关闭）⇒ 面留存 | 探针：`CloseAsync[202] 撞在途登记——只取消不关闭` |
| 5 | 根因：`RunOpenAsync` 的交付（`op.Completion.TrySetResult`）发生在 `_openOps` 摘除的 `finally` **之前**，而交付是**内联同步链**（`UniTaskCompletionSource` 同线程续延；`await` 落点就在 `TrySetResult` 调用栈内）⇒ 等待者的关闭请求此刻看到的登记**仍在** | `UIService.RunOpenAsync` |

### 交付

| 面 | 落点 |
| --- | --- |
| 根修（打开管线） | `UIService.RunOpenAsync`：`Retire(op)` 提前到**每次交付之前**（成功/取消/异常三径 + `finally` 兜底）；`Retire` 幂等、只摘自己的登记（取消路径可能已摘、或已被新 Show 顶替）|
| 服务收口（遮罩自收） | `FeedbackService`：开面代次 `_loadingEpoch`（开场 +1、收场归零 +1），在途开面落地时判代次——本段已结束且无在场活动面 ⇒ **落地即收**（孤儿面不认领文案/阻断，直接关闭）；阻断解除仍以计数归零为准（不等开面）|
| 守卫用例 | `UiU1EditModeTests.交付链内紧接Close_真关闭_不被在途登记吞掉`（`async` 续体形状＝产品面"打开完成就地做后续"）· `FeedbackServiceEditModeTests.Loading_开面在途即结束_孤儿面落地自收_不留永久阻断`（闸门挂起 prefab 加载造"开面在途"）——两例在根修前**红**、修后绿 |
| 设计回写 | [通用加载页设计](../归档记录/2026-10-22/UI旧体系/通用加载页专项设计.md) §3"在途开面即收场"· [UI框架总设计](../归档记录/2026-10-22/UI旧体系/UI框架总设计.md) §4.3"在途登记早于结果交付摘除" |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | 0 错误（`recompile_status` → `compilationFailed=false`）|
| EditMode（UI 家族定向） | UiU1 **19/19**（含新守卫）· FeedbackService **12/12**（含新守卫）· UiU0 6/6 · UiNavModal 9/9 · DialogService 11/11 · UICacheStrategy 3/3 · UiBindBasePathB 1/1 · UiConfirmApi 4/4 · PageFlowBattleMenu 4/4 · VisualSingleSource 8/8（合计 **77/77**）|
| Play 启动走廊实测 | `editor_play` 真跑：`[Nav] 入队 Go id=202` → `UIForm[202] 打开（System@400，Push）` → 大厅页排队 → `UIForm[202] 关闭` → `UIForm[12] 打开（Bottom@100）`——**遮罩开→收完整、首屏（大厅页含选图/选人数控件）正常渲染、控制台零错误**；截图 `Artifacts/play_boot_smoke.png` |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1721**（本批改动在场的工作树）。过程留痕：批内首次运行唯一红＝**他批未提交**的注释卫生（RoomServer/Tests 等 36 处 `2026-10-17` 日期戳进注释，`DisciplineScannerTests.纪律_代码注释不含施工痕迹`；这些行 HEAD 里不存在——不属本批），他批同工作区修净后复跑通过；**本批 4 件自始至终零违规** |
| doc-lint | OK |
| 生成物面 | 共享源集/表/`.proto` 均未动 ⇒ 无 BuildHash/生成物重跑面 |

### 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| 本地服"测试模式"进房路径 | 未在本批 Play 里重走（入口＝GM 面板"进入测试模式"/直入训练场开关；F9/F10 热键已退役）——该路径的竞态由两守卫用例**确定性复现**（修前红→修后绿），启动走廊则覆盖同一服务契约的端到端面。 |
| 编辑器内指针注入 | 本批试了点「设置」（`simulate_pointer` click 与 down/up 交替两式）：UI 无响应、控制台无导航日志——编辑器侧注入受限（沿型采样错帧，仓库既有留痕同结论）。**故本批不主张"可点性"实测结论**；dev Player 的 Runtime 侧不受此限（未跑）。 |
| 阻断设计本身 | 遮罩阻断＝全屏 `Image` 吃射线（`LoadingMask.Blocker.raycastTarget`）+ Back 拦截（`nav.BackInterceptor` 首位）——非模态、不停组；本轮未改设计。 |
| 同类面 | `Toast` 承载的懒打开同走 `_open`（`PushToastAsync`）：同窗形态存在，但 Toast 不阻断、无人收也不卡操作，未纳入本批。 |

---

## 第3章 对局场景组加载链修订（进房 → 开局）

<a id="ch3-对局场景组加载链修订"></a>

> **原文件**：`Docs/施工进度/对局场景组加载链修订.md`（归并前独立成文，内容整段并入本章）

> **来源**：用户提问（2026-10-18）「现在是不是进房就开战斗场景组，这样会有什么问题」→ 追问「是不是应该对局开始时加载好」→ 裁决**「挪到 StartGame 后加载」**；同批要求**一并修**卸载判据。
> **状态**：**已完成（附验证证据）**——2026-10-18（工作区未提交）。

### 决议与落点

| 面 | 落点 |
| --- | --- |
| 装载时点 | 对局场景组（Rig ＋ 地图）**从"进房（JoinAck）"挪到"开局（StartGame）"**，且**装载与卸载同归 `ProcedureBattle`**（原为 Match 装／Battle 卸的跨阶段所有权）——`ProcedureMatch` 不再注入 `ISceneService`/`IMapProvider`；**未开局散场不加载、不付费** |
| 开局入场遮罩 | `StartGame` 到达后在 `FeedbackService` 加载遮罩（"正在进入对局…"）内完成：加载 Rig ＋ 地图 → **相机硬门后移**（vcam 随 Rig 场景，硬门必须在场景就位之后裁决）→ `BattleViewRig.Attach` → `EnableBattleMenuBack` → `CloseRoomIfOpen`，**视图建成才收遮罩** |
| 卸载判据 | 收尾改 `if (!RootToken.IsCancellationRequested) UnloadBattleScenes();`——判据从**本阶段令牌**换成**宿主根令牌**（`ProcedureStageBase` 新增只读口 `RootToken`）：外部 `Request` 打断（房间页「离开」／设置页「退出对局」）与正常离场/失败**照卸**，**仅宿主关闭**跳过（该路径行为不变） |
| 行键来源 | 不变：`RoomClient.MapId`（JoinAck 定值）→ `BattleScenes.ResolveMapId` → 地图表 `scene` 列 |

### 为什么（提出时的三类问题与处置）

| 原问题 | 处置 |
| --- | --- |
| ① 房间等待期就能看到战斗场景（房间页背后即地图，Rig 的 vcam 一并进场） | 装载挪到 `StartGame` 后——等待期场景不在场 |
| ② 未开局离场／永不开局的房间白付装载成本与常驻内存 | 同上（未开局不加载） |
| ③ 加载窗口期传输泵停摆（Match 的泵挂在 `_pending`，置 null 后到 Battle 首帧无泵） | 装载移入 Battle：等待期由 `BattleContext.Tick`（阶段 `OnUpdate` 每帧驱动）持续泵网络 |
| ④ Match 侧加载失败残留（载了 Rig、地图缺行抛错 → Error，无人卸 Rig） | 装载与卸载同归 Battle ——失败路径的 `finally` 结算即卸已载场景 |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity 重编译 | **0 错**（`recompile_status`：`failed:false errors:[]`） |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1741** |
| 判据守卫 | `Tests/LiteFramework.Core.Tests/Hosting/ProcedureCancelTests.cs` 加 2 例：`阶段_被外部Request打断_阶段令牌取消而根令牌未取消_照卸判据可辨`（外部打断＝阶段令牌取消、根未取消⇒照卸；根取消⇒跳过）、`阶段_无根形态_根令牌恒未取消_照卸判据不吃无根形态` |
| 文档回写 | 《地图数据专项设计》场景加载形态行 · 《UI工作台设计》对局场景组行 · 《通用加载页设计》⑦行＋时间轴 · `BattleScenes.cs`/`ProcedureMain.cs` 注释 · [退出卸载修复](UI加载与导航线.md#ch4-退出卸载修复) 追记 · [多图与训练场布点](UI战斗HUD线.md#ch2-多图与训练场布点)/[UI工作台W2](UI线.md#ch7-UI工作台W2) 追记 |
| 审计登记 | [客户端审计](../审计/代码质量审计-客户端.md)：新登记（同族·已修）＋ 修复登记行；`ProcedureMatch 加载侧无宿主关闭分流` 条目加注（隐患面随装载迁至 Battle，条目保留） |

### 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| **Play 端到端未跑** | 新增时序（开局遮罩 → 加载 → 相机硬门 → Attach）只有编译 ＋ L1 ＋ 逻辑面背书；真实观感与"开局后加载"的入场延迟未实机核。 |
| 开局遮罩不拦游戏输入 | Loading 面按设计走**射线阻断**而非模态登记（`FeedbackService` 契约），故开局入场这几百毫秒内设备输入照采。若需拦应另裁（改模态语义，影响面大）。 |
| 宿主关闭期加载 | `ISceneService` 仍无"关闭期拒绝新加载"分流（既有登记条目，本批未动）。 |

---

## 第4章 退出卸载修复（宿主关闭路径不再引擎侧卸场景）

<a id="ch4-退出卸载修复"></a>

> **原文件**：`Docs/施工进度/退出卸载修复.md`（归并前独立成文，内容整段并入本章）

> **批次**：用户实机报告「退出 Play 报 `YooInternalException: AsyncOperationSystem is not initialized`」处置　**日期**：2026-10-16　**状态**：**已完成**（编译 0 错 + L1 [PASS] + 真实 Play 复核通过——见 §4）
> **来源**：用户报告（退出 Play 时 UnobservedTaskException 红栈，栈中 `ProcedureBattle.UnloadOne` → `SceneService.UnloadHandleAsync` → `SceneHandle.UnloadSceneAsync`）
> **影响面**：编辑器退出 Play / 宿主关闭（全平台关闭序同路径；仅编辑器侧"资源系统已被 YooAsset 驱动先行销毁"这一段是编辑器特有）

### 1. 取证（Editor.log 原文调用链）

关键两段日志**先后**排布，把根因钉死（同一份 `Editor.log` 内，先 `Shutdown` 后异常）：

```text
Async operation 'DownloadSchedulerOperation' has been aborted.      ← ① 资源系统已被销毁
YooAsset.AsyncOperationSystem:Shutdown ()
YooAsset.YooAssets:Destroy ()
YooAsset.YooAssetsDriver:OnApplicationQuit ()            ← YooAsset 包驱动（编辑器专用退出钩子）

YooInternalException: … AsyncOperationSystem is not initialized.     ← ② 我们才来卸场景
  at YooAsset.SceneHandle.UnloadSceneAsync ()
  at LiteClient.SceneService.UnloadHandleAsync (SceneService.cs:290)
  at LiteClient.SceneService.UnloadAdditiveAsync (SceneService.cs:211)
  at UniTaskExtensions.Forget → PublishUnobservedTaskException      ← 红栈（未被 await 的失败）
  at LiteGame.ProcedureBattle.UnloadOne (ProcedureBattle.cs:202)
  at LiteGame.ProcedureBattle.UnloadBattleScenes (…:190/:197)
  at LiteGame.<RunAsyncCore>d__20:MoveNext (…:167)                   ← finally
  ← ClientHost.ShutdownAsync → ClientScope.Cancel（根 Scope 取消）
  ← AppLifetime.OnApplicationQuit
```

两条 `UnloadOne`（地图场景 + `BattleRig`）各报一次，与"两个叠加场景"相符。

### 2. 根因

1. **退出顺序有两套写入者**：编辑器退出时 Unity 逐个回调 `OnApplicationQuit`——`YooAssetsDriver`（由 `YooAssets.Initialize()` 建的常驻件）在**它自己的**回调里 `YooAssets.Destroy()`（销毁驱动器 + `AsyncOperationSystem.Shutdown()` + 清空包裹）；`AppLifetime` 在它自己的回调里跑优雅关闭。**两者先后由 Unity 决定、不归我们控制**；实测该次驱动先行。
2. **我们的关闭序里夹着引擎侧卸载**：`ClientHost.ShutdownAsync` 第①步 `_rootScope.Cancel()` 级联取消阶段令牌，`ProcedureBattle.RunAsyncCore` 的 `await` 立即以取消收尾（**同步**续延），`finally` 里 `UnloadBattleScenes()` → `SceneHandle.UnloadSceneAsync()`。而 YooAsset 的**一切加载/卸载都经 `AsyncOperationSystem.StartOperation`**（`SceneHandle.cs:141/148`、`AssetHandle.cs:131`、`ResourceManager.cs` 多处）——系统已被销毁 ⇒ 必抛。只有 `HandleBase.Release()` 是安全的（provider 已销毁时告警返回，纯引用计数递减）。
3. **与设计不一致**：§6.2 Scene 行的**宿主关闭**退出动作是"释放资源租约"，`SceneService.ReleaseAll()` 的既有口径即"退出路径不卸场景，仅归还引用"（进程已在退出，卸场景无意义）。真正缺的是——**取消收尾没做这个分流**：正常离场该卸，宿主关闭不该卸，而当时的 finally 两条路都卸。
4. **次生问题**：`UnloadOne` 的 `try/catch` 包着 `UnloadAdditiveAsync(...).Forget()`——async 体内的异常进的是**返回任务**而非调用点，裸 `Forget()` 把"失败只记日志不阻塞离场"变成"未观察任务异常红栈"，连同批意图一起失真。

### 3. 修法（`Assets/LiteGame/App/Procedure/ProcedureBattle.cs`）

| 面 | 改动 |
|---|---|
| 收尾分流 | `finally` 内 `if (!ct.IsCancellationRequested) UnloadBattleScenes();`——**取消收尾（宿主关闭 / 阶段被打断）不卸场景**：该路径句柄归零由 `SceneService.ReleaseAll()`（随 `ContainerModule.ShutdownAsync`，关闭序第③步模块逆序的第一件）负责；正常离场（`Ended` / 失败）照卸。类注释与 `UnloadBattleScenes` 文档注释同批写明判据与理由（含"编辑器退出实测：驱动先跑 `YooAssets.Destroy()`"的实锤） |
| 失败可见 | `UnloadOne`：`_scenes.UnloadAdditiveAsync(location).Forget(ex => Log.Warning(…))`——异步失败落 Warning（去掉恒不可能命中的同步 try/catch：async 方法同步段的异常同样是进任务），恢复"离场不因表现面失败而阻塞"的文档契约 |

**判据选 `ct.IsCancellationRequested` 的依据**：阶段 CTS 链接宿主根令牌（`ProcedureStageBase.OnEnter`），由 **L1 `ProcedureCancelTests`** 钉住（`阶段_链接根令牌_根取消级联到在途阶段`、`宿主关闭_根令牌先于模块关闭被取消`）；正常离场时阶段 CTS 由 `OnLeave` 在自己的 `Request` 之后才取消，finally 跑到时恒未取消 ⇒ 两条路可辨。

> **追记（加载链修订批，2026-10-18）——本判据已被取代**：`ct.IsCancellationRequested` 把"**外部 `Request` 打断在途阶段**"与"宿主关闭"混为一谈——主动离场（房间页「离开」/设置页「退出对局」走外部 `Request(Main)`）同样经 `OnLeave` 取消本阶段 CTS，而容器仍在运行 ⇒ 收尾被误判成"取消收尾"**跳过卸载**，对局场景组残留于大厅画面（换图重进还会累积）。现改为 `if (!RootToken.IsCancellationRequested) UnloadBattleScenes();`——判据取**宿主根令牌**（`ProcedureStageBase` 新增只读口 `RootToken`：根取消 = 宿主关闭；无根形态恒 false）。宿主关闭路径行为**不变**（关闭序第①步先取消根 ⇒ 仍跳过卸载）。机制由 `ProcedureCancelTests` 新例钉住：`阶段_被外部Request打断_阶段令牌取消而根令牌未取消_照卸判据可辨`、`阶段_无根形态_根令牌恒未取消_照卸判据不吃无根形态`。

### 4. 验证

| 项 | 结果 |
|---|---|
| 编译 | **Unity 重编译 0 错误**（`compilationFailed=false`、控制台零 `error CS`）；另有旁路核证——`Assets/LiteGame/App` 单程序集 csc 编译 **0 错误**（工程 csproj 源清单 + `Library/ScriptAssemblies` 引用清单直跑 csc；工具 `AgentScripts/_compile_check/compile_app.py`） |
| L1 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1715**（含纪律扫描 / 表守卫 / BuildHash 守卫；本批未触共享源集，双常量不涉及） |
| 取消语义不变量 | L1 `ProcedureCancelTests`（根取消级联 / 根取消先于模块关闭）——本批判据所依赖的机制由既有用例覆盖 |
| **真实 Play 复核（通过）** | 真实操作路径：`clear_console` → `open_scene Boot.unity` → `editor_play`（Boot 现存 `DirectToTraining=1`/`UseLocalServer=1` 开发开关）→ 等进入测试模式对局 → `editor_stop` → 读 console。**进局证据**：`[Asset] ready package=DefaultPackage version=Simulate` · `[Config] 配置快照发布完成:12 张表` · `[Main] 直入训练场` · `[Battle] joined (JoinAck)` · `[Scene] 叠加场景已加载:BattleRig（叠加数 1）/ TrainingGround（叠加数 2）` · 十一个表现件 attach 齐 · `SceneManager` 场景表 = `Boot,BattleRig,TrainingGround`。**退出证据（16:12:05，本次会话错误计数 0）**：`[Scene] 宿主关闭释放 2 个场景句柄（退出路径不卸场景，仅归还引用）`，**全程无 `[Scene] 叠加场景已卸载`**（＝取消收尾确实没走卸载，正是本修的判别性证据——该卸载调用不存在了，`YooInternalException` 便无产生点），**无 `YooInternalException`、无 `UniTaskScheduler` 红栈**；各域服务关闭日志齐（实体/音频/VFX/UI/Lua）。 |
| 判据侧注 | 本次退出的 `OnApplicationQuit` 顺序恰好是"我们先行、YooAsset 驱动随后"（`Async operation … aborted` 出现在我们收尾之后）——**顺序不可控**，故判据不是"这次没崩"，而是"**宿主关闭路径上卸载调用已不存在**"；驱动先行的顺序由用户实测日志（§1）作证 |

### 5. 未验事项与残留（如实登记）

- **编辑器卡死一段是过程噪声，不影响本批结论**：会话中途编辑器被**并发测试运行**卡死（`Editor.log`：`Reloading assemblies for play mode` 后 `[PipelineTestRunner] Running 2 tests` → `InvalidOperationException: This cannot be used during play mode` ×2 → `Test execution timed out, cancelling` → `Invalidated previous test run`；主线程空转 ~1.5 核、`editor_status` 30s 超时、日志停写、无对话框窗口）。该形态与本仓**既有教训同型**（[运行阻断修复批](UI加载与导航线.md#ch5-运行阻断修复批) §过程如实登记「Play 态误发测试致管线假死」），**非本批代码所致**；恢复方式是"改一个源文件时间戳 + 把编辑器窗口提到前台"，随后的 domain reload 把它带回正常态（此后 Play 复核按 §4 走完）。
- **残留（已接受，登记备查）**：宿主关闭路径上，程序集/表现件释放各自 YooAsset 引用时（`AssetHandle.Release()`）若在 `YooAssets.Destroy()` 之后会走包内"已销毁"告警分支——安全但会留黄色告警；属"引用必须归零"与"包可能已先销毁"的固有张力，不做门控（门控＝真泄漏）。
- **同批观察（已登记进审计表"新登记"，未修）**：退出时两条 `Handle has already been released` 黄告警来自 `SceneService.ReleaseAll` 的**同句柄双释放**——它既直接 `kv.Value.Dispose()`（`_additives` 值）又让每个叠加场景的作用域 `SceneLease.Dispose()` 再放一次同一句柄（幂等，包内只告警）。
- **未跑面（按节律）**：本批未跑全量 EditMode/PlayMode——改动面（`ProcedureBattle` 收尾分流）无对应 EditMode 用例（既有三份 Battle* 用例不触卸载路径），且当时编辑器上另有工具在跑测试（并发再撞＝复现卡死）；夜间 L2 照常覆盖。
- **另有一条同族隐患（未修，登记在审计表）**：`ProcedureMatch` 侧的**加载**同样经 `AsyncOperationSystem`——宿主关闭若恰逢 JoinAck 后加载在途，会以 `YooInternalException` 走 `Match` 的失败分支（对局未能开始即退，用户不可见）。**（追记：加载链修订后装载已从 `ProcedureMatch` 迁至 `ProcedureBattle`（`StartGame` 后），该隐患面随之落在 Battle 的开局入场加载上。）**

### 6. 过程留痕

- 编辑器卡死期间为拿到编译证据，走了"工程 csproj 源清单 + `Library/ScriptAssemblies` 引用清单直跑 csc"旁路（`AgentScripts/_compile_check/compile_app.py`，产物落 `AgentScripts/_compile_check/`）；期间曾把 `Library/ScriptAssemblies` 里缺的两枚依赖产物补进 `Temp/bin/Debug/`、`obj/Debug/`（Unity 自身编译会重写这两处，无副作用）。更早一次为看得见状态拍了张屏（管线把 `--save_path` 解析到 `Assets/` 下，生成了 `Assets/AgentScripts/**` 与 `Assets/AgentScripts.meta`）——**已删除**，工作区无残留。
- 教训收口：**进 Play 前确认没有在途测试运行**（或至少：Play 期间不发测试命令）——本条在 [运行阻断修复批](UI加载与导航线.md#ch5-运行阻断修复批) 已有记录，本批再次踩到，说明仅靠文档纪律不够；"管线在途测试"与"进 Play"的互斥检查值得进 `unity-pipeline` 使用规范或门禁脚本。

---

## 第5章 运行阻断修复批（并行回归处置）

<a id="ch5-运行阻断修复批"></a>

> **原文件**：`Docs/施工进度/运行阻断修复批.md`（归并前独立成文，内容整段并入本章）

> 触发（2026-10-11 下午）：用户实机进玩法报错 `[GameEntry] 引导失败：重复注册:CommandCenter`。
> 边修边验（Play 实跑逐断），共三件阻断**串行显影**（修一个露下一个），全部以实跑收口。
> 归属：本批处置的是当日并行批（`58fbfd3` GM 批 / `5bbeb72` 资源三层模型批）的运行时回归；
> 本批自身改动与之同工作区并行，按显式文件清单隔离。

### 三件阻断与修复

| # | 症状（Play 实跑） | 根因 | 修复 |
|---|---|---|---|
| 1 | `[GameEntry] 引导失败：重复注册:CommandCenter——同类型只能有一个装配来源`；连锁 `GameEntry.Start → TakeContainer` 抛 `InvalidOperationException: 未装配` | GM 批（`58fbfd3`）在 `ContainerModule` 补注册 `RegisterInstance<CommandCenter>`——而上一行 `context.Put(commands)` 已按**具体类型**占键（`ClientContext.Put→RegisterInstance(product)` 按键=`运行时类型`）⇒ 同键重复，容器 `Add` 查重抛 | 删重复行：具体键经 `Put` 已存在（执行形态消费者 `Require<CommandCenter>` 照常可解）；接口键 `ICommandCenter` 保留 |
| 2 | `EditorSimulateBuildPipeline build failed … Collecting asset file already exists: 'Assets/ShareResource/Characters/CombatGirlsAnimationClips.asset'` → `AssetService.InitAsync` 抛 → 启动失败 | 三层模型迁移批（`5bbeb72`）把 `Fonts`/`UI`/`Characters`/`FX` 挪入 `ShareResource` 后，样例①组（`45aa839` 立，CollectPath=`Assets/ShareResource` 整目录 CollectAll）与四个子目录组**全面重叠**（立组时 ShareResource 下仅 SampleContent，无重叠） | 收集器收窄为 `Assets/ShareResource/SampleContent`（组名/寻址/PackSeparately 不变，样例①语义不变）——`FixCollectorOverlap`（pipeline `run_script`，配置资产禁手改）；`ContentSampleBuilder.RegisterCollectGroup` 同步幂等迁移（旧宽路径发现即迁窄/并存删宽）；运行手册 + 《资源收集目录专项设计》§5 同步 |
| 3 | `KeyNotFoundException: 'tbanimationprofile'`（`ConfigService.LoadAsync` → Luban `Tables` 构造）→ 启动失败 | 域化（同 `5bbeb72`）后 `TableLayout.Files` 条目为"领域/表名"，而 Luban 生成物 `cfg.Tables` 构造**按裸表名**回调 loader——客户端预取缓存仍按领域相对路径作键（服务端 `RoomServer.CombatNumbers` 已是"裸名→领域"正确范式，客户端 `ConfigService`/`AnimationProfileTableSource` 两处没跟上；两个 EditMode 用例件同因失效：`CombatNumbers` 用例未跟 `tbanimationprofile` 进清单、`ConfigSnapshot` 用例损坏键失靶） | `TableLayout.TableName`（裸表名派生口）补为单源；两个装载点缓存键改裸名（位置仍走 `TableDataPath`）；`CombatNumbersEditModeTests` 弃自带残留清单改走 `TableDataFiles` 全清单 + 裸名断言；`ConfigSnapshotEditModeTests` 缓存键改裸名（FromCache 同派生） |

### 验证（全 Play 实跑 + 定向 EditMode）

| 项 | 结果 |
|---|---|
| 阻断1 | Play 实测越过容器装配：UI 输入底座/UI 壳/音频壳/VFX/相机接线全就绪 |
| 阻断2 | Play 实测 `Package 'DefaultPackage' created …` → `[Asset] ready … version=Simulate` → 入口资源探针 OK |
| 阻断3 | Play 实测 `配置快照发布完成:9 张表 version=1`（玩法/移动/武器/动画 Profile 四段装载 log 齐）→ `[Main] 玩法场景已加载:TrainingGround`——**全程零 FATAL、零后续错误**（实跑会话停于玩法场景） |
| EditMode 定向 | `ConfigSnapshotEditModeTests` **3/3**（含"坏表→保留空态"——损坏键命中靶位）、`CombatNumbersEditModeTests` **2/2**、`CharacterAssetEditModeTests` **4/4**（动画表链代表） |
| L1 | `test.ps1 -Lane L1 -Profile PullRequest` **1573/1573 PASS**（`TableLayout` 两端源链接件——含服务端共编面） |

### 过程如实登记

- **为何门禁没拦住**（当日三批各自的盲区）：`58fbfd3` 的重复注册躲过 L1（纯 dotnet，不装配 Unity 容器）；`5bbeb72` 的收集重叠躲过 L1（不构建 bundle——编辑器模拟构建只在进 Play 时触发）且 EditMode/PlayMode 未在白天跑（既定节律）；表键漂移躲在 L1 之外（LiteClient 装载链在 Unity 侧）且对应 EditMode 用例近期未跑（`tbanimationprofile` 入清单时 `CombatNumbersEditModeTests` 未同步——本次同批修复）。**后续建议**：容器装配冒烟（进 Play 或等价装配用例）纳入 M4 值勤面；表用例清单改从 `TableDataFiles` 派生（本批已改一处）。
- **Play 态误发 EditMode 测试**：TestRunner 进入 "cannot be used during play mode" 错误循环致管线假死（`editor_status` 30s 超时×3），经 domain reload 恢复。教训：**Play 验证后必须先 `editor_stop` 再发任何测试命令**。
- 阻断 2/3 的修复均在 Play 前用定向用例先行验证（EditMode 定向跑绿）再进 Play 复验——避免"修一处露一处"的空转。

---

## 第6章 进房加载面 Back 取消接线（本地服加载页卡住检查）

<a id="ch6-进房加载面Back取消"></a>

> **来源**：用户报告「本地服加载页会有卡住的问题，检查一下」（2026-10-19）。
> **状态**：已修（客户端；Play 实测取消链通过）——工作区未提交。

### 诊断（实测）

| 面 | 事实 |
|---|---|
| 进程内本地服（`UseLocalServer=1`，直连对局 Room-A） | 连续 4 次进局：202 开→关正常，未复现永久卡住；其中 1 次旧代次孤儿面收场在 `Arena01` 载完前关闭（加载面提前收口，非卡住） |
| 本机 RoomServer 路径（「直连对局（本机服 Room-A）」真 KCP，`127.0.0.1:17777` 无监听） | 202 打开后停在加载面直到停 Play；`[KCP] sending handshake` 后无 `[Battle] joined`（JoinAck 预算 10s，停 Play 早于超时） |
| 取消链 | `ProcedureMatch` 只等阶段令牌；`FeedbackService.LoadingToken`（阻断期 Back 的取消信号）无消费者 ⇒ ESC/Back 被拦截消费但进房等待继续到超时 |

### 修复

`ProcedureMatch.RunAsyncCore`：开场取 `_feedback.LoadingToken`，与阶段令牌链接后传给 `WaitJoined`；取消且阶段未被打断（= 用户按 Back）时记日志并 `Request(Main)`（在线形态房间页原样保留），外部 Request/宿主关闭仍由发起方流转。

### 验证

| 项 | 结果 |
|---|---|
| Unity 重编译 | 0 错误 |
| Play 实测（KCP 无服） | 发起「直连对局」→ 202 打开 → 经 `nav.BackAsync()`（真实 Back 链，含 `BackInterceptor`）→ `[Match] 进入房间已取消（加载面 Back）` → `UIForm[202] 关闭` → 再次发起直连对局被 Main 消费（证明已回 Main） |
| 门禁 | L1 **[PASS] 1757** · `doc-lint` **0 violation / 0 warning** |

### 边界

- 未复现"永久卡住"；本轮修复解决的是**阻断期不可取消**。若本机 RoomServer 未启动，KCP 仍需等 10s JoinAck 超时才进 Error（kcp2k 无连接超时，客户端无从提前判定）；本次未缩短该预算。
- 验证经 `TryCancelLoading()` 调用（与 Back 拦截同入口），未做真实 ESC 键注入。
