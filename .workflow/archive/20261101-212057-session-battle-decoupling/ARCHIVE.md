# 归档：Session/Battle 解耦：对局运行归 Battle（A1 端口化 → A2 归位）

- 运行: session-battle-decoupling
- 创建时间: 2026-11-01T18:26:07+08:00
- 完成时间: 2026-11-01T21:20:53+08:00
- 归档时间: 2026-11-01T21:20:57+08:00
- 阶段: 10/10
- 任务: 完成 10/10，取消 0
- 验证: PASS（2026-11-01T21:16:36+08:00）

## 交付摘要

Session 域只保留账号/连接/传输，对局运行（Match/Battle 会话、运行工厂、房间门面）归 Battle 域。A1 新增 `IBattleClient`/`IBattleClientFactory`/`IAccountRuntime` 与 `BattleClientFactory`，完成 Account 会话与全部消费面端口化；A2 用 `unity-pipeline move_asset`（GUID 保留）迁移 4 文件 5 类型与 4 个端口，装配移交到 `BattleComposition`，诊断改 `Func<IBattleDevDiagnostics>` 注入。`SessionBoundaryTests` 从 A1 红测到 A2 终态绿；L1 1972、EditMode 33、PlayMode 1、完整 `wf verify` 66/66 全绿。

## 产物

- acceptance.md
- build-log.md
- evidence/red.json
- evidence/verify.json
- plan.md
- reviews/code-review.md
- reviews/plan-review.md
- reviews/task-review.md
- state.json
- tasks.md
- test-plan.md
