# 施工记录：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

> 运行 ui-p1-validation · 创建于 2026-10-23T00:02:25+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-23T00:03:06+08:00 · 新增任务 T1「例外登记：模型/加载/校验/匹配/到期/降级 + schema」
- 2026-10-23T00:03:06+08:00 · 新增任务 T2「核心扩展：九族名单、上下文路径、例外应用与稳定排序」
- 2026-10-23T00:03:13+08:00 · 新增任务 T3「四条规则：页面结构、缺 WidgetSpec、控件清单漂移、页面清单漂移」
- 2026-10-23T00:03:13+08:00 · 新增任务 T4「先行测试与负例覆盖（例外 + 四条规则 + 真实资产基线）」
- 2026-10-23T00:03:13+08:00 · 新增任务 T5「门禁、文档冻结与回写」
- 2026-10-23T00:16:35+08:00 · 开始任务 T1「例外登记：模型/加载/校验/匹配/到期/降级 + schema」
- 2026-10-23T00:16:35+08:00 · 完成任务 T1 · 证据: UiRuleExceptions 模型/加载/校验/匹配（任意位置 * 通配）/日期到期/降级标注/自身 invalid+expired；UiRuleExceptions.json 空登记 + schema；7/7 单测
- 2026-10-23T00:16:35+08:00 · 开始任务 T2「核心扩展：九族名单、上下文路径、例外应用与稳定排序」
- 2026-10-23T00:16:35+08:00 · 完成任务 T2 · 证据: Families 对齐 §6.2 九族（+渲染/适配）；上下文新增 RuleExceptionsPath/WidgetManifestPath/PageManifestPath；Run 应用例外 + SortIssues
- 2026-10-23T00:16:35+08:00 · 开始任务 T3「四条规则：页面结构、缺 WidgetSpec、控件清单漂移、页面清单漂移」
- 2026-10-23T00:16:36+08:00 · 完成任务 T3 · 证据: ui.page-structure（Page_* 根契约+安全区）、ui.widget-spec（Tmp_* 且非 Screens 缺 spec=Error）、ui.widget-manifest（缺失 Info/漂移 Error）、ui.page-manifest（缺失 Warning/漂移 Error/告警转 W…
- 2026-10-23T00:16:36+08:00 · 开始任务 T4「先行测试与负例覆盖（例外 + 四条规则 + 真实资产基线）」
- 2026-10-23T00:16:36+08:00 · 完成任务 T4 · 证据: 先红：rules 5/8 失败；实现后 rules 8/8、exceptions 7/7、既有 UiValidation 14/14
- 2026-10-23T00:17:36+08:00 · 开始任务 T5「门禁、文档冻结与回写」
- 2026-10-23T00:17:36+08:00 · 完成任务 T5 · 证据: L1 PASS 1819；doc-lint OK；L2 meta PASS（14200）；定向 rules 8/8 + exceptions 7/7 + 既有 14/14；设计 §6.3/§8.3 + README schema；审计新增 HUD 根契约债

## 关键决定

- **例外＝降级而非隐藏**：命中的问题仍出现在报告里（`Error→Warning`、`Warning→Info`，报文附「例外：Owner，到期」），不删问题、不静默——对齐设计 §6.3「到期自动回到 Warning/Error」与「不得先合再补」。
- **例外应用放在核心 `Run` 末尾**（方案 A）：所有规则（含未来规则）自动受益，规则作者不需要记住查例外。
- **规则族名单对齐设计 §6.2 九族**（结构/命名绑定/契约/样式/文本/布局适配/资源/性能/行为），并保留项目自定义「渲染/适配」（运行时视觉单一来源，§7）作为第十族；未接入族继续在报告「待接入」栏列出。
- **结构规则只覆盖 `Page_*`**：设计只冻结了页面根契约（《UI 工作台》§3.2）；HUD/UIMain 根契约未定，HUD 的缺口进审计表而不是硬判。
- **控件模板识别＝`Tmp_*` 且不在 `Screens/`**：`Screens/Tmp_*` 是页面基线/系统面，不属于控件库；否则 6 个基线页会被误判「缺 WidgetSpec」。
- **清单缺失不阻断**：控件清单不入库（纯生成物）→ 缺失报 Info；页面清单入库 → 缺失报 Warning；只有「清单存在但指纹漂移」才 Error（表改了没重跑/手改）。
- **例外文件为手工配置**（不是生成物），空登记入库，schema 与设计 §6.3 同一口径。

## 偏差与处理

- 方案原写「`asset`/`node` 支持尾部通配」，实现按**任意位置 `*` 通配**（真实用法 `Tmp_*.prefab` 的 `*` 在中间）；设计 §6.3 与测试已按任意位置描述。
- 既有 `UiValidationEditModeTests` 的「合法名不报错/页面 `_` 前缀 1 条 Error」断言按**总问题数**写死；新规则必然增加问题 → 改为按 `ui.binding-naming` 规则过滤计数（判据不变、更聚焦），未放宽被测算规则。
- 例外测试首版 3 条断言写错（把不命中项也算进"应降级"、把 invalid 自身问题算进原问题计数），实现无误；已修正断言后 7/7。
- 族名「绑定」→「命名/绑定」同步更新两条既有断言（与设计 §6.2 对齐）。

## 问题与修复

- **通配只支持尾部**：首版 `MatchesPattern` 仅处理 `EndsWith("*")`，`Tmp_*.prefab` 中段通配匹配失败（测试红）→ 改为按 `*` 分段顺序匹配（任意位置、可多个）。
- **控件模板误判基线页**：`Tmp_*` 按名字识别把 `Screens/Tmp_BaselineA/B/Dialog/Loading/Toast/ListScreen` 判成控件模板（6 条 Error，真实资产面用例红）→ 识别规则加「不在 `Screens/`」。
- **既有用例总问题数断言**：新规则导致 `ErrorCount` 变多（红）→ 断言收敛到被测规则（见偏差）。
- **`UiWidgetManifestGenerationResult` 类型名**：首版误写 `UiWidgetManifestGeneratorResult`（编译错）→ 按实际类型修正。

## 验证结果

- 先行测试：`ui-validation-rules-red` **5/8 失败**（规则骨架返回空，断言失败）；例外 7/7 通过；其余 red 命令通过；证据 `evidence/red.json`。
- 定向夹具：rules **8/8** · exceptions **7/7** · 既有 `UiValidation` **14/14**。
- 门禁预跑：L1 **PASS 1819** · `doc-lint` **OK** · L2 meta **PASS（14200）**。
- 最终 `wf verify` 全量命令记录见下（`evidence/verify.json`）。
- 2026-10-23T00:12:37+08:00 · 先行测试 PASS · 1/9 · 证据 evidence/red.json
- 2026-10-23T00:20:54+08:00 · 验证 PASS · 18/18 · 证据 evidence/verify.json

## 交付摘要

- **例外登记**：`UiRuleExceptions`（`Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`，空登记入库）+ `UiRuleExceptions.schema.json`；规则/资产/节点/Owner/原因/到期（`yyyy-MM-dd`）；任意位置 `*` 通配；命中降一级并标注；过期 `ui.rule-exception.expired`（Warning）、非法 `ui.rule-exception.invalid`（Error）。
- **核心扩展**：族名单对齐设计 §6.2 九族（保留项目自定义「渲染/适配」）；上下文新增 3 个可注入路径；`Run` 末尾统一应用例外 + `SortIssues` 稳定排序。
- **四条规则**：`ui.page-structure`（`Page_*` 根契约 + 安全区）、`ui.widget-spec`（控件模板缺 `WidgetSpec`=Error）、`ui.widget-manifest`（缺失 Info / 漂移 Error）、`ui.page-manifest`（缺失 Warning / 漂移 Error / 告警转 Warning）。
- **测试**：新增 15 例（规则 8 + 例外 7），既有 `UiValidationEditModeTests` 14 例随族名/新规则调整后全绿。
- **门禁**：`wf verify` **PASS 18/18**（L1 1819 · doc · l2-meta 14200 · 6 个 UI 夹具 · 其余）。

## 未决事项

- **P1-5/P1-6 未做**：Hub/CLI/CI 接线（含增量校验、工作台校验透视、例外编辑入口）、试点页挂 `LTextKeyRef`。
- **五族未接入**：样式/文本/布局适配/性能/行为仍标「待接入」——需要令牌表、语言表、布局基线、PlayMode 用例，另批。
- **HUD/UIMain 根契约未冻结**：`ui.page-structure` v1.0 只覆盖 `Page_*`；`Hud_Battle` 缺 `SafeAreaReceiver`/`GraphicRaycaster` 已进审计表（未硬判、未登记例外）。
- **例外只支持日期到期**：`v1.3` 版本到期记入设计 §6.3「v1.0 不含」。
- **清单规则的环境依赖**：页面清单规则读 `tbuiform`（Python/openpyxl），表读失败显式 Error；控件清单不入库，缺失只报 Info。
- **例外评审流程未做**：当前靠 code review 看 `UiRuleExceptions.json` diff。
- **工作区未提交**：代码/例外文件/schema/文档/配置未自行 commit。

## 文件清单

- 校验核心（改）：`Assets/LiteGame/Editor/UiTools/Validation/{IUiRule,UiValidator,UiRuleReport}.cs`
- 例外（新）：`Assets/LiteGame/Editor/UiTools/Validation/{UiRuleExceptions,UiRuleIds}.cs`；数据 `Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`
- 规则（新）：`Assets/LiteGame/Editor/UiTools/Validation/Rules/{PageStructureRule,WidgetSpecRule,WidgetManifestRule,PageManifestRule}.cs`
- 既有规则（改）：`Validation/Rules/{BindingNamingRule,UiBindingHashRule}.cs`（族名 `命名/绑定`）
- 测试：`Assets/Tests/EditMode/{UiRuleExceptionsEditModeTests,UiValidationRulesEditModeTests}.cs`（新）；`UiValidationEditModeTests.cs`（族名/按规则计数）
- 文档：`Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md`（§6.3、§8.3 锚与说明）、`UiRuleExceptions.schema.json`（新）、`UI系统/README.md`、`Docs/施工进度/UI系统线.md`（第 4 章）、`Docs/施工进度/README.md`、`Docs/审计/代码质量审计-客户端.md`
- 工装/配置：`.workflow/config.json`（新增 3 条命令）、`.workflow/active/ui-p1-validation/**`

## 后续建议

- **P1-5 接线**：`ui.validate` 已有菜单/CLI 入口，补工作台「校验透视」（按族/严重度过滤、定位节点）与增量模式（改动 prefab + 直接依赖 <10s）；例外编辑走面板（写 JSON 走同一 schema 校验）。
- **样式族**：`UiStyleTokens.schema.json` 已在 P1-0 冻结，先落地令牌目录（`UiThemeAsset` 读取面）再做「裸颜色/裸字号/字号阶梯」规则。
- **文本族**：接 `UiLTextTable`（P1-1）做 key 缺失/占位符/语言列规则。
- **行为族**：PlayMode 用例（复用视觉残留/输入穿透/焦点/列表重绑），不进 EditMode 静态扫描。
- **HUD 根契约**：先在《UI 工作台》/总设计冻结 HUD 根契约（是否要 GraphicRaycaster、SafeAreaReceiver 是否强制），再决定补组件或登记例外；在此之前 `Hud_Battle` 的债务留在审计表。
- **例外纪律**：每条例外必须带 Owner 与到期日，review 只看 `UiRuleExceptions.json` diff；过期即回到原级别，不允许「先合再补」。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-23T00:02:25+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-23T00:03:06+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-23T00:04:28+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-23T00:04:28+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-23T00:12:49+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-23T00:17:54+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-23T00:20:59+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-23T00:21:00+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-23T00:21:53+08:00 · 完成「回写与收尾」→ 完成
