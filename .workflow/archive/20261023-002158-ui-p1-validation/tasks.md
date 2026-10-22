# 实施任务清单：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 5/5（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 例外登记：模型/加载/校验/匹配/到期/降级 + schema | - | 完成 | UiRuleExceptions 文件（ruleId/asset/node/owner/reason/expiry）；缺失或非法日期→ui.rule-exception.invalid Error；过期→ui.rule-exception.expired Warning 且不降级；命中降级（Error→Warning/Warning→Info）并标注；asset/node 支持尾部通配；UiRuleExceptions.schema.json | unity-tests --filter UiRuleExceptionsEditModeTests | UiRuleExceptions 模型/加载/校验/匹配（任意位置 * 通配）/日期到期/降级标注/自身 invalid+expired；UiRuleExceptions.json 空登记 + schema；7/7 单测 |
| T2 | 核心扩展：九族名单、上下文路径、例外应用与稳定排序 | T1 | 完成 | Families 对齐 §6.2 九族；UiValidationContext 增加 RuleExceptionsPath/WidgetManifestPath/PageManifestPath；Run 应用例外并稳定排序；Render 附例外统计 | unity-tests --filter UiValidationRulesEditModeTests | Families 对齐 §6.2 九族（+渲染/适配）；上下文新增 RuleExceptionsPath/WidgetManifestPath/PageManifestPath；Run 应用例外 + SortIssues |
| T3 | 四条规则：页面结构、缺 WidgetSpec、控件清单漂移、页面清单漂移 | T2 | 完成 | ui.page-structure：Page_*/Hud_* 根须有 Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup + 子树 SafeAreaReceiver；ui.widget-spec：Tmp_* 根须挂 WidgetSpec；ui.widget-manifest：重生成与磁盘 sourceHash 不一致=Error、缺=Warning；ui.page-manifest：重对账与磁盘不一致=Error、缺=Warning、对账告警转 Warning | unity-tests --filter UiValidationRulesEditModeTests | ui.page-structure（Page_* 根契约+安全区）、ui.widget-spec（Tmp_* 且非 Screens 缺 spec=Error）、ui.widget-manifest（缺失 Info/漂移 Error）、ui… |
| T4 | 先行测试与负例覆盖（例外 + 四条规则 + 真实资产基线） | T1, T2, T3 | 完成 | UiRuleExceptionsEditModeTests + UiValidationRulesEditModeTests 覆盖命中/不命中/通配/过期/非法/漂移/正例/真实资产基线/报告排序，先红后绿 | wf verify --red + 定向 unity-tests | 先红：rules 5/8 失败；实现后 rules 8/8、exceptions 7/7、既有 UiValidation 14/14 |
| T5 | 门禁、文档冻结与回写 | T3, T4 | 完成 | 定向用例、L1、doc-lint、L2 meta 通过；设计 §6.3/§8.3 口径 + README schema + 进度卷第 4 章；审计（如有债）登记 | L1+doc+l2-meta | L1 PASS 1819；doc-lint OK；L2 meta PASS（14200）；定向 rules 8/8 + exceptions 7/7 + 既有 14/14；设计 §6.3/§8.3 + README schema；审计新增… |

## 任务详情

### T1 · 例外登记：模型/加载/校验/匹配/到期/降级 + schema

- 文件: Assets/LiteGame/Editor/UiTools/Validation/UiRuleExceptions.cs, Docs/设计文档/客户端/UI系统/UiRuleExceptions.schema.json

### T2 · 核心扩展：九族名单、上下文路径、例外应用与稳定排序

- 文件: Assets/LiteGame/Editor/UiTools/Validation/IUiRule.cs, Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs, Assets/LiteGame/Editor/UiTools/Validation/UiRuleReport.cs

### T3 · 四条规则：页面结构、缺 WidgetSpec、控件清单漂移、页面清单漂移

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/PageStructureRule.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/WidgetSpecRule.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/WidgetManifestRule.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/PageManifestRule.cs

### T4 · 先行测试与负例覆盖（例外 + 四条规则 + 真实资产基线）

- 文件: Assets/Tests/EditMode/UiRuleExceptionsEditModeTests.cs, Assets/Tests/EditMode/UiValidationRulesEditModeTests.cs

### T5 · 门禁、文档冻结与回写

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/README.md, Docs/施工进度/UI系统线.md, Docs/审计/代码质量审计-客户端.md, .workflow

