# 施工记录：脚本形态统一

> 运行 script-form-unification · 创建于 2026-10-21T18:48:37+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T18:50:26+08:00 · 新增任务 T1「补齐用户面脚本清单登记」
- 2026-10-21T18:50:29+08:00 · 新增任务 T2「内部库与一次性脚本归类」
- 2026-10-21T18:50:32+08:00 · 新增任务 T3「实现孤儿/编码/EOL 检查器」
- 2026-10-21T18:50:39+08:00 · 新增任务 T4「新登记入口契约基线」
- 2026-10-21T18:50:42+08:00 · 新增任务 T5「统一本次触碰文件的编码与行尾」
- 2026-10-21T18:50:44+08:00 · 新增任务 T6「回写脚本 README 与工具索引」
- 2026-10-21T18:50:47+08:00 · 新增任务 T7「全量验证与范围审计」
- 2026-10-21T18:50:56+08:00 · 更新任务 T3（verify）
- 2026-10-21T18:50:56+08:00 · 更新任务 T4（verify）
- 2026-10-21T18:51:14+08:00 · 更新任务 T3（acceptance）
- 2026-10-21T18:51:15+08:00 · 更新任务 T6（acceptance）
- 2026-10-21T18:51:15+08:00 · 更新任务 T4（acceptance）
- 2026-10-21T18:51:32+08:00 · 更新任务 T5（acceptance, files）
- 2026-10-21T18:52:49+08:00 · 更新任务 T4（files）
- 2026-10-21T18:53:47+08:00 · 开始任务 T1「补齐用户面脚本清单登记」
- 2026-10-21T18:54:12+08:00 · 完成任务 T1 · 证据: manifest: deploy 分类 + 6 条工具；toolchain.py check OK；docs --check current；Manifest/Cli/GeneratedDocs 15/15
- 2026-10-21T18:54:17+08:00 · 开始任务 T2「内部库与一次性脚本归类」
- 2026-10-21T18:54:46+08:00 · 更新任务 T2（acceptance）
- 2026-10-21T18:54:52+08:00 · 完成任务 T2 · 证据: renamed _toolchain_manifest/_toolchain_docs/_toolchain_surfaces + _local-cdn.ps1；patch_p1 标 @oneshot；import/dot-source/现行文档同步；toolchain check OK；doc-lint 0/0；结…
- 2026-10-21T18:55:04+08:00 · 开始任务 T3「实现孤儿/编码/EOL 检查器」
- 2026-10-21T18:55:32+08:00 · 完成任务 T3 · 证据: 新增 _toolchain_scripts.py；check_scripts 覆盖孤儿/五类豁免/编码/EOL；接入 toolchain.py check；14/14 单测；repo check 0 error / 20 存量 warning
- 2026-10-21T18:55:32+08:00 · 更新任务 T5（files）
- 2026-10-21T18:55:40+08:00 · 开始任务 T4「新登记入口契约基线」
- 2026-10-21T18:56:45+08:00 · 完成任务 T4 · 证据: gm/wg-peer/deploy-room 加 CmdletBinding；local-chain 补 comment-based help 并移到脚本顶部；缺参/未知命令 exit 2；六入口顶层前缀；契约测试 7/7；未发起 SSH/云侧调用
- 2026-10-21T18:56:52+08:00 · 开始任务 T5「统一本次触碰文件的编码与行尾」
- 2026-10-21T18:57:12+08:00 · 完成任务 T5 · 证据: .gitattributes scripts/** eol=lf（check-attr 验证）；gm/wg/deploy-room/_local-cdn 转 UTF-8 BOM+LF，scan 转 LF；去 EOL/BOM hash 前后一致；check 告警 20->12（余量为未触碰存量）
- 2026-10-21T18:57:25+08:00 · 开始任务 T6「回写脚本 README 与工具索引」
- 2026-10-21T18:57:43+08:00 · 完成任务 T6 · 证据: scripts/README 记录六类角色/canonical 六入口/内部库与@oneshot规则；技术支持补 deploy/local/doc-lint/scan 指针；工具索引 current；doc-lint 0/0
- 2026-10-21T18:57:43+08:00 · 开始任务 T7「全量验证与范围审计」
- 2026-10-21T18:59:56+08:00 · 重新打开任务 T5：T2 触碰的 publish/serve 也需按编码规则补 BOM
- 2026-10-21T18:59:59+08:00 · 开始任务 T5「统一本次触碰文件的编码与行尾」
- 2026-10-21T18:59:59+08:00 · 完成任务 T5 · 证据: .gitattributes scripts/** eol=lf；gm/wg/deploy-room/_local-cdn/publish/serve 转 UTF-8 BOM+LF，scan 转 LF；去 EOL/BOM hash 前后一致；check 告警 20->10（余量为未触碰存量）
- 2026-10-21T19:00:58+08:00 · 完成任务 T7 · 证据: L1 1801 PASS；doc 0/0；toolchain tests 44/44；check 0 error/10 存量 warning；docs current；BuildHash --check-shared OK；py_compile/PS parser/CDN 加载器 OK；改动面仅 scripts/Do…

## 关键决定

- **本次范围**：S-A 登记与检查地基 + S-C 仅触碰文件 + 新登记入口 S-B 基线；存量入口全面契约与 L2-D1 的 `args` schema 留后续运行，避免被未施工的 L2-D1 阻塞。
- **登记口径**：新增 `deploy` 分类与 `deploy.room` / `deploy.gm` / `deploy.wg-peer` / `env.local-chain` / `quality.doc-lint` / `quality.scan-missing-guids`；canonical 命令统一走 `toolchain.py run <id>`，直调保留兼容但不作为文档首选。
- **内部面**：`toolchain_*` 三个实现模块改 `_` 前缀；`local-cdn.config.ps1` 改 `_local-cdn.ps1`；`patch_p1_plugin_metas.py` 标 `@oneshot`；不接受“入口/内部库混放”的灰色形态。
- **检查器**：孤儿判定消费清单 command、`_` 前缀、`tests/`、`@oneshot`、已登记入口引用与 hook/CI surface 引用；不维护第二份脚本白名单。strict 只用于夹具测试，repo 当前保留 10 条存量告警（按触碰归一）。
- **编码/EOL**：`.gitattributes` 只约束 `scripts/**` 为 LF；本次触碰的 PS 文件转 UTF-8 BOM，Python 转 LF 无 BOM；未触碰存量不做一次性全仓格式化。
- **入口契约**：缺参/未知命令显式 usage + exit 2，避免 PowerShell Mandatory 交互提示；deploy 脚本不加伪造 dry-run；所有云侧验证只走 `Get-Help`/参数校验，不发起 SSH。
- **历史文档**：`Docs/施工进度/**` 中的旧路径/旧模块名作为当时证据保留，不回改；现行脚本与操作文档已同步。

## 偏差与处理

- 设计原把 `wg-peer.ps1` 的 2/3/4 写成脚本本地退出码；实际是远端 here-string 哨兵。已在施工前修正设计 §2/§4.3/§7，本地按 `throw`→1 与显式 usage→2 收口。
- T5 初版任务文件清单把未触碰的 gate 脚本也列入 EOL 归一，违反“按触碰归一”；评审时收窄为本次 T1/T2/T4 触碰文件。
- T2 触碰了 `publish-candidate.ps1` / `serve-cdn.ps1` 的 dot-source，但 T5 初版未把它们列入编码归一；T5 重开一次补 BOM，最终告警由 20 降到 10。
- `local-chain.ps1` 的 comment-based help 初次放在文件头 banner 之后，`Get-Help` 未识别；移到文件最顶部后通过契约测试。
- `gm.ps1` 原用 `[ValidateSet]` + Mandatory；为避免空默认值被 ValidateSet 拒绝并避免交互提示，改为手写 Action 校验 + exit 2。

## 问题与修复

- **CRLF 测试文案不匹配**：检查器最初输出 “must use LF line endings”，测试断言 “CRLF”；改为 “must use LF instead of CRLF”，语义更明确且测试通过。
- **Get-Help 中文输出乱码/标题本地化**：测试改为设置 `[Console]::OutputEncoding` 后断言帮助正文关键词，不依赖本地化标题。
- **BOM 与 dot-source**：`_local-cdn.ps1` 加 BOM 后实测可被 `pwsh` dot-source 并返回 Directory/Port，确认没有破坏配置加载。
- **strict 仍有 10 条存量告警**：均为未触碰的旧脚本（build-player/player-smoke/gen-build-hash/gen-candidate/_ssh-host/l0/nightly/restore/test/hook）；按计划保留为后续触碰批次的告警，不在本次强行全仓格式化。

## 验证结果

- 先行失败：`evidence/red.json`，26 个断言失败（缺条目/缺检查器/退出码不符/缺 `.gitattributes`），无环境错误。
- 最终验证：`evidence/verify.json` 5/5 PASS（L1 1801、Doc 0/0、toolchain-tests 44/44、toolchain-check 0 error/10 legacy warnings、docs current）。
- 附加验证：BuildHash `--check-shared` OK（459a3145904693b7，110 files）；Python `py_compile` OK；7 个触碰 PS 文件 AST 解析 OK；`_local-cdn.ps1` 实际加载返回 `LocalCDN` / `18090`；canonical `toolchain.py run quality.doc-lint` 与 `deploy.gm -Action bogus`（exit 2，无 SSH）实测。
- 2026-10-21T18:53:33+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T18:59:32+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json
- 2026-10-21T19:00:54+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json
- 2026-10-21T19:02:12+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json
- 2026-10-21T19:03:39+08:00 · 验证 PASS · 5/5 · 证据 evidence/verify.json

## 交付摘要

- 用户面脚本登记补齐：新增 6 个 canonical 工具，生成索引与技术支持同步；`toolchain.py check` 现在能发现孤儿脚本、编码与 EOL 问题。
- 内部面可机械识别：`_toolchain_*.py`、`_local-cdn.ps1`、`@oneshot`；旧模块名/旧 dot-source 在现行脚本与操作文档中清零。
- 新入口契约基线：帮助可用、缺参/未知命令 exit 2、顶层输出前缀、非交互验证；未执行任何云侧或破坏性动作。
- 文件形态：`.gitattributes` 固定 `scripts/**` 为 LF；本次触碰文件按编码规则归一，转换前后去 EOL/BOM 内容 hash 一致。
- 施工范围严格限定在 scripts/Docs/.gitattributes/.workflow；未改 Unity 资产、共享源集、协议、表或 BuildHash。

## 未决事项

- 存量入口的全面帮助/输出/退出码归一（S-B 全量）未做；剩 10 条存量编码/EOL 告警按“触碰即归一”滚动。
- L2-D1 的清单 `args` schema、`run --dry-run`、`refresh_hash` 严格失败仍归工具链 L2-D；S-B 的参数 fail-fast 依赖它。
- 共享根推导收口（`_common.py` / `_common.ps1`）未做，归 S-C2；本次只保证新入口与检查器不复制根推导。
- 语言边界规则已写进 scripts/README，但 S-D 的试点迁移未启动；不做全量 PS→Python。
- 当前工作区包含并行线的大量未提交改动；本次改动未提交，提交边界需用户确认。

## 文件清单

- 新增：`.gitattributes`、`scripts/_toolchain_scripts.py`、`scripts/_toolchain_manifest.py`、`scripts/_toolchain_docs.py`、`scripts/_toolchain_surfaces.py`、`scripts/tests/test_toolchain_scripts.py`、`scripts/tests/test_toolchain_contracts.py`、`scripts/content/_local-cdn.ps1`。
- 修改：`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/tests/test_toolchain.py`、`scripts/deploy/{gm,wg-peer,deploy-room}.ps1`、`scripts/local/local-chain.ps1`、`scripts/content/{scan_missing_guids.py,publish-candidate.ps1,serve-cdn.ps1}`、`scripts/gate/doc-lint.py`、`scripts/pipeline/patch_p1_plugin_metas.py`、`scripts/README.md`。
- 文档：`Docs/工具链/工具索引.md`（生成）、`Docs/工具链/技术支持.md`、`Docs/开发导航/代码地图.md`、`Docs/项目说明/05-验证与门禁.md`、`Docs/设计文档/架构/脚本形态统一专项设计.md`、`Docs/设计文档/README.md`、`Docs/施工进度/资源与工具链线.md`（第12章）。
- 原 `scripts/toolchain_manifest.py` / `toolchain_docs.py` / `toolchain_surfaces.py` / `local-cdn.config.ps1` 已改名，git 侧会显示为新增 + 删除。

## 后续建议

- 下一运行做 S-B 全量：存量 gate/build/content/local 入口的帮助与退出码归一；先做 `test.ps1` / `l0` / `restore-check` / `build-player` 的代表性试点。
- L2-D1 完成后接 S-B 的 `args` schema 与 `run --dry-run`，不要在脚本里另写一套参数表。
- S-C2 收口 `_common.py` / `_common.ps1`，把根推导与输出/退出契约从各脚本抽出；搬动脚本时按 directory-move-sync 核对注册面。
- 若启动语言迁移，只做低风险内容/扫描域试点，验收要求退出码、关键 stdout 与产物等价。
- 提交前先与并行线拆分 hunk；本次未提交，避免把 030aec97 之后的并行改动混入同一提交。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T18:48:37+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T18:50:23+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T18:51:00+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T18:52:01+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T18:53:44+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T19:01:26+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T19:02:12+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T19:03:54+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T19:04:31+08:00 · 完成「回写与收尾」→ 完成
