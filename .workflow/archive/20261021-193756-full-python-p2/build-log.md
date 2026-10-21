# 施工记录：全量 Python P2（内容/环境）

> 运行 full-python-p2 · 创建于 2026-10-21T19:28:52+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-21T19:29:18+08:00 · 新增任务 T1「迁移本地 CDN 配置加载器」
- 2026-10-21T19:29:18+08:00 · 新增任务 T2「迁移签名候选生成器」
- 2026-10-21T19:29:19+08:00 · 新增任务 T3「迁移 CDN 发布布局器」
- 2026-10-21T19:29:19+08:00 · 新增任务 T4「迁移本地 CDN 起服」
- 2026-10-21T19:29:26+08:00 · 新增任务 T5「迁移环境恢复检查」
- 2026-10-21T19:29:26+08:00 · 新增任务 T6「同步 P2 注册面与现行文档」
- 2026-10-21T19:29:27+08:00 · 新增任务 T7「全量验证与范围审计」
- 2026-10-21T19:31:13+08:00 · 开始任务 T1「迁移本地 CDN 配置加载器」
- 2026-10-21T19:31:22+08:00 · 更新任务 T1（acceptance）
- 2026-10-21T19:31:22+08:00 · 完成任务 T1 · 证据: _local_cdn.py；4/4 配置测试；旧 loader 待 publish/serve 切换后删除
- 2026-10-21T19:31:32+08:00 · 开始任务 T2「迁移签名候选生成器」
- 2026-10-21T19:31:45+08:00 · 完成任务 T2 · 证据: gen-candidate.py 规范 manifest/签名/锚自校验；2/2（含独立公钥验签）；旧 PS 删除
- 2026-10-21T19:31:53+08:00 · 开始任务 T3「迁移 CDN 发布布局器」
- 2026-10-21T19:32:00+08:00 · 完成任务 T3 · 证据: publish-candidate.py 布局/bundle/签名；2/2；旧 PS 删除
- 2026-10-21T19:32:05+08:00 · 开始任务 T4「迁移本地 CDN 起服」
- 2026-10-21T19:32:20+08:00 · 完成任务 T4 · 证据: serve-cdn.py 配置/覆盖/HTTP GET/缺目录；2/2；旧 serve 与旧 loader 删除
- 2026-10-21T19:32:34+08:00 · 开始任务 T5「迁移环境恢复检查」
- 2026-10-21T19:32:44+08:00 · 完成任务 T5 · 证据: restore-check.py 检查项/阻塞/新增 cryptography；2/2（repo PASS、空项目 FAIL）；旧 PS 删除
- 2026-10-21T19:32:52+08:00 · 开始任务 T6「同步 P2 注册面与现行文档」
- 2026-10-21T19:33:41+08:00 · 完成任务 T6 · 证据: 清单四 command/toolchain fallback/build-player/索引/现行文档改 Python；旧五 PS 删除；75/75 测试；check 0 error/6 存量 warning；doc-lint 0/0
- 2026-10-21T19:33:41+08:00 · 开始任务 T7「全量验证与范围审计」
- 2026-10-21T19:34:44+08:00 · 完成任务 T7 · 证据: L1 1801；Python 75/75；restore repo PASS；toolchain check 0 error/6 存量 warning；docs current；Doc 0/0；BuildHash OK；CI YAML/JSON/builder AST OK；无 Assets/Server 改动；未真…

## 关键决定

- **P2 一批五脚本**：`_local_cdn` + gen/publish/serve + restore；P3–P5 未做。
- **签名字节独立手工拼装**：不抽通用序列化器，保留与运行时/旧 PS 的跨实现对照；字段序/转义/空值逐项对齐。
- **RSA**：解析 .NET XML 私钥 → `cryptography` PKCS1v15+SHA256；内置锚表公钥自校验；测试用临时 RSA/anchors 独立验签，不读真实私钥。
- **publish**：用 `shutil` 复现“清空 ReleaseId 目录 + 复制 contents + bundle 镜像”；新增可选 `--key-path/--anchors-cs/--key-id` 仅用于测试/自动化，默认与 PS 一致。
- **serve**：保留 `python -m http.server` 子进程，只换入口；端口/目录可覆盖。
- **restore**：检查项不减少，新增 `cryptography`；.NET 9 继续 WARN；toolchain 非 0 只 Warn（与 PS 一致）。
- **旧 PS 硬切**：五个 `.ps1` 删除，清单/`toolchain.py`/`build-player.ps1`/文档同批切换；不保留 shim。

## 偏差与处理

- T1 的旧 `_local-cdn.ps1` 延后到 T4 删除，避免 publish/serve 迁移途中断链；T4 完成后已删。
- `publish-candidate.py` 增加可选 key/anchors/key-id 参数（PS 无），仅测试/自动化使用；默认行为不变。
- serve 缺目录的失败信息走 stderr；测试改为检查 stdout+stderr（实现契约不变）。
- restore 的 .NET 9 WARN 属既有环境，不作为失败；测试只断言 repo PASS 与空项目 blocking。

## 问题与修复

- **serve 测试失败**：测试只查 stdout，而 `fail()` 按统一契约写 stderr；改断言合并 stdout/stderr，并关闭子进程管道消除 ResourceWarning。
- **restore 版本正则**：初版用 `Match[1]` 依赖索引；改为显式 `group(1)`，可读性更好。
- **canonical manifest 拼装**：初版用占位串替换，易漂移；改为按 ReleaseManifest 顺序直接 append，测试校验字段序。
- **旧引用清理**：短名/命令样式需单独替换；grep 后仅剩历史环境记录中的旧路径，按历史保留。

## 验证结果

- 先行失败：`evidence/red.json`，14 个断言失败（配置/签名/发布/服务/restore 缺失 + 旧命令），无环境错误。
- 最终验证：`evidence/verify.json` 7/7 PASS（L1 1801、Doc 0/0、restore repo PASS、L0 测试、Python 75/75、toolchain check 0 error/6 存量 warning、docs current）。
- 附加：生成器/发布测试用临时 RSA + anchors 独立验签；serve 实际 HTTP GET；restore 空项目 blocking；CI YAML、配置 JSON、`build-player.ps1` AST 解析 OK；BuildHash `--check-shared` OK（459a3145904693b7）。
- 范围：改动仅 scripts/Docs/AGENTS/CI/构建器命令；未改 Assets/Server/Luban/Tests/ProjectSettings；未真实发布/CDN/云侧。
- 2026-10-21T19:30:52+08:00 · 先行测试 PASS · 1/1 · 证据 evidence/red.json
- 2026-10-21T19:34:35+08:00 · 验证 PASS · 7/7 · 证据 evidence/verify.json
- 2026-10-21T19:36:10+08:00 · 验证 PASS · 7/7 · 证据 evidence/verify.json

## 交付摘要

- 新增 `_local_cdn.py`（4/4）与 Python 版 `gen-candidate.py`（2/2，规范字节+签名+锚自校验）、`publish-candidate.py`（2/2，布局/bundle/签名）、`serve-cdn.py`（2/2，HTTP GET/缺目录）、`restore-check.py`（2/2，repo PASS/空项目 blocking）。
- 五个旧 `.ps1` 删除；清单四 command、`toolchain.py` fallback、`build-player.ps1`、工具索引、AGENTS/文档/skills 同批改 Python。
- `restore-check` 新增 cryptography 依赖；文档依赖面同步（openpyxl、cryptography）。
- 测试临时 RSA/anchors/目录/端口，无真实私钥、无真实 CDN/云侧。

## 未决事项

- P3–P5 未做：test/nightly/l2/install-task、build/smoke、deploy/local-chain 仍为 PS。
- `check` 仍有 6 条未触碰旧 PS 的编码/EOL 告警（build-player/player-smoke/gen-build-hash/_ssh-host/nightly/test），按批次清理。
- 真实签名机私钥、真实 CDN 发布、Windows CI 首次 Python restore 未在本次执行；只用临时夹具验证。
- 未提交：本轮改动在工作区；`.workflow/` 本地运行记录不入库。

## 文件清单

- 新增：`scripts/content/_local_cdn.py`、`scripts/content/gen-candidate.py`、`scripts/content/publish-candidate.py`、`scripts/content/serve-cdn.py`、`scripts/gate/restore-check.py`、`scripts/tests/_rsa_fixture.py`、`scripts/tests/test_local_cdn.py`、`test_gen_candidate.py`、`test_publish_candidate.py`、`test_serve_cdn.py`、`test_restore_check.py`。
- 删除：对应五个 `.ps1`。
- 修改：`scripts/gate/toolchain.json`、`scripts/toolchain.py`、`scripts/build/build-player.ps1`、`scripts/tests/test_toolchain.py`、`scripts/README.md`、`AGENTS.md`、`Docs/工具链/工具索引.md`、`Docs/工具链/技术支持.md`、项目说明 05/06/13、修改指南、代码地图、环境恢复指南/记录、待办、内容/UI/架构相关设计引用、`.workflow/config.json`（新增 restore 验证命令）。
- 方案文档：`Docs/设计文档/架构/脚本形态统一专项设计.md`（P2 状态）、`Docs/施工进度/资源与工具链线.md`（本章）。

## 后续建议

- 下一运行 **P3 门禁/CI**：`test.py`/`nightly-gate.py`/`l2-unity-gate.py`/`install-nightly-task.py`，同批改 CI 与计划任务并回读 action；P4 构建/Unity；P5 云侧/本地联测并清零 `.ps1`。
- 签名/发布迁移已在 P2 完成；P3 起不要再触碰 `gen-candidate`/`publish`/`serve`/`restore` 的口径。
- 提交前按工具链线拆分；本轮未提交，工作区仅剩 `.workflow/` 本地记录（不含 Assets/Server 变更）。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-21T19:28:52+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-21T19:29:13+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-21T19:29:32+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-21T19:29:51+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-21T19:31:10+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-21T19:35:18+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-21T19:36:10+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-21T19:37:50+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-21T19:37:55+08:00 · 完成「回写与收尾」→ 完成
