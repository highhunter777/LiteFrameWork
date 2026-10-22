# 方案：UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

> 运行 ui-p1-validation · 创建于 2026-10-23T00:02:25+08:00

## 背景与目标

- 设计依据：《UI 编辑器工具链专项设计》§6（校验核心：`UiIssue {ruleId,severity,asset,node,message,fixHint}`、九族规则表、§6.3 例外登记 `UiRuleExceptions`（Owner/规则/资产/节点/原因/到期，到期自动回到 Warning/Error、不得"先合再补"））；§8.3 生成物落点；《UI 制作规范》§20「规则 ID 用于校验报告；例外登记在 `UiRuleExceptions`，不得静默跳过」与 §255「例外必须包含：规则 ID、资产、节点（可选）、Owner、原因、到期版本/日期」。
- 现状：`Assets/LiteGame/Editor/UiTools/Validation/` 已有校验核心（`UiValidator`/`IUiRule`/`UiReport`/`UiIssue`/`UiValidationContext`），已接入 4 条规则（`ui.runtime-visual`、`ui.binding-naming`、`ui.binding-hash`、`ui.asset-table-paths`）；**没有例外登记机制**，六族名单与设计 §6.2 的九族不一致，P1-2/P1-3 遗留的三条规则（缺 `WidgetSpec`=Error、控件清单漂移、页面清单漂移/手改）没有落点。
- 目标（可观察）：同一核心可跑"结构/契约"等新规则；违规可通过 `UiRuleExceptions.json` 登记例外（必填 Owner/原因/到期，过期失效且可见），例外不得静默通过；报告稳定排序、可 CI 非零退出；正负例测试齐；口径写入设计 §6.3。

## 需求澄清与假设

- 需要确认的问题：无（沿用分批裁决：机制 + 真实资产基线一起交付）。
- 已确认答复（2026-10-23，用户）：接着做 P1-4。
- 采用假设：
  - **例外＝降级而非隐藏**：命中且未过期的例外把 `Error→Warning`、`Warning→Info`，并在 `message` 末尾附「（例外：owner，到期 expiry）」——问题仍出现在报告里（可审计），但不再阻断；与设计「到期自动回到 Warning/Error」一致。
  - **到期只支持日期**（`yyyy-MM-dd`，本地时区当天过期）；版本到期（`v1.3`）记入非目标，设计 §6.3 注明。
  - **例外文件为手工维护的配置**（`Assets/LiteGame/Editor/UiTools/UiRuleExceptions.json`），不是生成物；非法条目（缺 Owner/原因/到期）本身报 Error（`ui.rule-exception.invalid`），过期条目报 Warning（`ui.rule-exception.expired`）。
  - **规则族名单对齐设计 §6.2 九族**：结构 / 命名绑定 / 契约 / 样式 / 文本 / 布局适配 / 资源 / 性能 / 行为；未接入族继续在报告「待接入」栏显式列出。
  - 本批只落地**能对真实资产跑通**的规则：结构（页面根契约）、契约（缺 `WidgetSpec`、控件清单漂移、页面清单漂移）；样式/文本/布局/性能/行为仍标未接入（有设计依据，不虚报）。
  - 例外默认路径可被 `UiValidationContext` 覆盖（测试隔离），与既有 `BindManifestPath` 同模式。

## 范围与非目标

- 范围内：
  - 例外登记：`UiRuleExceptions` 模型/加载/校验/匹配（ruleId 精确、asset/node 支持 `*` 通配）、过期判定、降级应用、非法/过期自身报错；`UiRuleExceptions.schema.json`。
  - 核心扩展：`UiValidationContext` 增加 `RuleExceptionsPath`/`WidgetManifestPath`/`PageManifestPath`；`UiValidator.Families` 对齐九族；`Run` 应用例外并稳定排序。
  - 规则：
    - `ui.page-structure`（结构）：`Screens/Page_*`/`Screens/Hud_*` 根必须有 Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup，子树必须有 `SafeAreaReceiver`；prefab 不可读 → Error。
    - `ui.widget-spec`（契约）：`Widgets/Tmp_*` 根必须挂 `WidgetSpec`（P1-2 遗留的 Error 化）。
    - `ui.widget-manifest`（契约）：重新生成控件清单与磁盘清单比较，缺失→Warning、`sourceHash` 漂移→Error。
    - `ui.page-manifest`（契约）：重新对账页面清单与磁盘清单比较，缺失→Warning、`sourceHash` 漂移→Error、对账 warning 逐条转 Warning。
  - 测试：`UiRuleExceptionsEditModeTests`（例外匹配/过期/非法/降级不影响其他问题）+ `UiValidationRulesEditModeTests`（四条规则正负例、真实资产基线、报告确定性）。
  - 文档：§6.3 例外格式与匹配/到期/降级口径、§8.3 落点行、§6.2 族名对齐说明；README schema 登记；进度卷第 4 章；审计（如产生真实债则登记）。
- 非目标：样式/文本/布局/性能/行为族规则（需要令牌表/语言表/布局基线，另批）、增量校验与工作台面板（P1-5）、`UiRuleExceptions` 的版本到期、自动修复。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteGame/Editor/UiTools/Validation/{UiValidator,IUiRule,UiRuleReport}.cs`（核心、上下文、报告）。
  - `Assets/LiteGame/Editor/UiTools/Validation/Rules/*.cs`（4 条既有规则，RuleId 形如 `ui.binding-naming`）。
  - `Assets/LiteGame/Editor/UiTools/Widgets/{UiWidgetManifestGenerator,UiWidgetManifestJson}.cs`（P1-2）。
  - `Assets/LiteGame/Editor/UiTools/Page/{UiPageManifestSync,UiPageManifestJson}.cs`（P1-3）。
  - `Assets/LiteClient/UI/Widgets/SafeAreaReceiver.cs`、`Assets/LiteClient/UI/Spec/WidgetSpec.cs`。
  - `Assets/LiteGame/Editor/UiTools/UiDeterministicJson.cs`（序列化口径）。
- 约束：报告项字段不扩（保持 §6.1 契约）；例外不得改变问题集合（只降级+标注）；规则阈值/路径进常量；不按资产名特判；空/缺清单不伪造通过；`ui.validate` CLI 有 Error 非零退出。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | 例外在核心 `Run` 末尾统一降级；规则只产出原始级别 | 单一入口、所有规则自动受益；易测 | 例外加载失败要显式报错 | 推荐 |
| B | 每条规则自己查例外 | 规则自治 | 重复实现、容易漏；规则作者必须记住 | 不采用 |
| C | 例外文件生成期过滤（不出问题） | 报告干净 | 违反「不得静默跳过」；审计不可见 | 不采用 |

推荐方案与理由：A。设计明确例外是"问题可见但不阻断"，核心统一降级最不容易漏。

## 设计要点

- 例外文件（JSON）：
  ```json
  { "schemaVersion": "1.0",
    "exceptions": [
      { "ruleId": "ui.page-structure", "asset": "Assets/ShareResource/UI/Screens/Page_Legacy.prefab", "node": "", "owner": "art.ui", "reason": "旧页面待迁移", "expiry": "2026-12-31" } ] }
  ```
  必填：`ruleId`/`asset`/`owner`/`reason`/`expiry`；`node` 可空；`asset`/`node` 支持 `*`（仅尾部通配，如 `Widgets/Tmp_*.prefab`）。
- 匹配：`ruleId` 精确（区分大小写）→ `asset` 精确或通配 → `node` 空＝任意、否则精确/通配。命中第一条即用。
- 到期：`expiry` 必须 `yyyy-MM-dd`；`DateTime.Today > expiry` 即过期（当天仍有效）。
- 应用：`Error→Warning`、`Warning→Info`、`Info→Info`；`message` 追加「（例外：owner，到期 expiry）」。
- 自身问题：缺字段/日期非法 → `ui.rule-exception.invalid` Error；过期 → `ui.rule-exception.expired` Warning（可见、不阻断，但要求清理）。
- 报告：问题按 `(ruleId, asset, node, message)` 稳定排序后输出；`Render()` 末尾附例外统计（命中/过期/非法）。
- 规则细节：
  - `ui.page-structure`：只扫 `Screens/Page_*`、`Screens/Hud_*`（模板/基线不要求 Canvas）；根缺组件列表一次报出；`SafeAreaReceiver` 缺失单独报。
  - `ui.widget-spec`：`Widgets/Tmp_*` 根 `WidgetSpec` 缺失 → Error（修：挂 WidgetSpec 并跑清单）。
  - `ui.widget-manifest`：`Generate()` 与磁盘清单（`WidgetManifestPath`）比较 `sourceHash`；缺失→Warning「清单未生成」。
  - `ui.page-manifest`：`Sync(null, 磁盘清单)` 与磁盘比较；对账返回的 `Errors`→Error、`Warnings`→Warning；缺失→Warning。
- 上下文扩展：`UiValidationContext(..., ruleExceptionsPath, widgetManifestPath, pageManifestPath)`，默认走各自 `DefaultPath`。

## 通用性与扩展性

- 根因/共性问题：规则只产出问题、例外是横切关注点；此前无统一入口导致「先合再补」不可审计。
- 复用与扩展点：例外应用在核心（所有规则/未来规则自动受益）；新规则只实现 `IUiRule` 并登记；新增字段走例外 schema 次版本。
- 避免硬编码/特判：规则 ID 常量集中在 `UiRuleIds`；日期格式/通配/降级规则常量化；路径走 `UiValidator`/清单的 `DefaultPath`。
- 扩展方式：新增族＝实现规则 + 在 `Families` 置 `implemented=true` + 设计 §6.2 同步。

## 测试策略

- 层级：Unity EditMode（真实 prefab/清单/AssetDatabase；例外文件用临时路径注入）。
- 例外用例：命中降级（Error→Warning、Warning→Info）、不命中不降级、asset 通配、node 通配、过期不降级且报 `expired`、缺字段报 `invalid`、非法日期报 `invalid`、例外不影响其他问题。
- 规则用例：结构规则（临时页缺 Canvas → Error；完整页 → 通过）、`WidgetSpec` 缺失（临时模板 → Error）、控件清单漂移（临时清单 + 改 spec → Error）、页面清单漂移（临时清单 → Error）、真实资产基线（全量 `Run()` 无 `ui.page-structure`/`ui.widget-spec` Error）、报告稳定排序。
- 回归：既有 `UiValidator` 相关套件 + P1-1/P1-2/P1-3 套件。
- 先红后绿：先落例外/规则骨架（空实现），断言失败；实现至绿。

## 验收标准

- [ ] AC1：`UiRuleExceptions` 模型/加载/校验/匹配/到期/降级落地；非法与过期自身可见；schema 落地。
- [ ] AC2：四条新规则（结构/缺 spec/控件清单/页面清单）正负例齐；族名单对齐 §6.2 九族。
- [ ] AC3：报告稳定排序；`Run()` 真实资产无新增 Error（既有债按例外/告警处理并可见）。
- [ ] AC4：定向 EditMode 全绿；L1、doc-lint、L2 meta 通过；无 prefab/场景改动。
- [ ] AC5：设计 §6.3/§8.3 + README schema + 进度卷第 4 章回写；审计（如有债）登记。

## 风险与回滚

- 风险：真实资产可能在结构规则下暴露历史债 → 先把真实问题核清：确有缺组件的页面就登记例外（带 Owner/到期）或在进度卷明示，不静默放过；例外文件本身成为新的维护面（schema + 校验兜住）。
- 监控：定向用例、`ui.validate` CLI、L1、报告条数。
- 回滚：删除例外文件与新增规则/文档即可；核心改动仅「应用例外 + 排序」，去掉后行为回到现状。
