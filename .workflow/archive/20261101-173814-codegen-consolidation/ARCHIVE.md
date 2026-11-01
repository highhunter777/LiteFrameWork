# 归档：生成器收口（toolbox 单入口 + 腐烂路径修复 + 门禁）

- 运行: codegen-consolidation
- 创建时间: 2026-10-30T19:40:30+08:00
- 完成时间: 2026-11-01T17:38:08+08:00
- 归档时间: 2026-11-01T17:38:14+08:00
- 阶段: 10/10
- 任务: 完成 10/10，取消 0
- 验证: PASS（2026-11-01T17:37:24+08:00）

## 交付摘要

- 7 个生成器统一 `toolbox.py` 单入口：`all`（严格序）、`all --check`、`doctor`（path_report + tracked/untracked + 调用点清单）、`trig`（Unity eval → ToolWriteScope）；门禁/配置/编排/提醒/单测直调改道，人工直调打印 `[deprecated]`，`Luban/gen.bat` 兼容别名。
- 修 6 处路径腐烂 + 2 处注释腐烂；新增 `_codegen.normalize_eol/input_digest/signature/load_module`、`shared_out_file(root)`、各工具 `path_report()`；hook 覆盖 SHARED_TARGETS 全 10 目录。
- `luban`/`proto`/`sync-code` 补 `--check`（luban staging+CRLF+prune；proto 双模式 + sidecar；sync-code 行尾归一）；10 个 toolbox 文本产物统一 3 行头/指纹；`Battle.cs.input` sidecar（含 `.meta`）。
- `.workflow/config.json` 增 `codegen-check`（`toolbox all --check`）；`scripts/gate/toolchain.json`、`build-player`、`sync-and-sim-e2e`、`refresh-hash` 改走 toolbox；工具索引再生；`codegen-call-sites.json` 55 条分类守卫。
- 文档/文案面：当前命令面（说明/规范/设计/审计 + AGENTS/skills/Assets `.cs`）直调改 toolbox；设计文档 v1.2；进度卷第 35 章；历史进度/归档不改写。
- 最终证据：`wf verify` PASS 65/65；e2e summary ok=true；BuildHash 常量未变（shared=`be285f5c37e147d9`、meta=`75e0e85fe3c33cf2`）；`Battle.cs`/`SimTrigTables.cs` diff 0。

## 产物

- acceptance.md
- build-log.md
- evidence/red.json
- evidence/verify.json
- plan.md
- reviews/code-review.md
- reviews/plan-review.md
- reviews/task-review.md
- state.json
- tasks.md
- test-plan.md
