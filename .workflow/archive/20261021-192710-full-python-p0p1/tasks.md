# 实施任务清单：全量 Python P0/P1（地基 + hooks/L0）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 5/5（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 建立 _common.py 地基 | - | 完成 | _common.py 提供 repo_root/configure_stdio/info/warn/fail/run/EXIT_*；单测覆盖；后续入口可直接 import；只标准库 | python -m unittest scripts.tests.test_common -v | _common.py：repo_root/configure_stdio/info/warn/fail/run/EXIT_*；5/5 单测；仅标准库 |
| T2 | 迁移 hooks 提醒脚本到 Python | T1 | 完成 | shared-source-reminder.py 逐条移植路径/消息/ZCode+Claude schema；空/坏输入静默 0；两个 hook 配置指向 python；旧 PS 删除；测试覆盖四类 | python -m unittest scripts.tests.test_hook_reminder -v && python scripts/toolchain.py check | shared-source-reminder.py 移植；ZCode/Claude 配置改 python -X utf8；旧 PS 删除；hook 5/5；toolchain check OK |
| T3 | 迁移 L0 依赖与密钥扫描到 Python | T1 | 完成 | l0-dep-scan.py 保持四项检查/排除/退出码；repo baseline 仅已知 ParrelSync 个人路径违规、无其他类型；四类违规夹具 exit 1；--project-path 生效；旧 PS 删除；清单/fallback/build-player/CI 同批改 Python | python -m unittest scripts.tests.test_l0_dep_scan -v && python scripts/toolchain.py check | l0-dep-scan.py 四项检查+toolchain check；6/6 夹具/基线测试；旧 PS 删除；清单/fallback/build-player/CI 改 Python；repo baseline 仅 ParrelSync… |
| T4 | 同步 P1 注册面与现行文档 | T2, T3 | 完成 | scripts/README/AGENTS/05/13/修改指南/技术支持/代码地图/环境指南/待办/skills 的 L0 命令改 Python；工具索引 current；现行引用无旧 PS（历史施工卷除外） | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "l0-dep-scan.ps1\\|shared-source-reminder.ps1" scripts Docs AGENTS.md .agents .zcode .claude \| grep -v "Docs/施工进度" \| grep -v "归档" \|\| true | 现行脚本/CI/AGENTS/docs/skills 的 L0 与 hook 引用改 Python；工具索引 current；doc-lint 0/0；结构断言 2/2；旧引用仅历史卷/设计清单/测试断言 |
| T5 | 全量验证与范围审计 | T1, T2, T3, T4 | 完成 | Python 测试全绿；L0 基线测试覆盖 repo 已知 ParrelSync 违规与四类夹具；toolchain check/docs current；Doc 0/0；L1 1801；BuildHash --check-shared OK；未改 Unity/共享源集/协议/表/云侧；未执行 SSH | python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py && pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest | L1 1801；Python 62/62；L0 repo PASS（ParrelSync 已消失，测试兼容存量）；toolchain check 0 error/8 存量 warning；docs current；Doc 0/0；Buil… |

## 任务详情

### T1 · 建立 _common.py 地基

- 文件: scripts/_common.py, scripts/tests/test_common.py

### T2 · 迁移 hooks 提醒脚本到 Python

- 文件: .zcode/config.json, .claude/settings.json, scripts/hooks/shared-source-reminder.py, scripts/hooks/shared-source-reminder.ps1, scripts/tests/test_hook_reminder.py

### T3 · 迁移 L0 依赖与密钥扫描到 Python

- 文件: scripts/gate/l0-dep-scan.py, scripts/gate/l0-dep-scan.ps1, scripts/tests/test_l0_dep_scan.py, scripts/gate/toolchain.json, scripts/toolchain.py, scripts/build/build-player.ps1, .github/workflows/ci.yml

### T4 · 同步 P1 注册面与现行文档

- 文件: scripts/README.md, AGENTS.md, Docs/项目说明/05-验证与门禁.md, Docs/项目说明/13-调试手册.md, Docs/修改指南.md, Docs/工具链/技术支持.md, Docs/工具链/工具索引.md, Docs/开发导航/代码地图.md, Docs/环境/环境恢复指南.md, Docs/待办总览.md, .agents/skills/batch-wrapup/SKILL.md

### T5 · 全量验证与范围审计

- 文件: scripts, tests, Docs, .github, .zcode, .claude

