# 实施任务清单：UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | WidgetSpec 运行时声明组件与词表 | - | 完成 | WidgetSpec/WidgetSlotSpec/UiWidgetState/UiWidgetSizeBehavior 落地；只声明不驱动；运行时程序集零 UnityEditor 引用 | unity-tests --filter WidgetSpecEditModeTests | WidgetSpec + WidgetSlotSpec + UiWidgetState + UiWidgetSizeBehavior 落地（LiteClient.UI，零 UnityEditor 依赖）；WidgetSpecEditMod… |
| T2 | 编辑器作者面：插槽发现与 Inspector | T1 | 完成 | WidgetSpecAuthoring.DiscoverSlots 复用 UiBindSurfaceWalker（只收 Cot_/Slot_，排除 Clone_/模板内部/嵌套根）；Inspector 可编辑数组并支持发现插槽（Undo） | unity-tests --filter WidgetSpecEditModeTests | WidgetSpecAuthoring.DiscoverSlots 复用 UiBindSurfaceWalker（Cot_/Slot_，排除 Clone_/模板内部/嵌套根）；ApplyDiscoveredSlots 幂等保作者填写；Wi… |
| T3 | 控件清单模型、生成器、存储与 schema | T1 | 完成 | UiWidgetManifest 模型/生成器/JSON 存储/schema 落地；确定性（无时间戳/稳定排序/LF/无 BOM）、sourceHash、负例不写盘、输出限定 Assets/；缺 spec 记 Warning | unity-tests --filter UiWidgetManifestEditModeTests | UiWidgetManifest 模型/生成器/JSON 存储/schema；确定性（无时间戳/稳定排序/LF/无 BOM）、sourceHash、负例不写盘、输出限定 Assets/；缺 spec 告警；UiWidgetManifest… |
| T4 | 30 个模板 WidgetSpec 内容落盘 | T1, T2, T3 | 完成 | 按词表能力表补全 30 个 Tmp_* 的 Owner/Summary/Slots/States/Events/SizeBehavior/Constraints，经 unity-pipeline 一次性脚本幂等落盘；真实目录清单 specPresent 全 true、0 告警；prefab 只加组件/字段 | unity-tests --filter UiWidgetManifestEditModeTests（真实目录用例） | AgentScripts/BuildWidgetSpecs.cs + widget-specs.json 幂等落盘 30/30；prefab 仅新增 WidgetSpec 组件（+627 行纯新增，无删除/结构变化）；真实目录用例 30/… |
| T5 | 先行测试与负例覆盖 | T1, T3 | 完成 | WidgetSpecEditModeTests + UiWidgetManifestEditModeTests 覆盖 AC1-AC3 主路径与失败路径（插槽缺失/重复、事件空/重名/成员解析不到、约束空、缺 spec 告警、确定性、sourceHash、写盘读回、真实目录基线），先红后绿 | wf verify --red + 定向 unity-tests | 先红：evidence/red.json（widget-spec-red 2/3 失败、widget-manifest-red 9/11 失败，均断言失败）；实现后复跑 3/3 + 11/11 全绿 |
| T6 | 门禁、评审与回写 | T3, T4, T5 | 完成 | 定向 Unity 用例、L1、doc-lint、L2 meta 扫描通过；设计文档 §8.4 与 README schema 登记；进度卷回写；审计表如实挂账 | L1+doc+l2-meta | L1 PASS 1819；doc-lint OK 0 violation；L2 meta PASS（14184 meta）；定向夹具 3/3+11/11；设计 §8.4 + README schema 登记；进度卷 UI系统线 第2章；审… |

## 任务详情

### T1 · WidgetSpec 运行时声明组件与词表

- 文件: Assets/LiteClient/UI/Spec/WidgetSpec.cs, Assets/LiteClient/UI/Spec/WidgetSlotSpec.cs, Assets/LiteClient/UI/Spec/UiWidgetState.cs, Assets/LiteClient/UI/Spec/UiWidgetSizeBehavior.cs

### T2 · 编辑器作者面：插槽发现与 Inspector

- 文件: Assets/LiteGame/Editor/UiTools/Widgets/WidgetSpecAuthoring.cs, Assets/LiteGame/Editor/UiTools/Widgets/WidgetSpecInspector.cs

### T3 · 控件清单模型、生成器、存储与 schema

- 文件: Assets/LiteGame/Editor/UiTools/Widgets/UiWidgetManifest.cs, Assets/LiteGame/Editor/UiTools/Widgets/UiWidgetManifestGenerator.cs, Assets/LiteGame/Editor/UiTools/Widgets/UiWidgetManifestJson.cs, Docs/设计文档/客户端/UI系统/UiWidgetManifest.schema.json

### T4 · 30 个模板 WidgetSpec 内容落盘

- 文件: AgentScripts/BuildWidgetSpecs.cs, AgentScripts/widget-specs.json

### T5 · 先行测试与负例覆盖

- 文件: Assets/Tests/EditMode/WidgetSpecEditModeTests.cs, Assets/Tests/EditMode/UiWidgetManifestEditModeTests.cs

### T6 · 门禁、评审与回写

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/README.md, Docs/施工进度/UI系统线.md, Docs/审计/代码质量审计-客户端.md, .workflow

