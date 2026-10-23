# 归档：UI 系统 P2-2：起页/登记拆分（ui.page-scaffold / ui.page-register）

- 运行: ui-p2-scaffold-register
- 创建时间: 2026-10-23T22:34:41+08:00
- 完成时间: 2026-10-24T02:57:05+08:00
- 归档时间: 2026-10-24T02:57:06+08:00
- 阶段: 8/8
- 任务: 完成 11/11，取消 0
- 验证: PASS（2026-10-24T02:54:25+08:00）

## 交付摘要

P2-2 把“起页/登记”从 P2-1 的置灰占位拆成两个真实工具：`ui.page-scaffold`（美术：视觉基线→自包含 prefab+根契约/安全区+页面清单草稿）与 `ui.page-register`（工程：草稿契约门槛→表行/Lua/`Luban+BuildHash`→清单 Registered+缓存策略→契约按真实身份刷新；表写失败回滚复核、生成链失败回滚并重跑 Luban、契约刷新失败显式报红）。补了草稿契约链（schema `formId=0`、批量/漂移/diff 口径）、清单/契约原子写、工作台与外窗的指纹冲突保护与焦点重载、两个 window 工具与工具索引。新增 EditMode 31 例 + Python 10 例，UI 回归全绿，`wf verify` PASS。

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
