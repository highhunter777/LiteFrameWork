# 归档：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

- 运行: ui-p1-page-manifest
- 创建时间: 2026-10-22T23:28:19+08:00
- 完成时间: 2026-10-22T23:59:23+08:00
- 归档时间: 2026-10-22T23:59:30+08:00
- 阶段: 8/8
- 任务: 完成 6/8，取消 2
- 验证: PASS（2026-10-22T23:58:26+08:00）

## 交付摘要

- **页面清单模型**：`UiPageManifest` / `UiPageManifestEntry` / `UiPageFormEntry`（页面唯一 + `forms[]` 多行登记）；五态词表 `Draft/InReview/Registered/Wired/Frozen` + `UiPageStatusRules` 迁移表单源。
- **存储与指纹**：`UiPageManifestStore`（`UiDeterministicJson`、UTF-8 无 BOM、LF）+ `UiPageManifestHash`（身份+维护字段，不含告警/版本/时间戳）+ `UiPageManifest.schema.json`。
- **对账**：`UiPageManifestSync` 刷新身份字段、保留维护字段；新增/孤儿/重复 formId/非法状态/多行 lua/prefab 缺失全部显式报告；表读失败经新增 `TryListPages` 硬失败、不写盘；输出限定 `Assets/`。
- **编辑 API**：`EnsureDraft`（起页草稿、幂等）、`SetStatus`（迁移校验、同值无操作）、`SetEditorial`（Owner/设计稿/设备档/备注，编辑后重算指纹）。
- **基线入库**：12 页 / 13 表单 / 0 告警 / `sourceHash=sha256:bfbc67c7…`（`UiPageManifest.json`，6,235 B；`Tmp_Dialog` 201/206 合并为一条两行）。
- **测试**：`UiPageManifestEditModeTests` 15 例，先红后绿；回归 7 套件全绿。
- **门禁**：`wf verify` **PASS 16/16**（L1 1819 · doc · l2-meta · 4 个 UI 夹具 · 其余）。

## 产物

- build-log.md
- evidence/red.json
- evidence/verify.json
- plan.md
- reviews/code-review.md
- reviews/task-review.md
- state.json
- tasks.md
- test-plan.md
