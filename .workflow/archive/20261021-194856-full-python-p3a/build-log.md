# 施工记录：全量 Python P3a（test 主入口）

> 运行 full-python-p3a · 创建于 2026-10-21T19:42:28+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T19:42:54+08:00 · 新增任务 T1「迁移 test 主入口到 Python」
- 2026-10-21T19:42:54+08:00 · 新增任务 T2「同步 P3a 注册面与文档」
- 2026-10-21T19:42:54+08:00 · 新增任务 T3「全量验证与范围审计」
- 2026-10-21T19:44:59+08:00 · 开始任务 T1「迁移 test 主入口到 Python」
- 2026-10-21T19:46:04+08:00 · 完成任务 T1 · 证据: test.py L1 1801；test_entry 4/4；发现守卫/Doc/环境恢复；旧 test.ps1 删除
- 2026-10-21T19:46:08+08:00 · 开始任务 T2「同步 P3a 注册面与文档」
- 2026-10-21T19:46:08+08:00 · 完成任务 T2 · 证据: CI 4 处/清单 l1-l3-doc/toolchain 分派/nightly L1 调用改 test.py；索引 current；现行文档/skills 改 --lane/--profile；旧引用仅历史/设计对照
- 2026-10-21T19:46:08+08:00 · 开始任务 T3「全量验证与范围审计」
- 2026-10-21T19:47:18+08:00 · 完成任务 T3 · 证据: test.py L1 1801；Doc lane PASS；Python 79/79；toolchain check 0 error/5 warning；restore/L0/Doc 全过；BuildHash OK；CI YAML/manifest JSON/nightly AST OK；无 Assets/Serve…

## 关键决定

- **P3 拆 P3a/P3b**：本批只迁 `test.py`；nightly/l2/install 保留 PS，避免在无 Unity/管理员环境同时硬切。
- **L2 暂转调 PS**：test.py 优先找 `l2-unity-gate.py`，不存在则调 `l2-unity-gate.ps1`；P3b 切换后自动走 Python。
- **nightly 只改 L1 一行**：`nightly-gate.ps1` 的 L1 调用改 `python test.py --lane L1 --profile Nightly`，L2 仍走 PS，计划任务 surface 不变。
- **CI/清单/工具链同批切换**：4 处 CI、quality.l1/l3/doc、toolchain.py 分派、工具索引、现行文档/skills。
- **只删 test.ps1**：保留 nightly/l2/install PS，直到 P3b 计划任务重注册完成。

## 偏差与处理

- 初版全局替换 `-Lane/-Profile` 时误伤了 `R3-Profile` 锚点（变成 `R3--profile`）；doc-lint 抓到 2 处，已回退为 `R3-Profile`。
- 同一替换误把 `litegame-online-test-release` skill 的 `build-player.ps1 -Profile` 改成 `--profile`；已改回 `-Profile`（build-player 仍是 PS 参数）。
- `test.py` 初版在 Doc 车道前也检查 dotnet，导致无 dotnet 机器连 Doc 都跑不了；改为仅 L1/L3/All 检查。
- 工作流 `L1`/新增 `doc-lane` 验证命令改用新 test.py；避免删 test.ps1 后工作流自身断链。

## 问题与修复

- **R3 锚点误伤**：2 个文件恢复 `R3-Profile` 后 doc-lint 0/0。
- **build-player 参数误伤**：skill 两处恢复 `-Profile`。
- **test.py 发现守卫**：临时 Tests/Bad.csproj 在 dotnet 前被 `*.Tests.csproj` 契约拦截；测试通过。
- **L1 输出顺序**：`info` 与 dotnet 子进程混排，因 stdout 缓冲顺序有交错；不影响退出码/测试结果，未为本批引入刷新逻辑。

## 验证结果

- 先行失败：`evidence/red.json`，5 个断言失败（test.py 缺失/清单旧命令），无环境错误。
- 最终验证：`evidence/verify.json` 8/8 PASS（L1 1801、Doc、restore、L0、Python 79/79、toolchain check 0 error/5 warning、docs current、Doc lane）。
- 附加：`test.py --lane L1` 自行跑出 1801；`test.py --lane Doc` PASS；CI YAML/清单 JSON/nightly PS AST OK；BuildHash `--check-shared` OK。
- 范围：仅 scripts/Docs/AGENTS/CI；未改 Assets/Server/Luban/Tests/ProjectSettings；未跑 L2/云侧。
- 2026-10-21T19:43:31+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T19:47:05+08:00 · 验证 PASS · 8/8 · 证据 evidence/verify.json
- 2026-10-21T19:48:43+08:00 · 验证 PASS · 8/8 · 证据 evidence/verify.json

## 交付摘要

- `scripts/gate/test.py` 替代 `test.ps1`：L1/L2/L3/All/Doc、Profile、dotnet restore/build/test、TRX total、发现契约、环境变量恢复；L2 暂转调 PS。
- 新入口真跑 L1 **1801**、Doc 通过；CI/清单/`toolchain.py`/工具索引/现行文档/skills 全切 `test.py --lane/--profile`。
- `nightly-gate.ps1` L1 调用改 Python，夜间链不断；`test.ps1` 删除。
- 测试 4/4 + 全量 Python 79/79；无 Assets/Server 改动。

## 未决事项

- P3b 未做：nightly/l2/install-task 仍 PS；计划任务 surface 仍 `pwsh + nightly-gate.ps1`，需用户确认后重注册。
- L2/Unity 未跑；本批 L2 仅保留转调 PS 的兼容路径。
- `check` 仍有 5 条未触碰旧 PS 的编码/EOL 告警（build-player/player-smoke/gen-build-hash/_ssh-host/nightly），按 P3b/P4/P5 清理。
- 未提交：本轮改动在工作区；`.workflow/` 本地记录不入库。

## 文件清单

- 新增：`scripts/gate/test.py`、`scripts/tests/test_test_entry.py`。
- 删除：`scripts/gate/test.ps1`。
- 修改：`.github/workflows/ci.yml`、`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/gate/nightly-gate.ps1`、`scripts/tests/test_toolchain.py`、`scripts/README.md`、`AGENTS.md`、`Docs/README.md`、`Docs/修改指南.md`、`Docs/工具链/技术支持.md`、`Docs/工具链/工具索引.md`、`Docs/开发导航/代码地图.md`、`Docs/待办总览.md`、`Docs/项目说明/README/01/05/13`、测试/UI/性能/架构相关设计引用、`.agents/skills` 四个 skill、`Docs/施工进度/资源与工具链线.md`（第15章）。
- 方案：`Docs/设计文档/架构/脚本形态统一专项设计.md` P3 拆 P3a/P3b。

## 后续建议

- 下一运行 **P3b**：nightly / l2 / install-task；需 Unity/批处理环境与计划任务重注册（用户确认机器侧动作）。
- P4 build/smoke、P5 deploy/local-chain 后清零 `.ps1`；P3b 前不要改 nightly 计划任务 surface。
- 提交前按工具链线拆分；本轮未提交。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T19:42:28+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T19:42:49+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T19:42:58+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T19:43:09+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T19:43:54+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T19:47:48+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T19:48:43+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T19:48:56+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T19:48:56+08:00 · 完成「回写与收尾」→ 完成
