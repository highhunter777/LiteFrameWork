# UI 线（U0 止血 → U1 操作与所有权 → U2 产品闭环 → 工具链与工作台）

> **旧体系冻结（2026-10-22）**：本卷记录旧 UI 体系的施工历史，不再对应现行契约；新体系设计见 [UI 系统总设计](../设计文档/客户端/UI系统/UI系统总设计.md)。保留原文作为历史证据，不回填新状态。


> **定位**：UI 框架重构与工具链：U0/U1/U2 三段重构、UI 工具链（校验/编排）、工作台三批。

> **章法**：一卷一条线；每章＝原一份批次记录（标题、正文、证据原样并入，仅降一级标题）。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。
> 每章开头的引用块是该批的原始状态与证据；本卷只做归并与章节化，未改写任何批次结论。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | UI U0 施工进度：正确性止血 | `UI-U0.md` |
| 2 | UI U1 施工进度：操作与所有权 | `UI-U1.md` |
| 3 | UI-U2 产品闭环：施工进度 | `UI-U2.md` |
| 4 | UI 工具链 | `UI工具链.md` |
| 5 | UI工作台W0（场景+驱动） | `UI工作台W0.md` |
| 6 | UI工作台W1（进度演示接线 + Loading 视觉补齐） | `UI工作台W1.md` |
| 7 | UI工作台W2（语言行 · 状态注入 · HUD 预览 · 回写工具 · 发布剥离） | `UI工作台W2.md` |
| 8 | UI 制作主线（绑定合并 · 新建页 · 单页布局预览 · 工作台退役） | — |
| 9 | UI 制作可用性跟批（页面选择器 · 回写窗口退役） | — |

---

## 第1章 UI U0 施工进度：正确性止血

<a id="ch1-UI-U0"></a>

> **原文件**：`Docs/施工进度/UI-U0.md`（归并前独立成文，内容整段并入本章）

> 依据：《UI框架总设计》§13 唯一实施路线 U0 行（交付：UI-01/02/04/07；初始化失败回滚；全关/DevReload 防旧引用；补完整页面回归。退出条件：真 Lua 首开/关闭/复用成功；列表可往返；Covered/Paused 可清；旧 env 零访问）与 §4/§5/§8/§10 目标契约、§12.1 分层用例。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次范围

| 项 | 范围 | 状态 |
|---|---|---|
| UI-01 | Lua 实例工厂 + 生命周期回调显式传 self + 所有权分开 | 已完成（见下） |
| UI-02 | 首次与复用合流：复位视觉、重新排序、复用同判 Replace、复用播入场 | 已完成（见下） |
| UI-04 | Close/CloseAllOpen 覆盖 Covered/Paused；遮盖源由全部打开的全屏推导 | 已完成（见下） |
| UI-07 | VirtualList 重写为窗口复用；隐藏项可回窗、节点有界、监听对称（顺带 UI-08 的重复绑定/监听增长） | 已完成（见下） |
| 回滚 | OnInit/首次 OnShow 失败撤销登记、释放 Lua 引用、销毁对象并报失败 | 已完成（见下） |
| env 重建 | 全关 + 逻辑落空（旧 Lua 引用释放）→ 复用按当前注册表重解析 | 已完成（见下） |
| 完整页面回归 | 真 Lua + 真 prefab + 真配置投影的 EditMode 集成用例 | 已完成（见下） |

### 施工记录

#### 2026-09-23 · U0 全项交付（已完成，待提交）

**代码位置**：

- `Assets/LiteGame/LuaBridge/LuaBehaviourAdapter.cs`——注册表存**模块**，适配器即**实例工厂**：构造期执行 `module.new()`（无 `new` 的旧式表退化为"模块自身即实例"）；七个回调显式传 self（预置数组复用，热路径零分配）；`self.ui` 挂实例；`Release()` 只释放实例/回调/ui 门面表，**不 Dispose 共享模块**（§5.1 所有权分开）；`CallRaw` 加 released 守卫（§10.2 旧 env 零访问）。
- `Assets/LiteGame/UI/UIService.cs`——首次与复用合流为一条管线（`ReuseAsync` + 公共尾段 `FinishOpenAsync`：遮盖重算 → Replace 推导 → 播表现 → 关旧全屏）；复用走 `PrepareForShow` 复位 + `AssignDepth` 重排；ShowAsync 幂等口径扩到 Covered/Paused（不暗中重跑 OnShow）；打开失败回滚（撤登记 → `DisposeFailedOpen` → 抛失败）；`CloseAsync`/`CloseAllOpen`/`IsOpen` 覆盖 Covered/Paused；`RecomputeCovering` 遮盖源 = 仍打开的全屏（含 Covered/Paused）；新增 `DropAllLogic()`（env 重建前置）；新增可注入面 `loadPrefab`（fake loader 验收口，U1 换内容服务租约）。
- `Assets/LiteGame/UI/UIForm.cs`——`PrepareForShow()`（SetActive + alpha/interactable/blocksRaycasts/位置基线复位，基线取实例化时刻）；`EnterClosing` 接受 Active/Covered/Paused；`EnterActiveFromLoading` 返回成败（经新 `SafeCall.TryInvoke`）；`DropLogic`/`DisposeFailedOpen`（EditMode 用 `DestroyImmediate`——`Object.Destroy` 在编辑态只记 error 不生效）。
- `Assets/LiteGame/UI/Widgets/VirtualList.cs`——按 §8.2 重写为**窗口复用**：节点数 = ceil(视口/步长) + 2×Overscan + 1（不随数据量）；由 Content 偏移/视口尺寸/项尺寸算首末索引；Content 总尺寸按数据量维护（垂直路径只写垂直尺寸）；节点按索引重绑、先解绑（可选口 `IVirtualListUnbind`）再写数据；ScrollRect 监听只绑一次、OnDisable/OnDestroy 对称解绑；`HardCap` 改为超限**告警**（不做静默截断）；`RefreshWindow()` 公开口供无 ScrollRect 宿主与测试。
- `Assets/LiteGame/UI/UIFormInfo.cs`——新增 `IUIFormCatalog` 契约（§3 可注入面），`UIFormCatalog` 实现之；`UIService` 改依赖接口。
- `Assets/LiteFramework/Scripts/Core/Util/SafeCall.cs`——新增 `TryInvoke`（成败可判的隔离调用，回滚路径用；行为与其余 SafeCall 同语义：异常落地 Log.Error）。
- `Assets/LiteGame/Editor/DevReload.cs`——顺序钉死新增 ⓪′：全关后、`lua.Shutdown()`（env.Dispose）**前**调 `DropAllLogic()`（释放 Lua 引用必须趁 env 存活）。
- `Assets/LiteGame/App/GameEntry.cs`——无需改动：`logicResolver` 闭包不变，实例工厂职责移入适配器。
- `Assets/Tests/EditMode/UiU0EditModeTests.cs`（新增，6 例）+ `LiteGame.EditModeTests.asmdef` 补 `xLuaMain` 引用。

**实现口径偏离与登记**：

1. **真 Lua 用例的 env 装配**：测试内自带 loader（磁盘读 `Assets/LiteGame/Lua/**.lua`）并绑定 `log` 全局表（与 `LuaComponent.BindLog` 同款）——真实页面脚本调用 `log.info`，裸 env 缺该全局会炸（实测踩到：`UI.UIMain:14: attempt to index a nil value (global 'log')`）。
2. **`UIService` 的 `DontDestroyOnLoad` 加 `Application.isPlaying` 守卫**——编辑态调用 DDOL 抛 Runtime Error（实测），L2 EditMode 集成用例依赖此守卫；Play 态行为不变。
3. **`Bridge.ui:GetLogic(id)` 仍返回注册表模块而非页面实例**（Bridge 无 UIService 通道，且当前无 Lua 消费点）——页面实例所有权在 `LuaBehaviourAdapter.Logic`；归 U1 所有权/接缝批处置。
4. **`Pause` 仅限 Active**（Covered 页手动暂停会在单枚举模型里丢失遮盖标记）——展示状态独立记录（Visible/Covered/Paused 同时成立）属 U1 §4.1 两层状态重构。
5. UI-03/05/06/09（并发操作结果、资源租约、Shutdown、转场取消/输入仲裁）**不在 U0 范围**，未动。

**验证证据**（2026-09-23 本机实测，工作区含并行 C0 线未提交改动）：

- `dotnet test Tests/Tests.slnx`：**416 通过 / 0 失败**（LiteTesting 7 + LiteFramework 205（含 SafeCall.TryInvoke 新增 2 例）+ LiteSim 83 + LiteNet 121）。
- `powershell -NoProfile -File scripts/gate/l2-unity-gate.ps1`：**L2 通过，exit 0**——meta 扫描 11689 个全合法；Unity 编译 completed 无失败；控制台 0 条 CS 错误；**EditMode 36/36**（原 30 + 本批 6）。
- 退出条件逐条对账：
  - 真 Lua 首开/关闭/复用成功 ✅（`真Lua页面_首开_关闭_复用_实例与self契约`：真 tbuiform 投影 + 真 `UIMain.prefab` + 真 `UIMain.lua`，OnShow 计数首开 1 次/复用 1 次，`module.new()` 实例与共享模块非同表、`self.ui` 挂实例、BtnClose 点击经 Lua 闭包无错误）；
  - 列表可往返 ✅（`虚拟列表_窗口复用_首尾往返_缩容_刷新不重绑`：500 条 13 节点、滚尾滚回首条重新入窗、缩容解绑、窗口不变时 Refresh 5 次零重绑）；
  - Covered/Paused 可清 ✅（`三层全屏_Covered与Paused可关_CloseAllOpen全清`：三层全屏遮盖状态、遮盖中关闭、Paused 关闭、遮盖源消失立即 OnReveal、CloseAllOpen 全清）；
  - 旧 env 零访问 ✅（`env重建_逻辑落空后复用_按新注册表重解析且旧引用已释放`：DropAllLogic 后旧适配器 `Released=true`、env.Dispose → 新 env/新注册表 → 复用重解析出全新 Lua 实例并补跑 OnInit）。

**接手复核与提交（2026-09-23，Codely 会话按用户指示接管）**：

- 提交前工作区发现 `.github/workflows/ci.yml` 带有对已提交 C0-④ 接线的**重复追加**（L0 扫描×2、Player 构建/冒烟×2、超时 60→90）——属坏编辑，已回退到已验证的 HEAD 版本；本批提交不含该文件。
- 复核门禁（最终工作区态）：L1 `scripts/test.ps1 -Lane L1` **407 通过 / 0 失败**（=R0 后 405 + SafeCall.TryInvoke 2 例；记录中 416 为未过滤全量口径，含 EndToEnd/LongRunning 标记用例，两口径自洽）；Unity recompile `errors:[]`；**L2 EditMode 36/36 通过**；**L3 8 项通过**（含 30s 双端对跑——UI 批不触服务端面，回归确认无串扰）。
- 提交哈希：d72f0e1（feat(u0) UI 正确性止血，19 文件，已推送 origin/LiteGame；含本记录）。

### 已知边界（后续批次）

- §12.1 的 **L2 PlayMode 行未接**（PlayMode 测试程序集尚未建设，归 C0/C4 与 UI 计划）：本批"真页面"证据在 EditMode，资源加载为替身（`AssetDatabase` 直读），Player/真资源验收待 U1 租约与 Offline 包。
- 操作结果类型（成功/已打开/忙/取消…、requestId、队列上限/超时）＝ §4.3，属 U1；本批保持现有 `ShowAsync` 返回 UIForm / 失败抛异常的兼容口径。
- 输入协调者（转场锁 + 模态 + 暂停综合求解）＝ §6.3/§6.2，属 U1；本批 `PrepareForShow` 只回到输入基线。
- 缓存预算/淘汰/销毁入口（Resident/LRU/DestroyOnClose）＝ §5.2，属 U1；本批池语义不变（只增不毁）。
- 模板自检 36 条中 `VirtualList` 用例（`RealizedCount == 7`）在窗口复用语义下语义不变（7 条 < 可见容量），L2 实测未回归。

---

## 第2章 UI U1 施工进度：操作与所有权

<a id="ch2-UI-U1"></a>

> **原文件**：`Docs/施工进度/UI-U1.md`（归并前独立成文，内容整段并入本章）

> 依据：《UI框架总设计》§13 U1 行（UI-03/05/06/09；显示/实例作用域；租约、缓存预算、Shutdown、统一排序、转场取消与输入阻断）+ §4.3/§4.4/§5.2/§6.2/§6.3；《框架先行建设与业务接入专项设计》§11（U1 在 lease/scope 接缝可用后集成——C1 已交付 IContentService/AssetLease/ContentGeneration）。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| U1-① 操作记录与取消 | 在途打开操作合流（并发 Show 不返 null）；Busy；单人取消/全员撤工作；Close 权威取消在途；类型化 UIOpenException；展示代次 + 展示作用域 CTS；Tick 快照防重入 | 已完成（`d6e105e`，见下） |
| U1-② 租约与缓存预算 | IUIPrefabLease 注入（内容服务租约通道）；实例持租约到销毁；缓存预算 + LRU 淘汰；Destroy 完整销毁；CloseReason（系统关闭跳拦截与离场）；ShutdownAsync 全链 | 已完成（`d6e105e`，见下） |
| U1-③ 排序/输入/转场 | 组内统一排序（开序即深序/紧缩/容量拒绝/BringToFront）；interactable 与 blocksRaycasts 职责分离；转场 ct + 复位契约 + 结果 Kind | 已完成（`d131b1f`，见下） |
| U1-④ Player 全链与收口 | ProcedureMain 开真实主页面（真 prefab+真 Lua+租约）；冒烟标记；全量回归；文档同步 | 已完成（见下） |

### 施工记录

#### 2026-09-24 · U1-①② 操作合流/取消/租约/缓存/销毁/Shutdown（提交 `d6e105e`）

**代码位置**：

- `Shell/UI/UIOpenException.cs`（新）——类型化失败（Canceled/LoadFailed/InitFailed/Busy/Rejected + FormId；基类 InvalidOperationException 兼容旧捕获点）。
- `Shell/UI/UIService.cs`——`OpenOperation` 在途操作记录：并发 Show 合流共享一次加载（等待者经 `AttachExternalCancellation` 各自可取消；工作只受 `WorkCts`——全员退出才撤销）；数据冲突 Busy（首请求持有数据）；Close/Shutdown 对在途发权威取消（半成品就地回滚）；失败经 Completion 交付全部等待者同一异常。`IUIPrefabLease` 必填注入（`_leases[formId]` 持有到真正销毁；缓存实例算使用者）；`CacheBudget`（默认 16）+ LRU 淘汰（`_lastUsed` 记账；只淘汰非打开非引用实例）；`Destroy(formId)` 显式销毁；`CloseAsync(reason)`（User/Back/Replace/ScopeExit/Reload/Shutdown——系统语义跳过出栈拦截与离场，OnHide 照常）；`ShutdownAsync`（停接入→取消在途→全关→销毁缓存/租约→销毁 DDoL Root，幂等）；Tick 快照迭代（回调内开关界面防枚举修改）。
- `Shell/UI/UIForm.cs`——展示代次（每次打开递增 + `IsDisplayCurrent` 迟到核验）+ 展示作用域 CTS（`DisplayToken`：打开创建/关闭取消——页面内跨帧异步级联终止）；`DestroyInstance()`（DropLogic + 展示令牌收尾 + Disposed 终态 + Destroy GO）。
- `Shell/UI/UIFormState.cs`——增 `Disposed` 终态。
- `Shell/UI/UIPrefabLease.cs`（新）——`IUIPrefabLease` + `ContentPrefabLease`（AssetLease 适配）+ `UIPrefabLeases.Unowned`（编辑器/测试）。
- `Main/Hosting/GameModules.cs`（UiShell）——prefab 经内容服务租约（依赖序 Content②→UiShell⑧）；Shutdown 释放面接 `_uiService.ShutdownAsync()`。

**L2 EditMode（新增 14 例，`UiU1EditModeTests.cs`）**：合流（同数据三等待者同实例/一次加载）/Busy/单人取消不牵连共享工作/全员退出撤工作/Close 权威取消在途/类型化 LoadFailed（含 location 与根因）/展示代次（复用递增、迟到核验失败）/展示令牌关闭即取消/Tick 重入（OnUpdate 内开新界面不炸）/租约生命周期（打开与缓存期间持有、销毁才释放、销毁后再开全新加载）/缓存预算 LRU（超预算淘汰最旧、淘汰后再开重新加载）/Destroy（打开中拒绝、关闭后可销毁）/系统关闭（跳过拦截与离场、OnHide 照常）/Shutdown 全链（在途取消、双租约归零、停止接入、Root 销毁、幂等）。

#### 2026-09-24 · U1-③ 统一排序/输入锁/转场取消复位（提交 `d131b1f`）

**代码位置**：

- `Shell/UI/UILayerGroup.cs`——**开序即深序**（`RecalculateOrders`：入栈/移除/BringToFront/复用后统一重算，紧缩无洞）；废止 100 槽位回卷——`IsFull` 容量拒绝。
- `Shell/UI/UIService.cs`——容量不足 `UIOpenException(Rejected)`（创建/入栈前拒绝）；`BringToFront(formId)`。
- `Shell/UI/UIForm.cs`——暂停/恢复应用输入锁。
- `Shell/UI/Transition/`——`TransitionContext.Cts`（每事务取消源）；超时**先取消策略工作**再强制收尾；`ApplyComputedInput`（恢复为计算值——Paused/Closing/Recycled/Disposed 保持锁，不无条件写回 true）；`CloseGate` 锁 **interactable**（blocksRaycasts 全程不动——职责分离）；`TransitionResultKind`（Completed/Skipped/TimedOut/Cancelled/Failed）；`Runner.IsLocked`（输入协调求解口）。
- `Shell/UI/Strategies.cs`——`ITransitionStrategy.PlayShow/PlayClose` 与 `IReplaceTransition.PlayReplace` 增 `CancellationToken`；FadeSlide：ct 取消 `Kill(complete=true)` 跳终值即复位；不再碰 blocksRaycasts。

**L2 EditMode（新增 4 例 + 既有口径迁移）**：排序统一（开序即深序/移除紧缩/BringToFront）/组容量拒绝（100 满 + 101 拒）/转场输入锁（接受即锁 interactable、blocksRaycasts 全程不动、收尾按计算值恢复）/转场超时（取消令牌送达策略、TimedOut、输入恢复、降级立即完成）。既有用例迁移：转场门断言改 interactable 口径（`UiTransitionEditModeTests`）；U0 复用排序断言收敛为紧缩契约（幸存页回组基序位）。

#### 2026-09-24 · U1-④ Player 全链与收口

- `Main/Procedure/ProcedureMain.cs`——进入 Main 打开真实主页面（formId=1 UIMain：真 prefab + 真 Lua + 内容租约全链）+ `[UI] main open` 冒烟标记；失败进确定错误态。
- `scripts/player-smoke.ps1`——标记集补 `[UI] main open`（字面量判据，C1-⑩ 修复后口径）。

**验证（本批实测）**：Unity 编译 **0 错误**；EditMode **69/69**（51 存量 + U1 新增 18）；**Player 构建 + 冒烟 PASSED**（`[UI] main open` 真资源包页面打开命中——U1 退出条件"实际资源包 Player 可打开/关闭"的打开面）；全量 L1 **505/505**。

### 实施口径偏离与登记

1. **输入协调者为内聚实现非独立类**：转场锁（Runner.IsLocked）+ 暂停（UIForm 状态）+ 生命周期锁定态（ApplyComputedInput）共同构成 §6.2 协调语义；**模态栈与产品策略随 U2 导航接入**（真实消费者出现前不建空层）。
2. **缓存策略列（Resident/LRU/DestroyOnClose per-form）未接表**：tbuiform 无策略列（加列需 Luban 表变更）——U1 交付统一预算 + LRU + 显式 Destroy；per-form 策略列随 U2 表扩展。
3. **TransitionResultKind.Cancelled 预留**：U1 的取消源为超时与 Shutdown（系统关闭不走转场）；外部导航取消（Go/Back 期间）随 U2 接入。
4. **队列上限 QueueCapacity=8/OpenTimeout=10s 未实装**：§4.3 的串行导航队列属 U2 导航（Go/Back/Replace 单写者）；U1 已交付其前置（操作合流/拒绝语义/类型化结果）。
5. ~~**L2 PlayMode 未建**（lane 未接入）：真 UIService+真 Lua 的取消加载/三层遮盖等 PlayMode 场景以 EditMode 真资源用例 + Player 冒烟承接；PlayMode lane 随测试框架线接入后补。~~ **已于 2026-09-25 消解**：`Assets/Tests/UI/PlayMode` 建成并接入 `scripts/gate/l2-unity-gate.ps1` 同一门禁（真 UIService + 真 LuaEnv + 真 prefab + 真转场，12 例），见 [框架先行记录](框架先行与验收线.md#ch1-框架先行)。**但本批的取消加载/三层遮盖场景仍未在 PlayMode 下覆盖**——余项转记 [框架先行 §3](框架先行与验收线.md#ch1-框架先行) 样例②。

---

## 第3章 UI-U2 产品闭环：施工进度

<a id="ch3-UI-U2"></a>

> **原文件**：`Docs/施工进度/UI-U2.md`（归并前独立成文，内容整段并入本章）

> 依据：[《UI框架总设计》](../设计文档/客户端/UI/UI框架总设计.md) §4.3（单写者导航：队列上限/等待超时/可观测拒绝）、§6.2（模态栈/射线遮蔽/平台返回统一处理）、§9（本地化、文本与可访问性）、§13 U2（产品闭环）；[《动画模块专项设计》](../设计文档/客户端/动画/动画模块专项设计.md) §14（UiFx 原语中断复位）。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

### 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| U2-① 导航协调者 | `UINavigationController`：单写者串行、队列上限、等待超时、排队期取消、可观测拒绝计数 | **已完成**（2026-09-25） |
| U2-② 模态栈 | `UIService` 模态登记/推导、最顶模态 Back、射线遮蔽 | **已完成**（2026-09-25） |
| U2-③ 真实消费者接线 | `ProcedureMain` 改走导航；`ProcedureBattle` 用模态作游戏输入门 | **已完成**（2026-09-25） |
| U2-④ UiFx 中断复位 | `Pulse`/`Flash`/`Slide` 中断即复位回基线 | **已完成**（2026-09-25） |
| U2-⑤a LText 核心 | `LocalizationCatalog` / `LocalizationService` / `LocalizationTable`：回退链、缺 key 策略、复数、转义、key 校验、覆盖率 | **已完成**（2026-09-25） |
| U2-⑤b 字体/SafeArea | 字体族与 fallback 链、缺字检查、SafeArea 细化 | **已完成**（2026-10-09，见「U2 余部」） |
| U2-⑤c LText 控件接入 | `UIBindIndex` 按 key 设文本 + 语言变更自动刷新；Lua 门面 `SetTextKey/SetTextKeyArgs/SetTextKeyPlural/UnbindTextKey` | **已完成**（2026-09-25） |
| U2-⑤d 打字机 | LTextLabel 可取消打字机（§9 末条） | **已完成**（2026-10-09，见「U2 余部」） |
| U2-⑥a 弹窗形态裁决 | 用户 2026-09-25 裁决：弹窗分两类（独立 Canvas 类走服务 / 页面独有类页面自理）；新建页面级 `Screens/Dialog.prefab` | **已完成**（prefab 已探针验证可打开） |
| U2-⑥b Dialog 服务 | `DialogService`（结果/队列/优先级/互斥组/取消）+ `UIDialog` 等待式 + `UIService` 两接缝 | **已完成**（2026-09-25 接手交付，11/11；"Pending 之谜"已破案，见下） |
| U2-⑥c Loading/Error 页 | 加载/错误页统一入口 | **已完成**（2026-09-25：FeedbackService——Loading 嵌套阻断 + Back 首位拦截取消、Error/重试经 DialogService、Toast 计时面；空状态随大厅消费者落地）。**视觉已归位**：三面改走 System 层模板 form，见「反馈面归位」 |
| U2-⑦ 焦点 | 手柄/键盘焦点导航 | **已完成**（2026-10-09，见「U2 余部」） |
| U2-⑧ per-form 缓存策略列（U1 余项，§5.2） | `UICacheStrategy`（Lru 默认/Resident 不淘汰/DestroyOnClose 关即销毁）的壳消费面 | **已完成**（2026-09-26，壳面 3 例；tbuiform 表列与投影接线未做，生产页面暂全默认 LRU） |
| U2-⑨ 导航 Replace + 包③覆盖度余部（§6.1/§4.3） | `UINavigationController.ReplaceAsync`（显式替换当前记录：关当前顶再开新页，目标即顶幂等）+ PlayMode 补段（导航 Go/Back/Replace 真资源段、转场输入锁、列表真滚动、DevReload 环境重建、循环计数、诊断关联） | **已完成**（2026-09-26，EditMode +2、PlayMode 10 例；修 UIService 回滚租约泄漏与 VirtualList.Offset 符号两处真缺陷，见记录） |

### 未完成事项

#### Dialog 服务：谜团已破案，批次已完成（2026-09-25 接手批）

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

#### 已确证发现（历史保留，对后续有价值）

1. **`UIService.ShowAsync` 会 `Instantiate` prefab**（`UIService.cs:195`）——
   调用方持有的 prefab 原件上的组件**不是**屏幕上那个；要操作实例控件必须经返回的
   `UIForm.Root` 查找（本批 DialogService 即此做法）。
2. **`Assets/UI/Widgets/` 下 24 个 prefab 全部无 Canvas**，只有 `Screens/UIMain.prefab` 有——
   把子控件 prefab 当页面打开会抛 `MissingComponentException: There is no 'Canvas'`。
   这是弹窗分类裁决的直接依据。

**方法论沉淀**：上一批 40+ 轮"改-编译-跑"未中的根因即"未先隔离最小可复现"；本批用独立探针
fixture 复现并二分（泵动条件一改即绿），一轮定位——教训成立，照此执行。


### 施工记录

#### 2026-09-25 · 导航与模态交付（含一处真实缺陷修复）

**① 导航协调者**（`Assets/LiteGame/UI/UINavigationController.cs`）

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

##### ⑤ 排队期取消失效——真实缺陷与根因

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

#### 2026-09-25 · LText 核心（U2-⑤a）

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

#### 2026-09-25 · FeedbackService 统一反馈入口（U2-⑥c）

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

#### 2026-09-25 · LText 控件接入（U2-⑤c）

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

#### 2026-09-25 · 反馈面归位（视觉单一来源裁决 + ⑥c 返工）

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
   （**2026-10-10 订正**：该构建器已随提交 `45aa839` 删除，本条为历史缺陷记录，重建产物作为 prefab 资产保留。）
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

#### 2026-09-26 · per-form 缓存策略列（U2-⑧，U1 余项收口）

U1 交付段预告"per-form 缓存策略列随 U2 表扩展"——本批把 §5.2"缓存策略为 Resident、LRU、DestroyOnClose"的**壳消费面**落地（代码 `e5fd46c`）：

- `UICacheStrategy` 枚举（`UIFormInfo.cs`）：`Lru`（默认——关闭入池，超预算按最近使用序淘汰）/ `Resident`（关闭入池但**不参与淘汰**，主界面/高频页，预算满也保留）/ `DestroyOnClose`（关即销毁——不进池，释放租约与 GameObject，低频/重资源页）。
- `UIService.CloseFormInternal`：DestroyOnClose 分流——不走回收池，直接完整销毁后广播 `FormClosed`（对齐 §4.4 离场语义）。
- `UIService.EvictBeyondBudget`：淘汰候选**排除 Resident**——Resident 仍计入 cached 总数（"Resident 也必须计入总预算"口径不变），超预算时只从 LRU 策略实例中按最近使用序淘汰最旧者。
- 测试：`UICacheStrategyEditModeTests`（3 例）——DestroyOnClose 关即销毁且再 Show 为全新实例、Resident 不参与 LRU 淘汰、超预算淘汰最旧而 Resident 幸存。`ReleaseLayoutEditModeTests` 同批整理注释与分类标注（零行为变化）。

**边界（表列配置接入未做）**：tbuiform 表未扩展该列、`UIFormCatalog` 投影未接线——`UIFormInfo.CacheStrategy` 当前恒为默认 `Lru`，生产页面全部按默认 LRU；`Resident`/`DestroyOnClose` 仅可编程构造（测试即此形态）。列加进表后投影才能读（"壳不碰 Luban 类型、投影在装配层"纪律不变）；主界面声明 Resident、低频页声明 DestroyOnClose 的实际配置随表列扩展批落地。

#### 2026-09-26 · 导航 Replace + 包③覆盖度余部（U2-⑨）

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

### 测试面

- `Assets/Tests/EditMode/UiNavModalEditModeTests.cs`（7 例，全替身零真资源）：
  单写者串行、队列满在创建前拒绝、等待超时可观测、**排队期取消不执行**、
  最顶模态优先 Back、模态射线遮蔽与恢复、UiFx 中断复位=基线。
- `Tests/LiteFramework.Core.Tests/Localization/LocalizationTests.cs`（48 例，L1）：
  查询与回退链、缺 key 两档策略与计数、语言切换（事件/同值不触发/空值忽略）、
  参数替换与转义、越界与未闭合占位、复数（en one/other、zh 一律 other、无 one 回退 other、
  规则参数化）、key 命名正反例、表解析（正常/严格拒绝/宽松跳过/畸形）、覆盖率诊断。

### 验证证据

| 项 | 命令 | 结果 |
|---|---|---|
| L1 | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **761 通过 / 0 失败**（本批 +48 例） |
| L2 | `powershell -NoProfile -File scripts/gate/l2-unity-gate.ps1` | **通过**——12042 个 .meta GUID 全合法；Unity 编译零错误；EditMode **189/189**（含新增 `Screens/Dialog.prefab`） |
| L2（反馈面归位批） | `unity command run_tests --mode editor` | EditMode **217/217**（+10 FeedbackService、+7 视觉单一来源）；Unity 编译零错误 |
| L1（反馈面归位批） | `powershell -NoProfile -File scripts/test.ps1 -Lane L1 -Profile PullRequest` | **761 通过 / 0 失败**（含 buildHash 闭包调整后的复算守卫） |
| L2（缓存策略批 U2-⑧，2026-09-26） | `unity command run_tests --mode editor` / `--mode playmode` | EditMode **225/225**（装配批 222 + 本批 3）；PlayMode **12/12**；Unity 编译零错误。L1 778（本批未动 dotnet 侧，沿用两次一致实测） |

### 已知边界

- ~~**U2-⑤b/⑤d 与 ⑦ 未交付**（字体与 SafeArea、打字机、焦点）~~ **已于 2026-10-09 交付**（见文末批段）；
  U2 退出条件"大厅、长列表、HUD、确认弹窗四个真实消费者闭环 + 中英/输入/异常用例通过"仍待
  真实消费者页面（大厅/结算归 G3 消费者批）；已交付接缝 = 导航、模态、LText 服务层与控件接入、
  字体族 fallback、SafeArea 模板应用、打字机、焦点协调。
- ~~**无 PlayMode**：本批为 EditMode 全替身验证；真资源场景下的导航/模态行为未验证（L2 PlayMode lane 未建）。~~
  **已于 2026-09-25 消解**：`Assets/Tests/UI/PlayMode` 建成并接入同一门禁；**反馈面归位批的 Loading/Toast/错误弹窗已补真 prefab + 真转场 PlayMode 用例**（3 例），
  另覆盖真 Lua 页面生命周期、暂停/覆盖状态语义、动画资源组合。见 [框架先行记录](框架先行与验收线.md#ch1-框架先行)。
  **仍未在 PlayMode 覆盖**：本批的导航（Go/Back/Replace）与列表滚动、DevReload 环境重建段。
- **队列上限/超时为候选配置**（8 / 10s），标注"目标设备与真实包验证后调整"——尚无实测依据。
- **Toast 无淡入淡出**：多条并存的堆叠与到期是硬切（`SetActive`），表现层过渡随制作线。
- **19 件模板按构建器重建**：以生成器为准的裁决已执行，但差异（外观/结构）未经视觉评审；
  `WidgetPrefabCheck` 只覆盖可代码驱动的行为面，观感差异需人工过目。

#### 2026-10-04 · 业务期视觉权属裁决（制作侧同规：不走代码生成）

**裁决**（用户，G2 业务开发启动前）：**视觉相关不走代码生成，除非需要运行时逻辑调整视觉**。

- **制作侧**：业务 UI 页面/控件一律**编辑器手工制作 prefab 资产**——`WidgetPrefabBuilder.BuildListScreens` 确定性生成是**框架期测试先例**（BaselineA/B、ListScreen），不是业务期路径；
- **运行侧（既有 §7 不变）**：代码触碰视觉的唯一合法形态是**运行时逻辑驱动**——随状态/输入/瞄准点在 prefab 实例上写派生值（位置/参数/可见性/混合权重），不构建结构（不 new GameObject/不 AddComponent 视觉件）。

已同步：[UI 框架总设计 §7](../设计文档/客户端/UI/UI框架总设计.md)（制作侧同规并入视觉单一来源条目）、[修改指南「跨域接入」UI 行](../修改指南.md)（扩展点改为手工 prefab + tbuiform 条目 + UIBindIndex，构建器标注为框架期先例）。

#### 2026-10-09 · U2 余部（⑤b/⑤d/⑦）：字体族 / SafeArea / 打字机 / 焦点

> 状态：**已完成（附验证证据）**（EditMode 新用例 30 例已编译进包、留夜间 L2 跑）

**⑤b 字体族**：TMP 全局 fallback 链挂 `Assets/Fonts/NotoSansSC-Regular SDF`（7189 字形）并把图集改 **Dynamic + 多图集**（fallback 的"缺字动态补图集"标准形态；经用户裁决复用现有资产而非技能默认新建）。`Assets/Fonts/` 与 `Assets/TextMesh Pro/` 按资源规范不入 VCS——**配置契约由 `UiFontFamilyEditModeTests` 钉死**（干净克隆缺资产即红、按断言提示补配）；OFL 许可文件随资产归位。新增 `FontGlyphAudit`（§9"检查缺字"）：主字体→fallback 逐级覆盖判定（与 TMP 运行时查找序一致）、控制字符不计、按字符去重、报告可读；文本表（tbtext）落库后由装载侧平铺全量文本进审计入口。

**⑤b SafeArea**：`ListScreen.VirtualList` 挂 `SafeAreaReceiver`（贴边交互容器——UIMain/SafeArea 节先例同型；Dialog/Loading/Toast 均为居中内容不触边，不强挂）。EditMode 四形态（§7"至少覆盖常规横屏、超宽屏、刘海、窗口缩放"）经 `ISafeAreaStrategy` 注入缝：锚点归一数学、变化即重算不残留旧锚点、offset 归零锚点式几何；另钉模板契约（UIMain.SafeArea 与 ListScreen.VirtualList 必须挂接收器）。

**⑤d 打字机**：`LTextLabel`（§9"LTextLabel 打字机为表现行为，关闭/语言变化取消旧任务；不改变业务数据"）——TMP/UGUI 双目标（解析序与绑定层同口径）、`UiAnimationClock.Delta` 步进（UIClock 分域）、AutoTick/外部 Tick 单驱动者（双驱动 Debug 显性报错）、隐藏时 Reveal 登记**待播**（OnEnable 起播——不在暗处跑完）、取消落全量（数据不变——重开不残留半截）、**富文本截点不落标签内**（中间态无半截标签）。路由：`UIBindIndex.SetText`（命令式/本地化路径）经 Reveal 进打字机，**绑定路径（BindText/WriteTextRaw）恒瞬时**（数值不走表现行为）；语言变更经绑定重写文本自然取消旧任务。节点挂组件=行为声明，既有页面零影响（模板未挂即直写不变）。

**⑦ 焦点**：`UIFocusCoordinator`（§6.2"焦点保存/恢复、键盘/手柄 Submit/Cancel 和平台返回统一处理"）——焦点事实单源 = EventSystem 选择（焦点可见走 Selectable Selected 原生过渡态，不建第二套视觉）；打开接管首个可交互件（深度优先序确定性）、遮盖/暂停让出并保存、揭示/续跑**不抢占**恢复（他页持焦点时不动）、关闭摘除并清本页选择；EventSystem 解析缝注入（EditMode 直测——`EventSystem.current` 在编辑器不更新，实测）。`UiInput`（`Assets/LiteGame/UI/Input/`）输入底座：`[UIEventSystem]` 幂等建位 + `InputSystemUIInputModule.AssignDefaultActions()` 默认动作集（键盘/手柄导航与 Submit/Cancel 的引擎半部）+ 平台返回键直读（桌面先行口径 = ESC）；**§7 输入基础设施例外**入视觉扫描器白名单（与 [UIRoot] 层级骨架同判据）+ 总设计 §7 判据同步；**R12 边界表加 `Assets/LiteGame/UI/Input/` 判例**（同 `UI/Anim` 的 DOTween——游戏侧专属适配住专属子目录，InputSystem 直读收敛在一个文件）。平台返回统一口径：ESC 轮询（UIService.Tick）→ 协调者 → `UiShellModule` 装配点接 `UINavigationController.BackAsync`（Back 目标推导/BackInterceptor 拦截链归导航器，不重复实现）。

**用例**：`UiFontFamilyEditModeTests`（5：fallback 配置契约/中英代表文本全覆盖/逐级命中/缺字检出+控制字符+去重/报告）、`SafeAreaEditModeTests`（6：四形态+变化刷新+模板契约）、`LTextTypewriterEditModeTests`（9：节拍/完成一致恰一次/富文本配平×2/新 Reveal 取消/隐藏取消落全量/速率非正/待播/命令式路由/绑定恒瞬时/语言变更重入）、`UIFocusEditModeTests`（10：接管/无件不动/遮盖揭示/暂停续跑/不抢占/关闭摘除/不误清他页/无底座安全/平台返回转发/Tick 安全）——共 **30 例**，随夜间 L2 EditMode 段首跑。

**验证证据**（2026-10-09，白天门禁口径）：
- Unity 编译：pipeline `recompile` → `recompile_status = {"status":"completed","failed":false,"errors":[]}`（并行线 TestMode 重构 WIP 一度断编——`TestModeRuntime` 成员改走而 DebugTuner/ProcedureMain 悬空，其收敛后全量绿；期间 refresh 通道静默不编译、陈旧 DLL 掩盖 R12 表更新——`recompile` 强制后实证）。
- 纪律扫描：`RunDisciplineScan.All` → **lint OK（0 violations）**（R12 新边界生效后复跑）。
- L1：本批未动 dotnet 侧（LiteGame.UI 不在 L1 源集）——沿用 1531/1531（2026-10-09 输入链批时点实测）。
- L2（EditMode/PlayMode）未跑，留夜间流水线（用户裁决：白天批不跑 Unity 用例）。

**遗留登记**：
- **ESC→BackAsync 的运行期行为**（真按键→导航器 Back 链→页面关闭）与 **InputSystemUIInputModule 默认动作集**（键盘/手柄导航、Submit/Cancel）为引擎半部——实机/PlayMode 验证归夜间与 G2 真实对局段；EditMode 已钉簿记与转发语义。
- **根页面 Back 策略**（仅剩主页时按 ESC 的产品行为——退出确认/忽略）未裁决：现走导航器 BackInterceptor 优先链的既有语义（无拦截即关闭栈顶），产品策略随大厅批。
- **Android 返回键**随平台适配批接 InputSystem Action（现桌面先行口径 = ESC 直读）。
- **打字机速率/焦点序的页面级配置**（如 UIFormInfo 声明初始焦点控件/叙事 cps）未做——按"随消费者"留真实叙事页批。

---

## 第4章 UI 工具链

<a id="ch4-UI工具链"></a>

> **原文件**：`Docs/施工进度/UI工具链.md`（归并前独立成文，内容整段并入本章）

《UI编辑器工具专项设计》落地的施工记录。设计文档只描述最新设计，本文件只保留最新进度。

### 执行顺序（依专项设计 §7 排期 + §5 优先级）

| 步 | 工具 | 状态 | 设计依据 |
|---|---|---|---|
| 1 | **校验核心** | **已交付（2026-10-11）** | §4 |
| 2 | **绑定标记工具（底层两项：词表单源 + 绑定命名规则）+ 全仓命名整改** | **已交付（2026-10-11）** | §2 |
| 3 | **绑定标记工具（窗口/前缀预设 UI/批量重命名/引用点提示/生成物 hash）** | **已交付（2026-10-12）** | §2 |
| 4 | **样式工具补齐（`UiTextStyleRef` 文字档 + 对账扩 `Screens/`）** | **已交付（2026-10-13）** | §3 |
| 5 | **校验进 CI（生成物 hash 对账 + 门禁接入 + 资源族规则「表内 prefab 路径必须存在」）** | **已交付（2026-10-13）** | §4 + 修改指南 §2.3 |
| 6 | **编排面板（宿主窗口 + 绑定暴露/受控属性/校验·生成/控件库四分区；Odin 呈现层）** | **已交付（2026-10-15）** | §5 |
| 7 | UIPatch 调试补丁 | 待做（随 U4 消费者） | §6 |

### 步 1：校验核心（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Validation/`

| 件 | 职责 |
|---|---|
| `UiRuleReport.cs` | 报告模型：`UiIssue{ruleId,severity,asset,node,message,fixHint}` + `UiSeverity`（Info/Warning/Error）+ `UiRuleFamily`（含"是否已接入"）+ `UiReport`（收集/汇总/文本渲染，含"待接入族"栏） |
| `IUiRule.cs` | 规则端口：`RuleId`/`Family`/`Check(context)`；`UiValidationContext` **双侧**输入面（`SourceDirs` 源码面 + `AssetPaths` 资产面 + `ProjectRoot`） |
| `UiValidator.cs` | 校验核心：规则注册表 + `Run(context)→UiReport`；六族表 `Families`（未接入族显式登记）；菜单 `LiteGame/UI/校验/校验 UI 资产`；CLI `RunCli`（Error 非零退出）；`BuildDefaultContext`（源码扫描面 + Screens/Widgets 资产） |
| `Rules/RuntimeVisualRule.cs` | **首条规则**（渲染/适配族，`ui.runtime-visual`）：包既有 `VisualConstructionScanner`（源码面），产出统一报告项 |

**关键约束（§4 落地）**：
- 同一核心三入口共用（菜单/CLI/EditMode 测试）——不各写一套；
- 未接入的五个规则族（绑定/资源/文本/行为/性能）在报告里**显式列为"待接入"，不显示为自动通过**；
- 首条规则**只包装** `VisualConstructionScanner`，不改其判定（该扫描器仍由 `VisualSingleSourceEditModeTests` 直接消费）。

**为何首条选它**：视觉单一来源是既有唯一规则（零风险包装即可验证端口形状）；校验核心是其余工具（绑定标记/样式/编排面板/CI）的公共底座——故第一步做核心 + 首条规则。

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiValidationEditModeTests` | **6/6**（正例×2 / 负例×1 / 报告契约×2 / 端口×1） |
| 三入口共用 | 菜单 `LiteGame/UI/校验/校验 UI 资产` 跑通；`RunCli` 静态入口就位（Error→退出码 1）；测试直调核心 |
| L1 | **1594/1594** |
| 报告形状 | 窄上下文实跑：`PASSED=True errors=0`；"待接入规则族"列出 5 族（绑定/资源/文本/行为/性能），"渲染/适配"已接入 |

### 遗留（如实）

- **全量扫描慢**（~60s，扫 4 个源码目录——与 `VisualSingleSource` 同因）；经 unity-pipeline `run_script` 调用会撞其 30s 管道上限，但**菜单/CLI/EditMode 三入口无此限**，属工具调用方式而非校验器问题。后续若需 CI 高频跑，评估增量扫描或缓存；
- 边界：本步**只落地 1 条规则**，其余五族为骨架占位（`Families` 登记）；绑定/资源等资产面规则留后续步。

### 步 2：绑定标记工具·底层两项 + 全仓命名整改（已交付）

#### 交付物

| 件 | 落点 |
|---|---|
| 词表单源 | `Assets/LiteGame/Editor/UiTools/Naming/UiNamingWords.cs`——《命名词表与控件清单》**机器可读副本（权威）**：角色词 8 / 控件段 7 / 模板段 20 / `Tmp_` / 语义词 20；§9.2 文法正则**单源**（`GrammarPattern`）；查询口 `TryParse`（按首下划线切分）/ `IsKnownPrefix` / `HasExactlyOneUnderscore` |
| 绑定命名规则 | `Validation/Rules/BindingNamingRule.cs`（`ui.binding-naming`，**资产面**）：节点名过 §9.2 文法（Error）；前缀段属词表（未登记=Warning，§1 允许登记扩展）；`_` 私有件（模板内合法／**页面节点禁**）；**根名**走 `Page_`/`Hud_`/`Tmp_` 判据（存量页面根=Warning，§9.6 待页面批次）；**UGUI 契约件**（Slider fill/handle、ScrollRect viewport/content、Toggle graphic/target）与**模板实例内部件**按身份豁免 |
| 核心接入 | `UiValidator`：绑定族 `implemented=true`；规则注册表加 `BindingNamingRule`；资产面扩 `Hud/` |

#### 全仓命名整改（§9.6"随模板批次整形"，本批即该批）

| 面 | 处置 |
|---|---|
| 模板/夹具**根** | 改**文件名** `Tmp_<控件类型>`（Unity 规则"prefab 根名=文件名"）——27 件 Widgets + 6 件 Screens 夹具，走 `AssetDatabase.MoveAsset`（**保 guid**，实例/引用不断链；collector `AddressByFileName` 地址随名自动跟随） |
| 模板**内部件** | 词缀化：Button→`Button_<语义>`（剥 `Btn`）、文本→`Txt_<语义>`、纯实现件→`_<语义>`（`Blocker→_Blocker`、`Frame→_Frame`、`Star0..4→_Star0..4`）；**UGUI 契约件保留原名** |
| **克隆条目模板** | `_Template/_ItemTemplate` → `Clone_Template`/`Clone_Item`（§9.6） |
| 业务节点 | `UIMain/BtnClose→Button_Close`（§9.6 登记债，同批清；`BindName` 同步）；弹药分隔符 `Txt_/`→`Txt_Slash` |
| 夹具实例根 | `Baseline→Cot_Baseline`、`Loading→Loading_Block`、`Toast→Toast_Info`、`VirtualList→VirtualList_Rooms`、`Content→Dialog_Confirm` |
| 代码同步 | `UIDemoPage.Templates[]` 改 `Tmp_*`；`HpBar.cs`/`BattleHudView.cs`/`BattleHudDriver.cs` 文档注释同步 |
| 合计 | **35 个 prefab / 93 处节点改名 + 33 个文件改名**；全树 **0 MISSING 组件** |

#### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| 规则重跑 | **Error 0 / Warning 1**（仅 `UIMain` 根——§9.6 登记的页面根债，随页面批次；**不冒充通过**） |
| `UiValidationEditModeTests` | **9/9**（增绑定用例：合法名通过／多下划线 Error／自造段 Warning／页面 `_` Error／模板 `_` 合法／端口标识） |
| `VisualSingleSourceEditModeTests` | **8/8**（回归） |
| L1 | **1595/1595** |
| 实例不断链 | `Hud_Battle` 血条实例源正确解析为 `Tmp_HpBar.prefab`；`HpBar` 组件接线有效（`Slider=_Slider`） |
| 执行脚本 | `AgentScripts/ApplyUiRenames.cs`（单树规划+改名，幂等）+ `RenameTemplateFiles.cs`（保 guid 文件改名），未入库 |

#### 遗留（如实）

- **表/代码的按路径引用未跟改名**——**已修（2026-10-12 表路径前置批，见下节）**：文件改名保 guid 不断序列化链，按路径消费面（表 201-206、探针、夹具）已全量同步；且清扫面**超出本登记**——`ListPlayModeTests`、`SafeAreaEditModeTests`（路径＋两处节点名断言）、`UiU0EditModeTests`（节点查找）、`FlyTextPoolEditModeTests`（注释口径）同为漏网者（"审计面≠消费者面"第 N 次实锤）。"表内资源路径存在性"门禁仍留步 5；
- **`UIMain` 根名**未改（§9.6 明载"牵动流程/表引用，随页面批次裁决"）——规则报 Warning 追踪；
- **`Hud_Battle.prefab` 含制作侧未提交的 sprite 改动**（Synty → 自制 `Icons/`），本批对其只做 `Txt_/`→`Txt_Slash` 一处命名修正，**文件整体未随批提交**（随制作侧 sprite 批一并入）；
- 中途一次**误操作**（首版"盘上树规划 + 加载树按序改名"遍历序失配，弄乱 5 个 prefab）——已 `git checkout HEAD` 全部回滚，改**单一树**方案重跑，0 MISSING 佐证无残留；
- 窗口树/前缀预设 UI/批量重命名交互/引用点提示/生成物 hash——已随**步 3** 交付（见下）。

### uiform 表路径悬空修复（2026-10-12，步 3 前置批）

表源 201-206 六行 → `Tmp_*` → `toolbox.py luban` 重生成（bytes+lua 发布）→ `gen-build-hash.py` 重跑（shared+meta 双端）→ 按路径引用全量同步（探针 + 4 夹具 + 清扫出的 4 处漏网，清单见上节勘误后的登记项）。

**另修一桩双 LoadingMask**：`Screens/Tmp_Loading.prefab` 自 W1 起同时带壳根本地 mask+视觉与嵌套模板实例 mask——服务写入面 `GetComponentInChildren` 命中壳根、测试读取面 `FindObjectOfType` 命中嵌套，文案永远对不上（被路径红掩盖，PlayMode 首跑暴露）。按《通用加载页设计》§2 清壳：根只留 Canvas+CanvasGroup+嵌套实例（`Loading_Block` 单一 mask），W1 错置在壳上的本地视觉一并退场（批 A 手工制作时在模板本体重建）。

**验证**：L2 PlayMode 全量 **37/40**——本批涉及面全绿（FeedbackSurface 3/3〔含双 mask 修复〕、ListPlayMode 3/3、Navigation 4/4、UiState 3/3、租约组中非 Test.unity 依赖项全绿）；3 红＝Test.unity 既有债（见步 3 遗留）。EditMode 428 中改名跟随面全绿（SafeArea/FlyTextPool/UiU0/UiValidation）。

### 步 3：绑定标记工具（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Binding/`

| 件 | 职责 |
|---|---|
| `UiBindingRename.cs` | 命名引擎：树构建（页面/列表项〔Clone_ 子树〕作用域分列）、前缀组合（§9.5 词表下拉+仅填语义名，构造性过 §9.2 文法）、改名计划（节点名与 BindName **同批同写**——§9.1 同文；空 BindName 模板标记不发明索引名；漂移面按新名收口）、计划校验（文法+同批重名+现存兄弟占用，应用前挡）、应用（**深度序**——父改名不踩子路径） |
| `UiBindingReferenceScan.cs` | 引用点扫描：改名联动的 Lua/C# 字符串字面量清单（只提示不代改——§9.2 联动面是作者责任）；默认扫描根＝Lua/UI/App/Tests，命中带 file:line |
| `UiBindGenerationHash.cs` | 生成物 hash（§2.1.5）：绑定面指纹（BindRoot 配置 + BindSearch 同一遍历语义，嵌套生成根独立）＋清单（Editor 域 JSON，键=prefab GUID 改名不断链）＋对账（NotRecorded/Stale/GeneratedMissing=Error、OrphanEntry=Warning；GUIDToAssetPath 同 tick 删除缓存滞后→再核磁盘真身）；菜单 `LiteGame/UI/绑定/生成物 hash/记录（选中/全部）` |
| `Validation/Rules/UiBindingHashRule.cs` | `ui.binding-hash`（绑定族）：hash 对账进**校验核心三入口**（菜单/CLI/EditMode 共用）——CI 走既有 `UiValidator.RunCli`，不另起旁路；**记录是作者动作**，规则只判漂移不代记录（未记录/漂移显红，不冒充通过） |
| `UiBindingWindow.cs` | 窗口（菜单 `LiteGame/UI/绑定/绑定标记工具`）：树展示（BindName/类型徽标、**同文漂移显形**、Clone_ 作用域标记、生成根标记）、前缀预设（回显解析现名→前缀下拉+语义输入→预览所选）、批量（换前缀保留语义〔自造段可治愈到词表段〕／同文修复 BindName:=节点名）、**预览先行**（计划表+引用点清单+错误挡板）、生成物 hash 面板（状态+记录+校验） |
| `IUiRule.cs`（扩展） | `UiValidationContext.BindManifestPath`——清单注入缝（测试用临时清单隔离全局清单状态） |

**写入通道**（§1"撤销用 Prefab Stage；资产写入经 Pipeline API"）：目标 prefab 开 Prefab Stage → 编辑 Stage 实例**全程 Unity Undo**（保存归 Stage）；未开 Stage → `LoadPrefabContents` 改后 `SaveAsPrefabAsset`（资产文件写无编辑器级 Undo——预览先行兜底；选择/勾选变动即作废旧预览）。

#### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiBindingToolsEditModeTests` | **12/12**：组合文法／计划同文三形态（同步·空不发明·漂移收口）／批量换前缀（含自造段治愈、语义非法跳过）／重名挡应用前／父子同批深度序落位／引用扫描（cs+lua 合成件、真实 `UIMain.lua` 命中）／hash 同内容稳定+绑定面敏感+嵌套根独立／记录-校验-漂移-重记录-生成物缺失闭环（经校验核心规则面）／孤儿条目／规则端口／窗口绑定建树冒烟 |
| 全量 EditMode | **417/428**——新增 12 全绿；11 红为本批前既有（见遗留），本批零新增 |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636**（LiteSim 343 · LiteNet 543 · LiteTesting 7 · LiteFramework 715〔**纪律扫描在内，新文件零违规**〕· MetaServer 139〔PR 过滤口径；直跑全量另得 160/160〕） |
| 真实资产规则重跑 | `PASSED=True errors=0 warnings=1`——唯一 Warning 即 §9.6 的 UIMain 根名债（页面批次追踪中）；hash 规则零问题（现役 BindRoot=0 的诚实空态） |
| PlayMode | 步 3 批面 37/40（3 红为 Test.unity 既有债）；**2026-10-13 收口批后全量 40/40**（见遗留节清账） |

#### 遗留（如实）

- **窗口交互面属手测**：IMGUI 壳无自动化覆盖（冒烟用例只盖绑定/建树逻辑）；真实改名的走查随制作侧使用反馈，逐步 6 编排面板批一并打磨；
- **预设面落点＝工具窗口**（非 BindNode 组件 Inspector）——保 LiteCodeGen 零 LiteGame 依赖（词表单源不外泄）；《制作规范》§9.5 工具现状行已随批改口径；
- **同文修复批操作未对现役漂移点执行**：`UIMain` 的 BtnClose/Label 漂移收口归**页面批次**（须连 Lua `OnButton("BtnClose")` 同批改——工具只出引用点清单不代改）；
- **步 2 记录勘误**：步 2 曾登记「UIMain/BtnClose→Button_Close，BindName 同步」——与真身不符，**BindName 未同步**（§9.6 表行已勘误；功能链自洽，属同文漂移非断链）；
- **既有红——已清零（2026-10-13 收口批，EditMode 428/428 · PlayMode 40/40）**：
  - `Test.unity` 经用户改名 `Boot.unity`：**场景入库**（gitignore 例外移除）+ 全仓消费者改指（两条 PlayMode 用例、`BuildProfiles` 双端构建清单〔构建现场断链同批修复〕、三处活文档）——"删共享资产前全仓消费者清扫"的实锤案例；
  - `LTextTypewriter`×7＝**组件实缺非用例错**：截点表缺 0 哨兵（起播偏一显示两字符）、富文本截点未配平（`<color=red>红` 留未闭合开标签）、完成晚一拍（须越过末截点才收）、生命周期非 ExecuteAlways（隐藏取消/待播激活语义 EditMode 不触发）——修组件；另加守卫：无任务不落文本（编辑器装载/卸载 prefab 不碰资产文本）；
  - `BattleLaser`×3＝**用例字面量手推口径错**：命中柱半径 `HitscanRadius=0.45`（注释误写视觉身位 BodyBake 0.32——近弧差 0.13 是 z 漂移主因）+ 全精度烘焙常量斜率；按 SimRaycast 同式复算逐位对账后重校（判定几何权威守卫在 L1 `AimParallaxHitTests`，本组用例职责＝View 层端点映射）；
  - `BattleHud.弹药`＝用例笔误：未装备约定 `SelectedWeapon=-1` 须显式置位（C# int 默认 0＝装备槽 0 非未装备）；
  - 遗留一条**弱断言债**：`租约_取到即被释放的竞态` 只断言异常类型——"已释放"与"未初始化"两分支同过（级联根因已随场景修复消失，断言强度债仍在）；
- **生成物 hash 首个真实消费者待路径 B 启用**（BindRoot 现役 0）：记录/校验链路已就绪，步 5 已随门禁测试接入夜门；「表内 prefab 路径必须存在」资源族规则已随步 5 交付（见下）。

### 步 4：样式工具补齐（已交付）

**落点**：`Assets/LiteGame/UI/Style/`（运行时标记）+ `Assets/LiteGame/Editor/Style/`（档位单源与工具）

| 件 | 职责 |
|---|---|
| `UI/Style/UiTextStyleRef.cs` | 运行时**声明标记**（自有组件无 Odin）：`UiTextRole` 五角色（Title/Body/Caption/LabelSingle/Emphasis）；只声明不驱动——工具按声明应用/对账，运行时按状态改实例合法 |
| `Editor/Style/UiTextStyle.cs` | 文字档**单源**（与 UiStyle 颜色 token 同构分工——不复制颜色值）：§3.1 基线表 = 字号 24/20/14/20/20、对齐（完整对齐——垂直统一中行）、换行关/关/开/关/开、初始溢出 Truncate/Overflow/Overflow/Truncate/Overflow、Emphasis 唯一加粗档；TMP/UGUI 双映射 |
| `Editor/Style/UiTextStyleTool.cs` | **应用**（按标记精确写入，逐项比对跳过→幂等；失效标记/未知角色不猜不改）+ **对账**（三类：未标记〔整改清单〕/属性漂移〔逐项明细〕/引用失效〔font 缺失 + 标记无文本组件〕）；菜单「应用文字档/对账文字档」；扫描根可注入（测试用） |
| `Editor/Style/UiStyleTool.cs`（改） | 颜色面**扩 Screens**（§3.2 作用域 Widgets+Screens）；扫描根可注入；菜单文案随作用域改「刷新 UI 颜色/对账 UI 颜色」；对账返回结构化清单 |

#### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiTextStyleEditModeTests` | **8/8**：基线表值逐项核对（§3.1 数字）/ TMP 全属性写入+幂等（含 Italic 位不受字重档干扰）/ UGUI 映射（int 字号/加粗）/ 加粗位只动 Bold / 未标记报告不冒充 / 属性漂移报明细 / 引用失效两类（font 缺失须**非激活节点**——TMP OnEnable 自愈 null font 实证）/ 扫描根注入隔离 |
| 全量 EditMode | **436/436**（428 + 8 新增，零新增红） |
| 真实资产对账首跑基线 | 文字档：**未标记 77 / 属性漂移 0 / 引用失效 0**（标记面从零起步的诚实基线）；颜色（扩面后）：漂移 76 处（既有手调/非 token 色——报告不擅改） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636**（纪律扫描在内，新文件零违规） |

#### 遗留（如实）

- **标记面从零起步**：现役 33 个 prefab 的 77 处文本全部未标记（首跑基线）——整改清单随制作侧逐批标记（"未标记先报告，按整改清单收口"，§3.2）；对账/应用对未标记文本零副作用；
- **"声明覆盖"类报告待 SO 覆盖机制**（§3.2 第四类漂移分类）：基线即唯一声明面，合法偏离基线的覆盖声明随换肤/主题需求接 SO 资产（UiStyleAsset 同款先例）；
- **AutoSizing 范围声明**（§3.1"必须声明范围与验证样例"）待首个真实用例接入；
- **UGUI 无水平 Truncate**：不换行档映射 HorizontalOverflow=Overflow、水平截断由容器/Mask 承担（与 TMP Truncate 的视觉等价物是容器职责——口径已在工具注释登记）；
- 字号阶梯是**校验参考**不自动改值（§3.1"不把 22 等数值不看页面就自动改成 24"）——标记+应用是显式动作。

### 步 5：校验进 CI（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Validation/`（新规则）+ `UiValidator`（注册与族登记）+ `LiteGame.Editor.asmdef`（补 `Luban.Runtime` 引用——规则装载表产物用 ByteBuf）

| 件 | 职责 |
|---|---|
| `Rules/AssetTablePathRule.cs`（`ui.asset-table-paths`，资源族首条） | **表内资源路径存在性**——装载全部 Luban 表，反射每行公开字符串字段，`.prefab`/`.unity` 结尾即资产路径（无 `Assets/` 前缀按表内惯例补前），盘上不存在＝Error（行定位键随报告）；**通用反射面不枚举列**——未来任何表加路径列即被同一门禁罩住（uiform 悬空两次实锤的机制化，非点位补丁）；表目录整体缺席＝非工程上下文跳过（合成测试根）、单件产物缺失＝Warning；`CheckPaths` 纯核对面供合成负例直喂 |
| `UiValidator`（改） | 资源族 `implemented=true`（"表内路径存在性已接入；其余子项待接入"）+ 规则注册表加 `AssetTablePathRule` |
| 门禁测试 `门禁_真实资产面_命名hash表路径零Error` | **CI 接线本体**：三条资产面规则（命名/hash/表路径）跑真实数据断言零 Error——随 L2 门禁的 EditMode 段自动进夜门（Pipeline 模式 A / batchmode 模式 B 同入口）；视觉面由 `VisualSingleSourceEditModeTests` 既有用例单守（不重复 53s 全量源码扫描）；既有 `RunCli`（`-executeMethod` 非零退出）保留为 CI 批处理备用入口 |

#### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiValidationEditModeTests` | **14/14**（原 9 + 新 5：规则端口／路径判据不误伤 LuaPath／核对面悬空×2 报 Error 且在盘零误报／**真实表装载提取零悬空**（uiform 修复的回归钉，≥11 行实提取）／门禁真实资产面三规则零 Error＋唯一 Warning＝UIMain 根名债） |
| 既有契约用例随动 | 「资源族不再列待接入」断言翻转、「合成上下文零噪声」由表目录缺席跳过守卫保证（`报告项_五字段齐全` 复绿） |
| 全量 EditMode | **441/441**（436 + 5 新增，零新增红） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1636** |

#### 遗留（如实）

- **资源族其余子项待接入**（缺引用其余面/收集组/图集字体名册/生产包排除调试补丁——§4 资源族清单），随真实需求逐条接；
- ~~表清单为**显式登记**（九表 `(file, type)` 数组）——新表入 `Tables.cs` 后须同批补 `AssetTablePathRule.Tables` 一行~~ **已修（2026-10-13 表清单单源化，提交 `5e7fe22`；补记见[上云批C收尾补记](上云测试线.md#ch8-上云批C收尾补记)）**：清单改 `TableLayout.Files` 派生（`TableName` 裸表名→`cfg` 类名约定解析、失败 fail-fast）、`TableDataDir` 与装载/核对两处拼接口径同批走 `TableLayout.DataDir`/`TableDataPath` 单源——"新表接入第五件"退役（新表入清单零登记成本）；该债 2026-10-13 曾按红线 5 登记进[代码质量审计-客户端](../审计/代码质量审计-客户端.md) §二（"必须与 X 一致"只靠注释的违规形态），修复登记已随批C-⑤ 回写；
- 门禁测试断言 **Error=0 + Warning=1**——Warning 数随页面批次（UIMain 根名）收口后应改回 0 基线，届时同批更新。

### 步 6：编排面板（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Compose/`（宿主与四分区）＋ `Assets/LiteGame/Editor/UiTools/Binding/UiBindTreeView.cs`（共享只读树）＋ 运行时标记 `Assets/LiteGame/UI/UiShellPropertyAttribute.cs`

| 件 | 职责 | Sirenix |
|---|---|---|
| `UiComposeWindow.cs` | 宿主窗口（`OdinEditorWindow`，菜单 `LiteGame/UI/编排/UI 编排面板`）：Odin 绘制目标 prefab 与状态行（置页签组之上）+ **四个 Odin `TabGroup` 页签**（绑定暴露/受控属性/校验·生成/控件库）；Stage 跟随 | 有 |
| `UiComposeTarget.cs` | 目标解析**单源**：`StageRootFor` / `Resolve` / `FindNodeForReport`（报告 node 含根名 vs 树口径不含根的收口） | 无 |
| `UiBindingsSection.cs` | 绑定暴露分区：内嵌**只读**树 + 跳转绑定标记工具编辑（编辑动作不搬进宿主） | 无 |
| `UiShellPropertyAttribute.cs`（运行时） | 受控面声明（`LiteGame.UI`；零依赖、只声明不驱动） | 无 |
| `UiShellPropertyCatalog.cs` | 白名单发现：自有程序集组件 + 标注字段；值型最小集（bool/int/float/string/enum）+ `Unsupported` 显式 | 无 |
| `UiShellPropertySection.cs` | 受控属性分区：逐组件 **Odin 属性树只画白名单成员**（写入/Undo/脏标记归属性树） | 有 |
| `UiValidationSection.cs` | 校验分区：**窄档**（资产面只喂本 prefab）+ 全量档；报告列表化 + 定位节点 | 无 |
| `UiGenerateSection.cs` | 生成分区：`BindGenerator.Generate` → 同一步 `UiBindGenerationHash.Record`（先 `DisplayDialog` 确认——生成触发域重载） | 无 |
| `UiWidgetLibrary.cs` / `UiWidgetInstaller.cs` | 控件库：**目录即清单**（扫 `Widgets/` 的 `Tmp_*`）＋**只在 Prefab Stage 内**插入（Undo/嵌套/Variant 归原生） | 无 |
| `UiWidgetLibrarySection.cs` | 控件库分区：**inline `OdinMenuTree` + Odin 搜索栏**（左树；替掉手写过滤框）+ 右侧选中模板的组件/受控面预览 + 插入按钮（父级＝Stage 内选中节点，缺省 Stage 根） | 有 |

**改动既有件（消重）**：`UiBindingWindow.DisplayRoot` 改调 `UiComposeTarget.StageRootFor`；`UiBindingWindow.DrawTree` 改调 `UiBindTreeView.Draw`（窗口行为零变化：勾选/选中/漂移色/徽标/Ping 全保留）；`LiteCodeGen.Editor/BindMenus.FindRootInPrefab` private→public（生成分区复用，不复制实现）；`Assets/Tests/EditMode/LiteGame.EditModeTests.asmdef` 补 `LiteCodeGen.Editor` 引用（测被复用的生成根查找）。

**受控面标注集（本轮最小）**：`HpBar.ValueFormat/BackLerpSpeed`、`ProgressBar.ValueFormat`、`Stepper.Min/Max/Step`、`CountText.Duration`——只标面向内容作者的展示性/行为性字段；**接线引用字段**（`Slider`/`ValueText`/`BackFill`/`Label`）不标（属内部实现）。

**依赖边界**：Odin 只进 `LiteGame.Editor`（`restore-check.ps1` §4 / `toolchain.json` 已把 Odin 列为**阻塞式环境项**）；呈现层＝宿主窗口 + **Odin `TabGroup` 页签** + 目标字段 + 受控属性的 Odin 属性树 + 控件库 inline `OdinMenuTree`；页签体（绑定树/报告）走既有 IMGUI 绘制器，在 Odin 页签内混排；逻辑类与运行时标记**零 Sirenix**——"摘除 Odin 只换绘制层"。

#### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误（含 Odin 引用面：`LiteGame.Editor.asmdef` 自动引用 `Sirenix.OdinInspector.Editor.dll` 成立） |
| Odin 形态实证 | `AgentScripts/VerifyComposeTabs.cs`（未入库复验脚本）：属性树断言页签组 `#Compose` 含四页签且页签名与组内成员全对；临时冒烟窗口强制绘制受控属性/控件库/校验三分区，连续重绘 38 帧**零 error/exception**（冒烟窗口用后即删）——`[OnInspectorGUI, TabGroup(组名, 页签名)]` 与 inline `OdinMenuTree` 可用性由此钉住 |
| `UiComposePanelEditModeTests` | **5/5**：目标解析两态（Stage/资产、非 prefab→null）／窄档报出本 prefab 命名违规并按报告节点反查定位／**报告 node 含根名 vs 树口径不含根**（两口径收口钉）／未接入族显式列出 |
| `UiShellPropertyEditModeTests` | **5/5**：白名单只出标注成员／接线字段不进面／非自有程序集组件不产行／值型归类（含 `Unsupported`）／**真实模板 `Tmp_HpBar` 受控行** |
| `UiComposeGenerateEditModeTests` | **2/2**：生成根查找三态（根上/子物体/无）／记录-校验闭环（未记录 Error → 记录后「未记录·漂移」清零，记录值＝当前绑定面指纹） |
| `UiWidgetLibraryEditModeTests` | **6/6**：真实资产枚举 ≥27 且全 `Tmp_`／Screens 目录不误收／模板名判据／使用段映射／插入守卫（未绑定·未开 Stage 不可插且给原因） |
| 全量 EditMode | **609/609 全绿**——本批新增 18 全绿、零新增红（并行的房间席位红〔`MetaPagesEditModeTests`×5 · `BattleRemoteFireEditModeTests`×1〕已在对方批内修净，本批不含） |
| L1 门禁 | `test.ps1 -Lane L1 -Profile PullRequest` **[PASS] 1694** |
| L2 Unity 门禁 | **未跑**——本批**零序列化资产改动**（只新增/改 `.cs`、`.asmdef`、文档；无 prefab/scene/material/SO 变更），PlayMode 面不受影响；L2 的 EditMode 段已由上面的全量 EditMode 跑覆盖 |
| BuildHash | **未跑亦无需跑**：本轮不碰共享源集/`.proto`/Luban 表源（`UiShellPropertyAttribute` 落 `LiteGame.UI`，不在 `gen-build-hash.py` 闭包内） |
| 窗口交互面 | 属手测（IMGUI/Odin 绘制不做自动化断言——与既有工具窗口同口径） |

#### 遗留（如实）

- **绘制面只做逻辑侧断言**：宿主/分区绘制、Odin 属性树行编辑、模板插入的真实走查随制作侧使用反馈；
- **受控面标注从最小集起步**（4 件控件 7 个字段）——其余控件随使用增量标注；未标注＝不进面（不冒充已覆盖）；
- **对象引用类值型不支持**（`Unsupported` 显式列出）——控件/资产引用属接线面，不进受控面；
- **缩略图未做**（RenderTexture 成本）——列表以模板名 + 使用段替代，登记为增强项；
- **全量档校验较慢**（源码面扫描 ~60s）——窗口默认窄档，全量档仅显式按钮且无进度/取消；
- **Odin 依赖面**：未按 `restore-check.ps1` 安装 Odin 的机器 `LiteGame.Editor` 编译失败——由既有环境政策承担，与归档《编辑器设计规划》"Odin 只进 LiteGame.Editor"硬纪律一致（不额外拆 asmdef）。

---

## 第5章 UI工作台W0（场景+驱动）

<a id="ch5-UI工作台W0"></a>

> **原文件**：`Docs/施工进度/UI工作台W0.md`（归并前独立成文，内容整段并入本章）

《UI工作台设计》W0 执行（2026-10-11）。dev-only 人工目检工作台：不跑完整业务流程即可开/关任意已登记页、驱动反馈面、目检状态。

### 状态

**已完成（Unity 编译证据齐全；实机手测待本机——dev 件不做自动断言，设计 §4）**。

### 交付

| 面 | 落点 |
|---|---|
| 场景 | `Assets/Scenes/UiWorkbench.unity`（原 HUDDesign 改造：改名保持 GUID、旧 HUD 树已清、`GameEntry`+主相机就位；不在 Build Settings——编辑器 Play 使用，同 SimSandbox 口径） |
| 驱动件 | `Assets/LiteGame/DevHUD/UIWorkbenchDriver.cs`——**不旁路装配**：Update 轮询 `GameEntry.TakeContainer()`，服务容器 Resolve（Navigation/UIService/FeedbackService/IConfigService/流程机）；就绪门控 = `ConfigService.Loaded` ∧ `StageMachine.Current == Main`（未就绪五钮置灰+状态行提示）；真链路照跑（Patch 本地通道、Main 加载玩法场景不阻止） |
| 面板 | `WorkbenchPanel`（Canvas Overlay sortingOrder -100——低于页面语义层；1920×1080 match .5；含 GraphicRaycaster，按钮可点）：`Cot_Forms` 目录列 + `Clone_FormRow` 行模板（Button_Open/Button_Close/Txt_FormId）+ `Cot_Demos` 演示组五钮 + `Txt_Status` 状态行 |
| 目录行 | 运行时按真表 `ConfigService.Tables.Tbuiform.DataList` 逐行克隆——**五页登记后自动出现、零改码**；系统面（201/202/203）行置灰（反馈面走演示组——统一入口纪律）；开= `UINavigationController.GoAsync(id)`、关= `UIService.CloseAsync(id)`（UniTaskVoid 包装，失败入状态行） |
| 演示组 | Loading 开（`BeginLoading`，作用域存字段）/ 进度上报（**占位**——`LoadingScope.Report` 随加载页批A 接线）/ Loading 收场（Dispose）/ Error 弹窗 / Toast |
| 状态行 | 0.5s 节流：流程阶段 / 就绪 / IsLoading / 最近操作结果 |
| asmdef | `LiteGame.DevHUD` 补三引用：`UniTask`、`Unity.TextMeshPro`、`Luban.Tables`（表行枚举+TMP+异步） |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | `completed / failed:false / 0 错误`（三轮收敛：asmdef 引用名/嵌套 LoadingScope/TMP 实例方法——均即时修正） |
| 面板构建 | run_script `DRIVER wired / SAVED`（SerializedObject 接线 9 字段；幂等重跑清旧重建） |
| 场景 | UiWorkbench.unity 含 WorkbenchPanel/GameEntry/Main Camera（pipeline rename GUID 保持） |

### 边界与防锈（设计 §4/§5 摘要）

- 工作台**不做自动断言、不进 gate**——断言与证据归 L2（夜间）；实机手测待本机（编辑器 Play 打开本场景即跑真装配链）。
- dev-only 不担产品职责；**不做整场景运行时保存**、prefab 回写工具归 W2（2026-10-11 裁决见设计 §2/§5）。
- 失效条件：① L2 PlayMode 覆盖其手测场景，或 ② 出现更顺的页面预览工具——到期退役。

---

## 第6章 UI工作台W1（进度演示接线 + Loading 视觉补齐）

<a id="ch6-UI工作台W1"></a>

> **原文件**：`Docs/施工进度/UI工作台W1.md`（归并前独立成文，内容整段并入本章）

《UI工作台设计》W1 执行（2026-10-11）。依赖说明：`LoadingScope.Report` 属《通用加载页设计》§3 契约、原未实现——本批随 W1 一并落地最小面（Report/缓存/回读），加载页表单化完整批仍归加载页线。

### 交付

| 面 | 落点 |
|---|---|
| 契约落地 | `FeedbackService.ReportLoading(message, progress01)`（缓存首写不丢/嵌套后写胜出/开面后写全量）+ `LoadingScope.Report` + `CurrentLoadingMessage` 回读；`BeginLoading` 复位写全量；`EndLoading` 清缓存 |
| 控件 | `LoadingMask` 补 `ProgressFill`（Filled/Horizontal）+ `SetStatus(message, progress01)`（写全量：文案回落"加载中…"、进度 null=不定态隐藏） |
| 模板补齐 | `Assets/ShareResource/UI/Widgets/Tmp_Loading.prefab`（原空壳：无 Mask 无视觉）——`_Blocker`（全屏阻断）/`Txt_Message` + `LoadingMask` 接线（pipeline，Type.GetType+SerializedObject）；202 壳 `Screens/Tmp_Loading.prefab` 只留 Canvas+CanvasGroup+嵌套实例 |
| 工作台 | `Button_Progress` 接 `Report`（0.25 步进演示，未开 Loading 时提示先开）；状态行补加载文案回读 |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | `completed / failed:false / 0 错误` |
| `FeedbackServiceEditModeTests`（回归） | **11/11**（既有嵌套阻断/取消/合并语义零回归） |
| 模板构建 | run_script `MASK wired (Blocker/Message/ProgressFill) / PREFAB saved` |

### 遗留（如实）

- 加载页表单完整批（加载页批A/B：进度条显隐策略细化、Report 与真实场景进度对接）仍归加载页线；
- `Button_Progress` 演示为合成步进（无真实场景进度源）——真实消费者接入后替换。

---

## 第7章 UI工作台W2（语言行 · 状态注入 · HUD 预览 · 回写工具 · 发布剥离）

<a id="ch7-UI工作台W2"></a>

> **原文件**：`Docs/施工进度/UI工作台W2.md`（归并前独立成文，内容整段并入本章）

《UI工作台设计》W2 执行（2026-10-16）。W2 原文列为"可选，随真实需求"六项——本批逐项落地或裁决，含**判据实测校准**（探针）与**人类操作模拟验证**（《修改指南》§2.3 口径）。

### 状态

**已完成（附验证证据）**——2026-10-16。

### 交付

| 面 | 落点 |
| --- | --- |
| 工具条（新画布） | 场景 `UiWorkbench.unity` 新增 `WorkbenchOverlay`（Canvas sortingOrder 5000——页面层 100..499 之上）+ `Cot_Tools` 紧凑两行（宽 384：中/EN/当前语言/空列表/长文本/复位注入/HUD 预览/回写工具 + 窄幅读数）。**为何分两画布**：面板排序低于页面语义层（页面上浮、不遮目检），而语言/注入/HUD 预览要在**页面打开时也能点到**——页面近乎不透明会盖住下面的面板 |
| 语言切换行 | 驱动 `SetLocale` 经 Lua 桥 `Bridge.Text.SetLocale`（设置页同一条链）→ `LocalizationService.SetLocale` → 页面索引 `ILocaleBound` 自动重刷；工具条 `Txt_Locale` 回读当前语言 |
| 状态注入 | **空列表**＝对目标页全部 `VirtualList`/`SimpleListView` 注入零行源；**长文本**＝对目标页全部 TMP 文本追加样本（记录原文，可显式还原）；**复位**＝还原文本 + 列表走"关+开"真链路复位。作用域＝**本工作台目录打开过的最近页**（页面实例取用口未对产品开：`UIService` 公开面只有 `IsOpen`）；无目标页时状态行显式提示 |
| HUD 预览 | 内容服务租约取 `BattleHudView.PrefabLocation` → 裸实例化（零接线静态画面：无对局数据源，数值/准心为设计期默认）；再点关闭＝销毁实例 + 归还租约；随面板销毁归还 |
| 回写工具 | 新件 `Assets/LiteGame/Editor/UiTools/WriteBack/`：`PrefabWriteBackScan`（差异枚举："实例 vs 源 prefab 资产"逐属性，标来源与不可回写原因）、`PrefabWriteBackApply`（`SerializedObject` 逐属性直写资产 + Undo + `SaveAssets`）、`PrefabWriteBackWindow`（菜单 `LiteGame/UI/回写/Prefab 改动回写`；**Play 内可用**）。工作台工具条 `Button_WriteBack` 经菜单路径跳转 |
| 发布剥离 | ①`UIWorkbenchDriver` 整件套三宏（`UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| LITEFRAMEWORK_DEBUG`，与 `DevHUD.cs` 同款——release 剥净）②`LiteGameIgnoreRule.DevAssetExclusions`（`Assets/Scenes/UiWorkbench.unity` 精确排除——Scenes 收集组整目录 `CollectAll`，排除只能落在忽略规则）③守卫用例钉"路径存在 + 规则真的忽略 + 产品场景不误伤" |
| 面板抽 dev 预制体（追加，2026-10-16 用户反馈"Boot 勾选没工作台"） | 面板/工具条/驱动收成 `Assets/LiteGame/DevHUD/WorkbenchUI.prefab`（**面板资产单源**，20 字段接线随预制体走）；`UiWorkbench.unity` 瘦身为"引导件+相机+DebugTuner（工作台模式开）"；`DebugTuner.EnsureWorkbenchPanel` 在开关下自建（编辑器形态；dev 预制体不在收集组）。构建器 `AgentScripts/BuildWorkbenchPanel.cs` 改为"建预制体 + 场景瘦身"（引 `DebugTuner.WorkbenchPrefabPath` 单源常量）。**两个入口同一结果**：Boot 勾选即进工作台 / 打开工作台场景 Play。 |
| 工作台模式开关（追加，2026-10-16 用户提议） | `DebugTuner.WorkbenchMode`（字段排在"本地服务器"下方 = 新小节「UI 工作台」）+ 静态镜像 `WorkbenchModeEnabled`（沿用 `UseLocalServerEnabled` 同款 dev-only 范式，整件随三宏剥离）+ `ProcedureMain.EnterBattleSceneAsync` 的 `#if` 门禁读：勾选后 **Main 不加载对局场景组**。工作台场景的 DebugTuner **默认开**、Boot 默认关（各场景序列化值决定形态，Boot 行为不变）。**形态裁决**：不做"按活动场景名门控"（红线 5 违规形态），走既有 dev 开关面（设计 §5 已回写）。 |
| 顺手修（同批发现） | ①`_rowTemplate` 断线（W0 起）：根因＝构建脚本把 `RectTransform` 赋给 `GameObject` 字段，Unity **静默置空**（目录行一键开/关自 W0 起从未生效）；②按钮 Label 矩形 200×50 溢出邻钮抢射线——首轮可点性自检 34/38 暴露；修：Label 矩形=按钮矩形 + `raycastTarget=false` |

### 验证证据

| 项 | 结果 |
| --- | --- |
| Unity recompile | **0 错误**（多轮；`recompile_status.failed=false` + `console_status.compilationFailed=false` 双判据） |
| 新 EditMode 用例 | `PrefabWriteBackEditModeTests` **7/7**——显式记录默认勾选/数值差异默认不勾、未勾选不落盘+勾选后固化+复扫归零、实例新增组件（`IsAddedComponentOverride`）拒绝、场景引用（指向场景对象）拒绝、非实例选择显式失败、回写菜单路径单源守卫、dev 资产排除守卫 |
| Play 实机（可点性自检） | 面板+工具条 38 钮（含 2 个未激活行模板本体）：**射线命中 36/36**（页面打开状态下亦然——页面不挡面板/工具条） |
| 新守卫用例 | `WorkbenchUiAssetEditModeTests` **2/2**：①预制体存在 + 驱动 20 字段引用**齐备**（把 W0 那类"静默置空"缺陷机检化）②工作台场景**不含面板件**（面板资产单源） |
| Play 实机（Boot 勾选入口，用户场景原样复现） | Boot 的 `DebugTuner.WorkbenchMode` 勾上 → Play：面板+工具条自建（可点性 **36/36**）、场景清单只有 Boot（跳过对局场景组）、目录 `开` 点 12 真实换页（`UIForm[12] 打开`）；验完**已还原开关为 0**（Boot 默认关，YAML 复核 `WorkbenchMode: 0`） |
| Play 实机（关闭态回归） | Boot 开关关 → Play：无面板（Buttons=0）、`Boot+BattleRig+Arena01` 照旧加载（正常游戏流程行为不变） |
| Play 实机（工作台模式开关） | 工作台场景（开关开）：场景清单只有 `UiWorkbench`——**无 BattleRig/Arena01**，Console `[Main] 工作台模式：跳过对局场景组加载（DebugTuner.WorkbenchMode）`，工作台照常就绪；Boot 场景（开关默认关，回归）：`Boot + BattleRig + Arena01` 三场景照旧加载（行为不变） |
| Play 实机（操作模拟） | 射线定目标 → `pointerDown/Up/Click` 三派发（不直调回调）：开 12/14 ✅ 页面栈真实换页（`UIRoot/Bottom` 页名切换 + 状态行"开 12 完成"）；关 14 ✅；**空列表注入**（先灌 3 行合成数据 → 点击 → 列表 0 行 + 状态行"空列表注入:1 个列表（14）"）；**长文本注入** 17 处；**语言 en**：页面文本 "暂无战绩"→"No recent matches"、"返回大厅"→"Back to Lobby"（真 LText 链路）；**HUD 预览** present（截图可见准心/弹药 30/30）；**复位注入**（页面重开 14 复位列表）；**回写入口** → 编辑器窗口列表出现 `Prefab 回写` |
| 截图存证 | `AgentScripts/Artifacts/w2-page-hud-en.png`（页面英文 + 工具条 + 面板）、`w2-hud-preview2.png`（HUD 预览：准心/弹药/血条 + 紧凑工具条不压 HUD 底栏）——工作区本地（`AgentScripts/` 已忽略） |
| 判据探针 | `AgentScripts/ProbePrefabMods{,2..5}.cs`（一次性，保留供复核）：override 记录语义实测（见"偏差"节） |
| 验证工具（可重复用） | `AgentScripts/WorkbenchW2Probe.cs`——`Env`（可点性射线自检）/ `ClickNamed` / `ClickRow` / `State`（面板·工具条·页面栈·列表·长文本·编辑器窗口读数）/ `Scenes`（场景加载证据）；`AgentScripts/BuildWorkbenchPanel.cs`（面板+工具条幂等构建器，改布局后重跑即重建场景） |
| L1 门禁 | **[PASS] L1 completed 1702 tests**（`pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest`）。首轮**全 lane 零执行**——根因是**并行线提交 68c3feb0** 给 `ISceneService` 加 `Progress` 后 `Tests/LiteFramework.Core.Tests/GmSceneCommandTests.cs:21` 的 `FakeSceneService` 未同步（CS0535，非本批面）；本批顺带补该成员（按接口"无加载为 0"口径）后转绿——已在审计表登记 |
| doc-lint | **OK**（`python scripts/gate/doc-lint.py`） |
| BuildHash | **不涉及**——改动面（`LiteGame.DevHUD`/`LiteGame.Editor`/`LiteGame.UI` 无改动/`Tests`）均不在共享闭包；无协议/表源改动 |

### 与设计稿的偏差（如实登记）

| 项 | 偏差 | 处置 |
| --- | --- | --- |
| 来源判据 | 设计原文"驱动每帧写面天然屏蔽"预设 Unity 能区分"Inspector 编辑"与"脚本直写"。**实测（探针 1/5）**：`SerializedObject` 直写**不产生** override 记录；`RecordPrefabInstancePropertyModifications`（Inspector 口径）才记录，且记录项 `target` 是**源资产侧对象**；RectTransform 的位置/尺寸/锚点路径在实例创建时即登记为**默认项**、此后恒标默认（`IsDefaultOverride` 恒真——Inspector 改动同样认不出） | 工具改为两档标注：「**显式记录**」（非默认记录项，默认勾选）/「**数值差异**」（默认不勾，逐条核对「实例值 → 资产值」）；设计文档 §2 同批改写为实测口径 |
| 状态注入作用域 | 设计只写"空列表/长文本"注入 | 注入目标＝"本工作台目录打开过的页"——**不新增产品 API**（页面实例取用口未对产品开）；未开页 → 显式提示"状态注入需先经目录打开一个页面"。实测：上云入口自动开的登录页不在作用域内（目录开一次页即可）。设计 §2 登记 |
| HUD 预览层级 | — | HUD 根画布 sortingOrder 0（低于页面层 100+）——**与对局内层级一致**：开页时 HUD 在页面之下，目检 HUD 需先关页。设计 §2 登记 |
| 面板落点（W0 决策修订） | W0 记录"面板本体＝场景内 UI+驱动件"；用户反馈"Boot 勾选没工作台"暴露它无法从正常启动进入 | **改为 dev 预制体单源 + 开关自建**（设计 §1/§5 同批改写）：场景不再内嵌面板，Boot 与工作台场景两入口共用一份预制体；dev 预制体不在收集组 → 自建仅编辑器形态（开发版 Player 只享受跳过场景组），已登记 |
| 工具条布局（两轮） | 首版单行 ~1000px 宽 | 截图目检发现压住 HUD 底栏血条左段 → 改紧凑两行（≤400px，只占左下空地，镀不到血条/弹药） |
| 场景名门控跳过玩法场景 | 设计列为"如成负担随 W2 评估" | **已实现为 DebugTuner 开发开关（2026-10-16 追加；取代本行先前的"不实现"结论）**：`WorkbenchMode` 勾选后 Main 不加载对局场景组——不做按活动场景名的门控（红线 5），改走既有 dev 开关面（字段 + 静态镜像 + `#if` 门禁读，与 `UseLocalServerEnabled` 同款；红线 6 面沿用同族 dev-only 三宏豁免形态，见审计登记）。工作台场景默认开、Boot 默认关；**退役条件**＝"战斗场景按对局加载/离场卸载"落地后语义成为默认 |
| 战斗场景加载时机（用户同批提问） | 用户问"是否应开始对局才加载、对局结束卸载" | **未动代码，属改裁决面**：现状是《地图数据专项设计》D16/批1 登记的"Boot 常驻 ＋ 玩法场景加性加载（幂等常驻）"，且产品流程**零卸载**（唯一卸载是 GM 场景命令）。评估：运行期只有相机 vcam 与视觉背景依赖场景，判定面走 `tbmap` 两端解耦；搬动需改 5 处（Main 摘加载、Match/Battle 注入 `ISceneService`、Battle 收尾卸载、窗口⑤接进度、四份文档回写），并有一个视觉后果（Main/结算失去地图背景）与一个编辑器坑（"领养"场景卸载是 no-op）。**建议形态**：Match（JoinAck 后）加载并报进度 → Battle 收尾统一卸载。**待裁决后落地** |
| 发布剥离口径核对 | 设计 §5 登记"职责在发布管道，随发布批核对" | **核对结果＝两个真实缺口并本批修复**：①`UiWorkbench.unity` 原会被 Scenes 收集组整目录收进生产包；②`UIWorkbenchDriver` 原无三宏守卫（仅靠"场景不入构建列表"保持不可达，IL2CPP Minimal 剥离不保证移除）。另登记：L0 的"开发内容排除"静态检查（《热更与内容发布专项设计》§304）仍未实施——进审计表 |

### 追加批：流程整理（2026-10-16 用户裁决：开发页 dev 覆盖件 / 训练场独立地图数据 / 工作台改场景加载 / 房间列表文案接受）

**已落地（本批：工作台载体 + 入口整理）**

| 面 | 落点 |
| --- | --- |
| 工作台载体＝场景 | `UiWorkbench.unity`＝"相机（AudioListener 禁用态）+ 携带引导件 GameEntry（禁用态）+ `WorkbenchSceneEntry` + DebugTuner（工作台模式开）+ `WorkbenchUI.prefab` 实例"；`DebugTuner.EnsureWorkbenchScene` 在开关下**加性加载**（编辑器形态）。两形态共用同一场景：**直接 Play**（场景入口启用携带引导件走真装配）/ **Boot 加性加载**（不二次装配） |
| 入口矩阵（单点决策） | `ProcedureMain.ResolveLanding`：直入训练场/直入工作台 → **不开页**；本地服务器 → **大厅页**（跳过登录）；Meta → 登录页；否则不开页。`PageFlow.OpenEntryOnce` → `OpenLanding(Landing)` |
| 直入训练场（新开关） | `DebugTuner.DirectToTraining`（排在 `WorkbenchMode` **之上**，优先级更高）：Main 就绪即发测试模式请求（`ApplySnapshotOrDefaults` + `TestModeRuntime.RequestEnter`，请求桥读即清） |
| 开发页（dev 覆盖件） | 新件 `Assets/LiteGame/DevHUD/DevMenuPanel.cs` + `DevMenuUI.prefab`（画布 6000，**任何页面在场都可点**）：左上「开发」钮 → 面板 = 测试模式（训练场）/ UI 工作台 / 关闭 + 形态读数；由 `DebugTuner.EnsureDevMenu` 在编辑器形态开出（`DevMenu` 字段默认开） |
| F9/F10 删除 | 代码 3 处（`ProcedureMain` F9/F10 块、`ProcedureBattle.LeaveKey`）；离场入口＝UI（ESC→设置页→退出对局）；dev 入口＝开发页 + 直入开关 |
| dev 入口进局收页 | `PageFlow.CloseOpenPagesForBattle`（dev 入口直入对局时在开页面会盖住 HUD——产品路径由"房间页→开局"自然收口） |

**验证证据（Play 实测）**

| 项 | 结果 |
| --- | --- |
| 直接 Play 工作台场景 | 场景入口启用携带引导件 → 真装配 → 就绪 + 可点性 **36/36**，只加载 `UiWorkbench` |
| Boot 工作台模式加性加载 | `Boot + UiWorkbench` 两场景、面板 **36/36**、无对局场景组；修复"2 个 AudioListener"告警（加性形态用宿主的监听器） |
| 开发页 | 出场且「开发」钮射线命中；**测试模式**点击 → 进局（`阶段:Battle`）+ 在开页面被收（`Page_Login(inactive)`）；**UI 工作台**点击 → 工作台场景加载（`UiWorkbench loaded`，就绪:True） |
| 本地服务器 → 大厅 | Boot 临时开 `UseLocalServer` → 启动落 `Page_Lobby`（无登录页；房间列表显"加载失败"文案＝无 Meta，用户已接受）；验完**已还原 `UseLocalServer: 0`** |
| 编译/门禁 | Unity 0 错；doc-lint OK；L1（见下） |

**追加批 III-a：对局场景组按对局加载 / 离场卸载（2026-10-16）**

| 面 | 落点 |
| --- | --- |
| 单源 | 新件 `Assets/LiteGame/App/Procedure/BattleScenes.cs`：`Rig`/`Arena`/`Training` 路径 + `MapFor(testRoom)`（**视觉与判定必须同图**——训练场地图数据未落地前恒回首图，行内挂账） |
| 加载（Match） | `ProcedureMatch` 注入 `ISceneService`；JoinAck 后、移交 Battle 前 `LoadBattleScenesAsync`：Rig → 地图，进度进**"进房等待场"**窗口（`LaunchLoading.Report` 离散步进）；失败上抛 → Match 既有失败路径收口 |
| 卸载（Battle） | `ProcedureBattle` 注入 `ISceneService`；`finally` 收尾（`DetachView` 之后）`UnloadBattleScenes()`——Rig ＋ 两张地图（幂等；失败只记日志，不阻塞离场）；编辑器"领养"形态卸载是 no-op（手测预期） |
| Main 减负 | `ProcedureMain` 摘掉场景组加载、窗口④进度上报、工作台跳过读——Main 阶段只有启动场景 + UI；`WorkbenchMode` 的"跳过"语义随之退役（开关保留为工作台入口） |
| 装配 | `ContainerModule`：Main 去 `_scenes`；Match/Battle 加 `_scenes` |

**验证证据**：Boot Play → `Scenes` 探针只有 `Boot`（Main 不再加载）；开发页「测试模式」→ 进局后 `BattleRig + Arena01` 已加载（`阶段:Battle`、动画槽建立）。**待补（根因已查明＝存量缺陷，非本批）**：离场卸载的端到端 Play 确认被**设置页打不开**挡住——复验时发现 `Page_Settings`（HEAD 即有）两处存量缺陷：①`Txt_Exit`/`Button_Exit` 的 `BindName` 都是 `Exit` ⇒ `OnInit` 判重 fail-fast（**已修**：`AgentScripts/FixSettingsExitBindNames.cs`，节点名同文）；②`OnShow` 类型不符 `绑定索引[Dropdown_Quality] 期望 Dropdown 实得 Image`（**未修**，待定位 `BindIndexBuilder` 解析面与嵌套下拉件）。两条均已登记审计表；该页打通后即可用"ESC→设置页→退出对局"复验卸载。

**追加批 III-b：训练场＝第二张地图（2026-10-16，用户裁决"训练场作为第二张地图就行"）**

| 面 | 落点 |
| --- | --- |
| 场景标记件（原缺） | 新件 `AgentScripts/BuildTrainingGroundMap.cs`（幂等重建）：`MapCollision` 根 ＋ **7 件掩体代理**（BoxCollider/正缩放/无旋转）＋ 原顶层 `Cube` 归入采集根（`cover_04`）＋ **`spawn_00..15`**（半径 22 环）＋ `map_bounds`（`MapBoundsMarker` 0 / 50×50）——**布点即常量、改这里重跑即改布局**（最小灰盒，可后续调） |
| 数据布局 | `Luban/Data/map.json` → **`Luban/Data/Map/map_arena01.json`**（每图一文件）；`map.xml` 表注册 `input="Map"`（读整目录）——**加图＝加文件**，不改表声明 |
| 烘焙器 | `BakeTargets` 加 `MapTarget` 结构 ＋ `ArenaMap`/`TrainingMap`（行 id 引 `MapTableProjection.TrainingMapId` 单源；`BakeTargets` 改 public——run_script 以文件为编译单元直调）；`SimMapBaker.Bake(target)` 参数化（`All()`/`AllTraining()` 对外入口） |
| 表链 | 训练场烘焙：**8 障碍 ＋ 16 出生点**，摘要 `c78441a74f6bc5f9`（首图 `9c3d504907d8210e` 不变 ✓ 迁移内容保真）；`Luban/gen.bat` → `tbmap` **两行**（lua/bytes 双证）；`gen-build-hash.py`：shared `8ee08553b5e5f20f` / meta `83ce53cadebd7fd1`，`--check` 复算 **OK**（`Generated/*.cs` 无 schema 变化 ⇒ 常量不变） |
| 运行期选行 | `MapTableProjection.TrainingMapId = 2`（单源）；`BattleScenes.MapIdFor(testRoom)` 与 `MapFor(testRoom)` **同一个切换**（视觉＝判定同图）；`ProcedureMatch` 本地服建传输按模式选行；`AccountSession.TestRoom` 带上标记 → `MatchSessionFactory` 取同一行（客户端预测与本地权威同图） |

**门禁**：Unity 编译 0 错；**L1 [PASS] 1702**（含一处用例维护：`MapProjectionTests.篡改表字节_行内digest不符_拒载` 原用"尾部翻一位"——多图后尾部字节属最后一张图、改不到首图，改为**篡改首图行内 `content_digest` 串**，与行数无关）；doc-lint OK；`gen-build-hash.py --check` 双常量复算 OK。

**验证证据（Play 实测）**：开发页「测试模式」→ `[Match] 对局场景组已加载:Rig=BattleRig Map=Assets/Scenes/TrainingGround.unity`，场景清单 `UiWorkbench + BattleRig + TrainingGround`（**无 Arena01**），`阶段:Battle`；截图 `AgentScripts/Artifacts/w3-training-ground.png`（进房等待场＋HUD 100/100·30/30）。

**遗留**：训练场布点为最小灰盒（7 掩体/16 环型出生点/50×50 边界）——要打仗手感按需调 `BuildTrainingGroundMap.cs` 后重跑再烘；真服务器多图（协议无 `map_id`，D10）仍未设计。

**（先前查清的前置，供对照）**

**硬发现**：`TrainingGround.unity` **缺全部烘焙标记件**（无 `MapCollision` 根、无 `spawn_*`、无 `map_bounds`；只有顶层 `Cube` 带一个 BoxCollider）——`SimMapBaker` 会在"找不到 MapCollision 根"处直接 ABORT。故顺序为：

1. **补场景标记件**（unity-pipeline，禁直改 .unity）：建 `MapCollision` 根 → 障碍代理（仅 Box/Sphere/Capsule、正缩放、yaw∈{0,90,180,270}、禁 MeshCollider）→ `spawn_00..15`（不挂碰撞体）→ `map_bounds` + `MapBoundsMarker`（HalfWidth/HalfDepth>0 覆盖障碍）。
2. **烘焙目标扩展**（单源）：`BakeTargets` 加训练场目标（路径/map_id=2/name/输出文件）；`SimMapBaker` 入口参数化（`All()` 保持 Arena01 不变）。
3. **烘焙** → 产物进 Luban 数据源（现 `Luban/Data/map.json` 单行；多行形态需与 `Luban/Defines/map.xml` 的 `input` 口径一起定：目录输入或多行 JSON——生成链细节待小跑验证）。
4. `Luban/gen.bat` → `tbmap.bytes` 两行 + `Generated` 重生成 → `python scripts/codegen/gen-build-hash.py`（`Generated` 在共享闭包内）。
5. 运行期选行：`MapTableProjection` 加训练图 id 常量；`ProcedureMatch` 本地/测试路径按 `testRoom` 选行（真服务器侧协议无 `map_id`，多图需另设计——本轮范围外）。
6. `BattleScenes.MapFor` 翻到 `testRoom ? Training : Arena` + 直入训练场复验；文档/审计销账。

**遗留（本追加批未做，方向已裁决）**

- **对局场景组按对局加载/离场卸载**：加载搬到 `ProcedureMatch`（JoinAck 后、移交 Battle 前——必须早于 Battle 相机硬门；进度接"进房等待场"窗口），卸载在 `ProcedureBattle` 收尾；地名按模式参数化（正式 Arena01 / 测试 TrainingGround）。**（追记：加载链修订后装载与卸载都归 `ProcedureBattle`、在 `StartGame` 后加载——见《地图数据专项设计》场景加载形态。）**
- **训练场独立地图数据**：`tbmap` 现只有 `map_id=1`（arena01）——训练场需烘焙（`BakeTargets` 已列 TrainingGround）+ 新表行 + 生成物重生成（表源改动→跑 build-hash）+ 判定面摘要核对。
- **退出链在 dev 落点复用**：ESC→设置页→退出 的动作按落点分派（退出测试对局/关工作台 → 回大厅页），退出钮文案键；dev 落点收尾建议走"读即清请求桥"（`TestModeRuntime` 先例）。
- **从对局/战斗中开工作台**不会卸载已加载的战斗场景（正常入口路径在按需加载落地后天然无此场景）；如需强制收，走 `ISceneService` 卸载 + 阶段门（本次未做）。

### 流程批 IV（2026-10-16 续）：设置页打通 → 退出链端到端 → 卸载验证

**根因链与修复（四条存量缺陷，逐一实测锁定）**

| 序 | 缺陷 | 修复 |
| --- | --- | --- |
| ① | 设置页 `Dropdown_Quality` 绑定类型不符（控件 `UIDropdownWrap`(TMP) vs 自动检测落到 `Image`；受控 API 只认 UGUI `Dropdown`） | 控件根 `BindNode.CustomTypeName = LiteGame.UI.UIDropdownWrap`（`FixDropdownBindType.cs`）；`UIBindIndex` 下拉族双支持；`UIDropdownWrap.SetValue` |
| ② | 系统面三壳缺 `GraphicRaycaster` ⇒ 确认弹窗按钮射线空命中 | `FixCanvasRaycasters.cs` 扩展三壳 |
| ③ | 池化复用页丢语言服务 ⇒ 复用后 `OnShow` 断、"退出对局"钮不亮 | `UIForm.EnterActiveFromRecycled` 无条件补注入（幂等） |
| ④ | 进房等待场"先关后开"竞态 ⇒ 遮罩永久阻断（本次场景加载入窗后必现） | `FeedbackService.LoadingVisible` 只读口 + `ProcedureMatch` 等"面已开"再压加载 |

**验证证据（Play 实测，逐步留读数）**：开发页「测试模式」→ `阶段:Battle` 且遮罩正常关闭（`Tmp_Loading(inactive)`）→ HUD 菜单钮 → 设置页打开且 **`Button_Exit` 点亮** → 点退出 → 确认弹窗（`UIForm[206]`）→ 点确认 → `阶段:Main` + `UIRoot/Bottom: Page_Lobby(Clone)`（**回大厅页**）+ `[Scene] 叠加场景已卸载:TrainingGround/BattleRig（余 0）`、场景清单只剩 `UiWorkbench` —— **"退出对局 → 离场卸载 → 回大厅"端到端打通**。

**流程批 IV 收官：退出语义复用（落点文案 / dev 收尾 / 能力保全）**

| 面 | 落点 |
| --- | --- |
| 落点文案 | 新文案键 `UI.Settings.ExitWorkbench`（"退出工作台"；`#ltext.xlsx` → `gen.bat` 已重生成）；`Page_Settings.lua` 支持导航数据 `exitKey` 覆盖（缺省＝对局语义） |
| 菜单入口 | 工作台驱动装订平台返回（ESC）→ 对局菜单（设置页，带 `battleExit`＋`exitKey`）；开发页「设置菜单」钮＝同一入口（按键无法注入，同一方法用点击可验） |
| 退出收尾 | 驱动识别"本件开出的菜单已关 ∧ 大厅页在场"（**复查式**：大厅转场期 `IsOpen=false` 不得提前清标记）→ 读即清请求桥 → `ProcedureMain` 消费 → `DebugTuner.CloseWorkbenchScene()`：可卸则卸场景，**唯一场景不可卸则退化为隐藏工作台 UI**（自我还原返回装订＋收 HUD 预览） |
| 能力保全 | 原 F9 的"直连对局（Room-A/真 KCP）"能力搬到开发页「直连对局」钮（`TestModeRuntime.RequestDirect/TakeDirectRequest` → `ProcedureMain` 非测试房 `Request(Match)`）——F9/F10 删除后联测路径 B 仍可用 |
| 加固 | `MetaEntryService.LeaveMatch` 在 `Current == Main` 时 no-op（否则被流程机拒"重入禁止"抛错——工作台落点退出会走到） |

**验证证据（Play 实测）**：工作台落点 →「设置菜单」→ `Txt_Exit=退出工作台`（落点文案 ✓）→ 退出 → 确认 → `[Nav] Replace id=12`（回大厅页）＋ `[Workbench] 工作台 UI 已收尾（场景不可卸——隐藏形态；回大厅页）`、工作台状态读数 `(未找到)`＝已不在，**无"重入禁止"报错**；开发页钮清单复验（6 钮在场、可点）。

**流程批 V：文档扫尾（已完成）**——F9/F10 现存引用按新入口改写 **17 处**（`07-本地联测` 10、`代码地图` 1、`地图数据专项设计` 4、`02-总体流程` 2、`UI工作台设计` 1；历史施工记录按惯例保留）。

**载体形态收敛（2026-10-16 用户裁决"工作台不用直接 Play 了"）**

- `UiWorkbench.unity` 由"相机＋携带引导件（禁用）＋场景入口＋开关＋面板实例"收敛为**纯叠加 UI 场景**（只有面板预制体实例）；`WorkbenchSceneEntry.cs` 已删除（直接 Play 形态退役）。
- 入口＝**Boot 常驻 + 其余场景叠加**（用户裁决的形态）：`DebugTuner.WorkbenchMode` / 开发页 → 加性加载；**退出＝真卸载**（`SceneManager.UnloadSceneAsync`——初版用 `EditorSceneManager.CloseScene` 在 Play 内被静默拒绝，已修）；驱动对"无宿主直接 Play"给状态行提示。
- **验证证据（Play 实测）**：Boot（roots=2）＋`UiWorkbench`（roots=1）叠加、面板可点 36/36 → 「开发 → 设置菜单 → 退出工作台 → 确认」→ `[Workbench] 工作台模式：工作台场景卸载中（落点收尾 → 回大厅页）`、`Scenes` **只剩 `Boot`**、落在大厅页；守卫用例 2/2；Boot 开关已还原（`WorkbenchMode: 0`、`DevMenu: 0`＝你的值）。

**待做（后续批次，按建议顺序）**

| 序 | 项 | 现状 | 落点/线索 |
| --- | --- | --- | --- |
| ~~1~~ | ~~设置页 `Dropdown_Quality` 绑定类型不符~~ | **已办（流程批 IV：见上节四条修复）** | 定位 `BindIndexBuilder` 类型解析面 vs 页面内嵌套下拉件（`Widgets/Tmp_Dropdown` 实例 + BindName 经 PrefabInstance 覆写为 `Dropdown_Quality`）；先跑 `AgentScripts/FixSettingsExitBindNames.cs` 那次的复现路径（Play→开发页→测试模式→HUD 菜单钮） |
| ~~2~~ | ~~离场卸载的端到端 Play 确认~~ | **已办（同上：实测卸载余 0 + 回大厅）** 第 1 项修好后：ESC/菜单 → 设置页 → 退出对局 → 确认弹窗 → 断言 `Scenes` 只剩启动场景、`[Nav] Replace id=12` |
| ~~3~~ | ~~IV：dev 落点退出链复用~~ | **已办（流程批 IV：见上节）** | ESC→设置页→退出 在"直入训练场/直入工作台"下可用：退出动作按落点分派（离场＋卸载 / 关工作台场景）、退出钮文案按落点（需 `tbltext` 键）、dev 落点收尾走读即清请求桥（`TestModeRuntime` 先例） |
| ~~4~~ | ~~V：F9/F10 文档扫尾~~ | **已办（流程批 V：现行文档 17 处改写）** | 07 联测手册（F 键流程多处）、代码地图、修改指南、上云页面层级设计；历史施工记录按惯例保留 |
| ~~5~~ | ~~训练场布点为最小灰盒（7 掩体/16 环形出生点/50×50 边界）~~ | **已办（多图批：见[多图与训练场布点](UI战斗HUD线.md#ch2-多图与训练场布点)）** | 版本化工具 `TrainingGroundLayout`（`EnsureSeed` 非破坏补齐／`RebuildSeed` 全量重建；烘焙工具箱「地图」页两钮＋菜单）＋烘焙期出生点校验（不落障碍内/不越界 fail-fast）；旧 `AgentScripts/BuildTrainingGroundMap.cs` 收成薄转发 |
| ~~6~~ | ~~真服务器多图~~ | **已办（同上）** | 票据 v2 盖章 `mapId` → RoomServer 按表建房（模板 `map_id`／冲突与缺行拒房）→ `JoinAck.map_id` → 客户端按行选表行与场景（表新列 `scene`）；L3 真 HTTP＋真 KCP 用例为证 |

### 遗留（如实）

- **发布剥离的门禁面**：现在是"忽略规则 + 单源守卫用例"（钉路径存在性与规则行为），**没有**在出包流程里断言"bundle 清单不含 dev 件"——L0 侧缺口已登记审计表。
- **状态注入不覆盖"非工作台打开"的页面**（如上云入口自动开的登录页/流程开的页）——需先在目录开一次页；若后续要覆盖，须给 `UIService` 开只读"取已开 form"口（产品 API，另批裁决）。
- **回写工具边界**：结构面（新增组件/新增子对象）与场景对象引用显式列出并拒绝；不回滚实例 override（要实例回归干净态用 Unity 原生 Overrides ▸ Revert）。

### 操作说明

日常操作（打开/就绪、开页面、反馈面、语言与边界态注入、HUD 预览、回写闭环、排障）已抽为手册：[Docs/项目说明/08-UI工作台.md](../项目说明/08-UI工作台.md)（单源，避免本文件与手册两处同文）。

---

## 第8章 UI 制作主线（绑定合并 · 新建页 · 单页布局预览 · 工作台退役）

<a id="ch8-UI制作主线"></a>

> **批次**：制作主线批（2026-10-22，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §工作台](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-workbench)（旧体系施工记录）
> **操作手册**：[08-UI 制作与预览](../项目说明/08-UI工作台.md)

### 交付

| 件 | 变化 |
|---|---|
| 绑定编辑 | `UiBindingEditor` 并入 UI 制作主窗口绑定分区；`UiBindingWindow` 与 Hub `ui.binding` 退役；`UiBindTreeView`/`UiBindingRename` 继续做树与引擎单源 |
| 新建页 | `UiPageScaffold` + `UiPageWizardWindow`：复制 `Tmp_Page` 脚手架 → `Screens/Page_*.prefab`（自包含）＋ `Lua/UI/Page_*.lua`＋`#uiform.xlsx` 行 → 跑 Luban；表行助手 `scripts/codegen/_uiform_row.py`（校验/备份/回滚） |
| 命名文法 | `UiNamingWords.GrammarPattern`/`UiBindingRename.SemanticPattern` 收紧为真正的 PascalCase（测试暴露原正则允许小写） |
| 单页预览 | `UiPagePreview` + `UiPreviewDriver`：编辑器专用场景、目标 prefab 直接实例化、保存热重载、退出 Play 回原场景、预览中隐藏 DevHUD |
| 工作台退役 | 删 `UIWorkbenchDriver`、`WorkbenchUI.prefab`、`UiWorkbench.unity`、`BuildWorkbenchPanel.cs`、`WorkbenchW2Probe.cs`、`WorkbenchUiAssetEditModeTests`；删开发页工作台/设置菜单按钮、`DevStartupConfig.WorkbenchMode` 与请求桥；Boot 场景清掉残留序列化字段 |
| 守卫 | 新增 `UiPreviewEditModeTests`（预览场景在 Editor/被收集规则忽略、脚手架根契约、页面名校验、Hub 入口收口、工作台入口已退役）；`PrefabWriteBackEditModeTests` 去掉工作台驱动反射与排除名单断言 |
| Hub/文档 | `ui.page-new` 新增、`ui.compose` 改名“UI 制作”、`ui.binding` 移除；工具索引重生成；08 手册改写；工作台设计归档（旧路径保留指针） |

### 验证证据

| 项 | 结果 |
|---|---|
| Unity 编译 | 0 错 |
| L1（PullRequest） | **PASS 1819** |
| EditMode 目标夹具 | `UiPreview` 5/5、`UiBindingTools` 12/12、`UiComposePanel` 5/5、`UiComposeGenerate` 2/2、`PrefabWriteBack` 6/6、`EditorToolCatalog` 12/12、`UiWidgetProperty` 5/5、`UiWidgetLibrary` 6/6、`UiValidation` 14/14 |
| 全量 EditMode | **703/708**；5 红均在 UI 面之外：`BattleSession` inputDelay（期望 1 实得 3）、`ClipManifestDriver` Avatar null×3、`MetaEntry` 未配置 template（期望 null 实得空串）——单跑同样复现，非本批改动面 |
| 新建页端到端 | 临时 `Page_WizardSmoke3`：prefab/Lua/表行/Luban/Stage 步骤全过（`openStage:false` 形态）；产物根为普通 GameObject（非 variant），根组件 `Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup` + `Cot_SafeArea/SafeAreaReceiver`；验后回滚表行、删临时资产并重跑 Luban |
| 单页预览 | `Page_Login.prefab` 在 Play 内由 `UiPreviewDriver` 加载（`[UiPreview] 已加载`）；截图确认页面显示且 DevHUD 已隐藏；退出 Play 自动回 `Boot.unity` |
| 预览热重载 | 临时副本 `Page_PreviewProbe.prefab`：Play 中改 TMP 文本并保存，Console 出现**第二次** `[UiPreview] 已加载`（seq 1324/1325）；临时副本与探针脚本已删 |
| 窗口冒烟 | `UiComposeWindow` + `UiPageWizardWindow` 打开/重绘无 Console error；发现并修复 Odin `TabGroup` 标签含 `/` 的报错（改为「校验·生成」） |

### 遗留（如实）

- **真链路档不另造面板**：本批只交付布局档；需要本地化/真实数据时走 Boot → Preload → Main 联调或 L2 PlayMode（设计 §8.4）。
- `UI.Settings.ExitWorkbench` 文案键随工作台退役失去代码消费者；暂留 tbltext，避免无谓表 diff（已登记审计）。
- 工作台历史审计条目保留追溯；发布剥离的“bundle 清单不含 dev 件”L0 缺口仍按原登记追踪。
- `AgentScripts/BuildWorkbenchPanel.cs`/`WorkbenchW2Probe.cs` 已删；DevMenuUI 预制体不再由构建器生成，后续修改走 Unity 原生编辑。

---

## 第9章 UI 制作可用性跟批（页面选择器 · 回写窗口退役）

<a id="ch9-UI制作可用性跟批"></a>

> **批次**：可用性跟批（2026-10-22，工作区未提交）
> **触发**：用户反馈“prefab 回写工具没啥用；预览还要搜索 prefab”

### 交付

| 件 | 变化 |
|---|---|
| 页面选择器 | `UiPageScaffold.ListPages()` 读 tbuiform（`_uiform_row.py --list`）；UI 制作窗口顶部「已登记页面」下拉，选页即打开对应 Prefab Stage 并绑定目标；Project/Prefab Stage 手动选择仍可跟随；新建页成功后向导刷新页面清单 |
| 预览入口 | 选页后直接点「预览」，不再要求先去 Project 搜 prefab |
| 回写退役 | 删 `PrefabWriteBackWindow`/`Scan`/`Apply` 与 `PrefabWriteBackEditModeTests`；Hub `ui.writeback`、菜单 `LiteGame/快捷/Prefab 回写` 一并移除；Play 内调参走 Prefab Stage/Unity 原生 Overrides |
| Hub/文档 | 工具索引重生成；设计/手册/代码地图/调试手册/能力地图同步；工作台设计归档不变 |

### 验证

| 项 | 结果 |
|---|---|
| Unity 编译 | 0 错 |
| 页面清单 | `UiPageScaffold.ListPages()` 返回 13 行（含 `UI.Page_Login`） |
| 页面选择器真实操作 | 反射调用窗口的 `SelectPage(Page_Login)`（下拉同一入口）→ `PrefabStageUtility` 当前 Stage 与 `TargetPrefab` 均为 `Assets/ShareResource/UI/Screens/Page_Login.prefab`；Console 0 error |
| EditMode 目标夹具 | `UiPreview` 5/5、`EditorToolCatalog` 12/12、`UiBindingTools` 12/12、`UiComposePanel` 5/5、`UiComposeGenerate` 2/2、`UiWidgetLibrary` 6/6 |
| 回写退役 | `grep` 代码/工具清单无 `ui.writeback`/`PrefabWriteBack`；`toolchain.py check` OK；工具索引无该行 |
| L0 / Doc | L0 PASS；`doc-lint` OK |

### 遗留（如实）

- 页面清单读 xlsx 需要 Python + openpyxl（与新建页同一依赖）；读取失败时下拉为空并提示，不伪造条目。
- 回写窗口已删除；若后续出现“必须逐属性挑选、原生 Overrides 不够”的真实消费者，再按 `ToolInteractionSession` 契约恢复，不以菜单存在维持。
