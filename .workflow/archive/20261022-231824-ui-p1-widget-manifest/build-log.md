# 施工记录：UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

> 运行 ui-p1-widget-manifest · 创建于 2026-10-22T22:40:50+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-22T22:49:56+08:00 · 新增任务 T1「WidgetSpec 运行时声明组件与词表」
- 2026-10-22T22:49:57+08:00 · 新增任务 T2「编辑器作者面：插槽发现与 Inspector」
- 2026-10-22T22:50:03+08:00 · 新增任务 T3「控件清单模型、生成器、存储与 schema」
- 2026-10-22T22:50:03+08:00 · 新增任务 T4「30 个模板 WidgetSpec 内容落盘」
- 2026-10-22T22:50:08+08:00 · 新增任务 T5「先行测试与负例覆盖」
- 2026-10-22T22:50:08+08:00 · 新增任务 T6「门禁、评审与回写」
- 2026-10-22T23:06:32+08:00 · 开始任务 T1「WidgetSpec 运行时声明组件与词表」
- 2026-10-22T23:06:32+08:00 · 完成任务 T1 · 证据: WidgetSpec + WidgetSlotSpec + UiWidgetState + UiWidgetSizeBehavior 落地（LiteClient.UI，零 UnityEditor 依赖）；WidgetSpecEditModeTests 3/3（默认声明/发现/幂等）
- 2026-10-22T23:06:39+08:00 · 开始任务 T2「编辑器作者面：插槽发现与 Inspector」
- 2026-10-22T23:06:39+08:00 · 完成任务 T2 · 证据: WidgetSpecAuthoring.DiscoverSlots 复用 UiBindSurfaceWalker（Cot_/Slot_，排除 Clone_/模板内部/嵌套根）；ApplyDiscoveredSlots 幂等保作者填写；WidgetSpecInspector（Undo + 发现按钮）；WidgetSpe…
- 2026-10-22T23:06:39+08:00 · 开始任务 T3「控件清单模型、生成器、存储与 schema」
- 2026-10-22T23:06:39+08:00 · 完成任务 T3 · 证据: UiWidgetManifest 模型/生成器/JSON 存储/schema；确定性（无时间戳/稳定排序/LF/无 BOM）、sourceHash、负例不写盘、输出限定 Assets/；缺 spec 告警；UiWidgetManifestEditModeTests 11/11
- 2026-10-22T23:06:45+08:00 · 开始任务 T4「30 个模板 WidgetSpec 内容落盘」
- 2026-10-22T23:06:45+08:00 · 完成任务 T4 · 证据: AgentScripts/BuildWidgetSpecs.cs + widget-specs.json 幂等落盘 30/30；prefab 仅新增 WidgetSpec 组件（+627 行纯新增，无删除/结构变化）；真实目录用例 30/30 spec、0 告警
- 2026-10-22T23:06:45+08:00 · 开始任务 T5「先行测试与负例覆盖」
- 2026-10-22T23:06:45+08:00 · 完成任务 T5 · 证据: 先红：evidence/red.json（widget-spec-red 2/3 失败、widget-manifest-red 9/11 失败，均断言失败）；实现后复跑 3/3 + 11/11 全绿
- 2026-10-22T23:09:25+08:00 · 开始任务 T6「门禁、评审与回写」
- 2026-10-22T23:09:25+08:00 · 完成任务 T6 · 证据: L1 PASS 1819；doc-lint OK 0 violation；L2 meta PASS（14184 meta）；定向夹具 3/3+11/11；设计 §8.4 + README schema 登记；进度卷 UI系统线 第2章；审计表挂账 1 条
- 2026-10-22T22:47:00+08:00 · 探查 30 个模板结构（临时 run_script 导出节点/组件/公开事件面）后裁决 spec 内容；探查脚本用完即删。
- 2026-10-22T23:00:00+08:00 · 骨架 + 14 例测试落盘，先红（2/3、9/11 断言失败）；实现作者面/清单生成器后 3/3、11/11 全绿。
- 2026-10-22T23:05:00+08:00 · 30 个模板 spec 经 unity-pipeline 幂等落盘（applied=30 missing=[] errors=[]）；真实目录清单 30/30 spec、0 告警、`sourceHash=sha256:6b145441…`（19,782 B，冒烟后删除生成物）。

## 关键决定

- **WidgetSpec 作为模板根组件**（方案 A），清单只做投影：单一真相在模板，可枚举/可校验；属性不重复声明（`UiWidgetProperty` 单源）。
- **事件声明用 `类型.成员`** 并由反射校验（公开事件/UnityEvent 字段），不造语义事件名——清单与代码成员漂移会直接报错，而不是靠人工对账。
- **状态/尺寸行为用枚举**（设计 §6.5 词表 + `Fixed/Expand/FitContent`），内容裁决落在数据表而非自由文本。
- **缺 spec 只告警**（`specPresent=false`），Error 归 P1-4 校验规则；本批内容补全后真实目录 0 告警。
- **内容迁移用一次性脚本 + JSON 数据表**（`AgentScripts/`）：幂等、可复核；落盘后真相在 prefab，脚本不参与运行时；prefab 只新增组件（+627/−0）。
- **序列化口径抽 `UiDeterministicJson`**：页面契约与控件清单共用固定字段序/LF/无 BOM/无时间戳，避免第二份实现。

## 偏差与处理

- 方案写「事件只声明根组件真实成员」；实现按「模板内任意节点上同名组件的成员」解析（先按类型名找组件，再查成员），等价且对子节点组件更宽松——Dialog 等无事件成员者 events 为空并在 constraints 记事实。
- 方案 T4 依赖 T3；实际先落骨架/测试（tests-first）再实现，内容落盘在生成器可用后进行，顺序与方案一致。
- `UiWidgetManifestGenerationResult.Warnings` 初版与 `Manifest.Warnings` 双份，红测暴露「告警丢失」；改为结果类只暴露清单告警属性（单一来源）。

## 问题与修复

- **告警双份导致丢失**：结果类 `Warnings` 与 `Manifest.Warnings` 两个列表，生成器写前者、序列化读后者 ⇒ 清单 warnings 恒空（红测「告警需可定位到模板」失败）。修：结果类只暴露 `Manifest.Warnings`，写入口统一到清单。
- **事件声明与代码不符**：`Tmp_RedDot` 初版声明 `RedDot.OnChanged`，但该事件在 `RedDotNode` 上（`RedDot` 本身无公开事件）⇒ 生成器反射校验报错、真实目录用例红。修：数据表改为 `events=[]`（红点无根组件公开事件）；机制校验按设计生效，不静默放过。
- **真实目录用例依赖内容落盘**：`真实模板目录_全部声明且零告警` 在 T4 前必红；作为内容批的验收基线保留，红测阶段该失败属预期。

## 验证结果

- 先行测试：`widget-spec-red` 2/3 失败、`widget-manifest-red` 9/11 失败（断言失败）；证据 `evidence/red.json`。
- 最终验证：`wf verify` **PASS 15/15**（L1 1819 · doc · restore · l0 · toolchain-tests · toolchain-check · toolchain-docs-check · doc-lane · l2-meta · debug-kernel-editmode · ui-static-text · ui-contract · ui-contract-diff · widget-spec · widget-manifest）；证据 `evidence/verify.json`。
- 2026-10-22T22:58:16+08:00 · 先行测试 PASS · 2/7 · 证据 evidence/red.json
- 2026-10-22T23:12:30+08:00 · 验证 PASS · 15/15 · 证据 evidence/verify.json
- 2026-10-22T23:17:21+08:00 · 验证 PASS · 15/15 · 证据 evidence/verify.json

## 交付摘要

- **控件能力声明机制**：`WidgetSpec`（Owner/Summary/Slots/States/Events/SizeBehavior/Constraints）+ `WidgetSlotSpec` + `UiWidgetState`（设计 §6.5 词表）+ `UiWidgetSizeBehavior`（Fixed/Expand/FitContent），只声明不驱动、运行时零依赖。
- **作者面**：`WidgetSpecAuthoring`（插槽发现复用 `UiBindSurfaceWalker`，排除 `Clone_`/模板内部件/嵌套 `BindRoot` 子树；幂等并入保留作者填写）+ `WidgetSpecInspector`（Undo + 一键发现）。
- **控件清单**：`UiWidgetManifest` 模型/生成器/JSON 存储/schema；确定性（无时间戳/稳定排序/LF/无 BOM）、`sourceHash`（不含路径/GUID）、缺 spec 告警、声明不一致 Error 且不写盘、输出限定 `Assets/`。
- **30 件内容落盘**：按《UI 命名与控件词表参考》§3 能力表补全；`AgentScripts/BuildWidgetSpecs.cs` + `widget-specs.json` 幂等执行（applied=30 / missing=[] / errors=[]）；prefab 仅新增组件（+627/−0）。
- **序列化单源**：抽出 `UiDeterministicJson`，页面契约与控件清单共用；契约用例 10/10 保持逐字节一致。
- **测试**：14 例（`WidgetSpec` 3 + `UiWidgetManifest` 11），先红后绿；真实目录 30/30 spec、0 告警。
- **门禁**：`wf verify` **PASS 15/15**（L1 1819 · doc · l2-meta · 4 个 UI 夹具 · 其余）。

## 未决事项

- **P1-3～P1-6 未做**：`UiPageManifest`、契约校验规则族与 `UiRuleExceptions`、Hub/CLI/CI 接线、试点页挂 `LTextKeyRef`。
- **清单无工具入口**：只能经 C# API 触发（`UiWidgetManifestGenerator.Generate/GenerateToDisk`）；设计 §9.1 工具清单暂无独立 `ui.widget-manifest` 项，P1-5 需明确挂到工作台控件库透视还是新增工具。
- **生成物不提交**：清单按需生成（与 `UiBindManifest` 同口径），仓库无基线文件。
- **声明语义未校验**：状态/尺寸行为是人工裁决（词表 §3 + 设计 §6.5），「声明与实现一致性」归 P1-4；审计表已挂账。
- **事件解析按简单类型名**：跨命名空间同名组件理论上可能误判（当前 30 件模板类型名唯一）；P1-4 可收紧为全名。
- **`GenerateToDisk` 无显式路径重载**：P1-5 工具接线时按需补。
- **工作区未提交**：代码/30 个 prefab/文档/脚本均未 commit，提交与分批由用户裁决。

## 文件清单

- 运行时：`Assets/LiteClient/UI/Spec/{WidgetSpec,WidgetSlotSpec,UiWidgetState,UiWidgetSizeBehavior}.cs`
- 编辑器：`Assets/LiteGame/Editor/UiTools/Widgets/{UiWidgetManifest,UiWidgetManifestGenerator,UiWidgetManifestJson,WidgetSpecAuthoring,WidgetSpecInspector}.cs`、`Assets/LiteGame/Editor/UiTools/UiDeterministicJson.cs`、`Assets/LiteGame/Editor/UiTools/Contract/UiContractJson.cs`
- 内容：`Assets/ShareResource/UI/Widgets/Tmp_*.prefab`（30 个，仅新增 `WidgetSpec`）；迁移输入 `AgentScripts/BuildWidgetSpecs.cs`、`AgentScripts/widget-specs.json`
- 测试：`Assets/Tests/EditMode/WidgetSpecEditModeTests.cs`、`Assets/Tests/EditMode/UiWidgetManifestEditModeTests.cs`
- 文档：`Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md`（§8.4）、`Docs/设计文档/客户端/UI系统/UiWidgetManifest.schema.json`、`Docs/设计文档/客户端/UI系统/README.md`、`Docs/施工进度/UI系统线.md`（第 2 章）、`Docs/施工进度/README.md`、`Docs/审计/代码质量审计-客户端.md`
- 工装/配置：`.workflow/config.json`（新增两条验证命令）、`.workflow/active/ui-p1-widget-manifest/**`

## 后续建议

- **P1-3 `UiPageManifest`** 复用本批模式：生成物 + 确定性 JSON + `sourceHash` + 负例不写盘；页面清单可复用 `UiPageScaffold.ListPages()` 投影。
- **P1-4 校验规则**：直接消费清单 `warnings`/`sourceHash`/条目字段；补「缺 `WidgetSpec`=Error」与「状态/尺寸声明与实现一致性」两条规则。
- **P1-5 工具接线**：在工作台控件库透视里给「生成/校验清单」动作，并在设计 §9.1 补工具 ID（当前清单里没有独立项）。
- **内容复核**：30 件 spec 的状态/事件/尺寸由工程侧按代码与词表裁决，建议美术/TA 过一遍（尤其 `Tmp_StateButton`/`Tmp_Toggle`/`Tmp_TabGroup` 的状态集合与 `SizeBehavior`）。
- **新增模板流程**：挂 `WidgetSpec`（Inspector 一键发现插槽）→ 填 Owner/Summary/状态/事件/尺寸 → 跑清单 0 告警 → 才允许页面使用。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-22T22:40:50+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-22T22:49:49+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-22T22:51:26+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-22T22:51:31+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-22T22:59:19+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-22T23:09:52+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-22T23:17:28+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-22T23:17:28+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-22T23:18:19+08:00 · 完成「回写与收尾」→ 完成
