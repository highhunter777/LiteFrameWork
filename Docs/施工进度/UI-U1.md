# UI U1 施工进度：操作与所有权

> 依据：《UI框架总设计》§13 U1 行（UI-03/05/06/09；显示/实例作用域；租约、缓存预算、Shutdown、统一排序、转场取消与输入阻断）+ §4.3/§4.4/§5.2/§6.2/§6.3；《框架先行建设与业务接入专项设计》§11（U1 在 lease/scope 接缝可用后集成——C1 已交付 IContentService/AssetLease/ContentGeneration）。
> 本文件只记录施工状态与证据；目标与验收以设计为准，不在此重复定义。

## 批次规划

| 批 | 范围 | 状态 |
|---|---|---|
| U1-① 操作记录与取消 | 在途打开操作合流（并发 Show 不返 null）；Busy；单人取消/全员撤工作；Close 权威取消在途；类型化 UIOpenException；展示代次 + 展示作用域 CTS；Tick 快照防重入 | 已完成（`d6e105e`，见下） |
| U1-② 租约与缓存预算 | IUIPrefabLease 注入（内容服务租约通道）；实例持租约到销毁；缓存预算 + LRU 淘汰；Destroy 完整销毁；CloseReason（系统关闭跳拦截与离场）；ShutdownAsync 全链 | 已完成（`d6e105e`，见下） |
| U1-③ 排序/输入/转场 | 组内统一排序（开序即深序/紧缩/容量拒绝/BringToFront）；interactable 与 blocksRaycasts 职责分离；转场 ct + 复位契约 + 结果 Kind | 已完成（`d131b1f`，见下） |
| U1-④ Player 全链与收口 | ProcedureMain 开真实主页面（真 prefab+真 Lua+租约）；冒烟标记；全量回归；文档同步 | 已完成（见下） |

## 施工记录

### 2026-09-24 · U1-①② 操作合流/取消/租约/缓存/销毁/Shutdown（提交 `d6e105e`）

**代码位置**：

- `Shell/UI/UIOpenException.cs`（新）——类型化失败（Canceled/LoadFailed/InitFailed/Busy/Rejected + FormId；基类 InvalidOperationException 兼容旧捕获点）。
- `Shell/UI/UIService.cs`——`OpenOperation` 在途操作记录：并发 Show 合流共享一次加载（等待者经 `AttachExternalCancellation` 各自可取消；工作只受 `WorkCts`——全员退出才撤销）；数据冲突 Busy（首请求持有数据）；Close/Shutdown 对在途发权威取消（半成品就地回滚）；失败经 Completion 交付全部等待者同一异常。`IUIPrefabLease` 必填注入（`_leases[formId]` 持有到真正销毁；缓存实例算使用者）；`CacheBudget`（默认 16）+ LRU 淘汰（`_lastUsed` 记账；只淘汰非打开非引用实例）；`Destroy(formId)` 显式销毁；`CloseAsync(reason)`（User/Back/Replace/ScopeExit/Reload/Shutdown——系统语义跳过出栈拦截与离场，OnHide 照常）；`ShutdownAsync`（停接入→取消在途→全关→销毁缓存/租约→销毁 DDoL Root，幂等）；Tick 快照迭代（回调内开关界面防枚举修改）。
- `Shell/UI/UIForm.cs`——展示代次（每次打开递增 + `IsDisplayCurrent` 迟到核验）+ 展示作用域 CTS（`DisplayToken`：打开创建/关闭取消——页面内跨帧异步级联终止）；`DestroyInstance()`（DropLogic + 展示令牌收尾 + Disposed 终态 + Destroy GO）。
- `Shell/UI/UIFormState.cs`——增 `Disposed` 终态。
- `Shell/UI/UIPrefabLease.cs`（新）——`IUIPrefabLease` + `ContentPrefabLease`（AssetLease 适配）+ `UIPrefabLeases.Unowned`（编辑器/测试）。
- `Main/Hosting/GameModules.cs`（UiShell）——prefab 经内容服务租约（依赖序 Content②→UiShell⑧）；Shutdown 释放面接 `_uiService.ShutdownAsync()`。

**L2 EditMode（新增 14 例，`UiU1EditModeTests.cs`）**：合流（同数据三等待者同实例/一次加载）/Busy/单人取消不牵连共享工作/全员退出撤工作/Close 权威取消在途/类型化 LoadFailed（含 location 与根因）/展示代次（复用递增、迟到核验失败）/展示令牌关闭即取消/Tick 重入（OnUpdate 内开新界面不炸）/租约生命周期（打开与缓存期间持有、销毁才释放、销毁后再开全新加载）/缓存预算 LRU（超预算淘汰最旧、淘汰后再开重新加载）/Destroy（打开中拒绝、关闭后可销毁）/系统关闭（跳过拦截与离场、OnHide 照常）/Shutdown 全链（在途取消、双租约归零、停止接入、Root 销毁、幂等）。

### 2026-09-24 · U1-③ 统一排序/输入锁/转场取消复位（提交 `d131b1f`）

**代码位置**：

- `Shell/UI/UILayerGroup.cs`——**开序即深序**（`RecalculateOrders`：入栈/移除/BringToFront/复用后统一重算，紧缩无洞）；废止 100 槽位回卷——`IsFull` 容量拒绝。
- `Shell/UI/UIService.cs`——容量不足 `UIOpenException(Rejected)`（创建/入栈前拒绝）；`BringToFront(formId)`。
- `Shell/UI/UIForm.cs`——暂停/恢复应用输入锁。
- `Shell/UI/Transition/`——`TransitionContext.Cts`（每事务取消源）；超时**先取消策略工作**再强制收尾；`ApplyComputedInput`（恢复为计算值——Paused/Closing/Recycled/Disposed 保持锁，不无条件写回 true）；`CloseGate` 锁 **interactable**（blocksRaycasts 全程不动——职责分离）；`TransitionResultKind`（Completed/Skipped/TimedOut/Cancelled/Failed）；`Runner.IsLocked`（输入协调求解口）。
- `Shell/UI/Strategies.cs`——`ITransitionStrategy.PlayShow/PlayClose` 与 `IReplaceTransition.PlayReplace` 增 `CancellationToken`；FadeSlide：ct 取消 `Kill(complete=true)` 跳终值即复位；不再碰 blocksRaycasts。

**L2 EditMode（新增 4 例 + 既有口径迁移）**：排序统一（开序即深序/移除紧缩/BringToFront）/组容量拒绝（100 满 + 101 拒）/转场输入锁（接受即锁 interactable、blocksRaycasts 全程不动、收尾按计算值恢复）/转场超时（取消令牌送达策略、TimedOut、输入恢复、降级立即完成）。既有用例迁移：转场门断言改 interactable 口径（`UiTransitionEditModeTests`）；U0 复用排序断言收敛为紧缩契约（幸存页回组基序位）。

### 2026-09-24 · U1-④ Player 全链与收口

- `Main/Procedure/ProcedureMain.cs`——进入 Main 打开真实主页面（formId=1 UIMain：真 prefab + 真 Lua + 内容租约全链）+ `[UI] main open` 冒烟标记；失败进确定错误态。
- `scripts/player-smoke.ps1`——标记集补 `[UI] main open`（字面量判据，C1-⑩ 修复后口径）。

**验证（本批实测）**：Unity 编译 **0 错误**；EditMode **69/69**（51 存量 + U1 新增 18）；**Player 构建 + 冒烟 PASSED**（`[UI] main open` 真资源包页面打开命中——U1 退出条件"实际资源包 Player 可打开/关闭"的打开面）；全量 L1 **505/505**。

## 实施口径偏离与登记

1. **输入协调者为内聚实现非独立类**：转场锁（Runner.IsLocked）+ 暂停（UIForm 状态）+ 生命周期锁定态（ApplyComputedInput）共同构成 §6.2 协调语义；**模态栈与产品策略随 U2 导航接入**（真实消费者出现前不建空层）。
2. **缓存策略列（Resident/LRU/DestroyOnClose per-form）未接表**：tbuiform 无策略列（加列需 Luban 表变更）——U1 交付统一预算 + LRU + 显式 Destroy；per-form 策略列随 U2 表扩展。
3. **TransitionResultKind.Cancelled 预留**：U1 的取消源为超时与 Shutdown（系统关闭不走转场）；外部导航取消（Go/Back 期间）随 U2 接入。
4. **队列上限 QueueCapacity=8/OpenTimeout=10s 未实装**：§4.3 的串行导航队列属 U2 导航（Go/Back/Replace 单写者）；U1 已交付其前置（操作合流/拒绝语义/类型化结果）。
5. ~~**L2 PlayMode 未建**（lane 未接入）：真 UIService+真 Lua 的取消加载/三层遮盖等 PlayMode 场景以 EditMode 真资源用例 + Player 冒烟承接；PlayMode lane 随测试框架线接入后补。~~ **已于 2026-09-25 消解**：`Assets/Tests/UI/PlayMode` 建成并接入 `scripts/l2-unity-gate.ps1` 同一门禁（真 UIService + 真 LuaEnv + 真 prefab + 真转场，12 例），见 [框架先行记录](框架先行.md)。**但本批的取消加载/三层遮盖场景仍未在 PlayMode 下覆盖**——余项转记 [框架先行 §3](框架先行.md) 样例②。
