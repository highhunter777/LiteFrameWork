# 实施任务清单：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 10/10（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | [测试先行] Session 边界守卫 A1 版 | - | 完成 | 当前工作区运行该用例失败且失败原因明确（AccountSessionFactory 的 new BattleClient 和/或 RoomSessionFace 的 BattleClient 具体引用）；测试可编译、不删/弱化用例；T5 验证 2 文件白名单后 A1 断言转绿。 | dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo（预期红） | dotnet test SessionBoundaryTests：失败 2/2（AccountSessionFactory.cs:91 new BattleClient；AccountSessionFactory/RoomSessionF… |
| T2 | [A1] Battle 端口与 BattleClientFactory | T1 | 完成 | 三端口顶层声明位于 Battle/Abstractions；BattleClientFactory 内 new BattleClient( 恰一处；Unity 重编译 0 错。 | Unity 重编译 0 错（unity-pipeline）；grep -n 'new BattleClient(' Assets/LiteGame/Modules/Battle/BattleClientFactory.cs（恰 1 处） | 新增 IBattleClient/IBattleClientFactory/IAccountRuntime/BattleClientFactory；BattleClient : IBattleClient；grep new BattleC… |
| T3 | [A1] Account 会话端口化 | T2 | 完成 | 生产代码 new BattleClient( 仅 BattleClientFactory；AccountSessionFactoryTests 绿；MatchLoopLeak 可编译；Unity 重编译 0 错。 | dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter FullyQualifiedName~AccountSessionFactoryTests --nologo；grep -rn 'new BattleClient(' Assets/LiteGame --include=*.cs（仅 BattleClientFactory 1 处）；Unity 重编译 0 错（unity-pipeline） | AccountSession 实现 IAccountRuntime；AccountSessionFactory 7 参经 IBattleClientFactory；AccountSessionFactoryTests 5/5；L1 PAS… |
| T4 | [A1] Battle 消费面与匹配/运行工厂切换 | T3 | 完成 | Session 域除 T1 白名单外无 BattleClient/BattleContext 具体字段；四组 EditMode 用例绿；Unity 编译 0 错。 | python scripts/gate/unity-tests.py --mode editor --filter BattleSessionEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter BattleRunPortEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter MatchLoopLeakEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter RoomBridgeEditModeTests；Unity 重编译 0 错（unity-pipeline） | MatchSessionFactory 吃 IAccountRuntime/CreateMatchServices、Resolve<IBattleClient>；BattleContext/BattleInputPipeline/Batt… |
| T5 | [A1] A1 守卫收窄与全量验证 | T4 | 完成 | SessionBoundaryTests A1 绿；全仓生产代码 new BattleClient( 仅 BattleClientFactory；L1（PullRequest）绿；Unity 0 错。 | dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo；grep -rn 'new BattleClient(' Assets/LiteGame --include=*.cs（仅 BattleClientFactory 1 处）；python scripts/gate/test.py --suite L1 --profile PullRequest；Unity 重编译 0 错（unity-pipeline） | SessionBoundary 2/2；L1 PASS 1972；生产 new BattleClient( 仅 BattleClientFactory；2 文件白名单未扩大；Unity 编译待验收 |
| T6 | [A2] 运行会话搬迁与端口归位 | T5 | 完成 | A2 断言搬迁前红、搬迁后绿；AC4 目录清单成立；AC3 asmdef 与允许表绿；AC6 Battle 代码区无 DevHooks；L1 绿；Unity 0 错；目录搬动六面核对无漏项。 | dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo；python scripts/gate/test.py --suite L1 --profile PullRequest；python scripts/toolchain.py check；grep -rn 'DevHooks' Assets/LiteGame/Modules/Battle --include=*.cs（0）；find Assets/LiteGame/Modules/Battle/Session -maxdepth 1 -name '*.cs'（Git Bash，4 文件）；find Assets/LiteGame/Modules/Session -maxdepth 2 -name '*.cs'（Git Bash，核对 AC4 剩余清单）；Unity 重编译 0 错（unity-pipeline） | 4 文件 5 类型 move_asset 到 Modules/Battle/Session（GUID 保留）；4 端口迁 Battle/Abstractions；LuaBridge asmdef+允许表；DevHooks 直引移除/诊断构… |
| T7 | [A2] 装配移交、诊断注入与守卫收紧 | T6 | 完成 | SessionComposition 不再注册两工厂；Battle 代码区无 DevHooks；SessionBoundaryTests A2 绿；四组 EditMode 绿；L1 绿；Unity 0 错。 | python scripts/gate/unity-tests.py --mode editor --filter BattleSessionEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter BattleRunPortEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter MatchLoopLeakEditModeTests；python scripts/gate/unity-tests.py --mode editor --filter RoomBridgeEditModeTests；dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo；python scripts/gate/test.py --suite L1 --profile PullRequest；Unity 重编译 0 错（unity-pipeline） | BattleComposition 注册 Match/运行工厂+DependsOn Identity；SessionComposition 只留 Account/传输；GameEntry 注入 Func<IBattleDevDiagnos… |
| T8 | [A2] 代码注释、守卫终态与 PlayMode 冒烟 | T10 | 完成 | 代码区无 Map/Dev 域具体引用；Session 具体类型计数 0；PlayMode BattleSmokePlayModeTests 绿；domain-matrix --check 绿；L1/Unity 绿。 | python scripts/gate/domain-matrix.py --check；python scripts/gate/unity-tests.py --mode playmode --filter BattleSmokePlayModeTests；python scripts/gate/test.py --suite L1 --profile PullRequest；grep -rn 'IMapProvider\\|BattleScenes\\|LiteGame.Modules.Dev' Assets/LiteGame/Modules/Battle --include=*.cs（期望 0）；Unity 重编译 0 错（unity-pipeline） | 注释/错误文案同步（ProcedureBattle/IBattleRun/MatchSessionFactory/IAccountSessionFactory/IRoomSessionFace/BattleContext）；Battle … |
| T9 | [A2] 文档回写与审计登记 | T8 | 完成 | AC10 清单逐项完成；doc-lint 0 violation；toolchain check 通过。 | python scripts/gate/doc-lint.py；python scripts/toolchain.py check；python scripts/gate/test.py --suite Doc | 回写路线图/说明 02·03·14·17/规范 03/依赖注入/地图专项/施工进度第20章+README；审计追加 IBattleClient.Client 与 BattleSmoke 探针条目、修正类设计/技术债陈旧路径行号；doc-l… |
| T10 | [主代理] 域矩阵脚本与生成物 | T7 | 完成 | domain-matrix --check 绿；--write 后无 diff；失效允许边删除。 | python scripts/gate/domain-matrix.py --write；python scripts/gate/domain-matrix.py --check；git diff --stat scripts/gate/domain-matrix.py Docs/说明/18-域间引用矩阵.md（应稳定） | ALLOWED_EDGES 文案更新、删除归零 Session→Input；--write 同步 18-域间引用矩阵；--check OK；Battle→Config5/Input31、Procedure→Battle16/Session… |

## 任务详情

### T1 · [测试先行] Session 边界守卫 A1 版

- 描述: 新增 Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs：去注释/字符串扫描 Assets/LiteGame 代码区（\b 词界，避免 IBattleClient/IBattleRun 误命中），覆盖 BattleContext/BattleClient/GameplayMatchIdentity 三类具体类型。A1 白名单固定 2 文件（MatchSessionFactory.cs、BattleSession.cs，带 A2 批次号）；AccountSessionFactory.cs 另加独立断言“不含 new BattleClient(”。当前工作区因 AccountSessionFactory/RoomSessionFace 的具体引用而红；T3/T4 清除后，T5 验证 2 文件白名单内只剩 BattleContext。A2 断言（白名单清空）在 T6 搬迁前加入并记录红证据。只写测试，不写生产实现。

- 文件: Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs

### T2 · [A1] Battle 端口与 BattleClientFactory

- 描述: 新增 IBattleClient/IBattleClientFactory/IAccountRuntime 到 Assets/LiteGame/Modules/Battle/Abstractions，BattleClientFactory 到 Battle 域；BattleClient 实现 IBattleClient。本任务只加端口/工厂，不接线、不加消费者；全局 AC1 grep 留给 T3/T5。

- 文件: Assets/LiteGame/Modules/Battle/Abstractions/IBattleClient.cs, Assets/LiteGame/Modules/Battle/Abstractions/IBattleClientFactory.cs, Assets/LiteGame/Modules/Battle/Abstractions/IAccountRuntime.cs, Assets/LiteGame/Modules/Battle/BattleClientFactory.cs, Assets/LiteGame/Modules/Battle/BattleClient.cs

### T3 · [A1] Account 会话端口化

- 描述: AccountSession 实现 IAccountRuntime（Battle 属性保留、Client 显式实现、CreateMatchServices 返回未 Seal 的 Match 子容器、maps 委托由工厂构造传入）；AccountSessionFactory 新构造签名经 IBattleClientFactory 建客户端，服务域按 IBattleClient 登记；IAccountSessionFactory 注释同步；BattleComposition 注册 IBattleClientFactory；SessionComposition 补 Battle 依赖并注入 IBattleClientFactory/地图委托；LiteNet.Tests 增三端口源链接，AccountSessionFactoryTests 改端口替身与 Resolve<IBattleClient>；MatchLoopLeakEditModeTests 的 AccountSessionFactory 构造同步为 7 参（IBattleClientFactory/地图委托替身）。T3 结束时中间态可编译，但守卫仍允许 RoomSessionFace/MatchSessionFactory/BattleSession 的具体引用。

- 文件: Assets/LiteGame/Modules/Session/AccountSessionFactory.cs, Assets/LiteGame/Modules/Session/Abstractions/IAccountSessionFactory.cs, Assets/LiteGame/Modules/Battle/BattleComposition.cs, Assets/LiteGame/Modules/Session/SessionComposition.cs, Tests/LiteNet.Tests/AccountSessionFactoryTests.cs, Tests/LiteNet.Tests/LiteNet.Tests.csproj, Assets/Tests/EditMode/MatchLoopLeakEditModeTests.cs

### T4 · [A1] Battle 消费面与匹配/运行工厂切换

- 描述: BattleContext/BattleInputPipeline/MatchSession/BattleSession/RoomSessionFace/MatchSessionFactory/BattleRunFactory/NetworkDebug 改吃 IBattleClient/IAccountRuntime；MatchSessionFactory 经 account.CreateMatchServices() 建未 Seal 的 Match 子域（注册后 Seal），解析 IBattleClient、去掉 IMapProvider，经 account.Maps/ResolveMapId；SessionComposition 同步新的 MatchSessionFactory 构造；同步 MatchLoopLeak/BattleSession/BattleRunPort 测试的构造与断言。T4 结束时 A1 消费面全部切到端口。

- 文件: Assets/LiteGame/Modules/Battle/BattleContext.cs, Assets/LiteGame/Modules/Battle/BattleInputPipeline.cs, Assets/LiteGame/Modules/Session/MatchSessionFactory.cs, Assets/LiteGame/Modules/Session/BattleSession.cs, Assets/LiteGame/Modules/Session/RoomSessionFace.cs, Assets/LiteGame/Modules/Session/BattleRunFactory.cs, Assets/LiteGame/Modules/Session/SessionComposition.cs, Assets/LiteGame/Modules/Session/Abstractions/IBattleRunFactory.cs, Assets/LiteGame/Modules/Session/Abstractions/IMatchSessionFactory.cs, Assets/LiteGame/Adapters/Dev/Debug/NetDebug/NetworkDebug.cs, Assets/Tests/EditMode/MatchLoopLeakEditModeTests.cs, Assets/Tests/EditMode/BattleSessionEditModeTests.cs, Assets/Tests/EditMode/BattleRunPortEditModeTests.cs

### T5 · [A1] A1 守卫收窄与全量验证

- 描述: A1 收口：确认 SessionBoundaryTests 的 A1 白名单仍为 2 文件（MatchSessionFactory.cs、BattleSession.cs 的 BattleContext），验证 AccountSessionFactory/RoomSessionFace 的具体引用已在 T3/T4 清除、A1 断言转绿；跑 A1 全量验证。不再改生产代码。

- 文件: Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs

### T6 · [A2] 运行会话搬迁与端口归位

- 描述: 开始前先在 SessionBoundaryTests 加入 A2 断言（白名单清空）并运行记录红证据（此时具体类型仍在 Session 域）；再用 unity-pipeline move_asset 迁 4 文件 5 类型（BattleSession、MatchSession+MatchSessionFactory、BattleRunFactory、RoomSessionFace）到 Modules/Battle/Session/；IBattleRun/IBattleRunFactory/IMatchSessionFactory/IRoomSessionFace 迁 Battle/Abstractions；更新 asmdef（Battle/Modules/LuaBridge/EditMode）与 ClientAsmdefAllowlistTests；移除 BattleSession 的 DevHooks 直引（构造注入 IBattleDevDiagnostics，中间态可传 null），同步 BattleRunFactory/BattleSession 构造点与 SessionComposition 的构造调用、EditMode 诊断用例；清理 IBattleDevDiagnostics 注释中的 DevHooks 字样；确保搬迁后 Battle 程序集可编译。诊断生产实现在 T7 由 App 注入。

- 文件: Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs, Assets/LiteGame/Modules/Battle/Session/BattleSession.cs, Assets/LiteGame/Modules/Battle/Session/MatchSessionFactory.cs, Assets/LiteGame/Modules/Battle/Session/BattleRunFactory.cs, Assets/LiteGame/Modules/Battle/Session/RoomSessionFace.cs, Assets/LiteGame/Modules/Battle/Abstractions/IBattleRun.cs, Assets/LiteGame/Modules/Battle/Abstractions/IBattleRunFactory.cs, Assets/LiteGame/Modules/Battle/Abstractions/IMatchSessionFactory.cs, Assets/LiteGame/Modules/Battle/Abstractions/IRoomSessionFace.cs, Assets/LiteGame/Modules/Battle/Abstractions/IBattleDevDiagnostics.cs, Assets/LiteGame/Modules/Battle/LiteGame.Modules.Battle.asmdef, Assets/LiteGame/Modules/LiteGame.Modules.asmdef, Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef, Tests/LiteFramework.Core.Tests/ClientAsmdefAllowlistTests.cs, Assets/LiteGame/Modules/Session/SessionComposition.cs, Assets/Tests/EditMode/MatchLoopLeakEditModeTests.cs, Assets/Tests/EditMode/BattleRunPortEditModeTests.cs, Assets/Tests/EditMode/RoomBridgeEditModeTests.cs

### T7 · [A2] 装配移交、诊断注入与守卫收紧

- 描述: BattleComposition 注册 IBattleClientFactory/IMatchSessionFactory/IBattleRunFactory 并补 DependsOn += Identity；SessionComposition 只留 Account/传输并补 Battle 依赖；GameEntry.RegisterModules 向 BattleComposition 注入 Func<IBattleDevDiagnostics>（() => DevHooks.Diagnostics）；BattleRunFactory/BattleSession 构造注入诊断（T6 已加入，此处只接线）；确认 SessionBoundaryTests A2 断言保持绿；EditMode 诊断用例同步。

- 文件: Assets/LiteGame/Modules/Battle/BattleComposition.cs, Assets/LiteGame/Modules/Session/SessionComposition.cs, Assets/LiteGame/App/GameEntry.cs, Assets/LiteGame/Modules/Battle/Session/BattleRunFactory.cs, Assets/LiteGame/Modules/Battle/Session/BattleSession.cs, Assets/LiteGame/Modules/Battle/Session/MatchSessionFactory.cs, Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs, Assets/Tests/EditMode/MatchLoopLeakEditModeTests.cs, Assets/Tests/EditMode/BattleRunPortEditModeTests.cs, Assets/Tests/EditMode/BattleSessionEditModeTests.cs

### T8 · [A2] 代码注释、守卫终态与 PlayMode 冒烟

- 描述: T10 已完成域矩阵脚本与生成物；本任务只做代码注释同步（ProcedureBattle/IBattleRun/MatchSessionFactory/IAccountSessionFactory/IRoomSessionFace）、BattleContext.cs:20/85/154 注释与 :168 错误文案改为 IAccountRuntime.Maps、SessionBoundaryTests 词界/白名单终态；跑 PlayMode BattleSmokePlayModeTests 作装配/流程冒烟。

- 文件: Assets/LiteGame/Modules/Procedure/Stages/ProcedureBattle.cs, Assets/LiteGame/Modules/Battle/Abstractions/IBattleRun.cs, Assets/LiteGame/Modules/Battle/Session/MatchSessionFactory.cs, Assets/LiteGame/Modules/Session/Abstractions/IAccountSessionFactory.cs, Assets/LiteGame/Modules/Battle/Abstractions/IRoomSessionFace.cs, Assets/LiteGame/Modules/Battle/BattleContext.cs, Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs

### T9 · [A2] 文档回写与审计登记

- 描述: 回写路线图 §3.5/§4.2、说明 14/02/17（17:135/17:543）、说明 03（§5/§6/§7.1）、规范 03 §4、依赖注入专项、地图数据专项；Docs/施工进度/客户端重构线.md 新增第 20 章并同步 Docs/施工进度/README.md 的卷内章节数（当前卷内 19 章）；审计登记 IBattleClient.Client 逃生口与 BattleSmoke.TearDown 探针短路；顺手修正既有文档漂移；toolchain check 记录目录搬动六面。只改文档，不改生产代码。

- 文件: Docs/设计文档/架构/客户端架构重构演进路线图.md, Docs/说明/02-架构总览与程序集分层.md, Docs/说明/03-客户端框架与运行时.md, Docs/说明/14-代码地图与根目录.md, Docs/说明/17-全项目数据流.md, Docs/规范/03-领域查询表.md, Docs/设计文档/架构/客户端依赖注入专项设计.md, Docs/设计文档/游戏玩法/地图数据专项设计.md, Docs/施工进度/客户端重构线.md, Docs/施工进度/README.md, Docs/审计/客户端/登记/技术债.md, Docs/审计/客户端/登记/类设计.md

### T10 · [主代理] 域矩阵脚本与生成物

- 描述: 由主代理执行（施工子代理不改门禁脚本）：更新 scripts/gate/domain-matrix.py 的 ALLOWED_EDGES 失真文案（Session→Battle、Procedure→Battle、Procedure→Session、Session→Input），删除归零的 Session→Input，运行 --write 同步 Docs/说明/18-域间引用矩阵；确认 --check 绿且 --write 无 diff。

- 文件: scripts/gate/domain-matrix.py, Docs/说明/18-域间引用矩阵.md

