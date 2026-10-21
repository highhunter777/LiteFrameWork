# 实施任务清单：全量 Python P2（内容/环境）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 迁移本地 CDN 配置加载器 | - | 完成 | _local_cdn.py 提供 ConfigPath/Directory/Bind/Port；相对目录按仓库根解析；缺文件/缺字段 fail loudly；serve/publish 共用；旧 loader 在 T3/T4 切换后删除 | python -m unittest scripts.tests.test_local_cdn -v | _local_cdn.py；4/4 配置测试；旧 loader 待 publish/serve 切换后删除 |
| T2 | 迁移签名候选生成器 | T1 | 完成 | gen-candidate.py 规范 manifest 字段序/转义/前缀/排序/空目录与 PS 一致；RSA XML 私钥 + cryptography 签名；内置锚自校验；信封 UTF-8 无 BOM；测试独立公钥验签；旧 PS 删除 | python -m unittest scripts.tests.test_gen_candidate -v | gen-candidate.py 规范 manifest/签名/锚自校验；2/2（含独立公钥验签）；旧 PS 删除 |
| T3 | 迁移 CDN 发布布局器 | T1, T2 | 完成 | publish-candidate.py 清理/布局/bundle 镜像/candidate.json 与 PS 等价；配置/覆盖解析正确；gen-candidate 失败透传；测试临时 key/anchors/目录；旧 PS 删除 | python -m unittest scripts.tests.test_publish_candidate -v | publish-candidate.py 布局/bundle/签名；2/2；旧 PS 删除 |
| T4 | 迁移本地 CDN 起服 | T1 | 完成 | serve-cdn.py 配置/端口/目录覆盖生效；目录缺失 exit 1；python -m http.server 子进程；临时目录 HTTP GET 通过；旧 PS 删除 | python -m unittest scripts.tests.test_serve_cdn -v | serve-cdn.py 配置/覆盖/HTTP GET/缺目录；2/2；旧 serve 与旧 loader 删除 |
| T5 | 迁移环境恢复检查 | T1 | 完成 | restore-check.py 检查项/OK-WARN-MISS/阻塞退出与 PS 一致；新增 cryptography；repo PASS；空项目 blocking exit 1；旧 PS 删除 | python -m unittest scripts.tests.test_restore_check -v | restore-check.py 检查项/阻塞/新增 cryptography；2/2（repo PASS、空项目 FAIL）；旧 PS 删除 |
| T6 | 同步 P2 注册面与现行文档 | T1, T2, T3, T4, T5 | 完成 | 清单四个 command、toolchain.py restore 子命令、build-player.ps1、工具索引、AGENTS/文档/skills 全 Python；旧 PS 文件不存在；现行旧引用清零（历史卷除外） | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "gen-candidate.ps1\\|publish-candidate.ps1\\|serve-cdn.ps1\\|restore-check.ps1\\|_local-cdn.ps1" scripts Docs AGENTS.md .agents .github \| grep -v "Docs/施工进度" \| grep -v "归档" \| grep -v "Docs/审计" \|\| true | 清单四 command/toolchain fallback/build-player/索引/现行文档改 Python；旧五 PS 删除；75/75 测试；check 0 error/6 存量 warning；doc-lint 0/0 |
| T7 | 全量验证与范围审计 | T1, T2, T3, T4, T5, T6 | 完成 | Python 测试全绿；restore repo PASS；toolchain check/docs；Doc 0/0；L1 1801；BuildHash OK；未真实发布/云侧；未改 Assets/Server/协议/表 | python -m unittest discover -s scripts/tests -p "test_*.py" -v && python scripts/gate/restore-check.py && python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py && pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest | L1 1801；Python 75/75；restore repo PASS；toolchain check 0 error/6 存量 warning；docs current；Doc 0/0；BuildHash OK；CI YAML/J… |

## 任务详情

### T1 · 迁移本地 CDN 配置加载器

- 文件: scripts/content/_local_cdn.py, scripts/content/_local-cdn.ps1, scripts/tests/test_local_cdn.py

### T2 · 迁移签名候选生成器

- 文件: scripts/content/gen-candidate.py, scripts/content/gen-candidate.ps1, scripts/tests/test_gen_candidate.py

### T3 · 迁移 CDN 发布布局器

- 文件: scripts/content/publish-candidate.py, scripts/content/publish-candidate.ps1, scripts/tests/test_publish_candidate.py

### T4 · 迁移本地 CDN 起服

- 文件: scripts/content/serve-cdn.py, scripts/content/serve-cdn.ps1, scripts/tests/test_serve_cdn.py

### T5 · 迁移环境恢复检查

- 文件: scripts/gate/restore-check.py, scripts/gate/restore-check.ps1, scripts/tests/test_restore_check.py

### T6 · 同步 P2 注册面与现行文档

- 文件: scripts/gate/toolchain.json, scripts/toolchain.py, scripts/build/build-player.ps1, scripts/README.md, AGENTS.md, Docs/项目说明/05-验证与门禁.md, Docs/项目说明/06-环境与协作纪律.md, Docs/项目说明/13-调试手册.md, Docs/修改指南.md, Docs/工具链/技术支持.md, Docs/工具链/工具索引.md, Docs/开发导航/代码地图.md, Docs/环境/环境恢复指南.md, Docs/待办总览.md, Docs/设计文档/客户端/内容/资源收集目录专项设计.md

### T7 · 全量验证与范围审计

- 文件: scripts, Docs, AGENTS.md, .github

