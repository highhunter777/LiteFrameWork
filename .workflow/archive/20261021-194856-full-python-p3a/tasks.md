# 实施任务清单：全量 Python P3a（test 主入口）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 3/3（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 迁移 test 主入口到 Python | - | 完成 | test.py 支持 L1/L2/L3/All/Doc + Profile；dotnet restore/build/test、TRX total、过滤、环境变量恢复、发现守卫与 PS 等价；L2 转调 PS；真跑 L1 1801；旧 test.ps1 删除 | python -m unittest scripts.tests.test_test_entry -v && python scripts/gate/test.py --lane L1 --profile PullRequest | test.py L1 1801；test_entry 4/4；发现守卫/Doc/环境恢复；旧 test.ps1 删除 |
| T2 | 同步 P3a 注册面与文档 | T1 | 完成 | CI 4 处、清单 quality.l1/l3/doc、toolchain.py L1/L3/Doc 分派、nightly PS L1 调用改 test.py；工具索引 current；现行文档/AGENTS/skills 命令改 --lane/--profile；旧 test.ps1 引用清零（历史卷除外） | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "test\.ps1" scripts Docs AGENTS.md .agents .github \| grep -v "Docs/施工进度" \| grep -v "归档" \| grep -v "Docs/审计" \|\| true | CI 4 处/清单 l1-l3-doc/toolchain 分派/nightly L1 调用改 test.py；索引 current；现行文档/skills 改 --lane/--profile；旧引用仅历史/设计对照 |
| T3 | 全量验证与范围审计 | T1, T2 | 完成 | test.py L1 1801；Doc 0/0；Python 测试全绿；toolchain check/docs；BuildHash OK；nightly PS 调用 test.py；未跑 L2/云侧；无 Assets/Server 改动 | python scripts/gate/test.py --lane L1 --profile PullRequest && python scripts/gate/test.py --lane Doc && python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py | test.py L1 1801；Doc lane PASS；Python 79/79；toolchain check 0 error/5 warning；restore/L0/Doc 全过；BuildHash OK；CI YAML/man… |

## 任务详情

### T1 · 迁移 test 主入口到 Python

- 文件: scripts/gate/test.py, scripts/gate/test.ps1, scripts/tests/test_test_entry.py

### T2 · 同步 P3a 注册面与文档

- 文件: .github/workflows/ci.yml, scripts/gate/toolchain.json, scripts/toolchain.py, scripts/gate/nightly-gate.ps1, scripts/README.md, AGENTS.md, Docs/工具链/技术支持.md, Docs/工具链/工具索引.md, Docs/开发导航/代码地图.md, Docs/项目说明/05-验证与门禁.md, Docs/项目说明/13-调试手册.md, Docs/修改指南.md, Docs/待办总览.md, .agents/skills

### T3 · 全量验证与范围审计

- 文件: scripts, Docs, .github, AGENTS.md

