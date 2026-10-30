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

## 关键决定

- **T3 与 T2 顺序对调（实测发现）**：方案原序为 T2 搬迁 → T3 适配器上收，但实测程序集依赖使该序不可编译——`LiteGame.Adapters.Unity` 引用 `LiteGame.Modules.Input`/`Modules.Camera`、`LiteGame.Adapters.Luban` 引用 `LiteGame.Modules.Config`、`LiteGame.Adapters.LuaBridge` 引用 `LiteGame.Modules`；若先把段搬进 `Modules/**`（= 各域程序集），段内 `new NewInputIntentSource`/`new CinemachineCameraService`/`new ConfigService`/`new LuaBehaviourAdapter` 会立刻与适配器成环。故 DAG 改为 `T1 → T3 → T2`（已在 workflow 中更新依赖）。
- 能力端口按既有 `IAimWorldSink` 范式落地（可选能力接口 + 消费方 `is` + 未实现即退化）：`IRenderCameraSource`（`Modules/Camera/Abstractions/`）、`IAimCameraSink`/`ILookDevice`（`Modules/Input/Abstractions/`），实现留在 `CinemachineCameraService`/`NewInputIntentSource`。
- `FrameworkInfrastructure` 的池释放用 `(References as IDisposable)?.Dispose()`：`IReferencePool` 只约束取还，不该在接口上绑生命周期。

## 偏差与处理

- 方案 A4 原文"`GameEntry` 在 `InitializeAsync` 前调用工厂"不可行：`NewInputIntentSource` 的 `matchRules` 读口依赖 ③ `IDevSession`，前置创建会静默丢掉测试房爆头带覆写。改为**工厂委托注入 + 段内调用**（已写入 plan.md A4）。
- 方案 A12 的 `BattleViewGroupFactory` 注册落点原写 `AppComposition.ComposeAfter`，与 ⑱ `Procedure` 构造顺序冲突；改为批 1–3 在 `ContainerModule.CreateBattleViews` 处注册、批 4 归 `BattleComposition`（已写入 plan.md A12）。
- T1 过渡期把 Events 段命名为 `EventsModule.cs`（依赖守卫现按 `*Module.cs` 收集；批 1 迁移时统一改 `EventsComposition.cs` 并同步扫描面）。
- **契约变更（用户裁决，方案 B）：`IClientModuleGraph.DependsOn` 由 `IReadOnlyList<Type>` 改为 `IReadOnlyList<string>`（按模块名寻址）**。原因：业务域各一程序集且段就地住域目录，`typeof(段类)` 必然把域程序集连成环（`Modules.Input → LiteGame.Modules → Modules.Input`，Config/Settings/Presentation 同理）。`ClientHost.ValidateModuleGraph` 改按名校验 + 按名 DFS 环检测；13 段 `DependsOn` 全改字符串；`ModuleDependencyDeclTests` 身份改取 `public string Name => "X"`（回退文件名去 `Module`/`Composition` 后缀）、依赖改解析字符串字面量、Baseline 键改 `UiService <- Container`；`ClientHostTests` 三个测试替身同步。验证：Unity 0 error；L1 793/798（5 红＝迁移守卫预期）。


## 问题与修复

- `ConfigModule` 改经工厂委托产出后，`context.Put(_createConfig(content))` 触发 `模块产物与依赖声明必须可静态解析` 红（守卫只认 `Put<T>(...)`/`Put(变量)`/`Put(new X(`）：改 `Put<IConfigService>(...)`，并给 `IConfigService` 补 `GameplayDigestBase`，让 `ContainerModule`/`UiServiceModule` 经端口消费。
- 同一产物被 `ConfigModule.Put` 与 `ContainerModule.RegisterInstance` 重复登记会撞容器：删掉 Container 侧两条 `IConfigService`/`IConfigTableSource` 注册。
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
