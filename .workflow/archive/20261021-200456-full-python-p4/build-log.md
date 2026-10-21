# 施工记录：全量 Python P4（构建/冒烟）

> 运行 full-python-p4 · 创建于 2026-10-21T19:59:27+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T19:59:45+08:00 · 新增任务 T1「迁移 Player 构建器」
- 2026-10-21T19:59:45+08:00 · 新增任务 T2「迁移 Player 冒烟」
- 2026-10-21T19:59:45+08:00 · 新增任务 T3「同步 P4 注册面与文档」
- 2026-10-21T19:59:49+08:00 · 新增任务 T4「全量验证与机器侧验收记录」
- 2026-10-21T20:02:10+08:00 · 开始任务 T1「迁移 Player 构建器」
- 2026-10-21T20:02:10+08:00 · 完成任务 T1 · 证据: build-player.py 完整移植；help/缺 profile 2/2；PS 删除
- 2026-10-21T20:02:10+08:00 · 开始任务 T2「迁移 Player 冒烟」
- 2026-10-21T20:02:10+08:00 · 完成任务 T2 · 证据: player-smoke.py 完整移植；help/缺 exe 2/2；PS 删除
- 2026-10-21T20:02:10+08:00 · 开始任务 T3「同步 P4 注册面与文档」
- 2026-10-21T20:02:10+08:00 · 完成任务 T3 · 证据: 清单/CI/toolchain/索引/文档/skills 切 Python；旧 build/smoke PS 删除；check 1 warning
- 2026-10-21T20:02:10+08:00 · 开始任务 T4「全量验证与机器侧验收记录」
- 2026-10-21T20:03:22+08:00 · 完成任务 T4 · 证据: build/smoke help+缺 profile/exe；Python 91/91；L1 1801；Doc 0/0；check 0 error/1 warning；L2 meta/restore/L0 过；BuildHash OK；完整构建/冒烟未跑（机器侧）；无 Assets/Server

## 关键决定

- **完整移植、离线验收**：build-player/smoke 按 PS 逐段移植；本机无 Unity/Player，只验 help/缺 profile/缺 exe；完整 IL2CPP/Player 冒烟留机器侧。
- **Pipeline 封装**：build-player 集中 tab-field 3 Result JSON 提取、eval_file UTF-8 无 BOM、target/backend/scenes/bake/bundles/dry_run/confirm/轮询/产物新鲜度。
- **smoke**：literal 标记、MemLoop/InputModal 断言、清旧进程/log、超时/早退处理按 PS。
- **注册面**：清单 build.player/build.smoke、CI L2、toolchain build 子命令、工具索引、AGENTS/文档/skills 全 Python；两 PS 删除。
- **P5 范围**：scripts 只剩 deploy/local-chain 的 PS。

## 偏差与处理

- 完整构建/冒烟未跑：无 Unity 会话/Player 产物；机器侧验收明确待办，不宣称构建链通过。
- 全局替换 `build-player.ps1/player-smoke.ps1` 时误改了 `test_toolchain` 的“旧 PS 不存在”断言（变成断言 `.py` 不存在）；已恢复 `.ps1` 断言。
- 文档中旧 `build-player.py:行号` 引用随代码迁移失效；已去掉行号只留路径。
- `toolchain.py build` 子命令从 `_pwsh`/PS 路径改 `sys.executable`/`.py`；帮助文本同步。

## 问题与修复

- **结构断言误伤**：见上；修后 91/91。
- **CI/清单**：build/smoke 命令改 Python 参数（`--profile/--skip-restore-check/--timeout-sec`）；CI YAML 解析通过。
- **player-smoke 退出/清理**：Popen 轮询、terminate/kill、taskkill 旧实例；未用真实 exe 验证。
- **build-player dry/confirm**：Result JSON 嵌套 result、状态/valid/success 判据按 PS；未用真实 Pipeline 输出对照。

## 验证结果

- 先行失败：5 个断言失败（两脚本缺失/清单旧 build PS），无环境错误。
- 最终验证：`evidence/verify.json` 9/9 PASS（L1 1801、Doc、restore、L0、Python tests 91/91、toolchain check 0 error/1 warning、docs current、Doc lane、L2 meta）。
- 附加：build/smoke help+缺 profile/exe；CI YAML OK；BuildHash `--check-shared` OK；完整构建/冒烟未跑。
- 2026-10-21T20:00:24+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T20:03:10+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json
- 2026-10-21T20:04:52+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json

## 交付摘要

- `build-player.py`：门禁/Profile/Pipeline/bake/bundles/dry_run/confirm/轮询/产物新鲜度完整移植。
- `player-smoke.py`：清进程/log、启动、标记轮询、MemLoop/InputModal 断言完整移植。
- 清单/CI/toolchain/索引/现行文档/skills 全切 Python；两 PS 删除。
- Python 91/91、L1 1801、Doc 0/0、check 0 error/1 warning；无 Assets/Server 改动。

## 未决事项

- **完整 IL2CPP 构建/Player 冒烟未跑**：需 Unity 编辑器/Pipeline 与已构建 exe；本批只验离线面。
- P5 未做：deploy/local-chain 仍 PS；`scripts/**/*.ps1` 未归零。
- 1 条未触碰旧 PS（deploy/_ssh-host）编码告警；P5 清理。
- 未提交：本轮改动在工作区；`.workflow/` 本地记录不入库。

## 文件清单

- 新增：`scripts/build/build-player.py`、`player-smoke.py`、`scripts/tests/test_build_player.py`、`test_player_smoke.py`。
- 删除：`scripts/build/build-player.ps1`、`player-smoke.ps1`。
- 修改：`.github/workflows/ci.yml`、`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/tests/test_toolchain.py`、`scripts/README.md`、`AGENTS.md`、Docs/工具链/技术支持/工具索引/代码地图/根目录地图/项目说明 04/05/07/09/11/12/13/修改指南/待办/环境记录、设计文档相关引用、`.agents/skills` 四个 skill、`Docs/施工进度/资源与工具链线.md`（第17章）。

## 后续建议

- 机器侧跑一次 `python scripts/build/build-player.py --profile BuildProfiles/windows-x64.json` 与 `python scripts/build/player-smoke.py`，对照旧链路验收。
- 下一运行 **P5**：deploy-room/gm/wg-peer/local-chain + `_ssh_host`，最后清零 `.ps1`。
- 提交前按工具链线拆分；本轮未提交。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T19:59:27+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T19:59:39+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T19:59:55+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T20:00:01+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T20:00:56+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T20:03:49+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T20:04:52+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T20:04:52+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T20:04:56+08:00 · 完成「回写与收尾」→ 完成
