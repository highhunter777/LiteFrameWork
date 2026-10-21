# 实施任务清单：脚本形态统一

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 补齐用户面脚本清单登记 | - | 完成 | 清单新增 deploy 分类与 deploy.room/deploy.gm/deploy.wg-peer/env.local-chain/quality.doc-lint/quality.scan-missing-guids 六条；command 与 docs 路径存在；生成索引 current；清单契约测试断言新条目 | python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python -m unittest discover -s scripts/tests -p test_toolchain*.py -v | manifest: deploy 分类 + 6 条工具；toolchain.py check OK；docs --check current；Manifest/Cli/GeneratedDocs 15/15 |
| T2 | 内部库与一次性脚本归类 | - | 完成 | toolchain_manifest/toolchain_docs/toolchain_surfaces 改为 _ 前缀并更新导入；local-cdn.config.ps1 改为 _local-cdn.ps1 并更新 dot-source；patch_p1_plugin_metas.py 带 @oneshot；现行脚本与操作文档旧引用为零（历史施工卷保留原貌） | grep -rn "toolchain_manifest.py\\|toolchain_docs.py\\|toolchain_surfaces.py\\|local-cdn.config.ps1" scripts Docs .github .zcode .claude \|\| true; python -m unittest discover -s scripts/tests -p test_toolchain*.py -v; python scripts/gate/doc-lint.py | renamed _toolchain_manifest/_toolchain_docs/_toolchain_surfaces + _local-cdn.ps1；patch_p1 标 @oneshot；import/dot-source/… |
| T3 | 实现孤儿/编码/EOL 检查器 | T1, T2 | 完成 | 新增 _toolchain_scripts.py 并由 toolchain.py check 调用；未登记可执行脚本告警且 strict 升级为错误；_ 前缀/tests/@oneshot/已登记入口引用/hook surface 引用五类豁免；BOM/非 ASCII PS/CRLF 告警；strict 行为用临时夹具测试，repo 非 strict check 通过并如实列出存量告警 | python -m unittest discover -s scripts/tests -p test_toolchain*.py -v; python scripts/toolchain.py check | 新增 _toolchain_scripts.py；check_scripts 覆盖孤儿/五类豁免/编码/EOL；接入 toolchain.py check；14/14 单测；repo check 0 error / 20 存量 warni… |
| T4 | 新登记入口契约基线 | T1 | 完成 | 六个新入口有原生帮助；local-chain 未知命令、gm/wg-peer 缺必填参数返回 2；顶层摘要/错误行带 [域/工具] 前缀；不触发 SSH/云侧调用；grep 确认无调用面依赖交互式 Mandatory 提示；契约测试钉住 | python -m unittest discover -s scripts/tests -p test_script*.py -v; python scripts/gate/doc-lint.py | gm/wg-peer/deploy-room 加 CmdletBinding；local-chain 补 comment-based help 并移到脚本顶部；缺参/未知命令 exit 2；六入口顶层前缀；契约测试 7/7；未发起 SSH… |
| T5 | 统一本次触碰文件的编码与行尾 | T1, T2, T4 | 完成 | 新增 .gitattributes 声明 scripts/** eol=lf；仅本次 T1/T2/T4 触碰的 CRLF 文件归一为 LF；PS 含中文按 UTF-8 BOM 或去中文处理；转换前后去 EOL 内容 hash 一致；未触碰的存量脚本只告警，不做一次性全仓格式化 | git check-attr text eol -- scripts/toolchain.py scripts/deploy/gm.ps1; python -m unittest discover -s scripts/tests -p test_toolchain*.py -v; python scripts/toolchain.py check | .gitattributes scripts/** eol=lf；gm/wg/deploy-room/_local-cdn/publish/serve 转 UTF-8 BOM+LF，scan 转 LF；去 EOL/BOM hash 前后一… |
| T6 | 回写脚本 README 与工具索引 | T1, T2, T4, T5 | 完成 | scripts/README 记录六类角色、canonical command、内部库/一次性脚本规则与六个新入口；quality.doc-lint 与 quality.scan-missing-guids 在技术支持或 README 有指针；工具索引由清单重生成；旧路径/旧命令引用无残留；Doc lane 0/0 | python scripts/toolchain.py docs --check; python scripts/gate/doc-lint.py; grep -rn "local-cdn.config.ps1\\|toolchain_manifest.py" scripts/README.md Docs \|\| true | scripts/README 记录六类角色/canonical 六入口/内部库与@oneshot规则；技术支持补 deploy/local/doc-lint/scan 指针；工具索引 current；doc-lint 0/0 |
| T7 | 全量验证与范围审计 | T1, T2, T3, T4, T5, T6 | 完成 | 跑完清单检查/生成索引/Doc/L1/契约测试；确认无 Unity、共享源集、协议、表、BuildHash 改动；无 SSH/云侧调用；记录命令与结果 | python scripts/toolchain.py check && python scripts/toolchain.py docs --check && python scripts/gate/doc-lint.py && pwsh -NoProfile -File scripts/gate/test.ps1 -Lane L1 -Profile PullRequest | L1 1801 PASS；doc 0/0；toolchain tests 44/44；check 0 error/10 存量 warning；docs current；BuildHash --check-shared OK；py_comp… |

## 任务详情

### T1 · 补齐用户面脚本清单登记

- 文件: scripts/gate/toolchain.json, Docs/工具链/工具索引.md, scripts/tests/test_toolchain.py

### T2 · 内部库与一次性脚本归类

- 文件: scripts/toolchain.py, scripts/_toolchain_manifest.py, scripts/_toolchain_docs.py, scripts/_toolchain_surfaces.py, scripts/content/_local-cdn.ps1, scripts/content/publish-candidate.ps1, scripts/content/serve-cdn.ps1, scripts/pipeline/patch_p1_plugin_metas.py

### T3 · 实现孤儿/编码/EOL 检查器

- 文件: scripts/_toolchain_scripts.py, scripts/toolchain.py, scripts/tests/test_toolchain_scripts.py, scripts/tests/test_toolchain.py

### T4 · 新登记入口契约基线

- 文件: scripts/deploy/gm.ps1, scripts/deploy/wg-peer.ps1, scripts/deploy/deploy-room.ps1, scripts/local/local-chain.ps1, scripts/gate/doc-lint.py, scripts/content/scan_missing_guids.py, scripts/tests/test_toolchain_contracts.py

### T5 · 统一本次触碰文件的编码与行尾

- 文件: .gitattributes, scripts/deploy/gm.ps1, scripts/deploy/wg-peer.ps1, scripts/deploy/deploy-room.ps1, scripts/local/local-chain.ps1, scripts/content/scan_missing_guids.py, scripts/content/_local-cdn.ps1

### T6 · 回写脚本 README 与工具索引

- 文件: scripts/README.md, Docs/工具链/工具索引.md, Docs/工具链/技术支持.md

### T7 · 全量验证与范围审计

- 文件: scripts, tests, Docs/工具链, scripts/gate/toolchain.json

