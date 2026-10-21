# 实施任务清单：全量 Python P4（构建/冒烟）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 4/4（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 迁移 Player 构建器 | - | 完成 | build-player.py 完整移植门禁/Profile/Pipeline/bake/bundles/dry_run/confirm/轮询/产物新鲜度；help/缺 profile 离线；旧 PS 删除 | python -m unittest scripts.tests.test_build_player -v | build-player.py 完整移植；help/缺 profile 2/2；PS 删除 |
| T2 | 迁移 Player 冒烟 | - | 完成 | player-smoke.py 完整移植清进程/log/启动/标记/MemLoop/InputModal；help/缺 exe 离线；旧 PS 删除 | python -m unittest scripts.tests.test_player_smoke -v | player-smoke.py 完整移植；help/缺 exe 2/2；PS 删除 |
| T3 | 同步 P4 注册面与文档 | T1, T2 | 完成 | 清单 build.player/build.smoke、CI L2 构建/冒烟、工具索引、AGENTS/文档/skills 切 Python；两 PS 删除；旧引用清零（历史卷除外） | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "build-player.ps1\\|player-smoke.ps1" scripts Docs AGENTS.md .agents .github \| grep -v "Docs/施工进度" \| grep -v "归档" \| grep -v "Docs/审计" \|\| true | 清单/CI/toolchain/索引/文档/skills 切 Python；旧 build/smoke PS 删除；check 1 warning |
| T4 | 全量验证与机器侧验收记录 | T1, T2, T3 | 完成 | build/smoke help+缺 profile/exe；Python 全测；L1 1801；Doc 0/0；toolchain check/docs；BuildHash；完整构建/冒烟未跑如实记录 | python -m unittest scripts.tests.test_build_player scripts.tests.test_player_smoke -v && python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/gate/test.py --lane L1 --profile PullRequest && python scripts/gate/test.py --lane Doc && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py | build/smoke help+缺 profile/exe；Python 91/91；L1 1801；Doc 0/0；check 0 error/1 warning；L2 meta/restore/L0 过；BuildHash OK；完… |

## 任务详情

### T1 · 迁移 Player 构建器

- 文件: scripts/build/build-player.py, scripts/build/build-player.ps1, scripts/tests/test_build_player.py

### T2 · 迁移 Player 冒烟

- 文件: scripts/build/player-smoke.py, scripts/build/player-smoke.ps1, scripts/tests/test_player_smoke.py

### T3 · 同步 P4 注册面与文档

- 文件: .github/workflows/ci.yml, scripts/gate/toolchain.json, scripts/README.md, AGENTS.md, Docs/工具链/技术支持.md, Docs/工具链/工具索引.md, Docs/开发导航/代码地图.md, Docs/项目说明/05-验证与门禁.md, Docs/项目说明/10-Windows云主机部署.md, Docs/项目说明/11-联机测试流程.md, Docs/修改指南.md, Docs/待办总览.md, .agents/skills

### T4 · 全量验证与机器侧验收记录

- 文件: scripts, Docs, AGENTS.md, .github

