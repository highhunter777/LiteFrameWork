# 代码评审（主代理）：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00
>
> 代码由施工子代理撰写；本文件由主代理基于实际 diff 与验证证据评审落盘。

## 评审输入

- 代码基线：`git rev-parse HEAD` = `48ccda6179c9e7369d2ebe6966af1e9167094db4`；评审按工作区现状（含并行未提交改动，评审只针对本批文件）。
- 本批主要变更：新增 4 个 Battle 端口/工厂；`AccountSessionFactory`/`AccountSession` 端口化；A2 `move_asset` 迁移 4 文件 5 类型 + 4 个端口；`BattleComposition`/`SessionComposition`/`GameEntry` 装配移交；诊断 `IBattleDevDiagnostics` 注入；`SessionBoundaryTests` + 既有测试同步；`domain-matrix` 与文档回写。
- 评审角色：主代理（代码作者为施工子代理；主代理不是本批作者）。
- 评审范围：`Assets/LiteGame/Modules/{Battle,Session,Procedure}/**`、`Assets/LiteGame/App/GameEntry.cs`、`Assets/LiteGame/Adapters/{Dev,LuaBridge}/**`、`Assets/Tests/**`、`Tests/{LiteNet.Tests,LiteFramework.Core.Tests}/**`、`scripts/gate/domain-matrix.py`、相关 Docs；未评审并行线改动。
- 验证证据：`evidence/verify.json`（见复测记录）。

## 变更范围

- 对照任务与方案：T1–T10 全部完成；A1（端口化 + 消费面切换）与 A2（搬迁 + 装配移交 + 守卫收紧）均落在方案范围内；未改 `LiteSim`/`LiteNet`/`LiteSync`/BuildHash 共享源集，未改协议，未拆程序集，未收口 `IBattleClient.Client` 逃生口。
- 变更文件/模块：
  - 新增端口/工厂：`IBattleClient`、`IBattleClientFactory`、`IAccountRuntime`（Battle/Abstractions）；`BattleClientFactory`（Battle 域）。
  - Account：`AccountSessionFactory.cs`（`AccountSession : IAccountRuntime`、`Battle`/`CreateMatchServices()`/地图委托；工厂 7 参经 `IBattleClientFactory`）。
  - 消费面：`BattleContext`/`BattleInputPipeline` → `IBattleClient`；`MatchSessionFactory` 吃 `IAccountRuntime`、`Resolve<IBattleClient>`、`account.CreateMatchServices()`、`account.Maps/ResolveMapId`；`BattleSession`/`RoomSessionFace`/`BattleRunFactory` 端口化。
  - A2 搬迁：`BattleSession`/`MatchSessionFactory`（含 `MatchSession`）/`BattleRunFactory`/`RoomSessionFace` → `Modules/Battle/Session/`；`IBattleRun`/`IBattleRunFactory`/`IMatchSessionFactory`/`IRoomSessionFace` → `Battle/Abstractions/`（`move_asset`，GUID 保留）。
  - 装配：`BattleComposition` 注册客户端/Match/运行工厂、补 `Identity` 依赖、诊断注入；`SessionComposition` 只留 Account/传输、补 `Battle` 依赖；`GameEntry` 传 `() => DevHooks.Diagnostics`。
  - 测试/门禁/文档：`SessionBoundaryTests`、`AccountSessionFactoryTests`、`MatchLoopLeakEditModeTests`、`LiteNet.Tests.csproj`、`LuaBridge` asmdef/允许表、`domain-matrix.py`/说明 18、说明 02/03/14/17、规范 03、DI/地图专项、施工进度第 20 章、审计登记。

## 正确性与边界

- 主路径：`ProcedureMatch` 建 `AccountSession`（Account scope + `IBattleClient`）→ `ProcedureBattle` 经 `IBattleRunFactory.Create(IAccountRuntime)` 在 Battle 域建 Match 子域 + `BattleContext` → `BattleSession` 驱动；流程只认 `IBattleRun`/`IBattleRunFactory`。PlayMode `BattleSmokePlayModeTests` 从 Boot 起服到本地测试房进局/开火/离场全链通过。
- 生命周期：Account Scope 仍由流程 finally 释放；`AccountSession.Dispose` 先释放 Scope（含按 LIFO 释放的客户端），Match 子域由 `MatchSession.Dispose` 释放；`MatchLoopLeakEditModeTests` 100 次循环验证作用域/事件/传输/诊断读口无残留。
- 边界与错误路径：
  - `AccountSessionFactory` 创建客户端失败仍回滚 `accountScope.Dispose()`；`MatchSessionFactory` 建域失败仍 `matchServices?.Dispose()`。
  - `CreateMatchServices()` 明确返回**未 Seal** 的 Match 子容器，由 `MatchSessionFactory` 注册后 Seal；契约写入端口注释与 `AccountSessionFactory` 注释。
  - 地图委托由 `SessionComposition` 从 `IMapProvider`/`BattleScenes` 投影，`AccountSessionFactory` 构造 `?? throw`；Battle 不依赖 Map 域类型，缺图仍由 `BattleContext` fail-fast。
  - 诊断从静态 `DevHooks` 直引改为 `Func<IBattleDevDiagnostics>` 注入；发布形态不传实现 = 不登记；`BattleSession` 只在 Attach/Detach 点使用端口。
  - `IBattleClient.Client` 是已登记的过渡逃生口；`NetworkDebug` 只使用端口上的 `Disconnect`/`BeginReconnect`，没有引入具体类型。
- 回归风险：A2 搬迁与装配移交是同批；回滚需同时恢复文件位置/`DependsOn`/allowlist/矩阵。A1 可独立回滚（端口 + 构造点），已在方案与施工记录中保留边界。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：
  - `SessionBoundaryTests`：代码区扫描 + `\b` 词界；A1 红→绿（AccountSessionFactory/RoomSessionFace），A2 终态 2/2 绿；覆盖 AC1/AC2。
  - `AccountSessionFactoryTests` 5/5：Account scope 登记/释放、Join 事件透传、`Resolve<IBattleClient>`、服务域密封；覆盖端口登记与回滚面。
  - `MatchLoopLeakEditModeTests` 1/1：100 次进房/离场，作用域/事件总线/传输/诊断读口无残留。
  - `BattleSessionEditModeTests` 12/12、`BattleRunPortEditModeTests` 7/7、`RoomBridgeEditModeTests` 13/13：对局等待/终局/视图挂载/房间门面行为未回退。
  - `BattleSmokePlayModeTests` 1/1：Boot→Main→本地测试房进局→开局/开火→离场；`LastCreated==null` 与 VFX/音频归零。
  - `domain-matrix --check`、`doc-lint`、`toolchain.py check`、L1 1972 项、`sync-and-sim-e2e --skip-build --skip-smoke` 哈希/服务端对账。
- 是否存在迎合实现的测试：无。新增守卫按具体类型判红，未按实现文件名放行；既有测试未删/弱化，`MatchLoopLeak` 保留诊断附着/清空与作用域释放断言。
- 已知测试缺口（不阻塞，已登记）：
  - 真实 Player 冒烟未跑（按项目约定需显式出包指令）；以 Unity PlayMode 全链冒烟替代，联机手测留交付后。
  - `BattleSmokePlayModeTests.TearDown` 容器探针被 `_container = null` 短路，已登记技术债；根域残留证据由 `MatchLoopLeak` 的逐次作用域释放断言承担。
  - A2 守卫白名单清空在搬迁后执行，搬迁前红证据未单独采集；A1 守卫已覆盖同类具体引用，终态绿，已记录偏差。

## 通用性与扩展性

- 根因与复用边界：根因是“会话域同时承担账号生命周期与对局运行所有权”。修法是把对局运行的实现与端口一起归 Battle，并由 `IAccountRuntime` 依赖倒置提供会话材料；不是对 `AccountSessionFactory` 加特判。
- 扩展点与接入方式：新增对局能力只扩展 `IAccountRuntime`/`IBattleClient` 端口或 `BattleComposition` 的工厂注册，不新增 Session→Battle 具体依赖；新增会话材料仍需显式上端口，Battle 不反向引 `LiteGame.Modules`。
- 硬编码、特判、魔法值：地图/诊断/规则均经端口或装配注入；`SessionBoundaryTests` 的类型清单是显式契约（含 A2 批次号/终态），不是按文件名放行。
- 重复逻辑与过度抽象：`IBattleClient.Client` 保留为过渡逃生口，未为假想需求拆网络端口；`CreateMatchServices()` 返回容器是当前容器模型的必要窄口（方案 P-6 已裁决），未新增额外抽象。
- 相关场景复用与回归影响：Procedure/App 零具体 Battle 依赖；LuaBridge 经 `IRoomSessionFace` 端口；Map/Config/Input 仍按既有装配面消费；域矩阵 `Session→Battle` 12 全部端口引用、`Session→Input` 删除。
- 命名、结构与复杂度：端口/实现同侧（Battle/Abstractions + Battle/Session），Battle asmdef 不引 `LiteGame.Modules`；新增文件保持单职责；未引入 public 可变状态。

## 安全与性能

- 安全：未改协议、传输、安全信封、BuildHash；`IBattleClientFactory` 只转发构造，KCP 信封仍由调用方注入，fail-closed 口径不变；无新增输入边界/注入面；诊断注入在发布形态可为 null，不泄露敏感信息。
- 性能与资源：端口调用是纯转发，无新增热路径分配；`SessionBoundaryTests` 是 L1 文本守卫，不进入运行时热路径；Match/Account 释放顺序与既有作用域语义一致；`MatchLoopLeak` 100 次循环验证无残留。未引入周期性热路径日志。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | minor | `SessionComposition.cs` | A2 移除对局工厂后仍保留 `using LiteGame.Modules.Input` 与 `Input` 依赖声明，代码与注释仍写“账号/对局会话工厂” | 删除未用 using、移除 `Input` 依赖、同步类/`GameEntry` 注释；Unity 编译 0 错、L1 1972 PASS | 已修复 |
| C-2 | minor | `.workflow/active/session-battle-decoupling` / A2 守卫 | A2 白名单清空在搬迁后执行，搬迁前红证据未单独采集 | 在施工记录如实登记；A1 守卫覆盖同类具体引用；A2 终态 2/2 绿 | 已记录，不阻塞 |
| C-3 | low | `Assets/Tests/UI/PlayMode/BattleSmokePlayModeTests.cs:87-93` | TearDown 容器探针 `_container = null` 后断言恒真 | 登记 `Docs/审计/客户端/登记/技术债.md` 测试面；本批不改测试，根域残留由 `MatchLoopLeak` 承担 | 已登记 |
| C-4 | low | `Assets/LiteGame/Modules/Battle/Abstractions/IBattleClient.cs` | `Client`（`RoomClient`）为过渡逃生口 | 登记 `Docs/审计/客户端/登记/技术债.md` 架构边界；后续网络端口化批次收口 | 已登记 |
| C-5 | minor | 文档 | 施工进度第 20 章初稿 `Session→Battle 32→12` 计数不准 | 改为 `30→12（A1 峰值 42）`；`doc-lint` 复跑 | 已修复 |

无 blocker/major 问题。前序任务评审的 B1–B6/M1–M3/RB1–RM2 已按用户“任务评审默认跳过”裁决收口，施工记录保留前两轮独立评审与修订依据。

## 复测记录

- 评审轮次: 第 1 轮（本批主代理代码评审）。
- 修复后执行的命令与结果：
  - `dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo` → 2/2 PASS（A2 终态）。
  - `dotnet test Tests/LiteNet.Tests/LiteNet.Tests.csproj --filter FullyQualifiedName~AccountSessionFactoryTests --nologo` → 5/5 PASS。
  - `python scripts/gate/test.py --suite L1 --profile PullRequest` → 1972/1972 PASS（A1、A2 后各复跑）。
  - Unity `recompile_status` → `completed/failed=false/errors=[]`；A1/A2 后均确认 0 错。
  - EditMode：`BattleSessionEditModeTests` 12/12、`BattleRunPortEditModeTests` 7/7、`MatchLoopLeakEditModeTests` 1/1、`RoomBridgeEditModeTests` 13/13（共 33/33）。
  - PlayMode：`BattleSmokePlayModeTests` 1/1（异步 `run_tests` + `test_status` completed）。
  - `python scripts/gate/domain-matrix.py --check` → OK（12 域一致）。
  - `python scripts/gate/doc-lint.py` → 0 violation / 0 warning；`python scripts/gate/test.py --suite Doc` → PASS。
  - `python scripts/toolchain.py check` → OK（2 条既有 warning：`sync-and-sim-e2e.py`/`unity-tests.py` 未登记为可执行脚本）。
  - `python scripts/gate/sync-and-sim-e2e.py --skip-build --skip-smoke` → OK（read-client-hash/check-shared/server-reconcile）。
  - `python -m unittest scripts.tests.test_toolbox -v` → 26/26 PASS（config 恢复后）。
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run session-battle-decoupling verify` → **PASS（2026-11-01T21:16:36）**；全部配置命令通过；e2e 命令以 `--skip-build --skip-smoke` 运行（未指令不构建 Player，偏差已记录）。
- 复评执行与复评范围（主代理）：基于 `git diff`/`git status` 逐文件核对本批文件；确认 Step 边界、端口签名、装配序、测试断言与文档回写；未发现 blocker/major。C-1 修复后复跑 Unity 编译、L1 与完整 `wf verify`。
- 验证证据：`evidence/verify.json`（完整 `wf verify` 已运行；e2e 以 `--skip-build --skip-smoke` 运行，避免未指令出包；详见构建记录偏差）。

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 主代理
- 评审时间: 2026-11-01
