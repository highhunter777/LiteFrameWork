# 方案：全量 Python P3b（nightly/L2/install-task）

> 运行 full-python-p3b · 设计权威：[脚本形态统一专项设计](../../../Docs/设计文档/架构/脚本形态统一专项设计.md) v0.2 §6（P3b）

## 背景与目标

- P3a 已把 `test.py` 落地；L2 仍由 test.py 转调 PS。P3b 迁移 `l2-unity-gate`、`nightly-gate`、`install-nightly-task`。
- L2 需 Unity/Pipeline/batchmode，完整测试依赖编辑器；本批先做**可离线验证**的 meta 扫描/帮助/参数与代码移植，完整 L2 与计划任务重注册作为机器侧验收单独标注。
- 目标：三个 Python 入口；`test.py` 自动走 Python L2；`nightly` 以 `@ps-shim` 过渡到计划任务重注册；install 工具可 `--what-if` 打印 Python 任务动作。

## 范围与非目标

- 新增 `scripts/gate/l2-unity-gate.py`、`nightly-gate.py`、`install-nightly-task.py`。
- 删除 `l2-unity-gate.ps1`、`install-nightly-task.ps1`；`nightly-gate.ps1` 先改 `@ps-shim`（任务重注册成功后删）。
- 清单 `env.nightly`/`env.nightly-install`/surface runner+script；`toolchain.py doctor` install 路径；CI L2 命令；test.py L2 自动选 Python；文档/skills/索引。
- 非目标：不实际跑 batchmode/PlayMode、不改测试分类；实际计划任务重注册需机器侧执行；不提交。

## 需求澄清与假设

- 已确认：全量 Python；P3b 三个脚本；L2/计划任务需机器侧验收。
- 假设：Unity CLI/Pipeline 可用性同 PS；`unity test` 参数不变；`schtasks` 注册当前用户任务；P3b 本次可只做 what-if，完整 L2/注册另行执行。

## 候选方案与取舍

| 方案 | 思路 | 取舍 |
|---|---|---|
| A 三脚本 + shim（采用） | l2/nightly/install Python；nightly PS 暂薄壳 | 代码全迁，任务可继续运行，机器侧再切 |
| B 一次硬切含任务 | 同时删 PS/重注册任务 | 无 Unity/权限时无法验证，风险高 |
| C 只迁 l2 | nightly/install 后置 | 夜间链仍 PS，收益低 |

推荐 A：保留可回滚边界，先把代码与注册面切好。

## 通用性与扩展性

- 复用 `_common`；L2 的 Pipeline/batchmode 封装可被后续 P4 构建复用。
- install 从 manifest surface 读值，不硬编码任务名/脚本；nightly 日志/退出码模式可复用。
- 不抽通用任务调度框架；L2 的 Unity 命令集中单处，避免第二套。

## 现状与约束

- L2 PS 332 行：meta GUID 扫描；Pipeline（set_autotick/eval_file/recompile_status/console/异步 run_tests/test_status）；batchmode（unity test EditMode+PlayMode）；新鲜度 mtime 守卫；Total=0 判失败。
- nightly PS 94 行：L1/L2 编排、UTF-8 日志、PlayMode 是否执行标注、退出码。
- install PS 74 行：读 manifest surface；注册当前用户 Interactive/Limited 日任务；WhatIf/Force。
- Unity CLI/Pipeline 依赖本机；`install` 写计划任务，需用户级/管理员权限（视机器）。

## 设计要点

- **L2**：`--project-path/--unity-exe/--test-output/--meta-scan-only/--run-editmode-tests`；meta 扫描纯 Python；Pipeline 用 `unity command` 子进程 + 5s 轮询；batchmode `unity test`；保留 mtime/console/Total=0 判据；默认 UnityExe 沿用旧路径并接受 env/参数覆盖（迁移不改行为）。
- **nightly**：`--project-path/--skip-l1`；L1 调 test.py；L2 优先 Python l2，否则 PS；UTF-8 日志到 `TestResults/nightly`；PlayMode 标记同 PS；退出码 0/1。
- **install**：读 manifest surface；`--what-if` 只打印；`--force` 调 `schtasks` 注册 Python runner + nightly.py；XML 保留 Interactive/Limited/StartWhenAvailable/2h/IgnoreNew；回读 action。
- **过渡**：`nightly-gate.ps1` 改 `@ps-shim` 转发 Python；install/l2 PS 删除；任务重注册成功后删 shim。
- **注册面**：manifest surface runner `python`、script `nightly-gate.py`；CI L2 用 Python；`test.py` 已优先 Python L2。

## 测试策略

- l2：`--help`；repo `--meta-scan-only` PASS；临时非法 `.meta` FAIL；不跑 Pipeline/batchmode。
- nightly：`--help`；不实际跑 L2。
- install：`--what-if` 读 manifest 并打印 Python runner/script；不注册任务。
- 结构：清单/service/CI/test.py 自动选择/旧 PS 删除（nightly shim 允许）。
- 基线：Python 全测、L1、Doc、toolchain check/docs、BuildHash；完整 L2/任务注册另行机器侧验收。

## 验收标准

- [ ] L2 meta 扫描/帮助/参数与 PS 一致；Pipeline/batchmode 代码路径完整（未跑）。
- [ ] nightly 编排/日志/PlayMode 标注/退出码完整；L1 调 test.py。
- [ ] install `--what-if` 输出 Python 动作；实际注册脚本可执行（本次可只 what-if）。
- [ ] 清单/CI/toolchain/Surface/文档全 Python；nightly shim 显式。
- [ ] L1 1801、Python 全测、Doc 0/0、toolchain check、BuildHash。
- [ ] 未跑完整 L2/未改 Assets/共享源集；提交另行确认。

## 风险与回滚

- L2 无 Unity 验证 → 先 meta/静态，完整 L2 机器侧验收；保留 PS 一版可回退。
- 计划任务注册失败 → nightly PS shim 保留，任务继续可跑；manifest surface 警告。
- Pipeline/batchmode 命令差异 → 保留 PS 到机器侧 L2 通过；逐项对照。
- 回滚：恢复三个 PS、清单/CI/toolchain/文档命令；无数据迁移。
