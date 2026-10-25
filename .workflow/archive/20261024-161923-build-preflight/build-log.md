# 施工记录：构建 preflight：变更驱动最小重建集

> 运行 build-preflight · 创建于 2026-10-24

## 施工记录

| 时间 | 任务 | 动作 | 结果 |
|---|---|---|---|
| 2026-10-24 | T1 | 实现 `scripts/build/build-plan.py` 采集/归一/忽略过滤 | 完成 |
| 2026-10-24 | T2 | 15 条分类规则、双口径动作、语义序去重、unknown 保守 | 完成 |
| 2026-10-24 | T3 | 复用 `gen-build-hash.collect`；`parse_csproj` 对账 RoomServer/MetaServer；`MapTableProjection` 已知例外 | 完成 |
| 2026-10-24 | T4 | CLI（`--base/--changed-file/--json/--project-path`）、`[build/plan]`、跨仓库 fail-fast | 完成 |
| 2026-10-24 | T5 | `scripts/tests/test_build_plan.py` 42 例（矩阵 6 行、双口径、漂移注入、只读、JSON、归一/忽略/base 失败、删除/重命名、csproj 双端、按工程漂移） | 42/42 |
| 2026-10-24 | T6 | `toolchain.json` 登记 `build.plan`；工具索引重生成；05/11/施工进度回写 | 完成 |
| 2026-10-24 | T7 | L1、BuildHash、doc/toolchain 门禁；审计登记 `MapTableProjection` 漏登记 | 完成 |

## 关键决定

- 只做 advisory 只读 CLI，不改 `build-player.py`；内环/外环两套动作分开表达，避免“内容不用重打包”的误导。
- 共享/Meta 判定复用 `gen-build-hash.py` 常量与 `collect()`；对“假设新增但尚不存在”的路径用源集目录前缀判定，保证 diff 演练可用。
- `MapTableProjection.cs` 不补登记（会改 BuildHash），本批按共享件处理 + `manifestDrift known=true` 警告；补登记另批并已入审计表。
- `.meta` 跟随其资产/代码的 area 分类；避免把 `.meta` 误判成内容。

## 偏差与处理

- 首版用 `collect()` 成员判定，测试中“尚不存在的路径”落 unknown；改为“collect 成员 ∪ 源集目录前缀（排除 Editor/bin/obj）”。
- 首版 `_resolve_link` 以仓库根解析 csproj 相对链接，导致 `../../` 误报；改为按 csproj 文本归一（去前导 `../`）。
- 中文路径被 git 默认转义：`git -c core.quotepath=false`。
- 动作顺序最初把 `run_authoritative_tests` 放在重生成前；改为 重生成 → 测试 → 重建 → 发布。

## 问题与修复

- 首轮独立评审 3 blocker（内容重打包、Packages/deploy 误判、矩阵不可数据驱动）→ 修订 2 双口径 + 规则补充 + 固定用例。
- 后续评审继续指出 meta 吞并服务端动作、发布动作缺失、漂移跨 csproj 误报 → 修订 4-6 全部吸收。
- `test_single_source_helpers` 报根推导重复：`build-plan.py` 改用 `_common.repo_root()`。

## 阶段流转
- 2026-10-24T14:37:28+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-24T15:39:41+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-24T16:19:23+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-24T16:19:23+08:00 · 完成「回写与收尾」→ 完成

## 验证结果
- 2026-10-24T14:56:19+08:00 · 验证 FAIL · 15/40 · 证据 evidence/verify.json
- 2026-10-24T15:13:18+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-24T15:16:08+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json
- 2026-10-24T15:53:53+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json
- 2026-10-24T16:13:27+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-24T16:16:30+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json

## 交付摘要

- 新增只读 `python scripts/build/build-plan.py`：diff → 内环/交付双口径最小重建集；15+1 个 area 规则、动作语义序去重、未知路径保守升级、共享/Meta 源链接按工程对账。
- 工具清单登记 `build.plan`；05/11/施工进度/审计回写；审计登记 `MapTableProjection.cs` 漏登记（另批补登记）。
- 42 例 Python 契约；工具链/文档/门禁全绿；BuildHash 不变。

## 验证结果

- 最终 `wf verify`（聚焦本批 16 条）：**PASS**（L1 1838/1838、build-plan 42/42、lane-filter 4/4、weaknet 18/18、weaknet EditMode 9/9、doc/toolchain/restore/L0/L2 meta 全绿）。
- `gen-build-hash.py --check-shared` = `0a998c8ac6c9de89`（110 files，未变）。
- 独立代码复评第三轮 PASS（0 blocker/major）。

## 未决事项

- `MapTableProjection.cs` 补登记 `SHARED_LINK_FILES`：会改 BuildHash，另批处理（已入客户端审计表）。
- preflight 仍是 advisory：未接 `build-player.py`/CI；接入方式另批。
- 规则对目录 `.meta`、`Assets/LiteNet/Diagnostics` 等非典型路径偏保守（unknown/内容），不阻塞。

## 文件清单

- `scripts/build/build-plan.py`（新）、`scripts/tests/test_build_plan.py`（新）
- `scripts/gate/toolchain.json`（build.plan 条目）
- `Docs/工具链/工具索引.md`（重生成）
- `Docs/项目说明/05-验证与门禁.md`、`Docs/项目说明/11-联机测试流程.md`
- `Docs/施工进度/资源与工具链线.md`（第34章）
- `Docs/审计/代码质量审计-客户端.md`（两条）

## 后续建议

- 补登记 `MapTableProjection.cs` 并与两端发布同批跑 BuildHash。
- 视需要在 `build-player.py` 启动时打印 preflight 摘要（只读 advisory）。
- 为 `_actual_link_sets`/`--changed-file`/R100 重命名补独立桩用例。
