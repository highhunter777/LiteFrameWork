# 测试计划：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

> 运行 ui-p1-validation · 创建于 2026-10-23T00:02:25+08:00

## 测试策略

- 层级：Unity EditMode（真实 prefab/清单/AssetDatabase；例外文件与清单路径用临时路径注入，隔离全局状态）。
- 先红后绿：先落四条规则骨架（`Check` 返回空）→ 运行取得**断言失败**证据；实现至绿。例外机制随模型一并落地（纯数据 + 匹配），由 7 例单测守住。
- 真实资产基线：`UiValidationRulesEditModeTests.真实资产面_新规则零Error` 要求 `Screens`/`Widgets` 全量在 `ui.page-structure`/`ui.widget-spec`/`ui.widget-manifest`/`ui.page-manifest` 下零 Error，且页面清单与基线同步。
- 回归面：既有 `UiValidationEditModeTests`（核心契约/既有规则）+ P1-1/P1-2/P1-3 套件。
- 门禁：定向 UI 用例 + L1 + doc-lint + L2 meta + `ui.validate` CLI。

## 用例清单

### `UiRuleExceptionsEditModeTests`（7 例）

| 用例 | 覆盖 | 预期 |
|---|---|---|
| `命中例外_降级并标注` | AC1 | 命中项 Error→Warning 且消息带「例外：owner，到期」；不命中项仍 Error |
| `资产与节点通配_命中` | AC1 | 资产中段 `*`、空节点＝任意节点均命中 |
| `过期例外_失效且报expired` | AC1 | 过期不降级；`ui.rule-exception.expired` Warning 可见 |
| `非法条目_报invalid` | AC1 | 缺 owner / 日期非法各报一条 `ui.rule-exception.invalid` Error；原问题不降级 |
| `同一问题只降一级_Info不变` | AC1 | Error→Warning、Warning→Info |
| `文件读取_不存在为空_坏JSON报非法` | AC1 | 缺文件＝无例外；坏 JSON 显式报非法 |
| `日期边界_当天仍有效` | AC1 | 到期当天有效、次日起过期 |

### `UiValidationRulesEditModeTests`（8 例）

| 用例 | 覆盖 | 预期 |
|---|---|---|
| `页面结构_完整根契约通过` | AC2 | Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup + SafeAreaReceiver → 0 问题 |
| `页面结构_缺组件与安全区报Error` | AC2 | 根组件缺失 1 条 + SafeArea 缺失 1 条 Error，带 fixHint |
| `控件规格_缺spec报Error_有spec通过` | AC2 | 缺 `WidgetSpec`＝Error；挂上即 0 |
| `控件清单_未生成报Info` | AC2 | 缺失清单＝Info（提示生成，不阻断） |
| `控件清单_漂移报Error` | AC2 | 一致时 0；改 spec 未重跑 → Error「漂移」 |
| `页面清单_缺失报Warning_漂移报Error` | AC2 | 缺失＝Warning；磁盘清单与表不同步＝Error |
| `真实资产面_新规则零Error` | AC3 | 真实 `Screens`/`Widgets` 全量零 Error；页面清单基线已同步 |
| `报告_稳定排序` | AC3 | 按 `(ruleId, asset, node, message)` 排序 |

## 预期失败证据

- `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-validation verify --red`
- 结果：`UiValidationRulesEditModeTests` **5/8 失败**（规则骨架返回空：期望 Error 实得 0、期望 Info 实得 0 等），`UiRuleExceptionsEditModeTests` 7/7 通过（例外机制已随模型落地），其余 red 命令通过；均为断言失败，非环境/导入错误。
- 证据文件：`.workflow/active/ui-p1-validation/evidence/red.json`。

## 运行方式

```bash
python scripts/gate/unity-tests.py --mode editor --filter UiValidationRulesEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiRuleExceptionsEditModeTests --timeout 600
python scripts/gate/unity-tests.py --mode editor --filter UiValidationEditModeTests --timeout 600
python scripts/gate/test.py --lane L1 --profile PullRequest
```
