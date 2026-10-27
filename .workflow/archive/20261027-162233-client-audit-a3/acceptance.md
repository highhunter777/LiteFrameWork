# 验收标准确认：客户端审计 A3

> 运行 client-audit-a3 · 对应方案 `plan.md` · 确认人：用户（highhunter777）

## 验收标准

| 编号 | 验收内容 | 判定方式 |
|---|---|---|
| AC1 | 三大件拆分落地：`BattleViewRig` 不再持有取件/HUD/反馈/视角域字段与实现；`UIBindIndex` 不再持有本地化/输入订阅/列表域状态；`StageMachineCore` 无树字段与 `BuildTree`；外提件一类一文件 | 结构探针 `red-structure-check.py` 63 项由红转绿（`evidence/red.json` → `evidence/structure-green.txt`）+ 代码评审 |
| AC2 | 公共 API 不破：`UIBindIndex`/`HierarchicalStageMachine` public 成员集合与签名不变；`BattleViewRig` internal 构造/`LoadAsync`/`Attach`/`Tick`/`TickPreSample`/`Dispose` 签名不变；`StageMachineCore` 仅 protected 面受限变更（树字段删除/构造收敛/新增 `InitializeCore`/`_transitions` `_audit` 构造尾段赋值） | `git diff` 逐项核对；`: StageMachineCore` 派生类 grep；Unity 编译 |
| AC3 | Unity 重编译 0 错 | `unity command recompile` + `recompile_status failed=false` + `console` 无本批 error |
| AC4 | L1（PullRequest）全量 PASS；27 个定向 EditMode 类通过；若有失败项须 pristine 对照证明与基线一致且不落本批改动面 | `python scripts/gate/test.py --suite L1 --profile PullRequest`；`scripts/gate/unity-tests.py` 定向过滤；失败项 pristine 对照记录 |
| AC5 | 行为零变化：tick/dispose 相位逐行保持（方案相位表）；降级日志文案与异常类型不变；Play 冒烟显示 `context-created` + `view-attached`，`ended reason=Leave`，`[BattleView]`/`Hud_Battle` 已销毁，无新增 error/NRE | 代码评审逐行对照相位表；`editor_play` → `TestModeRuntime.RequestExit()` → `find_gameobjects` 与 `console`；截图 `evidence/a3-play.png` |
| AC6 | 审计回写完成：5 条登记删除、归档各追加 1 行；批次计划 A3 覆盖 4→5、A5 10→9（删 StageMachineCore 条）、未结清 127→122；客户端审计线新增一章；doc-lint 0 | `git diff` 核对登记/归档/批次计划/进度卷；`python scripts/gate/doc-lint.py` |
| AC7 | 三子批代码文件集互斥、可独立回退 | 每子批完成记录 `git status` 并计算相对上一快照的差集 → `evidence/a3-N-diff.txt`；三份差集无交集 |

## 提审记录

- 2026-10-27：以 `AskUserQuestion` 向用户逐条提交上述 7 条验收标准，并同时征询提交方式。
- 用户选择：**确认通过（推荐）**；提交方式选择 **三子批分别提交（推荐）**。

## 用户确认

- 确认结论: 通过
- 确认人：highhunter777（用户）
- 确认时间：2026-10-27
- 用户原话/选择：「确认通过（推荐）」；「三子批分别提交（推荐）」

## 变更记录

| 日期 | 变更 | 原因 |
|---|---|---|
| 2026-10-27 | 初版 7 条，用户确认 | — |
