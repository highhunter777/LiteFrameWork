# 方案：UI 系统 P1-2：控件规格（WidgetSpec）与控件清单（UiWidgetManifest）

> 运行 ui-p1-widget-manifest · 创建于 2026-10-22T22:40:50+08:00

## 背景与目标

- 设计依据：《UI 编辑器工具链专项设计》§8.3（生成物表：`UiWidgetManifest` → `Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json`，消费者＝控件库/文档/校验）、§9.2 T1（`WidgetSpec`、`UiWidgetManifest` 与 Page Contract 同批）；《UI 系统总设计》§1.4（Widget Spec＝控件模板的能力声明：插槽、属性、状态、事件、约束、Owner）、§6.5（状态词表、插槽容器约定）、§10.3（控件清单由 `Tmp_*` + WidgetSpec 汇总）；《UI 制作规范》§3.2（控件模板根 MUST 声明 WidgetSpec：插槽、可配置属性、状态、事件、尺寸行为）。
- 现状：控件库＝「目录即清单」（`UiWidgetLibrary` 扫 `Widgets/Tmp_*`）；受控属性白名单已有（`UiWidgetPropertyAttribute` + `UiWidgetPropertyCatalog`）；模板绑定 hash 已有（`UiBindGenerationHash`）；遍历单源已有（`UiBindSurfaceWalker`）。**但没有任何 `WidgetSpec` 组件、没有清单生成器，30 个模板均未声明能力。**
- 要解决的问题：模板能力只存在于文档表格与人脑里，工具/校验/文档各自读不到同一份声明；控件库没有机器可读清单。
- 目标（可观察）：`Tmp_*` 模板根可声明 WidgetSpec（插槽/状态/事件/尺寸行为/约束/Owner），编辑器可编辑且可从节点发现插槽；一条命令从真实模板目录生成确定性 `UiWidgetManifest.json`（含属性白名单与 bindingHash、`sourceHash`、无 spec 告警）；30 个模板按词表能力表补全并落盘；正负例测试齐；口径写入设计文档。

## 需求澄清与假设

- 需要确认的问题：无（用户已裁决 P1-2 范围）。
- 已确认答复（2026-10-22，用户）：**选 C**——除机制与清单外，按《UI 命名与控件词表参考》§3 的能力表把现有 30 个 `Tmp_*` 模板的 WidgetSpec 补全并落盘（允许 prefab 资产 diff）。
- 采用假设：
  - **内容口径**：`Summary` 取词表 §3「用途」；`States` 取设计 §6.5 状态词表内、按控件语义可判定者；`Events` 只声明**根组件上真实存在的公开事件或 UnityEvent 成员**（写成 `类型.成员`，机制可校验）；`SizeBehavior` 三档（Fixed/Expand/FitContent）按控件语义判定；`Constraints` 只写代码/文档已有事实（如 Toast 的 `MaxVisible`、VirtualList 的固定尺寸单轴）；`Owner` 统一 `art.ui`（RACI：模板视觉归 UI 美术）。
  - **落盘方式**：30 个模板的组件与字段由一次性脚本（`AgentScripts/`，经 unity-pipeline `run_script` 执行）幂等写入；之后**真相在 prefab**，脚本与数据表是迁移输入、不参与运行时。
  - **缺 spec 的严重度**：生成器记 Warning（清单 `specPresent=false`），Error 归 P1-4 校验规则；本批补全后真实目录应为 0 告警。
  - 假设影响与风险：prefab 只加组件/字段、不改节点结构与引用（视觉零变化）；事件名与代码成员一致性由生成器机制校验，不靠人工。

## 范围与非目标

- 范围内：
  - 运行时组件：`WidgetSpec`（+ `WidgetSlotSpec`/`UiWidgetState`/`UiWidgetSizeBehavior`）。
  - 编辑器作者面：`WidgetSpecAuthoring`（从节点发现插槽，复用 `UiBindSurfaceWalker`）+ `WidgetSpecInspector`（数组编辑、Undo、发现按钮）。
  - 清单：`UiWidgetManifest` 模型、`UiWidgetManifestGenerator`、`UiWidgetManifestJson/Store`、`UiWidgetManifest.schema.json`、确定性 JSON 与 `sourceHash`。
  - 内容：30 个 `Tmp_*` 模板的 WidgetSpec 补全（一次性脚本 + 数据表，经 unity-pipeline 落盘）。
  - 测试：`WidgetSpecEditModeTests`、`UiWidgetManifestEditModeTests`（正例/负例/确定性/真实目录基线）。
  - 文档：工具链专项设计 §8.4（WidgetSpec 字段模型与生成口径 v1.0 冻结）、UI系统 README 的 schema 登记、进度卷 `UI系统线.md` 第 2 章、审计表挂账（如仍有缺 spec 的模板）。
- 非目标：`UiPageManifest`（P1-3）、校验规则族与 `UiRuleExceptions`（P1-4，含「缺 spec=Error」）、Hub/CLI/CI 接线（P1-5）、清单 diff、样式令牌/图集检查（T3）、页面契约改动。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteGame/Editor/UiTools/Compose/UiWidgetLibrary.cs`（目录即清单，本批复用其模板枚举与使用段）。
  - `Assets/LiteGame/Editor/UiTools/Compose/UiWidgetPropertyCatalog.cs`（受控属性白名单，本批复用为清单 `properties[]`）。
  - `Assets/LiteGame/Editor/UiTools/Binding/UiBindSurfaceWalker.cs`（遍历单源，本批复用于插槽发现与过滤）。
  - `Assets/LiteGame/Editor/UiTools/Binding/UiBindGenerationHash.cs`（模板 bindingHash）。
  - `Assets/LiteGame/Editor/UiTools/Contract/UiContractJson.cs`（确定性 JSON 口径，本批抽共用）。
  - `Assets/LiteClient/UI/Style/UiTextStyleRef.cs`、`Localization/LTextKeyRef.cs`（运行时声明组件先例）。
- 约束（兼容、性能、数据、依赖）：
  - **红线 4**：prefab 改动只走 unity-pipeline（`run_script`/专用命令），禁直改 `.prefab`/`.meta`。
  - 运行时组件不得引用 UnityEditor；生成物禁手改；无时间戳、稳定排序、LF、UTF-8 无 BOM。
  - 不按模板名写特判：清单内容来自组件声明、属性来自 attribute、事件来自代码成员、模板清单来自目录。
  - 禁新增 public static 可变状态；`WidgetSpec` 只做声明、不驱动运行时。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | WidgetSpec 作为 `Tmp_*` 根组件；清单＝组件 + 属性白名单 + bindingHash 生成 | 单一真相在模板；可枚举/可校验；与 `BindRoot`/`UiTextStyleRef` 同型；设计「MUST 有 WidgetSpec」可机制化 | 需改 30 个 prefab（只加组件） | 推荐 |
| B | WidgetSpec 存独立 JSON（按模板名） | 不动 prefab | 第二真相：prefab 改名即孤儿；无法在 Inspector 就地声明；与设计措辞冲突 | 不采用 |
| C | 清单手写 | 无代码 | 与「生成物禁手改」冲突，无法校验/对账 | 不采用 |

推荐方案与理由：A。模板是能力声明的自然归属；清单只是投影，避免两份真相；插槽发现与校验都复用既有遍历/属性白名单，不新增第二套语义。

## 设计要点

- 接口/数据：
  - `WidgetSpec`（运行时声明组件，`LiteClient.UI`）：`Owner`、`Summary`、`Slots`（`WidgetSlotSpec[]`：`Name`/`Required`/`Description`）、`States`（`UiWidgetState[]`）、`Events`（`string[]`，形如 `StateButton.OnClickEvent`）、`SizeBehavior`（`UiWidgetSizeBehavior`）、`Constraints`（`string[]`，自由文本事实）。
  - `UiWidgetState`：Normal/Hover/Pressed/Disabled/Selected/Loading/Empty/Error（设计 §6.5 词表单源）。
  - `UiWidgetSizeBehavior`：Fixed（模板固定尺寸）/Expand（沿父容器拉伸）/FitContent（随内容自适应）。
  - `UiWidgetManifest`：`schemaVersion`、`generator{name,version}`、`widgets[]`（`template`/`prefabPath`/`prefabGuid`/`usageSegment`/`specPresent`/`owner`/`summary`/`slots[]`/`states[]`/`events[]`/`sizeBehavior`/`constraints[]`/`properties[]`/`bindingHash`）、`warnings[]`、`sourceHash`。
- 关键流程：`UiWidgetLibrary.Load()` 枚举模板 → 每模板读根 `WidgetSpec` + `UiWidgetPropertyCatalog.Collect` + `UiBindGenerationHash.ComputeHash` → 校验 → 稳定排序 → `sourceHash` → JSON（LF/无 BOM/无时间戳）→ 写盘 `Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json`。
- 确定性口径：widgets 按 `template` 序；`properties` 按 `nodePath/component/member`；`states` 按枚举值序；`events`/`constraints` 按 Ordinal 排序；`slots` 保持声明序（header/body/footer 语义序）。
- `sourceHash` 域：`template|owner|summary|slot(name/required/desc)|state|event|constraint|size|property(nodePath/component/member/kind)|bindingHash`；**不含** `prefabPath`/`prefabGuid`/生成器版本/告警。
- 失败与边界处理：
  - 缺 `WidgetSpec` → Warning + `specPresent=false`（不阻断生成）。
  - 插槽名不以 `Cot_`/`Slot_` 起头、插槽节点不存在、插槽重名 → Error。
  - 事件为空/重名/`类型.成员` 解析不到（组件不存在或无该公开事件/字段）→ Error。
  - 状态重复、约束空串/重名 → Error。
  - `prefab` 根名与文件名不一致 → Error。
  - 负例只返回错误、不写盘；输出路径必须在 `Assets/` 下。
- 编辑器面：`WidgetSpecInspector` 用 SerializedProperty 编辑数组并支持「从节点发现插槽」（`WidgetSpecAuthoring.DiscoverSlots`，Undo 可回滚）；发现规则＝`Cot_*`/`Slot_*` 容器，排除 `Clone_` 子树、模板内部件、嵌套 `BindRoot` 子树。

## 通用性与扩展性

- 根因/共性问题：模板能力此前只存在于文档表格与人脑里，工具/校验/文档各自读不到同一份声明。本批把能力声明放回模板、清单做投影。
- 复用与扩展点：模板枚举＝`UiWidgetLibrary`（目录即清单）；属性＝`UiWidgetPropertyCatalog`（attribute 单源）；遍历＝`UiBindSurfaceWalker`（hash/契约/插槽发现同源）；hash＝`UiBindGenerationHash`；序列化＝抽出的共用确定性 JSON 口径（与 Page Contract 一致）。
- 避免硬编码/特判的约束：不按模板名分支；状态/尺寸行为是枚举；事件用 `类型.成员` 由反射校验；30 件内容表只作为一次性迁移输入（落盘后真相在 prefab）。
- 扩展方式：新模板挂 `WidgetSpec` 即进清单；新状态/尺寸行为扩枚举并同步设计文档；清单新增字段走 schema 次版本。

## 测试策略

- 测试层级与关键用例：
  - `WidgetSpecEditModeTests`：默认值（空声明）；插槽发现只收 `Cot_*`/`Slot_*` 且排除 `Clone_`/模板内部/嵌套根；发现结果应用幂等（不重复添加、保留已有 description）。
  - `UiWidgetManifestEditModeTests`：清单汇总（spec + 属性 + bindingHash + usageSegment）；缺 spec → Warning + `specPresent=false`；确定性（两次序列化逐字节相等、无 `generatedAt`）；`sourceHash` 对声明敏感、对路径/GUID 不敏感；负例（插槽缺失/重复、事件空/重名/成员解析不到、约束空）→ `Ok=false` 且不写盘；写盘读回一致；真实模板目录 → ≥30 条、全部 `specPresent=true`、0 告警。
  - 回归：`UiWidgetLibraryEditModeTests` 6/6、`UiBindingToolsEditModeTests` 12/12、P1-1 三套件保持绿。
- 如何先失败后通过：先落最小骨架（`WidgetSpec` 组件 + `DiscoverSlots` 返回空 + 生成器返回空清单），运行定向用例取得断言失败证据（`evidence/red.json`），再实现至绿。

## 验收标准

- [ ] AC1：`WidgetSpec`（含状态/尺寸行为/插槽数据）与编辑器作者面（Inspector + 发现插槽）落地；运行时程序集零 UnityEditor 引用。
- [ ] AC2：`UiWidgetManifest` 生成器/存储/schema 落地；确定性 JSON、`sourceHash`、负例不写盘、输出限定 `Assets/`。
- [ ] AC3：30 个 `Tmp_*` 模板全部挂 `WidgetSpec` 且内容按词表能力表补全；真实目录生成清单 `specPresent` 全 true、0 告警；prefab 只加组件/字段。
- [ ] AC4：定向 EditMode 全绿；L1、doc-lint、L2 meta 扫描通过；无场景改动。
- [ ] AC5：设计文档 §8.4 与 README schema 登记；进度卷回写；审计表如实挂账。

## 风险与回滚

- 风险：
  - 30 个 prefab 批量改动：脚本幂等（已有 spec 则覆盖字段、不重复加组件）；不改节点/引用，视觉零变化；失败时按模板逐个删组件即可回滚（`RemoveComponent`）。
  - 事件名与代码成员漂移：生成器对 `类型.成员` 反射校验，漂移即 Error（不会静默）。
  - 真实目录基线断言随内容批变化：断言「全部 specPresent + 0 告警」与「≥30 条」，不钉死具体模板集合。
  - 清单生成性能：30 个模板 + 属性反射在编辑器一次生成，毫秒级；不做缓存。
- 监控/发现：定向 Unity 用例、`doc-lint`、L1、清单 `warnings[]` 计数。
- 回滚：删除组件与生成器/测试/文档改动即可；无表结构、无协议、无 BuildHash 面（不进共享源集）。
