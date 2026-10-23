# UI 系统线（现行体系：契约与校验 · 静态文案 · 工具链）

> **定位**：现行 UI 体系（`Docs/设计文档/客户端/UI系统/`）的施工记录：契约口径、静态文案、页面契约内核、校验与工具链接线。
> 旧体系历史见 [UI线](UI线.md)、[UI战斗HUD线](UI战斗HUD线.md)、[UI加载与导航线](UI加载与导航线.md)（均已冻结，不回填新状态）。

> **章法**：一卷一条线；每章＝一次批次记录。状态只由 git 事实回填。
> **状态图例**（全目录统一）：`已完成（附验证证据）` / `进行中` / `未开始` / `阻塞（附原因）`。

## 章节索引

| 章 | 标题 | 原文件 |
|---:|---|---|
| 1 | UI 系统 P1-0/P1-1：契约口径冻结 · 静态文案 · 页面契约内核 | — |
| 2 | UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest | — |
| 3 | UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表） | — |
| 4 | UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记） | — |
| 5 | UI 系统 P1-5/P1-6：Hub/CLI 接线与试点页静态文案（P1 收口） | — |

---

## 第1章 UI 系统 P1-0/P1-1：契约口径冻结 · 静态文案 · 页面契约内核

<a id="ch1-UI系统P1契约内核"></a>

> **批次**：P1-0/P1-1（2026-10-22，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §5 页面契约](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-contract) · [UI 美术协作与交接规范 §3](../设计文档/客户端/UI系统/UI美术协作与交接规范.md#ch-contract) · [UI 系统总设计 §4 保留基座](../设计文档/客户端/UI系统/UI系统总设计.md#ch-runtime-base)
> **方案与任务**：`.workflow/archive/20261022-222119-ui-p1-contract/`（plan.md / tasks.md / test-plan.md / reviews）

### 状态

**已完成（附验证证据；工作区未提交）**——P1-2～P1-6（WidgetSpec/UiPageManifest、契约校验规则与例外、Hub/CLI/CI 接线、试点页挂 key）不在本批范围。

### 交付

| 件 | 变化 |
|---|---|
| P1-0 口径冻结 | `sourceHash` 只覆盖接口面（control/text/style/template/event 五域规范行 + SHA-256，不含页面元数据/生成器版本/时间戳）；`controls[].kind`/`required` 初始推断（交互/列表/进度类记 `Command`+`true`，其余 `None`+`false`）；模板边界（`Tmp_*` 实例根记一条 `templates[]`，内部件不展开，非 `Tmp_*` 不透明跳过 + Warning）；`cacheStrategy` 默认词表真相＝`UICacheStrategy`（表列未落地前统一 `Lru`）；口径写入[工具链专项设计 §5.2.1](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-contract-rules)与[协作规范 §3](../设计文档/客户端/UI系统/UI美术协作与交接规范.md#ch-contract) |
| schema 漂移 | `UiPageContract.schema.json` 无 `generatedAt`；设计样例与 schema 一致（契约无时间戳，审计由工具日志承担） |
| 静态文案 | 新增 `LTextKeyRef`（只存 `Key`，`[DisallowMultipleComponent]`）与 `UiStaticTextBinder`（TMP 优先/UGUI `Text` 回退，缺目标与空 key 告警一次不阻断同页）；`UIForm` 增量接缝：`PrepareForShow` 惰性建 binder、`ApplyLocale` 一次取 locale 同驱 `ILocaleBound` 与静态文案、`EnterClosing` 退订、`DestroyInstance` 释放 |
| 编辑器读面 | `UiLTextTable`（`tbltext` 装载与 key 清单，失败显式返回错误）+ `LTextKeyRefInspector`（key 下拉 + 中/英预览 + 重载） |
| 遍历单源 | 新增 `UiBindSurfaceWalker`（深度优先输出 `UiSurfaceNode`：绑定根/`Clone_` 子树/嵌套 `BindRoot` 子树/模板实例根/模板内部件）；`UiBindGenerationHash` 改消费同一遍历，**既有绑定 hash 值不变**（`UiBindingToolsEditModeTests` 12/12 回归） |
| 契约内核 | `UiPageContract`（字段序固定、可选字段省略、`luaPath` 允许 null）、`UiPageMetadata`（tbuiform 投影接缝）、`UiContractJson`/`UiContractPaths`/`UiContractStore`（LF、UTF-8 无 BOM、无时间戳）、`UiContractGenerator`（`Generate`/`GenerateRegistered`/`GenerateToDisk`，负例不写盘）、`UiContractDiff`（Added/Removed/Renamed/Changed/Visual/SourceOnly + `IsBreaking`） |
| 测试 | 新增 `UiStaticTextEditModeTests`（6 例）、`UiContractGeneratorEditModeTests`（10 例）、`UiContractDiffEditModeTests`（5 例）；静态文案覆盖 TMP 优先与 UGUI 回退，契约生成覆盖 `Clone_`/嵌套 `BindRoot` 下的模板实例不进 `templates[]`、输出路径必须在 `Assets/` 下 |
| 工具面修正 | `scripts/gate/unity-tests.py` 两处：① `--timeout` 透传 `unity command --timeout`——CLI 默认 30s 会误杀「编译 + 夹具」超过 30s 的慢套件（首次复跑即被误判超时，编辑器侧实际 5/5 通过）；② 跑测前保证程序集新鲜：`Assets/**/*.cs` 比 `Library/ScriptAssemblies/*.dll` 新则强制重编译，且查 `console_status.groundTruth.compilationFailed` 直接判红——编译失败/未编译时 `run_tests` 会拿旧程序集照常“全绿”（本批 Editor 程序集 CS0118 实测踩中） |

### 验证证据

| 项 | 结果 |
|---|---|
| `python scripts/gate/unity-tests.py --mode editor --filter UiStaticTextEditModeTests` | **6/6 通过**（TMP 优先/UGUI 回退/语言刷新/关闭退订） |
| `python scripts/gate/unity-tests.py --mode editor --filter UiContractGeneratorEditModeTests` | **10/10 通过**（含模板边界过滤与输出路径守卫新例） |
| `python scripts/gate/unity-tests.py --mode editor --filter UiContractDiffEditModeTests` | **5/5 通过** |
| `python scripts/gate/unity-tests.py --mode editor --filter UiBindingToolsEditModeTests`（hash 回归） | **12/12 通过**——遍历抽取未改变既有 hash |
| 资产面 | 无 prefab/场景/表改动（本批只写 C#、文档与脚本） |
| 生成物落点 | `Assets/LiteGame/Editor/UiTools/Contracts/<PageName>.contract.json`（首次生成时由写盘器创建目录；本批未提交生成物） |

### 边界与遗留（如实）

- **P1-2～P1-6 未做**：`WidgetSpec`/`UiWidgetManifest`/`UiPageManifest`、契约校验规则族与 `UiRuleExceptions`、Hub/CLI/CI 接线、试点页面挂 `LTextKeyRef`——按设计分期推进。
- **`events[]` 恒空**：事件绑定真相仍在代码，契约只留可选段与 diff 通道；等引用对账批（P1-4）产生消费者再填。
- **两份遍历语义仍并存**：绑定**生成器**域仍用 `LiteCodeGen.BindSearch`（生成代码需要它自己的错误收集与标识符规则）；本批保证「hash / 契约 / 校验」同源，生成器域统一留给生成器改造批。
- **契约暂无工具入口**：只能经 C# API（`UiContractGenerator.GenerateRegistered`）触发；Hub 工具与 CLI 归 P1-5。
- **既有页面未挂 `LTextKeyRef`**：契约 `text[]` 在试点前为空是正常基线；`LTextKeyRef` 不做批量挂载（不写 prefab 是本批约束）。
- **`tbuiform` 无缓存策略列**：`cacheStrategy` 恒为默认 `Lru`；表列落地后只改 `UiPageMetadata` 投影层。

---

## 第2章 UI 系统 P1-2：控件规格 WidgetSpec 与控件清单 UiWidgetManifest

<a id="ch2-UI系统P1控件清单"></a>

> **批次**：P1-2（2026-10-22，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §8.4 控件规格与控件清单](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-widget-spec) · [UI 系统总设计 §1.4/§6.5/§10.3](../设计文档/客户端/UI系统/UI系统总设计.md#ch-runtime-base)
> **方案与任务**：`.workflow/archive/20261022-231824-ui-p1-widget-manifest/`（plan.md / tasks.md / test-plan.md / reviews）

### 状态

**已完成（附验证证据；工作区未提交）**——P1-3～P1-6（`UiPageManifest`、校验规则族、Hub/CLI 接线、试点页挂 key）不在本批范围。

### 交付

| 件 | 变化 |
|---|---|
| 声明组件 | `WidgetSpec`（Owner/Summary/Slots/States/Events/SizeBehavior/Constraints）+ `WidgetSlotSpec` + `UiWidgetState` + `UiWidgetSizeBehavior`（`LiteClient.UI`，只声明不驱动、零 UnityEditor 依赖） |
| 作者面 | `WidgetSpecAuthoring`（插槽发现复用 `UiBindSurfaceWalker`：只收 `Cot_*`/`Slot_*`，排除 `Clone_` 子树/模板内部件/嵌套 `BindRoot` 子树；并入幂等、保留作者填写）+ `WidgetSpecInspector`（Undo + 一键发现） |
| 清单 | `UiWidgetManifest` 模型、`UiWidgetManifestGenerator`、`UiWidgetManifestJson/Store`、`UiWidgetManifest.schema.json`；缺 spec 记 Warning（`specPresent=false`），插槽/事件/约束不一致记 Error 且不写盘，输出限定 `Assets/` |
| 内容 | 30 个 `Tmp_*` 模板按《UI 命名与控件词表参考》§3 能力表补全（`AgentScripts/BuildWidgetSpecs.cs` + `widget-specs.json` 幂等落盘；prefab **只新增组件/字段**：+627 行、0 删除） |
| 序列化单源 | 抽出 `UiDeterministicJson`（与页面契约共用固定字段序/LF/无 BOM/无时间戳口径），`UiContractJson` 改为委托 |
| 测试 | `WidgetSpecEditModeTests`（3 例）+ `UiWidgetManifestEditModeTests`（11 例），覆盖默认声明/插槽发现/汇总/确定性/`sourceHash`/负例/写盘读回/真实目录基线 |
| 文档 | 工具链专项设计 §8.4（字段模型与生成口径 v1.0 冻结）；UI 系统 README 登记新 schema |

### 验证证据

| 项 | 结果 |
|---|---|
| `python scripts/gate/unity-tests.py --mode editor --filter WidgetSpecEditModeTests` | **3/3 通过** |
| `python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests` | **11/11 通过**（含真实目录 30/30 spec、0 告警） |
| 真实目录清单冒烟（默认路径生成） | **30 条 / 30 spec / 0 告警 / 19,782 B / `sourceHash=sha256:6b145441…`**（冒烟后删除生成物，不提交） |
| 回归 | `UiWidgetLibrary` 6/6 · `UiBindingTools` 12/12 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiStaticText` 6/6 |
| 资产面 | 30 个 `Tmp_*.prefab` 仅新增 `WidgetSpec` 组件（+627/−0，无节点/引用变化）；无场景改动 |
| 先红证据 | `evidence/red.json`：`widget-spec-red` 2/3 失败、`widget-manifest-red` 9/11 失败（断言失败，非环境错误） |

### 边界与遗留（如实）

- **清单尚无工具入口**：只能经 C# API（`UiWidgetManifestGenerator.Generate/GenerateToDisk`）触发；Hub 工具与 CLI 归 P1-5；生成物不提交（与 `UiBindManifest` 同口径）。
- **状态/尺寸行为是人工裁决**：取值来自设计 §6.5 词表与词表 §3 能力表，语义一致性校验（如"声明了 Selected 但实现无选中态"）归 P1-4 规则族。
- **事件只声明真实成员**：`类型.成员` 由反射校验（公开事件/字段）；UGUI 内建事件需按 `类型.成员` 写法（如 `Button.onClick`），当前 30 件模板无此声明。
- **`UiPageManifest`（P1-3）与校验规则（P1-4）未做**；`BindSearch` 与 `UiBindSurfaceWalker` 两份遍历语义仍未统一（P1-1 已登记）。
- **工作区未提交**：本批改动（代码/30 个 prefab/文档/脚本）未自行 commit。

---

## 第3章 UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

<a id="ch3-UI系统P1页面清单"></a>

> **批次**：P1-3（2026-10-22，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §8.5 页面清单与状态词表](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-page-manifest) · [UI 系统总设计 §5.1](../设计文档/客户端/UI系统/UI系统总设计.md#ch-page-catalog) · [UI 美术协作与交接规范 §5.3](../设计文档/客户端/UI系统/UI美术协作与交接规范.md#ch-change)
> **方案与任务**：`.workflow/archive/20261022-235930-ui-p1-page-manifest/`（plan.md / tasks.md / test-plan.md / reviews）

### 状态

**已完成（附验证证据；工作区未提交）**——P1-4～P1-6（校验规则族、Hub/CLI 接线、试点页挂 key）不在本批范围。

### 交付

| 件 | 变化 |
|---|---|
| 模型与词表 | `UiPageManifest` / `UiPageManifestEntry` / `UiPageFormEntry` / `UiPageStatus`（五态：Draft/InReview/Registered/Wired/Frozen）+ `UiPageStatusRules`（迁移表单源） |
| 存储与指纹 | `UiPageManifestStore`（`UiDeterministicJson` 口径、UTF-8 无 BOM、LF）+ `UiPageManifestHash`（身份+维护字段，不含告警/版本/时间戳） |
| 对账 | `UiPageManifestSync`：按页面（prefab）唯一、`forms[]` 承载多行登记；身份字段从 `tbuiform` 刷新、维护字段保留；新增/孤儿/重复/非法状态/多行 lua 一致性与 prefab 缺失均显式报告；负例不写盘；表读失败经新增 `UiPageScaffold.TryListPages` 显式报错（不伪造空表） |
| 编辑 API | `UiPageManifestEditor`：`EnsureDraft`（起页草稿、幂等）、`SetStatus`（迁移校验、同值无操作）、`SetEditorial`（Owner/设计稿/设备档/备注，编辑后重算指纹） |
| schema | `UiPageManifest.schema.json`（表单/状态/路径/契约路径约束） |
| 基线 | 真实 `tbuiform` 生成 `Assets/LiteGame/Editor/UiTools/UiPageManifest.json`：**12 页 / 13 行 / 0 告警 / `sourceHash=sha256:bfbc67c7…`**（6,235 B，入库；`Tmp_Dialog` 201/206 合并为一个条目两行） |
| 测试 | `UiPageManifestEditModeTests` 15 例（首次生成/多行合并/lua 不一致/保留维护字段/新增/孤儿/重复 formId/非法状态/确定性/指纹/写盘边界/草稿幂等/状态迁移/真实基线） |
| 文档 | 工具链 §8.5（字段模型与对账口径 v1.0 冻结）；总设计 §5.1 与协作规范 §5.3 的五态表述改为指向 §8.5 |

### 验证证据

| 项 | 结果 |
|---|---|
| `python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests` | **15/15 通过**（含真实 12 页/13 行基线） |
| 回归 | `UiPreview` 5/5 · `UiWidgetLibrary` 6/6 · `WidgetSpec` 3/3 · `UiWidgetManifest` 11/11 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiStaticText` 6/6 |
| 先红证据 | `evidence/red.json`：`page-manifest-red` 12/14 失败（断言失败，非环境错误）；其余 7 条 red 命令通过 |
| 资产面 | 无 prefab/场景改动；新增唯一资产＝页面清单 JSON（编辑器目录、不进包） |

### 边界与遗留（如实）

- **清单尚无工具入口**：只能经 C# API（`UiPageManifestSync`/`UiPageManifestEditor`）触发；起页工具（`UiPageScaffold.Create`）尚未自动写草稿——接线归 P1-5。
- **状态基线统一 `Registered`**：`Wired`（已接线）属工程接线/评审动作，不由对账自动判定；13 行都已在表，故基线均为已登记。
- **多行页面的契约歧义**：`Tmp_Dialog` 登记 201/206 两行，而契约 `page.formId` 是单值（生成器取首行）；差异由调用方按 `forms[]` 选择，P1-4/P1-5 接线时明确（已在设计 §8.5 登记）。
- **手改检测未做**：`sourceHash` 已备，检测规则归 P1-4。
- **工作区未提交**：代码/清单 JSON/文档/配置未自行 commit。

---

## 第4章 UI 系统 P1-4：校验规则族与 UiRuleExceptions（结构/契约族 + 例外登记）

<a id="ch4-UI系统P1校验规则"></a>

> **批次**：P1-4（2026-10-23，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §6 校验核心](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-validation) · [§6.3 严重级别与例外](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-validation) · [§8.3 生成物](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-artifacts)
> **方案与任务**：`.workflow/archive/20261023-002158-ui-p1-validation/`（plan.md / tasks.md / test-plan.md / reviews）

### 状态

**已完成（附验证证据；工作区未提交）**——P1-5（Hub/CLI 接线）与 P1-6（试点页挂 key）不在本批范围。

### 交付

| 件 | 变化 |
|---|---|
| 例外登记 | `UiRuleExceptions`：规则/资产/节点/Owner/原因/到期（`yyyy-MM-dd`）；任意位置 `*` 通配；命中降一级并标注「例外：Owner，到期」；过期失效＋`ui.rule-exception.expired`（Warning）、非法条目 `ui.rule-exception.invalid`（Error）；空登记入库，配 `UiRuleExceptions.schema.json` |
| 核心扩展 | 族名单对齐设计 §6.2 九族（保留项目自定义「渲染/适配」）；`UiValidationContext` 新增 `RuleExceptionsPath`/`WidgetManifestPath`/`PageManifestPath`（测试可注入）；`Run` 末尾统一应用例外 + `SortIssues` 稳定排序 |
| 规则 | `ui.page-structure`（`Page_*` 根 Canvas/CanvasScaler/GraphicRaycaster/CanvasGroup + SafeAreaReceiver）；`ui.widget-spec`（控件模板缺 `WidgetSpec`=Error）；`ui.widget-manifest`（缺失 Info / 指纹漂移 Error）；`ui.page-manifest`（缺失 Warning / 漂移 Error / 对账告警转 Warning） |
| 测试 | 新增 15 例（`UiValidationRulesEditModeTests` 8 + `UiRuleExceptionsEditModeTests` 7）；既有 `UiValidationEditModeTests` 14 例随族名/新规则调整后全绿 |
| 文档 | §6.3 例外格式与降级口径、§8.3 例外文件说明、README schema 登记、审计新增 HUD 根契约债 |

### 验证证据

| 项 | 结果 |
|---|---|
| `unity-tests --filter UiValidationRulesEditModeTests` | **8/8 通过**（含真实 `Screens`/`Widgets` 面零 Error） |
| `unity-tests --filter UiRuleExceptionsEditModeTests` | **7/7 通过** |
| `unity-tests --filter UiValidationEditModeTests`（既有） | **14/14 通过** |
| 先红证据 | `evidence/red.json`：`ui-validation-rules-red` **5/8 失败**（断言失败）；其余 8 条 red 命令通过 |
| `wf verify` | **PASS 18/18**（L1 1819 · doc · l2-meta 14200 · 6 个 UI 夹具 · 其余） |

### 边界与遗留（如实）

- **只落地结构/契约族**：样式/文本/布局适配/性能/行为五族仍标「待接入」（报告显式列出），需要令牌表/语言表/布局基线/PlayMode，另批。
- **结构规则只覆盖 `Page_*`**：HUD/UIMain 根契约未在设计冻结；`Hud_Battle` 缺 `SafeAreaReceiver`/`GraphicRaycaster` 已进审计表，未硬判、未登记例外。
- **例外只支持日期到期**：`v1.3` 版本到期记入 §6.3「v1.0 不含」。
- **清单规则依赖环境**：页面清单规则读 `tbuiform`（Python/openpyxl），表读失败显式 Error；控件清单不入库，缺失只报 Info。
- **例外评审流程未做**：当前靠 code review 看 `UiRuleExceptions.json` diff。
- **工作区未提交**：代码/例外文件/schema/文档/配置未自行 commit。

---

## 第5章 UI 系统 P1-5/P1-6：Hub/CLI 接线与试点页静态文案（P1 收口）

<a id="ch5-UI系统P1工具接线与试点"></a>

> **批次**：P1-5/P1-6（2026-10-23）
> **设计**：[UI 编辑器工具链专项设计 §5](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-contract) · [§5.2.1 生成口径](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-contract-rules) · [§8.3 生成物](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-artifacts) · [§8.4/§8.5](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-page-manifest) · [§9 工具清单](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-phases)
> **方案与任务**：`.workflow/archive/20261023-*-ui-p1-hub-pilot/`（plan.md / tasks.md / test-plan.md / reviews）

### 状态

**已完成（附验证证据；P2～P5 不在本批范围）**——P1 的六个子项（P1-0～P1-6）全部落地；样式/文本/布局/性能/行为五族校验仍待接入（P1-4 已登记）。

### 交付

| 件 | 变化 |
|---|---|
| 契约批处理 | 新增 `UiContractBatch`：`tbuiform` 只读一次、按 prefab 去重、输出确定性；`Generate` 批量写盘、`Diff` 复用 `UiContractDiff` 汇总破坏性变更；根名不在 `Page_/Hud_/Tmp_` 契约面（常驻壳 `UIMain`）显式跳过并告警，同一 prefab 多行登记取首行并告警（其余行归页面清单 `forms[]`） |
| 契约生成器 | `GenerateRegistered` 增「按已读行生成」重载（表不再逐页读取）；新增 `IsContractPageName`（根名判定单源，生成器与批处理共用） |
| 工具面 | 四个动作登记进 Hub/CLI：`ui.contract`（生成全部登记页契约）、`ui.contract-diff`（基线 diff，破坏性判失败、无基线只告警）、`ui.widget-manifest`、`ui.page-manifest`；`scripts/gate/toolchain.json` 同步登记，`Docs/工具链/工具索引.md` 重生成 |
| 起页接线 | `UiPageScaffold.Create` 成功后自动对账页面清单并落盘（`ReconcilePageManifest`，可注入输出路径）；失败只记结果日志、不回滚页面 |
| 试点页 | `Page_Login` 的 `Txt_Title`/`Txt_GuestLogin`/`Txt_Settings` 挂 `LTextKeyRef`（`UI.Login.Title`/`UI.Login.GuestEnter`/`Common.Settings`），Lua 删除对应三处 `SetTextKey`；`Txt_Version`（带参）与 `Txt_Status`（随状态）仍归 Lua |
| 生成物基线 | 11 份页面契约入库（`Assets/LiteGame/Editor/UiTools/Contracts/`，UIMain 不产契约）；`UiWidgetManifest.json`（30 控件）与 `UiPageManifest.json`（12 页 / 13 行，`contractPresent` 转真）重生成入库 |
| 配置 | `.workflow/config.json` 增 `ui-toolchain-wiring-editmode`（验证）与 `ui-toolchain-wiring-red`（先红）/`editor-tool-catalog-editmode` |

### 验证证据

| 项 | 结果 |
|---|---|
| 先红 | `UiToolchainWiringEditModeTests` **5/5 失败**（断言失败：工具未登记、契约 text 为空、Lua 仍双写、契约基线缺失、`contractPresent=false`） |
| 新夹具 | `UiToolchainWiringEditModeTests` **8/8 通过**（含批量生成 11 页/跳过 UIMain/重复零 diff、无基线告警与破坏性判失败、起页对账注入路径） |
| 回归 | `EditorToolCatalog` 14/14 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiPageManifest` 15/15 · `UiWidgetManifest` 11/11 · `UiStaticText` 6/6 · `UiValidation` 14/14 · `UiValidationRules` 8/8 |
| 工具链 | `python scripts/toolchain.py check` **0 error**（1 条既存未注册脚本告警）；`docs --check` 通过 |
| 生成物 | 11 契约 / 30 控件 / 12 页 13 行；`Page_Login` 契约 `text[]`=3（三个 key 与节点见上）；重复生成逐字节一致、无时间戳 |
| 起页对账 | `UiPageScaffold.ReconcilePageManifest` 注入临时路径落盘成功；真实清单重生成后 `contractPresent=true` |
| 门禁 | L1 / Doc（见 `.workflow` 归档的 verify 证据） |

### 边界与遗留（如实）

- **UIMain 不产契约**：常驻壳根名不在 `Page_/Hud_/Tmp_` 契约面，批量生成告警跳过；若将来需要壳级契约，先冻结根契约并放开命名规则，另批。
- **契约 `owner`/`designRef` 未填**：页面清单已承载这两字段，契约投影暂不取值（生成器与批量口径一致），待接线批决定是否投影。
- **五族校验仍待接入**：样式/文本/布局/性能/行为（P1-4 已列报告「待接入」）。
- **多行登记只告警**：`Tmp_Dialog` 契约描述首行（201），206 由页面清单 `forms[]` 选择；契约带 `formIds[]` 属次版本追加。
- **起页仍一键建页+登记**：P2 把它拆成「美术起页（仅 prefab）」与「工程登记（表/Lua/清单）」，本批钩子按现有单流程对账。
- **工作区未提交**：本批改动（代码/契约与清单生成物/试点 prefab 与 Lua/文档/配置）未自行 commit；工作区另有 P2 工作台在途 WIP，不属本批。
