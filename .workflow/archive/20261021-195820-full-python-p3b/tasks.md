# 实施任务清单：全量 Python P3b（nightly/L2/install-task）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 5/5（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 迁移 L2 Unity 门禁 | - | 完成 | l2-unity-gate.py 保留 meta 扫描/Pipeline/batchmode/新鲜度/console/Total 判据；--help；repo meta-scan PASS；非法 meta FAIL；test.py 自动优先 Python；旧 l2.ps1 删除 | python -m unittest scripts.tests.test_l2_entry -v && python scripts/gate/l2-unity-gate.py --meta-scan-only | l2-unity-gate.py meta/Pipeline/batchmode/新鲜度/console/Total；3/3 离线；旧 l2.ps1 待 T4 删除 |
| T2 | 迁移夜间门禁并保留 PS shim | T1 | 完成 | nightly-gate.py 编排 L1/L2/日志/PlayMode 标注/退出码；L1 调 test.py；L2 优先 Python；nightly-gate.ps1 改 @ps-shim 转发；测试 --help | python -m unittest scripts.tests.test_nightly_entry -v | nightly-gate.py 编排/日志/PlayMode 标注；PS shim 转发；help 1/1 |
| T3 | 迁移计划任务安装器 | - | 完成 | install-nightly-task.py 读 manifest surface；--what-if 打印 Python runner/script；--force 用 schtasks XML 注册并可回读；旧 PS 删除；测试不注册实际任务 | python -m unittest scripts.tests.test_install_nightly -v && python scripts/gate/install-nightly-task.py --what-if | install-nightly-task.py what-if/XML/schtasks；1/1；实际注册成功（C:\Python313\python.exe -X utf8 nightly-gate.py）；l2/install PS … |
| T4 | 同步 P3b 注册面与文档 | T1, T2, T3 | 完成 | 清单 env.nightly/env.nightly-install/surface runner+script、toolchain doctor install、CI L2、工具索引、现行文档/skills 改 Python；l2/install PS 删除；nightly shim 显式；旧引用清零（历史卷除外） | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "l2-unity-gate.ps1\\|install-nightly-task.ps1\\|nightly-gate.ps1" scripts Docs AGENTS.md .agents .github \| grep -v "Docs/施工进度" \| grep -v "归档" \| grep -v "Docs/审计" \|\| true | 清单 env.nightly/env.nightly-install/quality.l2/surface runner=python、script=nightly-gate.py；CI L2 改 Python；toolchain doc… |
| T5 | 全量验证与机器侧验收记录 | T1, T2, T3, T4 | 完成 | L2 meta-scan PASS；Python 全测；L1 1801；Doc 0/0；toolchain check/docs；BuildHash；install --what-if；完整 L2/任务注册未跑需如实记录 | python scripts/gate/l2-unity-gate.py --meta-scan-only && python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/gate/test.py --lane L1 --profile PullRequest && python scripts/gate/test.py --lane Doc && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py | L1 1801；Python 全测；Doc 0/0；restore/L0/L2-meta 通过；toolchain check 0 error/4 warning；BuildHash OK；计划任务回读 python nightly-ga… |

## 任务详情

### T1 · 迁移 L2 Unity 门禁

- 文件: scripts/gate/l2-unity-gate.py, scripts/gate/l2-unity-gate.ps1, scripts/tests/test_l2_entry.py

### T2 · 迁移夜间门禁并保留 PS shim

- 文件: scripts/gate/nightly-gate.py, scripts/gate/nightly-gate.ps1, scripts/tests/test_nightly_entry.py

### T3 · 迁移计划任务安装器

- 文件: scripts/gate/install-nightly-task.py, scripts/gate/install-nightly-task.ps1, scripts/tests/test_install_nightly.py

### T4 · 同步 P3b 注册面与文档

- 文件: scripts/gate/toolchain.json, scripts/toolchain.py, .github/workflows/ci.yml, scripts/README.md, AGENTS.md, Docs/工具链/技术支持.md, Docs/工具链/工具索引.md, Docs/开发导航/代码地图.md, Docs/项目说明/05-验证与门禁.md, Docs/项目说明/10-Windows云主机部署.md, Docs/待办总览.md

### T5 · 全量验证与机器侧验收记录

- 文件: scripts, Docs, AGENTS.md, .github

