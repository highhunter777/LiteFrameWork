# 客户端 DI 升级

更新：2026-10-08。设计：[客户端依赖注入专项设计](../设计文档/architecture/客户端依赖注入专项设计.md)。
提交：`d1b4edb`（本机复跑 L1 1453/1453 确认；DI-4 Unity/AOT 验收仍未跑）。

| 批次 | 当前状态 |
| --- | --- |
| DI-1：统一 Root 装配、导航实例、依赖预检与所有权 | 已完成（附验证证据） |
| DI-2：服务作用域与生命周期校验 | 已完成（附验证证据） |
| DI-3：Account/Match 工厂迁移 | **已实施（.NET 面）**：工厂拆两件、Account 半边 L1 源链接直测；Match 半边静态核查，Unity 验收归 DI-4 |
| DI-4：Unity/AOT 本机验收 | 未开始；待本机执行 |

本线仅编辑源码与文档；不启动 Unity、不跑 Unity 测试。完成后补录实际改动、验证证据与未验证边界。

## DI-1 实施结果

- `ClientContext` 复用 Host 创建的 `ServiceContainer`；`Put/Get/Require/Product` 与容器解析读取同一实例。Context、容器和 RootScope 自身显式登记，重复同引用暴露幂等，换引用拒绝。
- `UiShellModule` 创建的 `UINavigationController` 成为唯一导航实例；ContainerModule、ProcedureMain 委托、FeedbackService 共用该实例，移除重复导航创建。
- 容器新增 `Validate()`、工厂显式依赖声明、抽象/无公共构造/构造歧义/循环/缺失依赖诊断；`Seal()` 先预检再冻结，预检无构造和工厂副作用。
- 新增 `ServiceOwnership`。构造/工厂实例按引用去重进入容器资源域；外部模块传入实例默认为 Borrowed，ContainerOwned 才由容器释放；ClientHost 收口 Root/服务域释放异常。
- 新增 `SetTickOrder`，驱动快照按显式类型序生成，别名按引用去重；当前客户端装配序为 Dispatcher→Clock→UI→Scheduler→VFX→Dialog→Toast→Procedure。
- `ServiceContainer` 保留同步构造语义；异步初始化与模块关闭仍归 `ClientHost`，Account/Match 尚未迁入 Root 容器。

## 验证

| 验证 | 命令/证据 | 结果 |
| --- | --- | --- |
| DI 定向回归 | `dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ServiceContainer|FullyQualifiedName~ClientComposition|FullyQualifiedName~ClientHostTests|FullyQualifiedName~ClientScopeTreeTests'` | **64/64 PASS**；`TestResults/client-di/di-contracts.trx` |
| 全量 L1 | `pwsh -NoProfile -File Scripts/gate/test.ps1 -Lane L1 -Profile PullRequest -ResultsDirectory TestResults/client-di2/l1` | **1448/1448 PASS**；`TestResults/client-di2/l1/l1.log` |
| 构建哈希 | `python Scripts/codegen/gen-build-hash.py --check` | `CHECK OK`，`07dbe182f67d0e69`，75 files |
| 空白检查 | `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` | 通过 |
| L0 | `pwsh -NoProfile -File Scripts/gate/l0-dep-scan.ps1` | 1 条既有 `JoinAdmissionGateTests.cs` 测试凭据字面量误报；本批零新增 |

未启动 Unity，未执行 Unity 编译/EditMode/PlayMode/资源导入/Player/IL2CPP；未修改序列化资源和 `.meta`。DI-3/DI-4 仍待后续批次。

## DI-2 实施结果

- 新增 `ServiceLifetime`：Singleton、Scoped、Transient；保留既有注册 API 默认 Singleton。
- `ServiceContainer.CreateScope("Account"/"Match")` 创建父子容器；子域可读取父域 Singleton，父域不能读取子域服务，子域不得覆盖父域已有服务。
- Scoped 服务按当前子域缓存，Transient 每次解析创建；两者的 IDisposable 均登记到当前服务域并按 LIFO 释放。
- Root 禁止注册 Scoped，可注册无状态 Singleton/Transient；Singleton 捕获同域 Scoped 时在 Seal/Resolve 阶段拒绝；Transient Tickable 在预检阶段拒绝。
- 子容器可独立 Seal/Dispose；父容器释放时级联子容器，子容器重复 Dispose 幂等。

## DI-2 验证

| 验证 | 命令/证据 | 结果 |
| --- | --- | --- |
| 作用域与生命周期回归 | `dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ServiceScopeContract\|FullyQualifiedName~ServiceContainer\|FullyQualifiedName~ClientComposition'` | **54/54 PASS**；`TestResults/client-di2/di2-contracts.trx` |
| 既有 DI-1 回归 | 同上过滤器包含 ServiceContainer/ClientComposition | 通过；无既有回归 |

DI-3 仍需把 `ProcedureMatch` 的 Account 服务和 `BattleContext` 的 Match 服务迁入类型化工厂；本批保留现有 `ClientScope` 迁移契约，未改变运行期对局所有权。

## DI-3 实施结果（2026-10-08，接手收尾）

**消费者迁移**（随 DI-1 统一装配批同批落在工作区，本段补录并收口验证）：

- `ProcedureMatch` 经 `IAccountSessionFactory` 建 Account 会话（`AccountSession`：Scope + Services + BattleClient），JoinAck 后随 `ProcedureArgs.AccountSession` 移交 Battle；失败/取消就地 Dispose。旧 `ProcedureArgs.BattleClient/AccountScope` 双字段收敛为单 `AccountSession`。
- `ProcedureBattle` 经 `IMatchSessionFactory` 建 Match 会话（`MatchSession`：服务子域 + `BattleContext` 工厂产物）；finally 关闭序 = 视图 → MatchSession（服务域 LIFO 释放，工厂产物 `BattleContext` 随域释放——其内部订阅退订由 `BattleContext.Dispose` 承担）→ AccountSession（资源域 LIFO：BattleClient → 服务域）。
- **工厂拆两件（本收尾批）**：原单类双接口的 `ClientSessionFactory` 按职责拆为 `AccountSessionFactory.cs`（Account 半边——依赖 ClientScope/ServiceContainer/BattleClient/IClientTransport，**零引擎依赖**）与 `MatchSessionFactory.cs`（Match 半边——依赖 BattleContext/IInputService，Unity 面）。拆分动机：单一职责 + **Account 半边可源链接进 .NET 测试工程直测真工厂**（非复制品）。`ContainerModule` 装配两件并注册/发布两接口；传输裁决仍经委托留给产品装配根（`ProcedureMatch.CreateTransportForFactory`——工厂不认识测试开关语义，测试房意图经 `testRoom` 参数透传）。

## DI-3 验证（.NET 面）

| 验证 | 命令/证据 | 结果 |
| --- | --- | --- |
| Account 工厂直测 | `dotnet test Tests/LiteNet.Tests --filter AccountSessionFactoryTests`（源链接 `AccountSessionFactory.cs` + `BattleClient.cs` 进 LiteNet.Tests） | **5/5 PASS**：创建即入域（Seal 后解析同引用/连接即 Join）/会话 Dispose 恰好一次（传输 Dispose 计数=1、幂等、父域不随子释放）/工厂失败就地回滚（作用域资源归零、根域可重试）/离场再进房同构（旧实例已释放、新会话独立、根域跨会话存活）/测试房意图经委托逐次透传 |
| DI 定向回归 | `dotnet test Tests/LiteFramework.Core.Tests --filter 'FullyQualifiedName~ServiceContainer\|FullyQualifiedName~ServiceScope\|FullyQualifiedName~ClientComposition\|FullyQualifiedName~ClientHost\|FullyQualifiedName~ClientScope'` | **72/72 PASS**（含 DI-1/DI-2 既有面零回归） |
| 全量 L1 | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` | **1453/1453 PASS**（LiteFramework 706 / LiteNet 347 / LiteSim 330 / LiteTesting 7 / MetaServer 63；run `pullrequest-20261008-071915`） |
| 构建哈希 | `python scripts/codegen/gen-build-hash.py --check` | `CHECK OK`，`07dbe182f67d0e69`——**本线改动不触哈希源集**（LiteFramework/LiteGame 不在 Sim/协议/表数据集内），两端握手不受影响 |
| Unity 静态核查 | 全仓 grep | 旧 `ClientSessionFactory` 类名/`ProcedureArgs.BattleClient`·`AccountScope` 旧字段消费点/`new ProcedureMatch`·`new ProcedureBattle` 构造点全清零；`BattleSessionEditModeTests` 独立自建 `BattleClient`（替身传输）不受影响 |
| L0 | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/gate/l0-dep-scan.ps1` | 1 条既有 `JoinAdmissionGateTests.cs` 测试凭据字面量误报（已裁决不管）；本批零新增 |

## 未验证边界（如实）

- **Unity 编译/EditMode/PlayMode 未跑**（无 Unity 环境）：`MatchSessionFactory`（BattleContext 面）、`ContainerModule` 装配两件、`ProcedureMatch/ProcedureBattle/ProcedureArgs` 为静态核查（grep 清零）+ 同装配 `.NET` 面共证；本机 Unity 验收（编译/资源绑定/生命周期循环/IL2CPP）归 **DI-4**。
- `AccountSessionFactory`（源链接件）与 `MatchSessionFactory` 接口签名在 Unity 侧的编译一致性由 DI-4 首跑确认。

## DI-3 缺陷修复（2026-10-09，Unity 首跑暴露——DI-4 验收前收口）

> 2026-10-09 用户真机 Play 首次跑 Unity 引导（DI 批后首次），连爆两个**该批潜伏缺陷**（.NET 面单测/静态核查覆盖不到装配序，正是 DI-4 登记的验收缺口）；逐个修复并以 Play Mode 实跑核证。

| # | 缺陷 | 根因 | 修复 |
| --- | --- | --- | --- |
| 1 | `重复注册:IAccountSessionFactory——同类型只能有一个装配来源`（引导失败→`未装配`） | DI-1 起 `ClientContext.Put` 直写统一容器（`Put<T>`＝按 T 键注册），而 `ContainerModule` 沿用旧形态在 `Put<IAccountSessionFactory>` 之外又显式 `RegisterInstance<IAccountSessionFactory>`——**同键二次注册**，`Add` 守卫当场抛 | 删除两行冗余 `Put`（注册面收敛为服务接口 `RegisterInstance`；具体类型无产物消费者不占键）——同文件 `CommandCenter` 形态（具体键 `Put` + 接口键注册）不受影响 |
| 2 | `驱动服务未编排:CommandCenter`（修 #1 后暴露） | `ValidateTickOrder` 要求**全部已注册 tickable 必须进驱动表**；命令中心批3（SendQueued/SendAsync 到期派发）给 `CommandCenter` 加了 `ITickable`，`ContainerModule.SetTickOrder` 清单未随动 | 驱动表补 `ICommandCenter`（置 ToastTicker 后、流程机前——排队命令对机器的 Request 当帧可被 Advance 应用）；设计文档 §5 驱动序同步 |

**验证（Play Mode 实跑）**：`[Host]` 模块迹 11/11 `init:done`（含此前必炸的 `Container init:done`）＋ 控制台 **0 error/0 exception**；`GameEntry.TakeContainer` 反射探针装配成功。`未装配`（`TakeContainer` 抛）为引导失败的下游症状，随引导修复消失。

**边界（如实）**：DI-4 余部（资源绑定/生命周期循环/IL2CPP/AOT）仍未验收；本修复只覆盖 Unity 引导链——批3 命令中心文档（§5 驱动序）与代码已同步。
