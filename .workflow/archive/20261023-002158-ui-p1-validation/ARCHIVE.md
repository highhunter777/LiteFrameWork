# 归档：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

- 运行: ui-p1-validation
- 创建时间: 2026-10-23T00:02:25+08:00
- 完成时间: 2026-10-23T00:21:53+08:00
- 归档时间: 2026-10-23T00:21:58+08:00
- 阶段: 8/8
- 任务: 完成 5/5，取消 0
- 验证: PASS（2026-10-23T00:20:54+08:00）

## 交付摘要

- **例外登记**：`UiRuleExceptions`（`Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`，空登记入库）+ `UiRuleExceptions.schema.json`；规则/资产/节点/Owner/原因/到期（`yyyy-MM-dd`）；任意位置 `*` 通配；命中降一级并标注；过期 `ui.rule-exception.expired`（Warning）、非法 `ui.rule-exception.invalid`（Error）。
- **核心扩展**：族名单对齐设计 §6.2 九族（保留项目自定义「渲染/适配」）；上下文新增 3 个可注入路径；`Run` 末尾统一应用例外 + `SortIssues` 稳定排序。
- **四条规则**：`ui.page-structure`（`Page_*` 根契约 + 安全区）、`ui.widget-spec`（控件模板缺 `WidgetSpec`=Error）、`ui.widget-manifest`（缺失 Info / 漂移 Error）、`ui.page-manifest`（缺失 Warning / 漂移 Error / 告警转 Warning）。
- **测试**：新增 15 例（规则 8 + 例外 7），既有 `UiValidationEditModeTests` 14 例随族名/新规则调整后全绿。
- **门禁**：`wf verify` **PASS 18/18**（L1 1819 · doc · l2-meta 14200 · 6 个 UI 夹具 · 其余）。

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
