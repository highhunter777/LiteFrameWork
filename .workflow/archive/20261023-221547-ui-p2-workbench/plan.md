# 方案：UI 系统 P2-1 单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23（v2：独立评审后修订）
> 设计：[UI 编辑器工具链专项设计 §2 工作台](../../../Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-workbench) · [§3 制作流水线](../../../Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md) · [§9 工具清单与阶段](../../../Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-phases) · [UI 系统总设计 §12 实施路线](../../../Docs/设计文档/客户端/UI系统/UI系统总设计.md)
> 修订依据：`reviews/plan-review.md`（独立子代理首轮 FAIL：AC1 口径、透视迁移丢动作、Lane 矩阵不可判、夹具与方案冲突、AC5 生成无证据）；用户裁决 2026-10-23：**本批退役 `ui.page-new`**。

## 背景与目标

- **现状**：P1（T1）已收口并提交（`4bb74642`；UIMain 退役批 `49bd81d2`）。制作面仍是过渡形态：
  - `ui.compose`（`UiComposeWindow`，Odin 宿主）：页面选择器 + 目标 prefab + 四个页签（绑定暴露 / 受控属性 / 校验·生成 / 控件库）；无角色 Lane、无页面/契约透视、无共享上下文条。
  - `ui.page-new`（`UiPageWizardWindow`）：一键起页，同时写 prefab + Lua 骨架 + `tbuiform` 行 + Luban + 页面清单对账——美术起页会连带写表，违反设计 §3.1/§3.6 的职责拆分；它还是 `UiComposeWindow.RefreshOpenPageList()` 的唯一外部调用方（删除宿主时必须同批改向导）。
  - 契约/清单/校验/样式动作散在 Hub 与 CLI（`ui.contract`/`ui.contract-diff`/`ui.page-manifest`/`ui.widget-manifest`/`ui.validate`），窗口内只有“校验·生成”一处入口。
  - 设计目标 ID `ui.workbench` / `ui.page-scaffold` / `ui.page-register` 均未建；`08-UI 制作与调试` 手册仍带“目标形态/旧工具仍可用”的过渡说明。
- **要解决的问题**：制作入口分裂（窗口 + 多个 action）、无角色 Lane（美术/工程动作混在一起、起页即写表）、页面元数据与契约 diff 在窗口内不可达、上下文（页面/prefab/Lane）在窗口与工具间重复实现。
- **目标（可观察）**：一个 `ui.workbench` 窗口承载 **页面 / 控件库 / 绑定 / 契约 / 校验** 五个透视，共享页面选择、目标 prefab 与 Lane 上下文；Hub/CLI/工具索引/手册只有这一个制作/起页入口（`ui.compose` 与 `ui.page-new` 退役）；Lane 门控由**可测矩阵单源**决定；P1 的契约/清单/校验内核与既有五个分区原样复用、不复制第二份逻辑。

**P2 总体拆批（本方案覆盖 P2-1，另两批只给边界与退出条件）**：

| 批次 | 范围 | 退出条件 |
|---|---|---|
| **P2-1（本批）** | 工作台宿主 + 角色 Lane + 透视迁移（页面/控件库/绑定/契约/校验）+ `ui.workbench` 登记 + `ui.compose`/`ui.page-new` 退役 | 单入口在册；五透视可用且共享上下文；Lane 矩阵与阶段可用性有夹具；文档/索引同步 |
| P2-2 起页/登记拆分 | `ui.page-scaffold`（仅 prefab + 草稿清单）/ `ui.page-register`（表/Lua/Luban/hash/状态）+ 页面透视闭环 | 美术独立起页不写表；工程登记可回滚；契约门槛生效 |
| P2-3 样式令牌与预览矩阵 | 令牌 JSON→`UiThemeAsset`、令牌/漂移应用、设备档/语言/长文本/截图矩阵、`ui.preview` 登记 | 美术独立交接；预览覆盖中/英/长文本/安全区 |

## 需求澄清与假设

- **需要确认的问题（已裁决）**：
  1. 起页/登记拆分是否并入本批 → **不并入**（用户 2026-10-23 确认，留 P2-2）。
  2. `ui.page-new` 处置 → **本批退役**（用户 2026-10-23 裁决；严格单入口，代价：P2-2 落地前无起页工具）。
- **已确认答复**：范围“只做宿主、透视和 Lane”（用户原话）；验收标准 AC1–AC6 已确认（`acceptance.md`）；`ui.page-new` 退役（同上）。
- **采用假设**：
  - A2：宿主用**纯 IMGUI `EditorWindow`**——宿主自身零 Sirenix（上下文条 + 左树 + 透视页签 + 底部状态行）；复用的存量分区里 `UiWidgetLibrarySection`/`UiWidgetPropertySection` 仍依赖 Odin（PropertyTree/OdinMenuTree），本批**不改写**（避免范围膨胀），P2-3/清理批再收敛；收益是窗口逻辑可外提为可测类。
  - A3：Lane 默认“工程”，用 `EditorPrefs` 记住上次选择（键名常量单源）；测试经注入口（`IWorkbenchSettings`）跑内存实现，不读真实 EditorPrefs。
  - A4：`ui.compose` 直接退役，不做别名——设计 §9.1 只定义 `ui.workbench`。
  - A5：**阶段可用性**与 Lane 门控分两层：Lane 决定“角色是否该做”，阶段决定“工具是否已建”（Scaffold/Register 在 P2-1 未建）；禁用提示按「阶段原因优先、Lane 原因其次」显示，避免“全局置灰掩盖角色差异”。
- **假设影响与风险**：P2-2 落地前无起页工具（用户已知情接受）；若后续要求恢复，可在 P2-2 以 `ui.page-scaffold` 取代。

## 范围与非目标

- **范围内**：
  - 新宿主 `UiWorkbenchWindow`（`Assets/LiteGame/Editor/UiTools/Workbench/`）：顶部上下文条（页面选择 / 目标 prefab / Lane / 设备档占位）＋左侧页面树＋五透视页签＋底部问题/最近操作状态行＋关闭时未保存提示。
  - 可测内核：`UiWorkbenchLane`、`UiWorkbenchAction`（含 `GenerateBinding`）、`UiLanePolicy`（动作×Lane×原因矩阵）、`UiWorkbenchAvailability`（阶段可用性 + P2-2 原因）、`UiWorkbenchPerspective`（id/标题/顺序/能力标签）、`UiWorkbenchState`（Lane/透视/目标/页面/未保存标记，方法返回是否变化）、`IWorkbenchSettings`（EditorPrefs / 内存两实现）。
  - 五个透视与迁移映射（**不得丢现有动作**）：

    | 透视 | 组成 | 能力标签（夹具断言） |
    |---|---|---|
    | 页面 | 页面清单只读列表 + Owner/设计稿/备注/状态编辑 + 起页/登记入口（P2-2 禁用） | `page.list` / `page.metadata` |
    | 控件库 | `UiWidgetLibrarySection`（插入模板）+ `UiWidgetPropertySection`（受控属性） | `widgets.insert` / `widgets.properties` |
    | 绑定 | `UiBindingsSection`（标记/改名/引用扫描）+ `UiGenerateSection`（路径 B 生成 + hash） | `binding.edit` / `binding.generate` / `binding.hash` |
    | 契约 | `UiContractBatch.Generate/Diff` + 报告视图 | `contract.generate` / `contract.diff` |
    | 校验 | `UiValidationSection`（窄档/全量档） | `validate.narrow` / `validate.full` |

  - **预览入口**：上下文条「预览」按钮（`UiPagePreview.Open`，动作 `Preview`）；P2-3 升级为预览透视（设备档/语言/截图矩阵）。

  - 工具面：`ui.workbench` 登记（`EditorToolHandlers` + `toolchain.json` + 工具索引重生成）；`ui.compose`、`ui.page-new` 退役（handler/窗口类/向导类/catalog 断言/文档引用同步）。`UiPageScaffold.Create` 在向导退役后**暂无窗口消费者**，作为 P2-2 起页/登记内核保留并在注释挂“P2-2 接线”（红线 10 的“待接线挂批次号”）。
  - 文档回写：工具链设计 §2（实现口径 v1.0 + 动作矩阵）、`08-UI工作台.md`（现行标题《08-UI 制作与调试》）、技术总结 04、修改指南工具表、`开发工具链平台专项设计.md`（UiComposeWindow 条目）、施工进度新章。
- **非目标**：
  - 起页/登记拆分与 `ui.page-scaffold`/`ui.page-register`（P2-2）；样式令牌 SO 管线、预览设备档/截图矩阵（P2-3）。
  - 运行时调试读模型/命令/注入（P3）；绑定生成器改造与模板 `bindingHash` 根节点债（另批）。
  - 目录大迁移（`Compose/` → `Workbench/`）——本批只新增 `Workbench/` 并删除旧宿主/向导文件，分区类留在原目录（避免 `directory-move-sync` 六面同步扩大范围）。
  - 不改契约/清单 schema，不改校验规则语义。

## 现状与约束

- **相关代码/模块**：

  | 模块 | 作用 | 本批关系 |
  |---|---|---|
  | `UiTools/Compose/UiComposeWindow.cs` | 现行 Odin 宿主（四页签 + 页面选择） | 退役（T7） |
  | `UiTools/Compose/UiComposeTarget.cs` | Stage 跟随/根解析 | 复用 |
  | `UiTools/Compose/Ui{Bindings,WidgetProperty,WidgetLibrary,Validation,Generate}Section.cs` | 五个 IMGUI 分区 | 复用（迁入对应透视） |
  | `UiTools/Page/UiPageWizardWindow.cs` | 一键起页（prefab+Lua+表+Luban） | 退役（T7）；`UiPageScaffold.Create` 留给 P2-2 |
  | `UiTools/Page/UiPageManifestEditor.cs` / `UiPageManifestStore` | 清单编辑/读写 | 复用（页面透视） |
  | `UiTools/Contract/UiContractBatch.cs` | 契约生成/diff | 复用（契约透视） |
  | `Editor/Toolchain/EditorToolHandlers.cs`、`scripts/gate/toolchain.json` | 工具登记/清单 | 同批改（T7） |
  | `Assets/Tests/EditMode/UiPreviewEditModeTests.cs` | 断言 `ui.page-new`/`ui.compose` 在册 | 同批改（T7）：改为 `ui.workbench` 在册、两者为 null |
- **约束**：
  - “工具只做约束/生成/校验/预览，不做运行期依赖”（总设计 §1.3 裁决 10）：窗口关闭无残留、不持有运行时状态。
  - 工具平台接缝（工具链设计 §1.3）：`id/kind/plane/purpose/writes/outputs/docs` 声明；窗口 `writes=["asset","source"]`、`destructive=false`；**显式保存、不自动写盘**、关闭给未保存提示；结果输出 `[ui/workbench]` + 计数。
  - 类单一职责、>400 行先拆分；禁 public 可变字段状态袋（状态类只读属性 + 方法）；禁硬编码（Lane/动作/透视/原因/文档路径进常量或表）。
  - Odin 只进 `LiteGame.Editor`；本批新代码零 Sirenix；不删 Odin 包。
  - 文档纪律：设计文档只写目标/现行形态，状态与证据归 `Docs/施工进度/`；工具索引由 `toolchain.py docs` 重生成。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| **A 新宿主 + 透视（IMGUI）** | 新建 `UiWorkbenchWindow`，复用五个 IMGUI 分区 + 新增页面/契约透视；Lane 策略纯类 | 与设计 §2.1/§2.2 一致；入口唯一；逻辑可测；Odin 依赖收窄 | 需迁移上下文/退役两窗口/改测试与文档；IMGUI 布局工作量 | **推荐** |
| B 保留 compose，旁挂只读工作台 | 新窗口只做页面/契约只读视图，制作仍在 compose | 改动最小 | 双制作入口，上下文重复，违反设计“一个宿主多个透视” | 不采用 |
| C 沿用 Odin 宿主扩页签 | `UiComposeWindow` 加页面/契约页签、加 Lane 下拉 | 复用 Odin TabGroup | 窗口逻辑与 Odin 耦合、EditMode 覆盖差、lifetime 仍要动向导 | 不采用（A2） |
| D 一步做完 P2 全量（含拆分/令牌/预览） | 单批交付 P2 全部 | 一次到位 | 范围过大、验收面失控、回滚粒度差 | 不采用 |

**推荐方案与理由**：A。设计明确“一个宿主窗口 + 多个透视 + 共享上下文”，五个分区已是可复用 IMGUI 绘制器；窗口逻辑外提为纯类后可进 EditMode 夹具，避免“UI 只能手测”。P1 的经验是单批范围过大时收口补遗会吞噬验证时间。

## 设计要点

- **接口/数据**：
  - `enum UiWorkbenchLane { Art, Technical, Engineering }`。
  - `enum UiWorkbenchAction { OpenPage, EditMetadata, Scaffold, Register, InsertWidget, EditBinding, GenerateBinding, GenerateContract, DiffContract, Validate, Preview }`。
  - `enum UiWorkbenchPerspectiveId { Page, Widgets, Binding, Contract, Validation }`（P2-3 追加 Style/Preview）。
  - `UiLanePolicy`：`IsAllowed(lane, action)`、`DisabledReason(lane, action)`（允许返回 null）、`Allowed(lane)`。
  - **动作矩阵（单源，T2 落表，窗口与夹具共用）**：

    | 动作 | Art | Technical | Engineering | 禁止原因（对不适用角色） |
    |---|:--:|:--:|:--:|---|
    | OpenPage | ✓ | ✓ | ✓ | — |
    | EditMetadata | ✓ | ✗ | ✓ | 页面元数据由美术/工程维护（TA 只读） |
    | Scaffold | ✓ | ✗ | ✗ | 起页是美术职责（设计 §2.3） |
    | Register | ✗ | ✗ | ✓ | 登记是工程职责（表/Lua/绑定生成） |
    | InsertWidget | ✓ | ✓ | ✗ | 控件插入属视觉组装；工程改结构须走美术评审 |
    | EditBinding | ✓ | ✓ | ✓ | — |
    | GenerateBinding | ✗ | ✗ | ✓ | 绑定代码生成是工程职责 |
    | GenerateContract | ✓ | ✓ | ✓ | — |
    | DiffContract | ✗ | ✓ | ✓ | 契约对账由 TA/工程评审触发；美术跑增量校验与预览 |
    | Validate | ✓ | ✓ | ✓ | — |
    | Preview | ✓ | ✓ | ✓ | — |

  - `UiWorkbenchAvailability`：`IsImplemented(action)` + `DisabledReason(action)`——P2-1 里 `Scaffold`/`Register` 未实现，原因“起页/登记拆分归 P2-2”；窗口提示优先级＝阶段原因 > Lane 原因。
  - `UiWorkbenchPerspective`：`Id/Title/Order/Enabled/Capabilities`（能力标签见范围表，夹具断言映射未丢失）。
  - `UiWorkbenchState`：`Lane/Perspective/TargetPath/SelectedPage/HasUnsavedEdits`；`SetLane/SetPerspective/SetTarget/SelectPage` 返回“是否变化”，`MarkDirty/ClearDirty`；零 UnityEditor 类型。
  - `UiWorkbenchPageList`：`LoadDefault(out error)` / `Load(path, out error)` / `FromManifest(manifest)`；清单缺失/损坏返回空 + 显式错误。页面选择源＝`UiPageManifest`（页面名/状态/Owner 可见；`Draft` 也显示但标“草稿”）。
  - `UiWorkbenchPageEditor`：`ApplyEditorial` / `ApplyStatus` 只改内存并重算 `sourceHash`；保存由窗口显式 `UiPageManifestStore.Save`。
  - `UiContractReportView`：`FromGeneration(result, outputDirectory)` / `FromDiff(result)`；字段 `Title/Summary/Written/Skipped/Breaking/Changed/Ok/OutputDirectory/Lines`。
- **关键流程**：
  1. 打开 `ui.workbench` → 读页面清单与上次 Lane/透视（EditorPrefs）→ 上下文条就绪；清单损坏 → 底部显式 Error + 重新加载按钮（不伪造空表）。
  2. 选页面（下拉/Project 选中/Prefab Stage 打开）→ `UiComposeTarget.Resolve` → 全透视共享；切透视不重置目标。
  3. 动作经两层门控：`UiWorkbenchAvailability`（阶段）→ `UiLanePolicy`（角色）；禁用按钮带原因 tooltip，底部状态行显示最近动作结果（`[ui/workbench]`）。
  4. 页面透视编辑 → 内存 + 显式“保存清单”→ `Store.Save`；未保存标记进标题/状态行；关闭时提示。
  5. 契约透视“生成全部/与基线 diff”→ `UiContractBatch` → `UiContractReportView`；破坏性红色标注。
- **失败与边界处理**：
  - 无目标 prefab：透视占位提示，动作置灰（不抛异常）。
  - 清单缺失/损坏：显式错误态；不自动生成、不覆盖。
  - 保存失败（只读/异常）：保留内存编辑值 + 显式错误，不写盘。
  - Lane/透视切换不改资产；关闭窗口不保存未显式保存的编辑。
  - `ui.compose`/`ui.page-new` 退役后无菜单/快捷键残留（Catalog 与文档断言）。

## 通用性与扩展性

- **根因/共性问题**：制作入口、角色动作、上下文三者在“窗口/菜单/CLI/文档/测试”五处各写一份，改一处漏四处（P1 前 `ui.contract` 就出现过“工具无入口”）。本批把入口收敛到 `ui.workbench`，把角色与阶段门控收敛到两张表。
- **复用与扩展点**：分区绘制器按 `Draw(UiComposeTarget)` 复用；新增透视＝加 `UiWorkbenchPerspective` 表项 + 绘制器 + 能力标签；新增动作＝加枚举 + 矩阵行 + 可用性行；P2-2/P2-3 以新动作/新透视接入，不改宿主骨架。
- **避免硬编码/特判**：透视顺序/标题/能力、Lane、动作、原因、EditorPrefs 键名、文档链接全部集中常量或表；窗口内禁止 `if (lane == …)` 分散判断；禁止为某透视复制清单/契约读取逻辑。

## 测试策略

- **EditMode 夹具**（`UiWorkbenchEditModeTests`，先红已落盘 10 例）：
  1. 角色矩阵：Art/Engineering 关键项 + **全动作 × 全角色穷举**（禁止必有原因、每动作至少一角色允许）。
  2. 阶段可用性：`Scaffold`/`Register` 在 P2-1 未实现且原因指向 P2-2；Lane 允许时仍显示阶段原因。
  3. 透视清单：5 项、顺序、标题唯一、全部启用；**能力标签映射**（binding.generate/widgets.properties 等不得丢）。
  4. 状态切换：选页/目标/脏标记在切透视与切角色后保留。
  5. 页面清单：真实基线 11 页/12 行只读；缺清单显式错误；编辑落盘确定性（临时路径双写逐字节一致 + `sourceHash` 变化）。
  6. 契约：报告投影（生成/破坏性）；**真实 `UiContractBatch.Generate(临时目录)` 写 11 份并断言无 Error、重复生成逐字节一致**（AC5 生成半证据）。
  7. 工具面：Catalog 含 `ui.workbench` 且 `Error == null`、`ui.compose`/`ui.page-new` 为 null。
- **手动冒烟**（T6 证据位）：Hub 打开工作台 → 五透视逐一切换 → 选真实页面（Stage 跟随）→ 起页/登记按钮显示 P2-2 原因 → 契约 diff 只读跑一次 → Console 无 error、状态行有 `[ui/workbench]`。
- **回归**：`UiPreview`/`UiBindingTools`/`UiValidation`/`UiToolchainWiring`/`EditorToolCatalog`/`UiWidgetLibrary`/`UiComposePanel`（若引用旧窗口则同批改）。
- **先失败后通过**：红证据已观察（10/10 断言失败，见 `test-plan.md`）；新增 2 例（能力标签、阶段可用性）与生成集成例在 T2/T5/T6 落盘后必须全绿。

## 验收标准

- [ ] AC1 **单一入口**：`toolchain.json`、`EditorToolHandlers`、工具索引、`08` 手册中制作/起页入口只有 `ui.workbench`（起页按钮 P2-2 接入）；`ui.compose`、`ui.page-new` 在代码/清单/测试断言中退役；无第二个制作窗口或起页向导。
- [ ] AC2 **五透视可用**：页面/控件库/绑定/契约/校验在同一窗口切换，共享页面选择、目标 prefab 与 Lane；切换不丢上下文；绑定透视含编辑/生成/hash，控件库含插入/受控属性。
- [ ] AC3 **Lane 门控**：美术不可登记/绑生成、工程不可起页、TA 不可登记；禁用项有原因文案（阶段原因优先）；矩阵夹具覆盖全部动作×Lane。
- [ ] AC4 **页面透视**：真实清单 11 页/12 行只读展示；Owner/设计稿/备注/状态编辑落盘确定性（重复保存逐字节一致、`sourceHash` 重算）；真实清单不被夹具改动。
- [ ] AC5 **契约透视**：全量生成（写临时目录 11 份、无 Error、重复逐字节一致）与基线 diff 可跑；报告含页数/跳过/破坏性/输出目录；失败显式。
- [ ] AC6 **退役与文档**：`08` 手册去掉旧工具过渡说明并改写入口；技术总结 04、修改指南工具表、工具索引、施工进度新章同步；`ui-workbench-editmode` 夹具、`doc-lint` 0/0、`toolchain.py check` 0 error、L1 全绿。

## 风险与回滚

- **风险**：
  1. **P2-2 前无起页工具**（用户已裁决接受）：手册与 Hub 需显式说明“起页/登记随 P2-2 提供”，避免误以为工具损坏。
  2. IMGUI 布局返工：先落“可用但朴素”布局，视觉细节留 P2-3。
  3. 窗口不可在 EditMode 实例化：判定逻辑全部外提纯类，窗口只绘制与转发。
  4. 退役波及面：`ui.compose` → `UiPageWizardWindow` 调用、`UiPreviewEditModeTests` 断言、`08`/技术总结/修改指南引用；T7 同批 grep 清单化处理。
  5. 页面清单基线 11/12 硬编码：与既有 P1 夹具同口径；页面增减时随批更新（夹具注释标明来源）。
  6. 清单编辑误写：显式保存 + 校验 + 失败不落盘；夹具只写临时路径。
  7. Odin 依赖：宿主自身零 Sirenix，但存量控件库/受控属性分区仍用 Odin（本批不改写）；新窗口在缺 Odin 的 CI 形态下不可用——与现状一致，依赖面调整留后续批。
- **监控/发现**：新增夹具 + `toolchain.py check` + `doc-lint` + L1；手动冒烟五项（见测试策略）。
- **回滚**：单批单提交可 revert；退役点集中（`EditorToolHandlers` 两行、`toolchain.json` 两段、窗口/向导两个文件、测试断言两处、文档三处）；`UiPageScaffold` 内核未删，恢复向导成本低；清单/契约生成物本批不变（页面透视只读基线 + 生成到临时目录）。
