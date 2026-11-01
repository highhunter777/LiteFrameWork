# 测试计划：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

> 运行 session-battle-decoupling · 创建于 2026-11-01T18:26:07+08:00

## 测试策略

- 测试层级（单元/集成/端到端/契约）：L1 文本守卫（`Tests/LiteFramework.Core.Tests`，不依赖 Unity）；A1/A2 落定后由既有 L1 用例、EditMode 定向用例与 PlayMode `BattleSmokePlayModeTests` 覆盖行为回归。
- 关键风险与覆盖重点：
  1. Session 域重新长出 Battle 具体类型（`BattleContext`/`BattleClient`/`GameplayMatchIdentity`）——用代码区扫描守卫按“文件+类型”白名单钉住，A1 白名单 2 文件带 A2 批次号，A2 清空。
  2. `AccountSessionFactory` 重新直接 `new BattleClient`——独立断言钉住。
  3. 词界误判：`IBattleClient`/`IBattleRun` 不含于具体类型计数（`\b` 词界）。
  4. 既有行为面（Account scope 登记/释放、Match 循环无残留、诊断附着/清空、房间门面）由既有测试更新后继续覆盖，不降低断言强度。
- 测试先行范围：本轮只新增文本守卫（T1）；既有测试的接口/构造更新属于 T3/T4/T6/T7 施工内容，不在此阶段改生产代码。
- 扫描口径：复用 `ProcedureBoundaryTests.StripCommentsAndStrings` 同款轻量词法（块注释/行注释/字符串字面量置空），只扫代码区；文件范围 `Assets/LiteGame/Modules/Session/**/*.cs`。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| SB-1 `AccountSessionFactory_不直接新建BattleClient` | L1 文本守卫 | T1 / AC1、AC2（A1） | 读 `Assets/LiteGame/Modules/Session/AccountSessionFactory.cs`，去注释/字符串后断言不含 `new BattleClient(` | 当前失败（`:91` 命中）；T3 后通过 |
| SB-2 `Session域具体类型引用仅白名单` | L1 文本守卫 | T1 / AC2（A1） | 扫描 `Modules/Session/**/*.cs` 代码区，匹配 `\bBattleContext\b`/`\bBattleClient\b`/`\bGameplayMatchIdentity\b`；白名单固定 `MatchSessionFactory.cs`、`BattleSession.cs`（带 `A2` 批次号） | 当前失败（`AccountSessionFactory.cs`/`RoomSessionFace.cs` 命中）；T3/T4 清除后通过 |
| SB-3 `Session域无Battle具体类型引用` | L1 文本守卫 | T6 / AC2（A2） | 同一扫描，白名单清空 | T6 搬迁前加入并记录红证据；搬迁后通过 |
| 既有回归 | L1/EditMode/PlayMode | T3/T4/T6/T7/T8 / AC1、AC5、AC6、AC9 | `AccountSessionFactoryTests`、`MatchLoopLeakEditModeTests`、`BattleSessionEditModeTests`、`BattleRunPortEditModeTests`、`RoomBridgeEditModeTests`、`BattleSmokePlayModeTests` | 按任务清单逐项通过，不删用例/不降断言 |

## 预期失败证据

- 命令：`python .agents/skills/staged-delivery/scripts/workflow.py --run session-battle-decoupling verify --red`
- 观察到的失败：`SB-1` 命中 `AccountSessionFactory.cs` 的 `new BattleClient(`；`SB-2` 命中 `AccountSessionFactory.cs` 与 `RoomSessionFace.cs` 的 `BattleClient` 具体引用（白名单仅 2 文件）。
- 为什么这是正确的失败：当前工作区仍由 Session 域直接构造/持有 `BattleClient`，正是本交付要消除的耦合；失败是断言失败而非编译错误/环境故障。

## 运行方式

- 全量测试：`python scripts/gate/test.py --suite L1 --profile PullRequest`（最终由 `wf verify` 统一跑配置命令）
- 仅运行新增测试：`dotnet test Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj --filter FullyQualifiedName~SessionBoundaryTests --nologo`
- 红测：`python .agents/skills/staged-delivery/scripts/workflow.py --run session-battle-decoupling verify --red`

## 豁免与说明

- A2 守卫（SB-3）是同一守卫的白名单收紧，按 T6 描述在搬迁前加入并记录红证据；A1 期间不加入空表白名单，避免 L1 长期常红。
- 文档/矩阵类验收（AC8/AC10）不先写失败测试，按任务清单用 `domain-matrix --check`、`doc-lint`、`toolchain.py check` 做替代验证。
