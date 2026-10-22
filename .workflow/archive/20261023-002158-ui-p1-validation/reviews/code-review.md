# 代码评审：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

> 运行 ui-p1-validation · 创建于 2026-10-23T00:02:25+08:00

## 变更范围

- 对照任务与方案：T1（例外）→ AC1；T2（核心扩展）→ AC2/AC3；T3（四条规则）→ AC2；T4（先行测试）→ AC1–AC3；T5（门禁/文档）→ AC4/AC5。
- 变更文件/模块：
  - 校验核心（改）：`Validation/IUiRule.cs`（上下文加 3 个路径）、`Validation/UiValidator.cs`（九族+渲染/适配、注册 4 条新规则、应用例外+排序）、`Validation/UiRuleReport.cs`（`SortIssues`）。
  - 例外（新）：`Validation/UiRuleExceptions.cs`、`Validation/UiRuleIds.cs`、`UiTools/UiRuleExceptions.json`（空登记）。
  - 规则（新）：`Validation/Rules/{PageStructureRule,WidgetSpecRule,WidgetManifestRule,PageManifestRule}.cs`。
  - 既有规则（改）：`BindingNamingRule`/`UiBindingHashRule` 族名 `绑定`→`命名/绑定`。
  - 测试：`Assets/Tests/EditMode/{UiRuleExceptionsEditModeTests,UiValidationRulesEditModeTests}.cs`（新增 15 例）；`UiValidationEditModeTests.cs`（族名与新规则兼容：断言收敛到被测规则）。
  - 文档：§6.3 例外口径、§8.3 例外文件说明、`UiRuleExceptions.schema.json`（新）、`UI系统/README.md`、审计表（HUD 根契约债）、进度卷第 4 章。
  - 工装：`.workflow/config.json`（新增 `ui-validation-rules-editmode`/`ui-rule-exceptions-editmode`/`ui-validation-rules-red`）。

## 正确性与边界

- 主路径：`UiValidator.Run(context)` → 8 条已接入规则产出 `UiIssue` → `UiRuleExceptionSet.Load(path).Apply`（校验/过期/匹配/降级/标注/自身问题）→ `SortIssues` → 报告。
- 边界与错误路径（均有用例）：命中/不命中、任意位置通配、空节点、过期、缺 owner、日期格式错、坏 JSON、当天到期边界、缺清单（Info/Warning）、漂移（Error）、真实资产面零 Error、排序稳定。
- 回归风险：既有 4 条规则行为不变（仅族名）；`UiValidationEditModeTests` 14/14 通过；例外文件缺失＝无例外（不误报）；合成上下文（无资产）不触发清单规则。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：是。15 例覆盖例外语义六面 + 四条规则正负例 + 真实资产基线 + 排序；先红证据（5/8 失败）证明先红后绿。
- 是否存在迎合实现的测试：否。断言基于报告契约与磁盘/表事实；既有用例的断言从「总问题数」收敛为「被测规则问题数」（判据更聚焦，不是放宽——同规则内条数不变）。

## 通用性与扩展性

- 根因与复用边界：例外是横切关注点，放核心一处；规则只实现 `IUiRule`。复用 `UiWidgetManifestGenerator`（P1-2）、`UiPageManifestSync`（P1-3）、`UiRuleIds` 常量、`UiValidator.ProjectRoot`。
- 扩展点与接入方式：新族＝实现规则 + 注册 + `Families` 置 implemented；新例外字段走 schema 次版本；`RuleExceptionsPath` 可注入，工具/测试隔离。
- 硬编码、特判、魔法值：规则 ID 常量集中；日期格式/降级/通配常量化；页面/模板识别按前缀（含 `Screens/` 排除，注释说明原因），不按具体资产名分支。
- 重复逻辑与过度抽象：`FullPath` 在两个清单规则里各 1 行（可接受，避免为一个表达式造工具类）；例外匹配与报告排序均在核心。
- 相关场景复用与回归影响：`ui.validate` CLI/菜单自动带上新规则；例外文件缺失不影响既有调用方。
- 命名、结构与复杂度：新增文件单一职责（常量/例外模型/四条规则），最大文件约 220 行。

## 安全与性能

- 安全：无网络/权限面；例外文件是源码内配置（可 review）；过期自动失效，避免长期豁免；`invalid`/`expired` 自身可见。
- 性能与资源：`Run()` 额外 4 条规则——结构/规格规则各遍历一次 prefab 列表（14192 资产里 UI 面约 40 个），清单规则一次生成/对账；编辑器亚秒级，CI 可接受。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 中 | `UiRuleExceptions.MatchesPattern` | 首版只支持尾部 `*`，`Tmp_*.prefab` 中段通配匹配失败（例外测试红） | 改为按 `*` 分段顺序匹配（任意位置、可多个），文档同步 | 已修复 |
| C-2 | 中 | `WidgetSpecRule.IsWidgetAsset` | `Tmp_*` 按名识别把 `Screens/Tmp_BaselineA/B/Dialog/Loading/Toast/ListScreen` 判成控件模板 → 6 条假 Error（真实资产面用例红） | 识别加「不在 `Screens/`」（Screens 下 Tmp_* 是页面基线/系统面） | 已修复 |
| C-3 | 低 | `WidgetManifestRule` | 生成结果类型名误写 `UiWidgetManifestGeneratorResult`（编译错） | 改为 `UiWidgetManifestGenerationResult` | 已修复 |
| C-4 | 中 | 既有 `UiValidationEditModeTests` | 「合法名不报错」断言总 `ErrorCount==0`，新规则必然增加问题（红） | 断言收敛到 `ui.binding-naming` 规则计数（同规则内条数不变）；族名断言同步 `命名/绑定` | 已修复 |
| C-5 | 信息（设计约束） | 例外文件 | 当前空登记，没有真实例外；HUD 缺口未登记为例外 | HUD 根契约未冻结，进审计表；例外文件留空，等真实豁免时登记 | 已接受 |
| C-6 | 信息（环境） | `PageManifestRule` | 规则读 `tbuiform` 需 Python/openpyxl（与起页/表路径规则同依赖） | 沿用既有环境依赖；表读失败显式 Error 不静默 | 已接受 |
| C-7 | 信息（延后） | 例外到期 | 只支持日期到期，`v1.3` 版本到期未做 | 设计 §6.3 已注明 v1.0 不含 | 已登记 |

无其他阻塞问题。检查过的风险面：例外语义（可见/降级/过期）、通配边界、清单缺失与漂移、合成上下文隔离、报告确定性、真实资产假阳性。

## 复测记录

- 修复后执行的命令与结果（最终代码状态）：
  - `python .agents/skills/staged-delivery/scripts/workflow.py --run ui-p1-validation verify` → **PASS 18/18**（含新增 2 条验证命令）。
  - 定向夹具：`UiValidationRules` **8/8** · `UiRuleExceptions` **7/7** · 既有 `UiValidation` **14/14**。
  - `python scripts/gate/doc-lint.py` → OK（0 violation）；L2 meta 扫描 14200 个 `.meta` 全合法。
- 验证证据：evidence/verify.json

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: DeepSeek Harness（本批执行代理，独立复核 diff、真实资产面结果与命令输出）
- 评审时间: 2026-10-23T00:25:00+08:00
