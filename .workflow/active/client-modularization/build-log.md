# 施工记录：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 运行 client-modularization · 创建于 2026-10-30T19:40:28+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-30T21:15:45+08:00 · 新增任务 T1「批1 框架装配入口与 Events 独立」
- 2026-10-30T21:15:46+08:00 · 新增任务 T2「批1 装配段就地迁移与守卫同步」
- 2026-10-30T21:15:46+08:00 · 新增任务 T3「批1 适配器委托注入与能力端口」
- 2026-10-30T21:15:48+08:00 · 新增任务 T4「批2 业务域垂直化与 asmdef 合并」
- 2026-10-30T21:15:49+08:00 · 新增任务 T5「批2 对局冒烟用例与命令接线」
- 2026-10-30T21:15:50+08:00 · 新增任务 T6「批3 确认框端口与 Baseline 清空」
- 2026-10-30T21:15:51+08:00 · 新增任务 T7「批4 组合根与 Container 拆解」
- 2026-10-30T21:15:52+08:00 · 新增任务 T8「批5 外层域边界收口」
- 2026-10-30T21:15:53+08:00 · 新增任务 T9「全批 文档回写与收尾」
- 2026-10-30T21:25:47+08:00 · 开始任务 T1「批1 框架装配入口与 Events 独立」
- 2026-10-30T21:25:48+08:00 · 完成任务 T1 · 证据: 新增 LiteFramework/Runtime/Framework/{FrameworkInfrastructure,FrameworkClocks}.cs；PlatformInfrastructureModule 改用 Create 并 Put 池/文件/JSON/日志/派发器/命令中心；ClocksModule…
- 2026-10-30T21:35:47+08:00 · 更新任务 T3（deps）
- 2026-10-30T21:35:48+08:00 · 更新任务 T2（deps）
- 2026-10-30T21:35:49+08:00 · 开始任务 T3「批1 适配器委托注入与能力端口」
- 2026-10-30T22:24:54+08:00 · 完成任务 T3 · 证据: 能力端口 IRenderCameraSource/IAimCameraSink/ILookDevice 落地并替换 4 处 is 具体适配器；新增 App/AppComposition.cs 的工厂委托（ConfigService/NewInputIntentSource/CinemachineCameraServi…
- 2026-10-30T22:45:23+08:00 · 开始任务 T2「批1 装配段就地迁移与守卫同步」
- 2026-10-30T22:45:24+08:00 · 完成任务 T2 · 证据: 12 段就地迁入 Modules/<域>/<域>Composition.cs，Container/Ui 移 App/ 并删 Bootstrap/Modules；DependsOn 改模块名；BridgeCapabilitySet 下沉解环；各域 asmdef 补 UniTask/LiteGame.Core + 允许表…
- 2026-10-30T23:13:08+08:00 · 开始任务 T4「批2 业务域垂直化与 asmdef 合并」
- 2026-10-30T23:23:23+08:00 · 完成任务 T4 · 证据: 批2 结构落地：Core/Battle 删除；View+Presentation 迁入 Modules/Battle 按职责子目录（Aim/Arena/Characters/Combat/Commands/Hud/Setup）+ Abstractions 六端口（IBattleViewGroupFactory 独立成…
- 2026-10-30T23:52:22+08:00 · 开始任务 T5「批2 对局冒烟用例与命令接线」
- 2026-10-30T23:52:30+08:00 · 完成任务 T5 · 证据: A12 落地：Assets/Tests/UI/PlayMode/BattleSmokePlayModeTests.cs（Boot.unity 起服→Main→RequestEnter→Battle→IsBuilt/Hud.Root/Crosshair→自瞄开火四路表现（ShotPulse/VoiceCount/Vfx…
- 2026-10-31T00:02:42+08:00 · 开始任务 T6「批3 确认框端口与 Baseline 清空」
- 2026-10-31T00:02:42+08:00 · 完成任务 T6 · 证据: A13：ModuleDependencyDeclTests.Baseline 清空（最后一笔 UiService <- Container 由确认缝端口化解）并通过；Ui 段不再 Require<FeedbackService>（App/UiServiceModule.cs 产 ConfirmDialogRouter…

## 关键决定

- **批 4 分两个可交付检查点**（体量/风险实测）：① 组合根钩子与跨域装订迁移（`ComposeRoot` + `AppComposition.ComposeAfter`，其它段不动）——已提交；② 段拆解（Map/Meta/Session/Procedure 四段成型、`ContainerModule` 去 `IClientModule`、18 段序、归属表迁移）。两次提交都保持全绿（L1 + Boot 冒烟），避免"半个批 4"落在工作区。
- **组合根执行器与模块图校验抽成协作件**：`ComposeRootRunner`（窗口开关/失败回收/逆序第一释放位）与 `ModuleGraphValidator`（重复名/缺依赖/环）从 `ClientHost` 移出——`ClientHost` 类行数冻结 454 不许涨，`InitializeAsync`/`ShutdownAsync` 拆到 ≤80 后按规则删除冻结键（不是调阈值）。
- **T3 与 T2 顺序对调（实测发现）**：方案原序为 T2 搬迁 → T3 适配器上收，但实测程序集依赖使该序不可编译——`LiteGame.Adapters.Unity` 引用 `LiteGame.Modules.Input`/`Modules.Camera`、`LiteGame.Adapters.Luban` 引用 `LiteGame.Modules.Config`、`LiteGame.Adapters.LuaBridge` 引用 `LiteGame.Modules`；若先把段搬进 `Modules/**`（= 各域程序集），段内 `new NewInputIntentSource`/`new CinemachineCameraService`/`new ConfigService`/`new LuaBehaviourAdapter` 会立刻与适配器成环。故 DAG 改为 `T1 → T3 → T2`（已在 workflow 中更新依赖）。
- 能力端口按既有 `IAimWorldSink` 范式落地（可选能力接口 + 消费方 `is` + 未实现即退化）：`IRenderCameraSource`（`Modules/Camera/Abstractions/`）、`IAimCameraSink`/`ILookDevice`（`Modules/Input/Abstractions/`），实现留在 `CinemachineCameraService`/`NewInputIntentSource`。
- `FrameworkInfrastructure` 的池释放用 `(References as IDisposable)?.Dispose()`：`IReferencePool` 只约束取还，不该在接口上绑生命周期。
- **批 2：相机拆成 ② `CameraComposition` 薄段**（主相机保证 + `ICameraService`，适配器经 `Func<ICameraService>` 工厂注入），`BattleComposition` 只留战斗表现内核；段名 `Presentation` → `Battle`（`ContainerModule.DependsOn` 同步：删 `Presentation`、补 `Camera`/`Battle`）。理由见 plan A8b——主相机是投影面，必须早于 `Ui`/`Input`；表现内核只被战斗与设置消费，属战斗域。
- **批 2：HUD 读模型构造上移到 `BattleViewRig`**（`BattleHudAssembler` 只接 `IBattleReadModel` 实例）。`Presentation/Battle/Hud` 与 `Presentation/Hud` 合并进 `Battle/Hud` 后，装配器落进 `PresentationReadModelGuardTests` 的扫描面：它当时用 `Func<SimWorldState>` 现场 `new BattleReadModel(...)`，等于出现"第二个翻译点"。把构造留给装配点（Rig，本来就持 `SimView`）后，守卫基线仍是"唯一翻译点 = BattleReadModel.cs"，不需要加例外（守卫只减不增）。
- **批 2：越肩几何判据的参照系用相机视线水平轴**（不是角色 `transform.forward`）。实测该角色 prefab 的模型朝向轴是 +X（不是 +Z），用 `forward` 当"身后"方向会把 20° 肩偏读成 155°（首跑即红）。判据改为"相机在角色身后、相对视线轴横向偏出 15°~45° 且在右侧"——与场景 vcam 的 `FollowOffset` 解耦，也不绑定美术朝向约定。

## 偏差与处理

- **批 4 的跨域装订先落位**：`AppComposition.ComposeAfter` 已接管 Lua 门面/身份绑定/模态登记/输入拦截/注册表回填/联机入口接流程机/开发装饰/帧驱动序/装配面密封；`ProductSettings` 与 `BattleViewGroupFactory` 仍在 `ContainerModule`（其产物被流程机/视图工厂消费，须在段拆解时随归属表一起搬，否则顺序错位）。
- 方案 A4 原文"`GameEntry` 在 `InitializeAsync` 前调用工厂"不可行：`NewInputIntentSource` 的 `matchRules` 读口依赖 ③ `IDevSession`，前置创建会静默丢掉测试房爆头带覆写。改为**工厂委托注入 + 段内调用**（已写入 plan.md A4）。
- 方案 A12 的 `BattleViewGroupFactory` 注册落点原写 `AppComposition.ComposeAfter`，与 ⑱ `Procedure` 构造顺序冲突；改为批 1–3 在 `ContainerModule.CreateBattleViews` 处注册、批 4 归 `BattleComposition`（已写入 plan.md A12）。
- **A16 的 `ContainerModule.Assemble`（152 行）四方法拆分未在批 1 执行**（批 1 任务已收口，但该键仍在 `ArchitectureBudgetTests` 冻结表里）。本批未扩范围：仍按 plan 的"批 4 只搬方法"口径，在 T7 拆 `ContainerModule` 时同批拆方法并删 152 行键。
- **批 2 冒烟（A12）暴露两处既有产品缺陷并同批修复**（见"问题与修复"）：① `VfxHandleTable.CollectExpired` 以 `Go != null` 为前提，挂点随视图根销毁后被误判为"加载在途"→ 特效表项永久挂账；② `AudioService._voices` 的"同源 voice 键"在代理解绑后不摘 → `VoiceCount` 离场恒为 1。A12 的"≤2s 归零"判据正是打在这两处上，不修则门禁恒红。
- T1 过渡期把 Events 段命名为 `EventsModule.cs`（依赖守卫现按 `*Module.cs` 收集；批 1 迁移时统一改 `EventsComposition.cs` 并同步扫描面）。
- **批 3：确认缝改"端口 + 后绑定"**：新增 `LiteFramework.UI.IConfirmDialog` + `ConfirmDialogRouter`（可后绑定的实现槽）；UI 段（`App/UiServiceModule.cs`）**产出**路由器并交给 `LuaBehaviourAdapter`（`self.ui:ShowConfirm` 的服务端），装配根（`ContainerModule`）在 `FeedbackService` 建好后 `Bind(feedback.ConfirmAsync)`；`ModuleDependencyDeclTests.Baseline` 清空。落点说明：plan 把契约写进 `App/Bootstrap/Contracts/**`，但批 1 已把 `App/Bootstrap` 收敛为只含 `Probes/Fallbacks`（A2），且契约要被 `LiteFramework` 的 `FeedbackService` 与产品 UI 段同时看见——故落 `LiteFramework/Runtime/Modules/UI/`（框架 UI 能力层）。未绑定语义：按取消返回 false、只记一条 Warning（`WarningCount` 诊断读口），不抛不悬挂。
- **批 2：A7 的 `IBattleViewGroupFactory` 独立成文件**（原来与 `IBattleViewGroup` 同文件）：`DomainBoundaryTests` 按文件名判"六端口齐备"，同文件会让判据与实现形态耦合。
- **批 2：A12 观测面补两个 internal 读口**——`BattleViewRig.Feedback`（→ `BattleFeedbackAssembler`）与其 `DamageNumbers`。A12 原文只列了 `IsBuilt`/`Hud`/`View` 三个 `BattleViewRig` 成员，但判据同时要求读 `BattleDamageNumberDriver.ActiveCount`——没有读取面这条断言无法落地（驱动实例由反馈装配件私有持有）。
- **契约变更（用户裁决，方案 B）：`IClientModuleGraph.DependsOn` 由 `IReadOnlyList<Type>` 改为 `IReadOnlyList<string>`（按模块名寻址）**。原因：业务域各一程序集且段就地住域目录，`typeof(段类)` 必然把域程序集连成环（`Modules.Input → LiteGame.Modules → Modules.Input`，Config/Settings/Presentation 同理）。`ClientHost.ValidateModuleGraph` 改按名校验 + 按名 DFS 环检测；13 段 `DependsOn` 全改字符串；`ModuleDependencyDeclTests` 身份改取 `public string Name => "X"`（回退文件名去 `Module`/`Composition` 后缀）、依赖改解析字符串字面量、Baseline 键改 `UiService <- Container`；`ClientHostTests` 三个测试替身同步。验证：Unity 0 error；L1 793/798（5 红＝迁移守卫预期）。


## 问题与修复

- `ConfigModule` 改经工厂委托产出后，`context.Put(_createConfig(content))` 触发 `模块产物与依赖声明必须可静态解析` 红（守卫只认 `Put<T>(...)`/`Put(变量)`/`Put(new X(`）：改 `Put<IConfigService>(...)`，并给 `IConfigService` 补 `GameplayDigestBase`，让 `ContainerModule`/`UiServiceModule` 经端口消费。
- 同一产物被 `ConfigModule.Put` 与 `ContainerModule.RegisterInstance` 重复登记会撞容器：删掉 Container 侧两条 `IConfigService`/`IConfigTableSource` 注册。
- **批 2 冒烟首跑：VFX 离场 6s 不降（活跃 3~4 恒定）**。根因不是"寿命太长"，而是 `VfxHandleTable.CollectExpired` 的 `inst.Go != null` 前提：跟随特效挂在武器挂点上，视图根拆除时 GameObject 被 Unity 销毁 → 字段变 fake-null → 被当成"加载在途"跳过，表项永久残留（`ActiveCount` 永不归零）。修法：判据只看 `ExpireAt <= now`（pending 的 `ExpireAt` 为 +∞，天然不会命中），并在 `VfxInstance.Go` 的文档里写明 null 的两种语义。回归用例：`VFX_挂点被外部销毁_仍按寿命到期回收_活跃归零`。
- **批 2 冒烟二跑：只剩"自然寿命"级别的残留**（火光/弹着按 2~5s 寿命慢慢退）——A12 要求离场 ≤2s 归零，故补**归属人回收面**：`IVFXService.Play/PlayAt` 增可选 `owner`、`StopAll(object owner)`；`BattleMuzzleFlashDriver`/`BattleImpactFxDriver`/`BattleRemoteMuzzleFlashDriver` 以自己的 `this` 为归属人播放，`Dispose` 时 `StopAll(this)` 一次收回。回归用例：`VFX_按归属人整体收回_不影响无归属与他人`；`BattleCombatFxEditModeTests.Dispose后…` 增断言"两个驱动各按归属人收回一次"。
- **批 2 冒烟三跑：VFX 归零后卡在 `AudioService.VoiceCount == 1`**（分组快照 Effect=0/4，即实际没有在播）。根因：`_voices` 的键表只在 `Shutdown` 清空，代理解绑/停止后键仍挂账 → `VoiceCount`（在册同源 voice 数）恒 ≥1。修法：`ReleaseProxy` 与 `Configure`（代理易主）两处调 `ForgetVoicesOf` 摘键；`BattleAudioDriver` 记录自己登记的同源 voice 句柄，`Dispose` 逐个 `Stop`。回归：`BattleAudioDriverEditModeTests` 4/4 保持绿。
- **Boot 冒烟发现 `checksum-mismatch` 诊断包**（`[Battle] 诊断现场包已落盘: …-checksum-mismatch-…`，2026-10-30 本地测试房）：本批只改装配与端口、未触判定与协议，判为既有行为，**本批不处理**；批 2 冒烟时对照是否复现。

## 验证结果

- 待填写（verify 会自动追加每次运行记录）
- 2026-10-30T21:20:02+08:00 · 先行测试 PASS · 3/11 · 证据 evidence/red.json

## 交付摘要

- 待填写

## 未决事项

- 待填写（无则写“无”）

## 文件清单

- 待填写

## 后续建议

- 待填写

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-30T19:40:28+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-30T19:45:31+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-10-30T21:13:41+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-10-30T21:15:00+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-10-30T21:16:15+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-30T21:16:22+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-30T21:20:13+08:00 · 完成「先写测试」→ 「正式施工」
