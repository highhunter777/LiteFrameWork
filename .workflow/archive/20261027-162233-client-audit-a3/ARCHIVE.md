# 归档：客户端审计 A3：三大件拆分（BattleViewRig / UIBindIndex / HierarchicalStageMachine）

- 运行: client-audit-a3
- 创建时间: 2026-10-27T12:24:36+08:00
- 完成时间: 2026-10-27T16:22:11+08:00
- 归档时间: 2026-10-27T16:22:33+08:00
- 阶段: 10/10
- 任务: 完成 6/6，取消 0
- 验证: PASS（2026-10-27T16:21:46+08:00）

## 交付摘要

- A3 三子批完成（A3-1 `f18c2af1` / A3-2 `e30297b8` / A3-3 `30b006b3`；收口修订 `33344480` 标签订正 / `d09fcd16` 台账订正；文档回写 `34a4d869`）：A3-1 `BattleViewRig` 1058→258 行（取件/HUD/反馈/视角/权威反馈六件外提 + 8 个 Acquire 同构收口）；A3-2 `UIBindIndex` 697→约 430 行（本地化/输入订阅/列表三件外提）；A3-3 层级树表示法收回层级机，共享内核去表示法并加初始化守卫。
- 公共 API 不破（UIBindIndex/HSM public 成员不变；StageMachineCore 仅 protected 面受限变更）；行为由相位表 + 既有用例 + Play 冒烟佐证。
- 审计登记删除 5 条（类设计高 3 + 低 1 + 重复实现 1），归档追加 4+1 行；批次计划 A3 5 / A5 9 / 未结清 122；进度卷第 5 章；修改指南与相关设计/说明文档引用同步。

## 产物

- acceptance.md
- build-log.md
- config.backup.json
- evidence/.battle-targeted.txt
- evidence/a3-1-diff.txt
- evidence/a3-2-diff.txt
- evidence/a3-3-diff.txt
- evidence/a3-play.png
- evidence/console-play.json
- evidence/find-hud.json
- evidence/find-view.json
- evidence/red.json
- evidence/structure-green.txt
- evidence/targeted-battle.txt
- evidence/targeted-debugkernel.txt
- evidence/targeted-fsm.txt
- evidence/targeted-uibind.txt
- evidence/verify.json
- plan.md
- red-structure-check.py
- reviews/code-review.md
- reviews/plan-review.md
- reviews/task-review.md
- state.json
- tasks.md
- test-plan.md
