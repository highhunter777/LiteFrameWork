# 验收标准确认：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | 生产代码中 `new BattleClient(` 只出现在 Battle 域的 `BattleClientFactory`；`AccountSessionFactory` 不再直接 new 客户端（测试直构不在此限）。 | 源码检索 `Assets/LiteGame/**`（排除测试）。 | 下一阶段回填 |
| AC2 | A1 阶段 Session 域只允许 2 个已登记文件（`MatchSessionFactory.cs`/`BattleSession.cs`）暂时引用 `BattleContext` 具体类型；A2 后 `Modules/Session/**` 对 `BattleContext`/`BattleClient`/`GameplayMatchIdentity` 的具体类型引用为 0（端口引用保留）。 | `SessionBoundaryTests` 先红后绿（代码区扫描，`\b` 词界）+ 检索复核。 | 下一阶段回填 |
| AC3 | `IAccountRuntime`/`IBattleClient`/`IBattleClientFactory` 住在 `Battle/Abstractions`；Battle 程序集不引用 `LiteGame.Modules`；LuaBridge 新增对 Battle 的引用并登记允许表。 | asmdef references + `ClientAsmdefAllowlistTests`。 | 下一阶段回填 |
| AC4 | 4 个运行文件（5 个类型：`BattleSession`、`MatchSession`+`MatchSessionFactory`、`BattleRunFactory`、`RoomSessionFace`）迁到 `Modules/Battle/Session/`；`Modules/Session/` 只剩账号/传输/装配与端口文件。 | 目录清单断言（方案 AC4 列出的剩余文件）。 | 下一阶段回填 |
| AC5 | 4 个运行端口住在 `Battle/Abstractions`；`SessionComposition` 不再注册 `IMatchSessionFactory`/`IBattleRunFactory`；Procedure 只经端口消费。 | 注册面检索 + `ProcedureBoundaryTests`。 | 下一阶段回填 |
| AC6 | `Assets/LiteGame/Modules/Battle/**` 代码区不再引用 `DevHooks`；诊断由 App 侧 `Func<IBattleDevDiagnostics>` 注入；诊断附着/清空断言仍绿。 | 代码区扫描 + `MatchLoopLeakEditModeTests`。 | 下一阶段回填 |
| AC7 | Battle 代码区（去注释/字符串）与 asmdef 不引用 `IMapProvider`/`BattleScenes`/`LiteGame.Modules.Dev`；地图经 `IAccountRuntime.Maps/ResolveMapId`（LiteSim 类型）；`BattleContext.cs:168` 错误文案同批改为 `IAccountRuntime.Maps`。 | 代码区扫描 + asmdef references + 文案复核。 | 下一阶段回填 |
| AC8 | `domain-matrix --check` 绿；失真允许边文案更新后 `--write` 一致；A2 后归零的允许边（如 `Session→Input`）删除；`SessionBoundaryTests` 具体类型计数为 0（端口＝`Battle/Abstractions/**` 顶层声明）。 | `python scripts/gate/domain-matrix.py --check` + 守卫用例。 | 下一阶段回填 |
| AC9 | Unity 重编译 0 错；L1（PullRequest）全绿；EditMode filter `BattleSessionEditModeTests\|BattleRunPortEditModeTests\|MatchLoopLeakEditModeTests\|RoomBridgeEditModeTests` 绿；PlayMode `BattleSmokePlayModeTests` 绿（进本地测试房→开局/开火→离场，断言 `LastCreated==null` 与 VFX/音频归零）；`doc-lint` 0 violation。PlayMode 冒烟属装配/流程冒烟，不作为红线 13 真机操作证据；真实 Player 冒烟按项目约定需用户显式指令出包，列为交付后联机测试项。 | `scripts/gate/test.py --suite L1 --profile PullRequest`、`scripts/gate/doc-lint.py`、`unity-tests.py` 定向 filter、`BattleSmokePlayModeTests`。 | 下一阶段回填 |
| AC10 | A2 收尾回写：路线图 §3.5/§4.2、说明 14/02/17（`17:135`/`17:543`）、说明 03（§5/§6/§7.1）、规范 03 §4、依赖注入专项、地图数据专项、`Docs/施工进度/客户端重构线.md` 新增第 20 章并同步索引；同步代码注释；`BattleContext.cs:168` 文案；审计登记（`IBattleClient.Client` 逃生口、`BattleSmoke.TearDown` 探针短路、既有文档漂移）；`toolchain.py check` 通过并记录目录搬动核对。 | 文档 diff + `doc-lint` + `toolchain.py check`。 | 下一阶段回填 |

## 提审记录

- 提审时间：2026-11-01
- 提审方式：对话中逐条列出 + 结构化提问（AskUserQuestion）
- 用户原话或选择：确认全部（推荐）

## 用户确认

- 确认结论: 通过
- 确认人: highhunter777
- 确认时间: 2026-11-01
- 变更要求：无

## 变更记录

- 无（方案已按六轮独立子代理评审修订至 PASS；用户 2026-11-01 确认 AC1–AC10）
