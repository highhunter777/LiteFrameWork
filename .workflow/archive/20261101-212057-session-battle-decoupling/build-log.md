# 施工记录：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-11-01T19:43:36+08:00 · 新增任务 T1「[测试先行] Session 边界守卫 A1 版」
- 2026-11-01T19:43:39+08:00 · 新增任务 T2「[A1] Battle 端口与 BattleClientFactory」
- 2026-11-01T19:43:41+08:00 · 新增任务 T3「[A1] Account 会话端口化」
- 2026-11-01T19:43:45+08:00 · 新增任务 T4「[A1] Battle 消费面与匹配/运行工厂切换」
- 2026-11-01T19:43:47+08:00 · 新增任务 T5「[A1] 装配接线与 A1 收口」
- 2026-11-01T19:43:51+08:00 · 新增任务 T6「[A2] 运行会话搬迁与端口归位」
- 2026-11-01T19:43:54+08:00 · 新增任务 T7「[A2] 装配移交、诊断注入与守卫收紧」
- 2026-11-01T19:43:58+08:00 · 新增任务 T8「[A2] 矩阵文案、守卫与代码注释收口」
- 2026-11-01T19:44:02+08:00 · 新增任务 T9「[A2] 文档回写与审计登记」
- 2026-11-01T19:44:15+08:00 · 更新任务 T1（description）
- 2026-11-01T19:50:51+08:00 · 更新任务 T1（acceptance, description, files）
- 2026-11-01T19:50:54+08:00 · 更新任务 T2（acceptance, description, files, verify）
- 2026-11-01T19:50:57+08:00 · 更新任务 T3（acceptance, description, files, verify）
- 2026-11-01T19:51:01+08:00 · 更新任务 T4（acceptance, description, files, verify）
- 2026-11-01T19:51:05+08:00 · 更新任务 T5（acceptance, description, files, verify）
- 2026-11-01T19:51:10+08:00 · 更新任务 T6（acceptance, description, files, verify）
- 2026-11-01T19:51:14+08:00 · 更新任务 T7（acceptance, description, files, verify）
- 2026-11-01T19:51:17+08:00 · 更新任务 T8（acceptance, description, files, verify）
- 2026-11-01T19:51:31+08:00 · 更新任务 T4（files）
- 2026-11-01T19:51:31+08:00 · 更新任务 T6（acceptance, description）
- 2026-11-01T19:51:34+08:00 · 更新任务 T8（verify）
- 2026-11-01T19:51:54+08:00 · 更新任务 T5（title）
- 2026-11-01T20:00:40+08:00 · 更新任务 T1（acceptance, description）
- 2026-11-01T20:00:44+08:00 · 更新任务 T3（acceptance, description, files, verify）
- 2026-11-01T20:00:48+08:00 · 更新任务 T4（description）
- 2026-11-01T20:00:52+08:00 · 更新任务 T5（acceptance, description, verify）
- 2026-11-01T20:00:55+08:00 · 更新任务 T1（acceptance）
- 2026-11-01T20:01:02+08:00 · 更新任务 T6（acceptance, description, files, verify）
- 2026-11-01T20:01:07+08:00 · 更新任务 T7（acceptance, description）
- 2026-11-01T20:01:13+08:00 · 新增任务 T10「[主代理] 域矩阵脚本与生成物」
- 2026-11-01T20:01:18+08:00 · 更新任务 T8（acceptance, deps, description, files, title, verify）
- 2026-11-01T20:01:22+08:00 · 更新任务 T9（description）
- 2026-11-01T20:07:58+08:00 · 开始任务 T1「[测试先行] Session 边界守卫 A1 版」
- 2026-11-01T20:07:58+08:00 · 完成任务 T1 · 证据: dotnet test SessionBoundaryTests：失败 2/2（AccountSessionFactory.cs:91 new BattleClient；AccountSessionFactory/RoomSessionFace 白名单外 BattleClient）；evidence/red.json…
- 2026-11-01T20:08:32+08:00 · 开始任务 T2「[A1] Battle 端口与 BattleClientFactory」
- 2026-11-01T20:20:00+08:00 · 完成任务 T2 · 证据: 新增 IBattleClient/IBattleClientFactory/IAccountRuntime/BattleClientFactory；BattleClient : IBattleClient；grep new BattleClient( 仅 BattleClientFactory:15；L1 PASS …
- 2026-11-01T20:20:00+08:00 · 开始任务 T3「[A1] Account 会话端口化」
- 2026-11-01T20:20:01+08:00 · 完成任务 T3 · 证据: AccountSession 实现 IAccountRuntime；AccountSessionFactory 7 参经 IBattleClientFactory；AccountSessionFactoryTests 5/5；L1 PASS；Unity 编译待验收
- 2026-11-01T20:20:01+08:00 · 开始任务 T4「[A1] Battle 消费面与匹配/运行工厂切换」
- 2026-11-01T20:20:01+08:00 · 完成任务 T4 · 证据: MatchSessionFactory 吃 IAccountRuntime/CreateMatchServices、Resolve<IBattleClient>；BattleContext/BattleInputPipeline/BattleSession/RoomSessionFace/BattleRunFacto…
- 2026-11-01T20:20:01+08:00 · 开始任务 T5「[A1] A1 守卫收窄与全量验证」
- 2026-11-01T20:20:01+08:00 · 完成任务 T5 · 证据: SessionBoundary 2/2；L1 PASS 1972；生产 new BattleClient( 仅 BattleClientFactory；2 文件白名单未扩大；Unity 编译待验收
- 2026-11-01T20:40:19+08:00 · 开始任务 T6「[A2] 运行会话搬迁与端口归位」
- 2026-11-01T20:40:19+08:00 · 完成任务 T6 · 证据: 4 文件 5 类型 move_asset 到 Modules/Battle/Session（GUID 保留）；4 端口迁 Battle/Abstractions；LuaBridge asmdef+允许表；DevHooks 直引移除/诊断构造注入；Unity 编译 0 错；L1 PASS 1972；MatchLoopL…
- 2026-11-01T20:40:19+08:00 · 开始任务 T7「[A2] 装配移交、诊断注入与守卫收紧」
- 2026-11-01T20:40:19+08:00 · 完成任务 T7 · 证据: BattleComposition 注册 Match/运行工厂+DependsOn Identity；SessionComposition 只留 Account/传输；GameEntry 注入 Func<IBattleDevDiagnostics>；SessionBoundary A2 2/2；EditMode 33…
- 2026-11-01T20:40:20+08:00 · 开始任务 T10「[主代理] 域矩阵脚本与生成物」
- 2026-11-01T20:40:20+08:00 · 完成任务 T10 · 证据: ALLOWED_EDGES 文案更新、删除归零 Session→Input；--write 同步 18-域间引用矩阵；--check OK；Battle→Config5/Input31、Procedure→Battle16/Session14、Session→Battle12/Map6
- 2026-11-01T20:40:20+08:00 · 开始任务 T8「[A2] 代码注释、守卫终态与 PlayMode 冒烟」
- 2026-11-01T20:40:20+08:00 · 完成任务 T8 · 证据: 注释/错误文案同步（ProcedureBattle/IBattleRun/MatchSessionFactory/IAccountSessionFactory/IRoomSessionFace/BattleContext）；Battle 域 IMapProvider/DevHooks grep 0；PlayMode …
- 2026-11-01T20:42:02+08:00 · 开始任务 T9「[A2] 文档回写与审计登记」
- 2026-11-01T20:42:02+08:00 · 完成任务 T9 · 证据: 回写路线图/说明 02·03·14·17/规范 03/依赖注入/地图专项/施工进度第20章+README；审计追加 IBattleClient.Client 与 BattleSmoke 探针条目、修正类设计/技术债陈旧路径行号；doc-lint 0 violation；toolchain check OK；Unity…

## 关键决定

- A1 端口住 `Modules/Battle/Abstractions`：`IBattleClient`/`IBattleClientFactory`/`IAccountRuntime` 由 Battle 声明、Session 实现/注入；`BattleClientFactory` 是生产代码唯一 `new BattleClient` 处。
- `AccountSession` 实现 `IAccountRuntime`：`Battle` 保留具体名，`IAccountRuntime.Client` 显式实现同实例；`CreateMatchServices()` 返回未 Seal 的 Match 子容器；地图以 `Func<int,SimMapData>`/`Func<int,int>` 构造注入，Account 工厂不依赖 Map 域类型。
- Account 服务域按 `IBattleClient` 端口登记；`MatchSessionFactory` 用 `Resolve<IBattleClient>`/`typeof(IBattleClient)`，不再解析具体客户端。
- 装配序保持 Battle ⑯ → Session ⑰；`SessionComposition.DependsOn` 增 `Battle`，`BattleComposition` 注册 `IBattleClientFactory`。
- 测试同步：`AccountSessionFactoryTests` 7 参 + `Resolve<IBattleClient>`；`MatchLoopLeakEditModeTests` 7 参 + `MatchSessionFactory` 去 `maps:` 参数（`FakeMapProvider` 改作地图委托来源）。
- A2 搬迁用 `unity-pipeline move_asset`（GUID 保留）：4 文件 5 类型 → `Modules/Battle/Session/`；4 运行/房间端口 → `Modules/Battle/Abstractions/`；LuaBridge asmdef 增 `LiteGame.Modules.Battle` 并登记允许表。
- A2 装配：`BattleComposition` 注册 `IBattleClientFactory`/`IMatchSessionFactory`/`IBattleRunFactory`、补 `Identity` 依赖、经 `Func<IBattleDevDiagnostics>` 注入诊断；`SessionComposition` 只留 Account/传输并补 `Battle` 依赖；`GameEntry` 传 `() => DevHooks.Diagnostics`。
- 域矩阵：`ALLOWED_EDGES` 文案更新并删除归零的 `Session→Input`；`--write` 后 `Session→Battle` 12（端口）、`Session→Input` 0、Battle 顶层类型 120。

## 偏差与处理

- Unity Editor 未运行（仅 Unity Hub）：本批无法执行 Unity 重编译/EditMode/PlayMode；已用 L1（1972 项）与定向 dotnet 测试替代，Unity 验证留到最终 verify（需用户开编辑器）。新增 4 个 `.cs` 未创建 `.meta`——按资产管线禁手工建 meta，待 Unity 首次导入生成。（后记：用户打开 Unity 后已生成 `.meta`，A2 期间完成 Unity 重编译/EditMode/PlayMode 验证。）
- T3/T4 一次改到终态（不保留“先旧签名后新签名”的中间编译态），与方案最终状态一致，减少往返。
- `domain-matrix --check` 因新增 4 个 Battle 顶层类型而暂过期；按任务归属留到 T10 `--write`，A1 不跑该门禁。
- A2 守卫清空白名单在搬迁后执行；搬迁前红证据未单独采集（A1 白名单已覆盖同类具体引用），终态 2/2 绿。已在施工记录如实登记。

## 问题与修复

- `ClientScope.Register` 以 `IBattleClient` 静态类型登记客户端；`AccountSessionFactoryTests` 5/5 验证同实例、释放面与连接行为不变。
- `MatchSessionFactory` 去 `IMapProvider` 后，`MatchLoopLeakEditModeTests` 的地图夹具改作 `Func<int,SimMapData>` 来源；`SessionComposition` 同步 4 参构造。
- 未删除/跳过/弱化任何既有用例；`SessionBoundaryTests` 2 文件白名单保持到 A1 收口，A2 清空为终态。
- A2 搬迁后 Unity 重编译 0 错；EditMode 33 项（`BattleSession`12/`BattleRunPort`7/`MatchLoopLeak`1/`RoomBridge`13）与 PlayMode `BattleSmokePlayModeTests` 1/1 全绿。
- 文档回写 `doc-lint` 0 violation、`toolchain.py check` OK（2 条既有 warning）；`domain-matrix --check` OK。

## 验证结果

- 2026-11-01T20:07:43+08:00 · 先行测试 PASS · 1/19 · 证据 evidence/red.json
- 2026-11-01T21:04:26+08:00 · 验证 FAIL · 12/13 · 证据 evidence/verify.json（聚焦配置首跑；`toolchain-tests` 因临时配置行号漂移误报，已恢复配置后复跑）
- 2026-11-01T21:16:36+08:00 · 验证 PASS · 66/66 · 证据 evidence/verify.json（完整命令；`sync-and-sim-e2e` 以 `--skip-build --skip-smoke` 运行，未构建 Player）
- 定向复测：`SessionBoundaryTests` 2/2；`AccountSessionFactoryTests` 5/5；EditMode 33/33（BattleSession 12、BattleRunPort 7、MatchLoopLeak 1、RoomBridge 13）；PlayMode `BattleSmokePlayModeTests` 1/1；Unity 重编译 0 错；`domain-matrix --check` OK；`doc-lint` 0 violation；`toolchain.py check` OK（2 条既有 warning）；e2e 哈希/服务端对账 OK。

## 交付摘要

Session 域只保留账号/连接/传输，对局运行（Match/Battle 会话、运行工厂、房间门面）归 Battle 域。A1 新增 `IBattleClient`/`IBattleClientFactory`/`IAccountRuntime` 与 `BattleClientFactory`，完成 Account 会话与全部消费面端口化；A2 用 `unity-pipeline move_asset`（GUID 保留）迁移 4 文件 5 类型与 4 个端口，装配移交到 `BattleComposition`，诊断改 `Func<IBattleDevDiagnostics>` 注入。`SessionBoundaryTests` 从 A1 红测到 A2 终态绿；L1 1972、EditMode 33、PlayMode 1、完整 `wf verify` 66/66 全绿。

## 未决事项

- 真实 Player 冒烟未跑：按项目约定需用户显式指令出包；PlayMode `BattleSmokePlayModeTests` 已作装配/流程冒烟替代，联机手测留交付后。
- `IBattleClient.Client`（`RoomClient`）仍是过渡逃生口，已登记 `Docs/审计/客户端/登记/技术债.md`；后续网络端口化批次收口。
- `BattleSmokePlayModeTests.TearDown` 的容器探针存在 `_container = null` 短路，已登记测试面技术债；本批未改测试。
- A2 守卫白名单清空在搬迁后执行，搬迁前红证据未单独采集；A1 守卫覆盖同类具体引用，终态 2/2 绿。
- `.workflow/config.json` 的 `sync-and-sim-e2e` 已加 `--skip-build --skip-smoke`，避免本地 verify 自动构建 Player（未指令不打包）；若恢复完整出包 e2e，需在明确指令下改回并重跑 verify。
- 工作区仍有并行线未提交改动（非本批），提交/归档时需按用户指令区分。

## 文件清单

- 新增端口/工厂：`Assets/LiteGame/Modules/Battle/Abstractions/{IBattleClient,IBattleClientFactory,IAccountRuntime}.cs`、`Assets/LiteGame/Modules/Battle/BattleClientFactory.cs`（含 Unity 生成 `.meta`）。
- Account/消费面：`Assets/LiteGame/Modules/Session/AccountSessionFactory.cs`、`Assets/LiteGame/Modules/Session/SessionComposition.cs`、`Assets/LiteGame/Modules/Battle/{BattleContext,BattleInputPipeline,BattleClient,BattleComposition}.cs`、`Assets/LiteGame/Modules/Procedure/Stages/ProcedureBattle.cs`、`Assets/LiteGame/App/GameEntry.cs`。
- A2 搬迁：`Assets/LiteGame/Modules/Battle/Session/{BattleSession,MatchSessionFactory,BattleRunFactory,RoomSessionFace}.cs`（原 `Modules/Session/` 同名文件与 `.meta` 迁出）；`Assets/LiteGame/Modules/Battle/Abstractions/{IBattleRun,IBattleRunFactory,IMatchSessionFactory,IRoomSessionFace}.cs`（原 Session/Abstractions 迁出）。
- asmdef/允许表：`Assets/LiteGame/Adapters/LuaBridge/LiteGame.Adapters.LuaBridge.asmdef`、`Tests/LiteFramework.Core.Tests/ClientAsmdefAllowlistTests.cs`。
- 测试：`Tests/LiteFramework.Core.Tests/SessionBoundaryTests.cs`、`Tests/LiteNet.Tests/AccountSessionFactoryTests.cs`、`Tests/LiteNet.Tests/LiteNet.Tests.csproj`、`Assets/Tests/EditMode/MatchLoopLeakEditModeTests.cs`。
- 门禁/生成物：`scripts/gate/domain-matrix.py`、`Docs/说明/18-域间引用矩阵.md`、`.workflow/config.json`（session-boundary 命令 + e2e skip-build）。
- 文档/审计：`Docs/说明/{02,03,14,17}*.md`、`Docs/规范/03-领域查询表.md`、`Docs/设计文档/架构/客户端依赖注入专项设计.md`、`Docs/设计文档/游戏玩法/地图数据专项设计.md`、`Docs/施工进度/客户端重构线.md`（第 20 章）、`Docs/施工进度/README.md`、`Docs/审计/客户端/登记/{技术债,类设计}.md`。

## 后续建议

- 在用户明确指令后，运行一次完整 `sync-and-sim-e2e`（含 Player 构建）与真实联机手测，作为交付后验收；本批的 PlayMode 冒烟只覆盖装配/流程。
- 下一个网络端口化批次收口 `IBattleClient.Client` 逃生口，把 Battle/Session 真正需要的 `RoomClient` 成员上端口。
- 修正 `BattleSmokePlayModeTests.TearDown` 的容器释放探针（先保存引用再断言），让根容器释放证据不再依赖 `MatchLoopLeak` 替代。
- 若团队希望重新默认构建 Player 的 e2e，恢复 `.workflow/config.json` 的 e2e 参数；建议把“出包验证”与“日常 verify”拆成两条命令，避免本地无指令构建。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-11-01T18:26:07+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-11-01T18:28:20+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-11-01T19:39:16+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-11-01T19:42:17+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-11-01T19:44:18+08:00 · 完成「实施任务清单」→ 「子代理评审任务」
- 2026-11-01T20:03:32+08:00 · 完成「子代理评审任务」→ 「子代理先写测试」
- 2026-11-01T20:07:58+08:00 · 完成「子代理先写测试」→ 「子代理正式施工」
- 2026-11-01T20:42:14+08:00 · 完成「子代理正式施工」→ 「测试代码」
- 2026-11-01T21:20:19+08:00 · 完成「测试代码」→ 「主代理评审代码」
- 2026-11-01T21:20:33+08:00 · 完成「主代理评审代码」→ 「回写与收尾」
- 2026-11-01T21:20:53+08:00 · 完成「回写与收尾」→ 完成
