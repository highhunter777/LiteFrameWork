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
| 6 | UI 系统 P1 边界收口：五族校验 · 契约补全 · 遍历统一 | — |
| 7 | UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位 | — |
| 8 | UI 系统 P2-1：单工作台与角色 Lane（宿主 + 透视迁移） | — |
| 9 | UI 系统 P2-2：起页/登记拆分（草稿契约 · 表/Lua/生成链 · 冲突保护） | — |
| 10 | UI 系统 P2-3a：令牌源与主题资产（JSON→UiThemeAsset，退役硬编码） | — |
| 11 | UI 系统 P2-3 B2：令牌引用组件与应用（UiStyleRef · 运行期解析 · 状态色令牌化） | — |

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
| `python scripts/gate/unity-tests.py --mode editor --filter UiStaticTextEditModeTests` | **10/10 通过**（TMP 优先/UGUI 回退/语言刷新/关闭退订） |
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

---

## 第6章 UI 系统 P1 边界收口：五族校验 · 契约补全 · 遍历统一

<a id="ch6-UI系统P1边界收口"></a>

> **批次**：P1 边界收口（2026-10-23，staged-delivery）
> **设计**：[UI 编辑器工具链专项设计 §6 校验核心](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-validation) · [§5.2 契约字段模型](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-contract) · [UI 制作规范 §3.1/§3.3 根契约（本批对齐实况改文）](../设计文档/客户端/UI系统/UI制作规范.md#ch-prefab)
> **方案与任务**：`.workflow/archive/20261023-172440-ui-p1-boundary-closure/`（plan.md / tasks.md / test-plan.md / build-log / reviews）
> **用户裁决**：①结构族扩覆盖前「改规范对齐实况」（§3.1 四件套限定 Page_*、系统面/HUD 根新口径）；②UIMain 冻结「无壳契约」。

### 状态

**已完成（附验证证据）**——P1 留下的五项待接边界（五族校验、owner/designRef 投影、events[] 填充、UIMain 裁决、遍历两份语义）全部收口；收口补遗（LF 行尾锁定/页面清单夹具稳定/门禁全异步）同批完成；终验 `wf verify` **29/29 PASS**。P2～P5 不在本批范围。

### 交付

| 件 | 变化 |
|---|---|
| 文本族（4 规则） | `ui.text.static-key`（`LTextKeyRef`＋Lua `SetTextKey*` 两路 key 对账；表读失败显式 Error）、`ui.text.placeholder`（静态键挂带参条目 Error／`SetTextKeyArgs` 参数数不符 Warning）、`ui.text.locale-column`（Zh 空 Error／En 空 Warning）、`ui.text.rich-text`（标签白名单 Warning）；表数据面＝`UiValidationContext.LTextSeed` 种子注入＋`UiLTextTable.TryGetEntries` 视图 |
| 样式族（2 规则） | `ui.style.color-drift`（复用 `UiStyleTool.ReportDrift`——非 token 色 Warning）、`ui.style.text-drift`（复用 `ReportTextDrift`＋`ReportBrokenRefs` 两路——未标记/漂移 Warning、失效 Error）；零第二份阈值/遍历 |
| 布局/性能（4 规则） | `ui.layout.scaler`（Page_* 根参数偏离＋子树第二套 Error）、`ui.performance.raycast-target`（预算 Warning）、`ui.performance.layout-depth`（嵌套链深度 Warning）、`ui.performance.animator`（页面根 Error／子树 Warning）；阈值单源 `UiRuleThresholds`（预算 60/深度 4/白名单/API 名单） |
| 契约族（3 规则） | `ui.contract.drift`（重生成逐字节对账——含手改检出；判据单源 `UiContractBatch.Regenerate`）、`ui.contract.reference`（required 未消费 Warning／越界引用 Error）、`ui.contract.text-double-write`（静态文案节点被 Lua 再写 Error）；新增 `UiLuaReferenceScanner`（受控 API 调用点/事件接线/handler 尽力解析/字面量全文匹配） |
| 行为族 | `UiBehaviorPlayModeTests` 三用例（复用复位＝`PrepareForShow` 复位面/语言切换＝`OnLocaleChanged` 只重写不重跑 OnShow/模态遮挡＝`UIModalStack.RecomputeBlocking`）；gate 脚本新增 **PlayMode 异步通道**（`run_tests --async_tests` 发射＋`test_status` 轮询终态） |
| 契约生成器 | `ApplyManifestEditorial`（owner/designRef 从页面清单投影，元数据不入 hash）＋`ApplyEvents`（Lua 接线进 events[]，稳定排序＋hash 重算，幂等）；`UiContractBatch.GenerateRegistered(…, manifest)` 全链＋`Generate/Diff` 清单感知；**11 契约重生成入库**（owner=art.ui 全投影、`Page_Login` events=2〔`Button_GuestLogin.onClick→_Login`、`Button_Settings.onClick→anonymous`〕、Lobby 5/Room 2/Settings 5） |
| 遍历统一 | 核心下沉 `LiteCodeGen/Editor/EditorTools/BindSurfaceWalk.cs`；`UiBindSurfaceWalker` 变委托包装（公共 API 不变）；`BindSearch` 变「核心遍历＋生成器域适配」（Clone_/模板边界不入；标识符校验/重名/OrderIndex 保留）；关键字表补 `partial`（审计 L268） |
| 结构族扩覆盖 | `PageStructureRule` 三类口径（Page 四件套＋SafeArea／Hud 三件〔射线按需〕／Screens Tmp_ 系统面〔Canvas+CanvasGroup、**禁**缩放器、射线按需〕）；制作规范 §3.1/§3.3 同批改文（用户裁决 A） |
| 收尾接线 | 13 规则注册进 `UiValidator.Rules`；`Families` 十族全部 `implemented=true`（各族 Note 列明未落地子项与原因）；config.json 增 10 门禁命令（9 EditMode＋1 PlayMode） |
| 门禁脚本 | `unity-tests.py`：`_result` 统一解码双重编码字符串（recompile/test_status 路径 `AttributeError` 根治）＋ EditMode/PlayMode 统一异步通道（`run_async`）；审计修复登记 5 行回写（L247/L268/L252＋两项新登记） |
| 收口补遗·行尾锁定 | `.gitattributes` 对 `Assets/LiteGame/Editor/UiTools/Contracts/**` 与 `UiTools/*.json` 固定 `text eol=lf`——`core.autocrlf=true` 下全新检出不再变 CRLF，契约漂移字节对账不因平台误报（`git checkout-index` 模拟检出 CRLF=0） |
| 收口补遗·夹具稳定 | `UiPageManifestEditModeTests` 改 `OneTimeSetUp/TearDown`＋陈旧裸目录先清＋负例输出独立文件名——根治 `TempPageManifestTests.meta` 写入竞态（连续两次 15/15） |
| 收口补遗·门禁通道 | EditMode/PlayMode 统一走 `run_tests --async_tests` 发射＋`test_status` 轮询终态；`cancelled/interrupted/error` 与 0 用例判红——长夹具不再被管线 30s 传输超时截断/部分通过假绿（控件清单 11/11 实测） |

### 验证证据

| 项 | 结果 |
|---|---|
| 先红 | `wf verify --red` **PASS**（新增 7 条 red 命令全部断言失败转红；既有 10 条通过；编译 0 错先行） |
| 新夹具 | 文本 7/7 · 样式 4/4 · 布局性能 6/6 · 扫描器 6/6 · 契约对账 9/9 · 遍历统一 7/7 · 行为 PlayMode **3/3**（test_status 终态：`复用复位/语言切换/模态遮挡` 全过） |
| 回归 | 校验核心 14/14（十族全接入门禁＋真实面校准钉）· 校验规则 10/10（HUD/Tmp_ 新口径）· hash 12/12（断言对齐后）· 接线 8/8 · 生成器 10/10 · diff 5/5 |
| 真实面校准 | 全量 `UiValidator.Run()` **Error=0 / Warning=303**（颜色 161＋未标记文本 141＋UIMain 根名债 1——样式对账债 SHOULD 级可见，钉进门禁断言；文本/引用对账/双写/布局/性能在真实面零问题）；两轮探针（重生成前 11 Error 全为契约漂移→重生成后归零） |
| 生成物 | 11 契约含 owner/events；重复生成逐字节一致；`UiToolchainWiringEditModeTests` 基线断言复绿 |
| PlayMode 门禁 | `ui-behavior-playmode` 通道端到端 **3/3 passed**（发射→轮询→终态判定全绿） |
| 收口补遗验证 | 页面清单夹具连续两次 **15/15**；控件清单异步通道 **11/11**；`git checkout-index` 模拟全新检出 CRLF=0；`python scripts/gate/doc-lint.py` **0/0** |
| 终验 | `wf verify` **PASS 29/29**（2026-10-23T17:23；L1 1819 + 全部 UI 夹具 + PlayMode 3/3 + doc/l0/toolchain） |
| 审计 | L247/L268/L252 修复登记＋两项新登记（HEAD 即有红夹具/gate 脚本两洞）回写 |

### 边界与遗留（如实）

- **UIMain 无壳契约为冻结决策**（用户裁决 B）：批量生成维持跳过＋告警；若将来需要壳级契约，先冻结壳根契约并放开命名规则，另批。
- **样式族不含**：令牌 SO 管线（§8.1 JSON→`UiThemeAsset`——P2/T3）、状态色硬编码（Selectable ColorBlock 的 token 映射未裁决）、「令牌不存在」（无令牌引用机制）。
- **文本族不含**：缺字/字体 fallback（SDF Dynamic 模式运行时补字，无静态判据）。
- **布局/性能族不含**：锚点漂移/超宽屏溢出（待设备档与布局基线——P4）、图集依赖（项目零 spriteatlas 基建）、Player 侧 rebuild/GC/耗时（总设计 §11.6「Player 性能另测」）。
- **行为族不含**：焦点例（焦点契约未冻结）；列表重绑/取消/转场输入锁已有既有夹具覆盖（List/LuaPage/Navigation PlayModeTests）。
- **Warning 基线 303**（颜色 161＋未标记 141＋UIMain 1）是**如实登记的样式债**：本批只让债可见（SHOULD 级设计口径），prefab 整改（样式工具批量收敛/补标记）另批；整改后门禁钉随批更新。
- **命名/绑定族 pending 子项**（BindName 同文/同页同词/编号连续性）与五族边界无依赖，另批。
- **模板 `bindingHash` 不含模板根 `BindNode`**（`UiBindGenerationHash.cs` 跳过根）——根绑定变更不改变 hash，根绑定模板（Tmp_Dropdown 等）契约里恒为同一 hash；本批约束「既有 hash 逐位不变」，另批与生成器/令牌改造一起修。
- **`UiContractBatch.TryLoadManifest` 对损坏清单 fail-open**（`UiContractBatch.cs` 捕获所有异常返回 null）——`ui.contract` 可能在清单损坏时重写契约并丢 owner/events；`ui.page-manifest` 规则会显式 Error，但生成器应改 fail-fast（另批）。
- **多行页面契约主行口径**：批量生成取表行顺序首行、漂移重生成取清单 `forms[0]`（按 formId 升序）——表行顺序非升序时会产生假漂移；当前 `Tmp_Dialog` 未触发，另批统一主行判据。
- **本批已提交**（`fed30ebd`，工作区检查点；同提交含并行线 DevMenu 退役等改动）；按批收口纪律统一提交。

---

## 第7章 UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

<a id="ch7-UIMain退役"></a>

> **批次**：UIMain 退役（2026-10-23，staged-delivery）
> **设计**：[UI 系统总设计 §5.1 页面编号](../设计文档/客户端/UI系统/UI系统总设计.md#ch-page-catalog) · [UI 编辑器工具链专项设计 §5.2/§8.5](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-page-manifest) · [UI 制作规范 §3](../设计文档/客户端/UI系统/UI制作规范.md#ch-prefab)
> **方案与任务**：`.workflow/archive/20261023-183425-ui-uimain-retirement/`（plan.md / tasks.md / test-plan.md / build-log / reviews）

### 状态

**已完成（附验证证据；已提交 `49bd81d2`）**——产品注册/启动锚/main.lua/页面清单已摘除；测试载体已改名归位；定向夹具全绿；全量 `wf verify` **30/30 PASS**。

### 交付

| 件 | 变化 |
|---|---|
| 产品注册 | `tbuiform` 删 id=1（唯一写入面 `_uiform_row.py` 新增 `--delete-id`，带备份/确认/回滚）；Luban 重生成 `tbuiform.lua`/表数据/`LuaKeys.g.cs`（UI 常量 7→6）；`ui.page-manifest` 对账 11 页/12 行、无 UIMain、0 告警 |
| 启动锚 | `RegistryFiller.KnownUiKey = LuaKeys.UI.Page_Login` 单源；`main.lua` 去 `UI.UIMain` require/全局，冒烟日志改 `main.lua loaded` |
| 测试载体 | `UIMain.prefab` → `Tmp_UiTestPage.prefab`（unity-pipeline 保 GUID；根同名、去 CanvasScaler、BindName `Button_Close`/`Txt_Label`）；`UIMain.lua` → `Lua/Tests/UiTestPage.lua`（模块/类/viewName 同步） |
| 测试夹具 | 页面清单 11/12 断言；Wiring 无跳过；Validation 302/0；SafeArea/BindingTools/U0/LuaPage/EnvRebuild/Navigation 指向新载体；新增 `UiUimainRetirementEditModeTests` 6 例与 `UiPageManifestEditor.Remove` 显式退役 API |
| 门禁 | 异步通道修 filter 透传（工具 `--timeout` 不能放在 filter 后）、域重载前置等待、陈旧 completed 忽略；本轮 red 19 命令与定向复跑均经该通道 |
| 生成物 | `gen-build-hash.py` 复算 shared=`459a3145904693b7`；11 份产品契约不变 |
| 文档 | 代码地图/技术总结 04·11/项目说明 03/UI 系统总设计/工具链设计/schema/审计/本章回写 |

### 验证证据

| 项 | 结果 |
|---|---|
| 先红 | `wf verify --red` PASS（19 命令；退役守卫 5/6 断言失败，page-manifest/ui-toolchain-wiring/ui-validation-core 三条既有断言按目标态转红） |
| 退役守卫 | `UiUimainRetirementEditModeTests` **6/6**（页面清单/LuaKeys/锚/main.lua/载体路径/契约面判据） |
| 定向夹具 | PageManifest **16/16** · Wiring **8/8** · Validation **14/14**（Warning 302、命名 0）· ValidationRules **10/10** · SafeArea **6/6** · BindingTools **12/12** · U0 **7/7** · LuaPage **4/4** · EnvRebuild **3/3** · Navigation **4/4** |
| 生成物 | 页面清单 11 页/12 行；LuaKeys 6 常量；BuildHash shared 已复算 |
| 终验 | 全量 `wf verify` **PASS 30/30**（2026-10-23T18:33:46；L1 1819 + 全部 UI EditMode + PlayMode 3/3 + 退役守卫 6/6 + doc/l0/toolchain） |

### 边界与遗留（如实）

- **测试载体仍在产品收集目录**：`Tmp_UiTestPage` 位于 `Assets/ShareResource/UI/Screens`（YooAsset `UIScreens` 按目录收集；PlayMode 真资源链路需要）。产品包仍有一个小测试 prefab/Lua；若要求包内零测试资产，另开测试收集组批（方案候选 C）。
- **11 份产品契约不变**：UIMain 原本 `contractPresent=false`；退役只改页面清单，不改产品契约内容。
- **UIMain 历史引用**：归档文档与 git 历史保留；live 文档仅在“已退役”说明中出现。合成夹具 `signed-candidate*.json` 的 `lua/ui/UIMain.lua` 是签名固定样例，未改。
- **样式债基线**：`UiValidation` 的 Warning 从 303 降为 302（仅 UIMain 根名债消失）；颜色 161/文字 141 未变。
- **已提交 `49bd81d2`**（本批范围；`代码地图`/`项目说明 03` 与并行 DevMenu/调试线改动同文，随该线提交）。工作区另有并行线未提交：DevMenu 退役/调参注册表/测试房语义批（`GM与调试线` 第 15～19 章）。

---

## 第8章 UI 系统 P2-1：单工作台与角色 Lane（宿主 + 透视迁移）

<a id="ch8-UI系统P2工作台"></a>

> **批次**：P2-1（2026-10-23，staged-delivery）
> **设计**：[UI 编辑器工具链专项设计 §2 工作台](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-workbench) · [§9 工具清单与阶段](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-phases) · [UI 系统总设计 §12 P2](../设计文档/客户端/UI系统/UI系统总设计.md)
> **方案与任务**：`.workflow/archive/` 的 `*-ui-p2-workbench/`（plan.md / acceptance.md / tasks.md / test-plan.md / build-log.md / reviews）

### 状态

**已完成（附验证证据；已提交 92361185）**——宿主、五透视、Lane/阶段双层门控、`ui.workbench` 登记与 `ui.compose`/`ui.page-new` 退役已落地；新增夹具 **21/21**、回归 `UiPreview` **6/6**、全量 `wf verify` **31/31 PASS**。独立代码评审（三轮）发现并修复：选页不绑定目标、Lane 编辑旁路、未保存关闭提示、编辑非原子、打开 Stage 后目标不回绑——全部有夹具或门控覆盖。P2-2（起页/登记拆分）与 P2-3（样式令牌/预览矩阵）不在本批。

### 交付

| 件 | 变化 |
|---|---|
| 宿主 | 新增 `Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchWindow.cs`：纯 IMGUI（宿主零 Sirenix）+ 顶部上下文条（页面选择 / 目标 prefab / Lane / 设备档占位 / 预览）＋五透视页签＋底部状态行＋未保存提示；输出 `[ui/workbench]` |
| 门控内核 | `UiWorkbenchLane`/`UiWorkbenchAction`（11 动作）/`UiLanePolicy`（动作×角色矩阵含禁止原因，窗口与夹具单源）/`UiWorkbenchAvailability`（阶段层：起页/登记 P2-1 未建，原因指向 P2-2；提示优先级＝阶段 > Lane）/`UiWorkbenchPerspective`（5 透视 + 11 个能力标签） |
| 上下文 | `UiWorkbenchState`（Lane/透视/目标/选中页/未保存标记；变更方法返回是否变化，零 UnityEditor 依赖）+ `IWorkbenchSettings`（EditorPrefs 与内存两实现，键名常量单源） |
| 页面透视 | 新增 `UiWorkbenchPageSection`：真实清单 11 页/12 行只读列表 + Owner/设计稿/备注/状态编辑（经 `UiPageManifestEditor` 单源校验）+ 显式保存（`Store.Save`，失败不写盘）+ 缺清单/损坏显式错误；起页/登记按钮置灰并显示 P2-2 原因 |
| 契约透视 | 新增 `UiWorkbenchContractSection`：`UiContractBatch.Generate/Diff` 窗口入口 + `UiContractReportView`（页数/跳过/破坏性/输出目录） |
| 分区迁移 | 绑定透视＝`UiBindingsSection`+`UiGenerateSection`（路径 B 生成+hash）；控件库＝`UiWidgetLibrarySection`+`UiWidgetPropertySection`；校验＝`UiValidationSection`（窄档/全量档） |
| 工具面 | `ui.workbench` 登记（`EditorToolHandlers` + `toolchain.json` + 工具索引重生成）；`ui.compose`、`ui.page-new` 退役（两个窗口类删除、catalog 断言改、`UiPageScaffold.Create` 留作 P2-2 内核并注明） |
| 测试 | 新增 `UiWorkbenchEditModeTests` 13 例（矩阵穷举/阶段可用性/透视与能力标签/上下文/清单投影与确定性保存/契约报告与真实生成到临时目录/工具面与退役）；`UiPreviewEditModeTests` 6/6 改指新入口 |
| 文档 | `08-UI工作台`（现行形态与入口表）、技术总结 04、修改指南工具表、开发工具链平台设计（窗口条目）、工具索引（重生成）、本章 |

### 验证证据

| 项 | 结果 |
|---|---|
| 先红 | `wf verify --red` PASS（20 命令；`ui-workbench-red` exit=1，10/10 断言失败；其余 19 条通过）——`evidence/red.json` |
| 新增夹具 | `UiWorkbenchEditModeTests` **21/21**（矩阵逐项允许集/能力标签精确/选中回调与目标绑定/缺清单/原子迁移/契约生成到临时目录 11 份且逐字节一致/真实 diff/Stage 目标解析/内存偏好） |
| 回归 | `UiPreviewEditModeTests` **6/6**（`ui.workbench` 在册、`ui.compose`/`ui.page-new` 为 null） |
| 工具链 | `python scripts/toolchain.py check` OK（1 条既存未注册脚本告警）；`docs` 已重生成 |
| 全量 | `wf verify` **PASS 31/31**（L1 + UI EditMode 17 条 + PlayMode + doc/l0/toolchain；证据 `evidence/verify.json`） |

### 边界与遗留（如实）

- **起页/登记留 P2-2**（用户裁决）：页面透视的起页/登记按钮置灰并显示原因；P2-1 后无窗口起页工具（手工复制 `Tmp_Page` + `_uiform_row.py` + `Luban/gen.bat`），手册已写明。
- **`UiPageScaffold.Create` 暂无窗口消费者**：作为 P2-2 内核保留（注释挂 P2-2 批次号）。
- **样式令牌/预览矩阵留 P2-3**：设备档为占位；样式族遗留（令牌 SO 管线、状态色 token 映射）不变。
- **控件库/受控属性分区仍用 Odin**：宿主零 Sirenix，但复用分区是存量 Odin 实现；纯 IMGUI 化留清理批。
- **页面清单基线 11 页/12 行**：与既有 P1 夹具同口径；页面增减时随批更新断言。
- **已提交 92361185**（57 文件；按 hunk 拆分，共享文件只含本批改动）。工作区仍有并行 DevMenu/调参/测试房语义批未提交（debug.tunables/GmDebugTunablesPage 等）。
- **手工冒烟限制**：窗口打开/状态/五透视已用 `eval_file` 冒烟（`title=UI 工作台 | Perspective=Page | 5`，Console error=0）；逐透视点击与截图受编辑器主循环限制未自动化，逻辑由 21 例夹具覆盖，视觉留人工一次确认。
---

## 第9章 UI 系统 P2-2：起页/登记拆分（草稿契约 · 表/Lua/生成链 · 冲突保护）

<a id="ch9-UI系统P2二"></a>

> **批次**：P2-2（2026-10-24，工作区未提交）
> **设计**：[UI 编辑器工具链专项设计 §3.1 起页 / §3.5 契约 / §3.6 登记 / §9.1 工具](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-workbench) · [UI 系统总设计 §12 P2](../设计文档/客户端/UI系统/UI系统总设计.md)
> **方案与任务**：`.workflow/active/ui-p2-scaffold-register/`（plan.md v4 / acceptance.md / tasks.md / test-plan.md / reviews）

### 状态

**功能完成（代码 + 静态夹具/命令证据；真实点击与人工签收待做；已提交 `0574da90`——提交号 2026-10-25 补记）**——`ui.page-scaffold` 与 `ui.page-register` 上任、草稿契约链、工作台/外窗冲突保护已落地；C# 骨架、运行时缓存消费、预览矩阵/样式令牌不在本批（见边界）。

### 交付

| 件 | 变化 |
|---|---|
| 起页内核 | `UiPageScaffold`：基线表（`Tmp_BaselineA/B`/`Tmp_ListScreen`/`Tmp_Page`）、`EnsureRootContract`（四件套幂等补齐；安全区优先挂现有拉伸 `Cot_*` 容器，`Tmp_ListScreen` 保留既有 `SafeAreaReceiver`）、`Scaffold`（基线复制→根契约→清单草稿；失败删除新 prefab 并恢复清单字节）；删除一键 `Create` |
| 登记内核 | `UiPageRegistration`：契约门槛（草稿契约 `formId=0`、`page.name`、`prefabGuid` 一致）、Draft/表单空预检、id/layer/缓存策略校验、Lua 骨架（已存在保留）、表写核验→回滚→复核、`Luban → BuildHash` 代码生成链（失败回滚并重跑 Luban 复原）、清单 `Registered`+formId+缓存策略、契约按真实身份刷新、仅 `BindRoot` 页面记录绑定 hash；端口 `IUiPageTablePort`/`IUiPageCodegenPipeline`/`IUiPageContractRefresher` 供故障注入 |
| 表门面 | `UiPageTableRow`/`UiPageTableHelper`/`UiPageToolProcess`：表读/写/回滚/删除单源；`_uiform_row.py` 增加 `--table`（备份目录随表）、layer 0–3、`Page_` PascalCase；`UiPageScaffold.PageRow` 全调用点迁移（Sync/ContractGenerator/Batch/3 夹具） |
| 草稿契约 | schema `formId` 允许 0；`UiPageMetadata.IsDraft` + 生成器草稿口径（单源 `DraftLayer=0`/`DraftFullScreen=true`）；`UiContractBatch.Generate/Diff` 纳入草稿（可注入 manifest）、`Regenerate(allowDraft)`；漂移规则覆盖草稿；diff 页面段破坏性口径（0→真实/缓存/层级/`luaPath null→路径` 非破坏） |
| 清单/缓存 | `UiPageManifestStore.Save` 与 `UiContractStore.Save` 共用 `UiAtomicFile`（临时件+替换/重试/退化覆盖）；`SetCacheStrategy`（词表 `UICacheStrategy` 单源）；对账保留缓存策略并刷新草稿 `contractPresent`；契约投影同值 |
| 窗口/工作台 | `UiPageScaffoldWindow`/`UiPageRegisterWindow`（纯 IMGUI，`Open()`/`Open(prefill)`）；工作台页面透视按钮触发事件打开窗口；`UiWorkbenchAvailability` 起页/登记已实现、页面透视能力补 `page.scaffold`/`page.register`；输入即标脏、保存前磁盘 `sourceHash` 冲突拒绝、`ReloadIfChanged` 保留选中/目标 |
| 工具面 | `ui.page-scaffold`/`ui.page-register` 登记为 window；`toolchain.json` 写面/产物/菜单；工具索引重生成 |
| 夹具 | 新增 `UiPageScaffoldRegisterEditModeTests` **31 例**（起页/草稿契约/登记/工作台/端口注入失败）；Python `test_uiform_row` **10 例**；`unity-tests.py` 显式 `--project-path`（多编辑器环境） |

### 验证证据

| 项 | 结果 |
|---|---|
| 新 EditMode 夹具 | `UiPageScaffoldRegisterEditModeTests` **31/31 通过**（含表写“已落行但报失败”回滚复核、回滚后核验读表失败 Unknown、Luban/BuildHash 失败回滚、契约刷新失败、缓存策略对账保留、草稿晋升 diff 非破坏、工作台“应用→保存”落盘） |
| Python 表助手 | `test_uiform_row` **10/10 通过**（Windows；非 Windows 跳过只读写入失败用例）——`--table` 副本上的 list/check/confirm/rollback/delete、layer 0–3、PascalCase、真实表字节不变 |
| 先红证据 | 骨架阶段该夹具 **23/25 断言失败**（历史骨架口径，非 31 例全集；非编译/环境错误）；Python **4 failures + 1 error**（`--table` 未实现）；`evidence/red.json` |
| UI 回归 | 20 套件全绿：Workbench 21、Preview 6、PageManifest 16、Validation 14、ValidationRules 10、RuleExceptions 7、ContractGenerator 10、ContractDiff 5、ContractReconciliation 9、ToolchainWiring 8、UimainRetirement 6、BindingTools 12、Catalog 12、Text 7、Style 4、LayoutPerformance 6、LuaReferenceScanner 6、BindSearch 7、WidgetManifest 11、WidgetSpec 3 |
| 全量门禁 | `wf verify` **PASS**（L1 35s / doc / toolchain-check / docs-check / 契约/工作台/新夹具/ Python 表助手）；`doc-lint` **0/0**；`toolchain.py check` **OK**（1 条既存未注册脚本告警） |
| 数据面 | 夹具结束断言真实 `#uiform.xlsx`、`UiPageManifest.json`、`UiBindManifest.json` 字节不变、真实契约基线 **11 份**逐字节不变；`gen-build-hash.py --check-shared` **OK**（`0a998c8ac6c9de89`，110 files） |
| 冒烟 | `eval_file` 打开起页/登记窗口：`P2-2-SMOKE scaffold=UiPageScaffoldWindow register=UiPageRegisterWindow`，Console **0 error**；真实点击/输入流程留人工一次确认（自动化只证窗口对象） |

### 边界与遗留（如实）

- **C# 骨架不在本批**：窗口显示“逻辑语言固定 Lua”，C# 依赖页面先挂 `BindRoot`/命名空间配置，归接线前置；设计 §3.6 已按此口径改写。
- **缓存策略只进清单/契约**：`tbuiform` 无缓存列，运行时暂不消费；登记写入 `UiPageManifest.cacheStrategy`，对账保留、契约投影同值。
- **页面类型/目标模块**：页面类型由登记 `layer`+`full_screen` 表达；目标模块由资源收集目录/模块目录表达，不作为起页输入（设计 §3.1 已同步）。
- **契约评审仍是人工门**：工具只机检“草稿契约存在/同名/guid 一致/`formId=0`”，G2 评审签字无机器信号。
- **草稿批量依赖清单**：缺清单时批量显式告警“草稿契约未纳入”，登记侧会提示先生成契约；不静默。
- **工作流配置未迁移**：项目仍用 8 阶段变体（缺独立方案/验收门禁阶段）；本批按技能契约手工执行独立评审与用户确认并落盘，待并行 TPS 线归档后再迁移配置。`red_commands` 精简为只跑新增测试（原 20 条历史红命令移除）。
- **`unity-tests.py` 环境修正**：显式 `--project-path`（本机同时打开 WindFrameWork/Test 两个 Editor 时会因“Multiple Unity Editor instances”失败）；`eval_file` 冒烟走显式 `--timeout`。
- **未跑 Player 构建**：本批只改编辑器工具与生成物，无运行时/协议变更；L1 与 UI EditMode 已覆盖逻辑面。
- **提交**：已提交 `0574da90`（并行线改动按批拆分、未随本批入库；提交号 2026-10-25 补记）。

---

## 第10章 UI 系统 P2-3a：令牌源与主题资产（JSON→UiThemeAsset，退役硬编码）

<a id="ch10-UI系统P2令牌底座"></a>

> **批次**：P2-3a（2026-10-24，staged-delivery）
> **设计**：[UI 编辑器工具链专项设计 §8.1 样式工具](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-style-tools) · [§8.3 生成物](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-artifacts) · [UI 系统总设计 §6.1 设计令牌](../设计文档/客户端/UI系统/UI系统总设计.md#ch-style)
> **方案与任务**：`.workflow/archive/20261024-164859-ui-p2-token-base/`（plan.md / acceptance.md / tasks.md / test-plan.md / reviews / evidence）

### 状态

**已完成（附验证证据；已提交 `a5c62f65`）**——P2-3 四批中的第 1 批：令牌值收敛到 JSON→SO、两条旧值表退役、漂移规则与生成工具上任。B2（引用组件与应用）、B3（预览矩阵/设备档）、B4（存量收敛与收口）不在本批。

### 交付

| 件 | 变化 |
|---|---|
| 令牌源 | 新增 `Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`：12 个 `color.*` + 5 个 `font.*`；`spacing/radius/elevation/motion/icon` 空集（按消费者落地，不预造）；格式对齐已冻结 schema 2.0 |
| 模型与校验 | `UiThemeTokens`（七组模型，`JsonProperty(Order)` 固定字段序）+ `UiThemeTokenValidator`（ID 文法/组前缀/颜色 hex/字号 8–96/行高 1–2/字重枚举/七组必在；一次收集全部问题）+ `UiThemeColorHex`（`#RRGGBBAA` 与 `Color32` 编解码、量化取整单源） |
| 规范文本 | `UiThemeTokenJson`：组内按 ID `Ordinal` 排序、LF + 末尾换行、可选字段省略、未知字段拒绝（`MissingMemberHandling.Error`）；`sourceHash`＝规范文本 SHA-256 |
| 生成物 | `UiThemeAsset`（`LiteClient.UI`，纯数据）+ `UiThemeAssetGenerator`（快照比较决定是否写盘/负例零写盘/路径守卫/手改覆盖并报告）+ `UiThemeSnapshot`（生成与规则共用的比较面）+ `UiThemePaths`；生成 `Assets/GameData/Config/UI/UiTheme.asset`（12/5/0×5，`sourceHash=78f288ad…`） |
| 读面退役 | `UiTokenCatalog`（每次操作装载一次、无静态缓存；缺资产/类型不符显式失败）；`UiStyle` 12 色与 `UiTextStyle` 5 档改为只读属性（值来自生成物，C# 零颜色/字号字面量）；`UiStyleTool`/`UiTextStyleTool` 增 `…With(catalog, …)` 重载，既有公开重载与调用点源兼容 |
| 规则 | 新增 `ui.style.token-drift`（Family 样式）：按 JSON 重算快照与 SO 逐字段比对（含 `sourceHash`），不一致/缺文件/非法 JSON 均 Error、FixHint 指向 `ui.style.tokens`；`UiValidationContext` 增 `ThemeJsonPath`/`ThemeAssetPath` 注入缝 |
| 工具 | `ui.style.tokens` 登记（Hub/CLI 在册，`toolchain.py list` 可见）；`toolchain.json` + 工具索引重生成 |
| 夹具 | 新增 `UiThemeTokensEditModeTests`（13 例）、`UiThemeAssetGeneratorEditModeTests`（7 例）、`UiStyleRetirementEditModeTests`（7 例） |

### 验证证据

| 项 | 结果 |
|---|---|
| 先红 | `wf verify --red` PASS：三套 **12/13、6/7、6/7 断言失败**（断言失败，非编译/环境错误），`evidence/red.json` |
| 新夹具 | tokens **13/13** · generator **7/7** · retirement **7/7** |
| 生成幂等 | eval 连跑两次：首次「变更=是」、第二次「变更=否」；夹具断言二次生成后资产逐字节不变 |
| 回归 | 样式两规则 4/4 · 文字档 8/8 · 校验核心 14/14（**Warning 302＝颜色 161 + 未标记文本 141，未漂**）· 工具清单 12/12 · 接线 8/8 |
| 工具链 | `toolchain.py check` OK（1 条既存未注册脚本告警）；`docs` 重生成后 `docs --check` 通过；`ui.style.tokens` 在 `list` 可见 |
| 资产面 | 无 prefab/场景改动；`Assets/ShareResource/` 零变更（生成物只有 JSON/SO/代码/夹具/文档） |
| 全量门禁 | `wf verify` **PASS 40/40**（2026-10-24T16:52；L1 / doc / restore / l0 / toolchain 三件 / l2-meta / 全部 UI EditMode + PlayMode / 表助手 + 本批三套 + 并行线四条） |

### 边界与遗留（如实）

- **B1 无运行期消费者**：`UiThemeAsset` 是纯数据；B2 起由样式引用组件/解析器消费。SO 随 `Assets/GameData/Config` 收集组进包（≤20KB）。
- **五组空集**：`spacing/radius/elevation/motion/icon` 尚无消费者，按"按消费者落地"留空；B2 补值时不改工具/规则/夹具形状。
- **8bit 量化**：JSON 只能存 `#RRGGBBAA`，与旧浮点常量差 ≤0.5/255（不可见，且落在对账阈值 1e-4 内）；本批**未跑** `ui.style.colors-refresh`，存量 161 处颜色收敛归 B4。
- **typography 预留字段**：`lineHeight`/`fontFamily` 是 schema 必填但 B1 无消费者（行高归 B2 应用器；字体名供交接/校验）。
- **CLI 只能打开不能执行**：`toolchain.py run` 对 editor action 回「use open」；生成动作的可执行面是 Hub 按钮与 `unity command eval`，API 面已由夹具覆盖。
- **批外修复**：`scripts/gate/unity-tests.py` 的重复根路径推导（P2-2 引入，`test_single_source_helpers` 基线红）改为复用 `_common.repo_root()`——与 `scripts/gate/test.py` 同型，不属本批设计面但阻塞全量门禁。

---

## 第11章 UI 系统 P2-3 B2：令牌引用组件与应用（UiStyleRef · 运行期解析 · 状态色令牌化）

<a id="ch11-UI系统P2引用应用"></a>

> **批次**：P2-3 B2（2026-10-25，staged-delivery）
> **设计**：[UI 系统总设计 §6.1/§6.2/§6.5](../设计文档/客户端/UI系统/UI系统总设计.md#ch-style) · [UI 编辑器工具链专项设计 §8.1/§8.3](../设计文档/客户端/UI系统/UI编辑器工具链专项设计.md#ch-style-tools)
> **方案与任务**：`.workflow/active/ui-p2-token-refs/`（plan.md / acceptance.md / tasks.md / test-plan.md / reviews / evidence）
> **用户裁决**：D1＝方案 B（装配期应用）；D2＝refs 应用并入 `ui.style.colors-refresh`；D3＝状态色**本批令牌化**（未采纳豁免推荐；范围只辖自研三 widget，uGUI Selectable 留 B4）。

### 状态

**已完成（附验证证据；已提交 `5f5322bd`）**——方案经独立子代理 4 轮评审（两轮 FAIL：8 枚令牌清单补齐/33 处计数修正/双写者裁决；末轮 PASS）＋用户两轮确认（AC 总确认＋D3 增量确认）；T1–T9 全部完成、独立代码评审 PASS（minor 修复复测收口）；终验 `wf verify` **46/46 PASS**（评审修复后复跑 2026-10-25T18:38：L1 1843 + 全部 UI EditMode/PlayMode + doc/toolchain + 并行线四条）。

### 交付

| 件 | 变化 |
|---|---|
| 解析核心单源 | `LiteClient.UI.UiStyleResolver`（FromAsset/TryColor/TryTypography/Apply 结果枚举/HitEpsilon 单源/Hits 判定）；编辑器 `UiTokenCatalog` 变委托壳（公开面签名不变，`Resolver` 属性透出核心）；SO 路径常量下沉 `LiteClient.UI.UiThemePaths`（编辑器引用之） |
| 状态令牌补集 | `UiStyleTokens.json` 补 8 枚 `color.state.*`（值取 prefab 序列化现值量化、零可见变化；映射表含来源 file:line 落总设计 §6.5）；`ui.style.tokens` 重生成（20 色，幂等 changed=False） |
| 引用组件 | `UiStyleRef`（colorToken 声明、空合法、只声明不驱动、单组件约束） |
| 编辑期应用 | `UiStyleRefApplier`（refs 段：权威写者/幂等/报告/跳过 IThemeBound；迁移段：三 widget 状态色最近匹配填 token、不命中不填）＋ `UiStyleTool.ApplyColorsWith` 三段式编排（裸值段跳过已声明与 IThemeBound）＋ `TryNearestMatch` 带 ID 公开单源 |
| 运行期消费链 | `UiServiceModule` 一次装载 `UiTheme.asset`（内容服务租约，失败降级 null＋Warning 一次）→ `UIService` ctor 形参 `themeProvider` → `RunOpenAsync` 赋值 `UIForm.ThemeProvider` → `PrepareForShow`/`ApplyTheme`（refs 应用＋`IThemeBound` 注入；降级＝保持烘焙值） |
| widget 令牌化 | StateButton/TabGroup/StarRating 伴生 token 字段＋`IThemeBound`（组件层级能力接口；取值序＝token 可解析→令牌现值，否则序列化 fallback）；顺手修 `StateButton.OnSelectedChanged` 裸调用 NRE（存量债，新夹具撞出，审计登记） |
| 校验规则 | `ui.style.ref` 五判据（UiStyleRef 未知令牌 Error/值漂移 Warning/无 Graphic Error＋widget 三态〔未知 Error/漂移 Warning/空合法〕＋同节点 UiStyleRef＋IThemeBound 双写者 Error）；注册进 `UiValidator.Rules`，样式族 Note 更新 |
| 试点与迁移 | `Page_Login/Cot_SafeArea/Txt_Title` ← `UiStyleRef(color.brand.primary)`（run_script 经 Unity API，非手改 YAML）；迁移 4 prefab/10 字段 0 未命中；幂等重跑 0/0 |

### 验证证据

| 项 | 结果 |
|---|---|
| 新夹具 | UiStyleResolver **10/10** · UiThemeStateTokens **3/3** · UiStyleRef **6/6** · UiWidgetTheme **7/7** · UiStyleRefRule **7/7** |
| PlayMode | UiThemeApplyPlayModeTests **2/2**（真页面装配期＝令牌现值；null provider 降级不抛） |
| 先红 | 五套 26 例骨架期真红（8/4/2/6/6 失败——断言级非环境）；首轮红证据作废原因（测试编译错＋系统代理拦管线）如实记录于 test-plan |
| 回归 | UiStyleRetirement 8/8 · UiThemeTokens 17/17 · UiThemeAssetGenerator 8/8 · UiStyleRule 4/4 · UiValidation **14/14**（真实面 Error=0，Warning 基线 302→298——widget 状态色入令牌集，债降 4，基线钉随批更新） |
| 幂等 | refs 重跑 applied=0、迁移重跑 filled=0；SO 重生成 changed=False |
| 终验 | `wf verify` **PASS 46/46**（两轮：17:41 首绿、18:38 评审修复后复跑）；评审修复面复跑 UiStyleRef 7/7、UiWidgetTheme 7/7 |

### 边界与遗留（如实）

- **uGUI 原生 Selectable.colors**（33 处/17 prefab）与三 widget C# 默认色字面量：审计登记、留 B4（本批未新增同型）。
- **克隆体边界（已登记设计）**：运行时动态克隆体不在 PrepareForShow 遍历面——B2 单主题下烘焙值＝令牌现值无可见差异；主题切换批前需补克隆体注入机制。
- **五组空集不变**（spacing/radius/elevation/motion/icon）——按消费者落地。
- **门禁脚本批外修复**：`unity-tests.py` unity 调用加 `--proxy-disable`（系统代理拦 127.0.0.1 报 502，实测 Clash 7890；脚本只连本机编辑器永不需要代理）。
- **已提交 `5f5322bd`**（66 文件按批拆分——共享文件 surgical blob：config.json 仅 6 门禁、toolchain.json 仅 summary/updatedAt、unity-tests.py 仅 proxy 修复、审计仅本批 5 行、INDEX 仅本批行；并行线改动原样留工作区）。
