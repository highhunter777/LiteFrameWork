# 方案：UI 系统 P2-2：起页/登记拆分（ui.page-scaffold / ui.page-register）

> 运行 ui-p2-scaffold-register · 创建于 2026-10-23T22:34:41+08:00 · 方案 v3（2026-10-23，按独立评审 B1–B4/I1–I8 与两次用户裁决修订：缓存策略写清单 + 补草稿契约生成链）
> 批次：P2-2（《UI 编辑器工具链专项设计》§3.1/§3.6/§9.1/§9.2 T2；《UI 系统总设计》§12 P2）

## 背景与目标

### 现状

- P2-1（已提交 `92361185`）把 `ui.compose`、`ui.page-new` 退役，工作台 `ui.workbench` 成为唯一制作入口；页面透视的「起页」「登记」按钮**置灰**并由 `UiWorkbenchAvailability` 给出「归 P2-2」原因（`UiWorkbenchAvailability.cs:12-23`、`UiWorkbenchPageSection.cs:88-103`）。
- 现存 `UiPageScaffold.Create`（`Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs:133-238`）是**一键建页**：复制编辑器模板 `Tmp_Page` → 写 Lua → 写 `#uiform.xlsx` → 跑 Luban → 打开 Stage。角色未拆分，无窗口消费者。
- 页面清单已有草稿/状态 API（`UiPageManifestEditor.EnsureDraft/SetStatus`），`Draft → Registered` 合法（`UiPageStatus.cs:52-53`）；但 `UiPageManifestSync` 每次对账都会把 `cacheStrategy` **强制重置为 `Lru`**（`UiPageManifestSync.cs:118`）。
- 页面契约是美术→工程唯一交接物：设计 §3.5「无契约不进入工程交接」、§3.6 登记输入含「已评审契约」（`UI编辑器工具链专项设计.md:141,148`）；P2-1 归档方案把「契约门槛生效」列为 P2-2 退出条件。
- **契约链缺口（复核实测）**：`ui.contract` 批量只遍历 `tbuiform` 行（`UiContractBatch.cs:201,257`），草稿页（起页后无表行）无法生成契约；`UiContractGenerator.Generate` 拒绝 `formId<=0`（`:57`），schema 又要求 `formId` 最小值 1（`UiPageContract.schema.json:91`）。因此“登记前必须有契约”在当前工具链下不可达，本批必须补“草稿契约”生成链。
- `scripts/codegen/_uiform_row.py` 是 `#uiform.xlsx` 唯一写者（校验/备份/回滚/删除），但表路径与备份目录硬编码（`_uiform_row.py:27-28`），且 `layer` 只校验 `<0`（`:90-91`），页面名正则允许小写（`:33`）。
- 基线 prefab 的页面根契约不齐：`Tmp_BaselineA/B` 只有 Canvas+CanvasGroup，内容在嵌套实例 `Cot_Baseline`（拉伸容器）；`Tmp_ListScreen` 只有 Canvas+CanvasGroup，内容为 `VirtualList_Rooms`；均缺 `CanvasScaler`/`GraphicRaycaster`/`SafeAreaReceiver`。
- 工作台页面编辑的未保存判断只在“应用”后置位（`UiWorkbenchState.cs:19`、`UiWorkbenchPageSection.cs:180-213`），保存不校验磁盘版本（`:215-243`），存在被外部窗口（登记）写入后又被工作台陈旧内存覆盖的风险。
- `UiPageManifestStore.Save` 直接 `File.WriteAllText` 后 `ImportAsset`（`UiPageManifestJson.cs:42-54`），不是原子替换。

### 要解决的问题

1. 美术无法独立起页：现有唯一路径要么置灰，要么一键写 Lua/表（跨界）。
2. 工程登记没有工具面：只能手工跑 `_uiform_row.py` + `Luban/gen.bat` + 清单对账（`Docs/修改指南.md:198-200`）；且登记门槛所需的“登记前契约”在草稿页上**无法生成**（见现状“契约链缺口”）。
3. 起页产物不符合页面根契约（缺缩放/射线/安全区），且安全区节点可能与内容脱节。
4. 工作台与外部登记窗口之间缺少版本冲突保护。
5. 草稿页缺少契约生成/对账通道，美术无法在交接前把契约作为可评审产物。

### 目标（可观察）

- 美术在工作台页面透视点「起页」→ 窗口输入页名/基线 → `Assets/ShareResource/UI/Screens/Page_*.prefab`（自包含、根契约齐备、内容在安全区容器内）+ 页面清单 `Draft` 条目；**不写 Lua、不写表**。
- 美术对草稿页生成**草稿契约**（`formId=0`）并跑 diff/评审；工程登记前必须看到该契约。
- 工程选中草稿页点「登记」→ 窗口填 id/layer/全屏/缓存策略 → 校验契约存在 → 写 `#uiform.xlsx`（带备份）→ 生成 Lua 骨架 → Luban + BuildHash 复算 → 清单 `Registered`（含 formId 与所选缓存策略）→ 若挂 `BindRoot` 才记录绑定 hash。
- 两个动作按 Lane 门控（美术起页、工程登记），并作为 `ui.page-scaffold` / `ui.page-register` 进入 Hub/CLI 工具清单。
- 失败显式、可核验、可恢复：起页失败不留半成品 prefab/清单；登记失败按“表行核验 → 回滚 → 复核”处理，绝不静默。

## 需求澄清与假设

- **用户已确认（2026-10-23）**：
  1. 登记范围取 **B**——窗口提供缓存策略选择（Resident/Lru/DestroyOnClose），写入 `UiPageManifest.cacheStrategy` 并让对账保留该维护字段；`tbuiform` 仍无缓存列，运行时暂不消费；**C# 骨架生成不在本批**（作为明确边界写入设计与进度）。
  2. 契约门槛取 **A**——本批补“草稿契约”生成链：schema 允许 `formId=0` 表示草稿，`ui.contract` 批量与工作台可对未登记页生成/对账契约，登记时重生成填真实 id；不把门槛降级、不删设计输入。
- 采用假设：
  1. **逻辑语言**：本批登记只写 Lua 骨架；C# 路径 B 需要 prefab 先挂 `BindRoot` 并配 ScriptName/Folder，属 §3.7 接线前置，本批窗口显示“Lua（C# 归接线/后续批次）”并拒绝 C# 输入。
  2. **页面类型/目标模块**：页面类型由登记时的 `layer` + `full_screen` 表达（HUD 属独立轨、不走 `tbuiform`）；目标模块不入表（资源按目录/收集组组织），二者都写入设计与进度的“边界与遗留”。
  3. **契约门槛**：登记要求**草稿契约**存在且可读（`page.name` 匹配、`formId=0`）；登记成功后重生成契约填真实 formId。契约评审签字无机器信号，如实记为人工门（G2），工具把“无契约不可登记”机检化。
  4. **草稿契约口径**：`formId=0`、`luaPath=null`、`layer`/`fullScreen` 取**单源常量** `UiContractGenerator.DraftLayer`（0）/`DraftFullScreen`（true），`cacheStrategy` 取清单值；登记后重生成覆盖为真实身份。`sourceHash` 不含页面元数据，表单身份变化不产生接口漂移。
  5. **基线集合**：设计 §3.1 的 `Tmp_BaselineA/B`、`Tmp_ListScreen` + 编辑器空白骨架 `Tmp_Page`；新增基线＝`Baselines` 加一行，不做目录扫描（避免误收系统面）。
  6. **工作流配置**：项目 `.workflow/config.json` 是 8 阶段变体（缺 `review-plan`/`confirm-acceptance`）。并行 TPS 运行已处于 `implement`，插入前置阶段会让其归档完整性校验失败；本批沿用 8 阶段配置，但独立方案/代码评审与用户验收确认仍按 staged-delivery 契约手工执行并落盘，待并行线归档后再迁移配置。
- 假设影响与风险：C# 页面在本批后仍需手工接线；缓存策略只进清单与契约，不改变运行时缓存行为（`tbuiform` 无列）。

## 范围与非目标

- 范围内：
  1. 内核拆分：`UiPageScaffold.Scaffold`（仅 prefab + 草稿 + 根契约），`UiPageRegistration.Register`（契约门槛/表/Lua/Luban+BuildHash/清单/状态/缓存策略/hash），移除一键 `Create`。
  2. 表读面/写面单源 `UiPageTableHelper`（`UiPageTableRow`、list/suggest/check/write/rollback/delete），`--table` 与备份目录随表隔离；`UiPageToolProcess` 统一进程调用。
  3. **草稿契约链**：schema `formId` 允许 0（草稿）；`UiPageMetadata.IsDraft` + `UiContractGenerator` 草稿口径；`UiContractBatch.Generate` 纳入清单草稿页（`forms` 空）；`Regenerate(entry, allowDraft)` 供漂移规则复用；`UiContractBatch.Diff` 覆盖草稿契约；清单对账刷新草稿 `ContractPresent`。
  4. 页面清单：`UiPageManifestStore.Save` 原子替换；`cacheStrategy` 作为维护字段（编辑 API + 对账保留 + 契约投影一致）；`UiPageManifestEditor.SetCacheStrategy`。
  5. 起页根契约补齐 `EnsureRootContract`（模板与起页共用；安全区优先挂现有 `Cot_*` 拉伸内容容器）。
  6. 两个纯 IMGUI 窗口 + 工作台按钮接线、Lane/阶段门控、焦点重载与保存冲突保护、透视能力标签补 `page.scaffold`/`page.register`。
  7. 工具清单登记（`EditorToolHandlers` + `toolchain.json` + 工具索引重生成）。
  8. EditMode 夹具、Python 助手单测、文档回写（08 手册、修改指南、技术总结 04、设计 §3.1/§3.5/§3.6/§5.2/§9、施工进度第 9 章）。
- 非目标：
  - C# 骨架生成、绑定代码生成（`GenerateBinding` 仍为门控位）。
  - 设备档/预览矩阵、样式令牌（P2-3）。
  - 运行时缓存策略消费（等 `tbuiform` 列）。
  - 不改 `UIService`/`UIForm`/导航等运行时基座；不改 `tbuiform` 列结构。
  - 不迁移工作流配置（见假设 6）。
  - 不做契约评审流程本身（人工 G2 门；工具只保证“无草稿契约不可登记”）。

## 现状与约束

- 相关代码/模块：
  - `Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（一键建页、表读面、进程调用、Reconcile）
  - `UiPageManifestEditor.cs`、`UiPageManifestSync.cs`、`UiPageManifest.cs`、`UiPageManifestJson.cs`、`UiPageStatus.cs`
  - `Workbench/UiWorkbenchPageSection.cs`、`UiWorkbenchWindow.cs`、`UiWorkbenchAvailability.cs`、`UiLanePolicy.cs`、`UiWorkbenchState.cs`、`UiWorkbenchPerspective.cs`
  - `Contract/UiPageContract.cs`、`UiContractGenerator.cs`、`UiContractBatch.cs`、`UiContractDiff.cs`（`cacheStrategy` 投影、草稿契约）
  - `Docs/设计文档/客户端/UI系统/UiPageContract.schema.json`（`formId` 允许 0 表示草稿）
  - `Binding/UiBindGenerationHash.cs`（`Record(prefabPath, manifestPath)` 可注入）
  - `Toolchain/EditorToolHandlers.cs`、`scripts/gate/toolchain.json`
  - `scripts/codegen/_uiform_row.py`、`scripts/codegen/gen-build-hash.py`（工具 id `codegen.build-hash`）
  - 夹具：`UiWorkbenchEditModeTests`、`UiPreviewEditModeTests`、`UiToolchainWiringEditModeTests`、`UiPageManifestEditModeTests`、`UiContractReconciliationEditModeTests`
- 约束：
  - **Unity 序列化资产**（prefab 创建/改名/组件）只经 Unity API（`PrefabUtility`/`AssetDatabase`），禁直改文件。
  - **门控单源**：可点性＝`UiLanePolicy` + `UiWorkbenchAvailability`；窗口内禁止散落 `if (lane)`。
  - **禁硬编码**：基线集合、根契约参数、缓存策略词表（`UICacheStrategy`）、保留 id、工具 id 均单源；表路径/命令来自常量与 `toolchain.json`。
  - **禁新增 public static 可变状态**：窗口/内核不引入静态事件/缓存；跨窗口同步靠清单文件内容指纹。
  - **错误契约**：失败显式（`Ok=false` + `FailedAt` + `Error` + 恢复指令），不静默成功。
  - 测试禁止触碰真实 `#uiform.xlsx`、真实 `UiPageManifest.json`、真实 `UiBindManifest.json`、产品 Lua 目录与真实契约（临时契约文件用后即删）：全部经注入路径/临时表。
  - 夹具必须先红后绿，且红必须是断言失败（先落可编译骨架，不制造编译错误）；负例断言具体 `FailedAt`/错误文本/字节不变，禁止“骨架恒失败”式的假绿。
  - 收尾跑 `python scripts/gate/test.py --lane L1 --profile PullRequest`、`doc-lint`、`toolchain.py check`、`docs --check`、UI EditMode 回归、`gen-build-hash.py --check-shared` 与 `wf verify`。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A | 内核拆分 + 两个 window（`ui.page-scaffold`/`ui.page-register`），工作台按钮打开并预填上下文 | 与设计 §9.1/§3.1/§3.6 及 08 手册一致；核心纯 C# 可直测；Hub/CLI 可达；职责单一 | 工作台与外窗需版本冲突保护（已有明确设计）；窗口不可点测 | **推荐** |
| B | 工作台内联表单 + 两个 action 工具 | 单一窗口、上下文天然共享 | 与设计 window 形态不符；CLI 无法交互输入；页面透视膨胀 | 不取 |
| C | 单窗口两页签（起页/登记合一） | 入口最少 | 破坏“美术/工程拆分”可观察边界；工具 id 与设计不符 | 不取 |

**入口口径（修正）**：工作台是**唯一编排/工作台入口**；`ui.page-scaffold`/`ui.page-register` 是设计 §9.1 定义的**独立窗口工具**，允许从 Hub/CLI 直开（直开不受工作台 Lane 限制，但工具本身职责单一：起页只建 prefab、登记只做登记）。08 手册与设计同步改写为这一口径。

## 设计要点

### 1) 模块与职责

| 件 | 职责 |
|---|---|
| `Page/UiPageTableRow.cs`（新） | `tbuiform` 行投影 DTO（从 `UiPageScaffold.PageRow` 迁出，字段不变） |
| `Page/UiPageTableHelper.cs`（新） | `#uiform.xlsx` 读写唯一门面 + `IUiPageTablePort` 默认实现：`TryListPages/ListPages/SuggestId/Check/Write/Rollback/Delete/TryFindRow`；`Write` 返回 `{Ok, BackupPath, Error}`（**备份先于写，失败也回传句柄**）；`--table`/备份目录注入 |
| `Page/UiPageRegistration` 端口 | `IUiPageTablePort`（默认 `UiPageTableHelper`）：测试可注入“已落行但返回失败/超时”的假表；`IUiPageContractRefresher`（默认 `UiContractBatch.Regenerate` + `UiContractStore.Save`）：测试可注入刷新失败 |
| `Page/UiPageToolProcess.cs`（新，internal） | python/工具命令执行、输出解码、引号与 JSON 提取（从 `UiPageScaffold` 抽出） |
| `Page/UiPageCodegen.cs`（新） | 代码生成链端口：`IUiPageCodegenPipeline { RunLuban(); RefreshBuildHash(); }` + 默认实现（`codegen.luban` → `codegen.build-hash`） |
| `Page/UiPageScaffold.cs`（重构） | 起页内核：基线表、`EnsureRootContract`、`Scaffold(...)`；保留 `IsValidPageName`/`LuaDir`/`TemplatePath`/`EnsureTemplate`；删除 `Create` |
| `Page/UiPageRegistration.cs`（新） | 登记内核：`Register(UiPageRegisterRequest)` |
| `Page/UiPageScaffoldWindow.cs` / `UiPageRegisterWindow.cs`（新） | 纯 IMGUI 输入与结果呈现；`Open(string prefill = null)` 与无参 `Open()` |
| `UiPageManifestSync.ReconcileToDisk(...)`（移动） | 由 `UiPageScaffold.ReconcilePageManifest` 迁入；两处调用点同步改指 |
| `Page/UiPageManifestJson.cs` | `Save` 改临时文件 + 原子替换；失败清理临时件 |
| `Page/UiPageManifestEditor.cs` | 新增 `SetCacheStrategy`（词表单源 `UICacheStrategy`） |
| `Page/UiPageManifestSync.cs` | `cacheStrategy` 改为保留维护字段（空/非法才回落默认并告警） |
| `Contract/UiContractGenerator.cs` | `ApplyManifestEditorial` 增加 `cacheStrategy` 投影；新增草稿口径（`UiPageMetadata.IsDraft`，`formId=0`）与单源常量 `DraftLayer=0`/`DraftFullScreen=true` |
| `Contract/UiContractBatch.cs` | `Generate` 纳入清单草稿页；`Regenerate(entry, allowDraft)`；`Diff` 覆盖草稿契约；报告区分草稿数 |
| `Contract/UiContractJson.cs` | `UiContractStore.Save` 临时文件+原子替换；schema `formId` 最小值改 0（草稿），设计 §5.2 同步 |
| `Contract/UiContractDiff.cs` | 页面段破坏性口径：`formId 0→真实` 非破坏；`layer/fullScreen/cacheStrategy` 变化非破坏；`luaPath null→路径` 非破坏 |
| `Validation/Rules/ContractDriftRule.cs` | 草稿契约走 `Regenerate(allowDraft:true)`，漂移仍判 Error（登记前必须重生成） |
| `Workbench/UiWorkbenchPageSection.cs` | 起页/登记按钮触发事件；输入变化即标脏；保存前磁盘指纹校验；`ReloadIfChanged` 保留选中与目标 |
| `Workbench/UiWorkbenchWindow.cs` | 订阅事件 → 打开窗口（登记预填选中页）；`OnFocus` 调 `ReloadIfChanged` |
| `Workbench/UiWorkbenchAvailability.cs` | 清空 Pending（起页/登记已实现），保留阶段层扩展点 |
| `Workbench/UiWorkbenchPerspective.cs` | 页面透视能力标签补 `page.scaffold`/`page.register` |
| `Toolchain/EditorToolHandlers.cs` | 注册两个 window handler |
| `scripts/gate/toolchain.json` | 两条 window 工具 + 写面/产物/菜单/docs |
| `scripts/codegen/_uiform_row.py` | `--table` 覆盖全部分支、备份目录随表、`layer` 0–3、`Page_` PascalCase |

### 2) 草稿契约链（新增，用户裁决 A）

1. **schema/模型**：`UiPageContract.schema.json` 的 `page.formId` 改为 `minimum: 0`，描述“0=草稿（未登记）；登记后重生成填真实 id”；`UiPageMetadata` 增 `IsDraft`（默认 false），`UiContractGenerator` 在 `IsDraft` 时允许 `formId=0`，否则维持“必须正整数”。
2. **草稿生成**：`UiContractBatch.Generate(outputDir, manifest)`（新增 manifest 注入重载；null＝读默认清单）在既有“按表行生成”之后，追加清单中 `forms` 为空、`status=Draft`、prefab 存在的 `Page_*` 条目：`formId=0`、`luaPath=null`、`layer=UiContractGenerator.DraftLayer`（0）、`fullScreen=UiContractGenerator.DraftFullScreen`（true）、`cacheStrategy`/`owner`/`designRef` 取清单；同一页面已在表行集合中则不重复。缺清单时**显式 Warnings**（“清单缺失，草稿契约未纳入”）。`UiContractBatchEntry` 增 `Draft` 标记，报告显示“草稿 N”。`Diff(baselineDir, manifest)` 同口径覆盖草稿。
3. **对账**：`UiContractBatch.Regenerate(entry, allowDraft)`——草稿走草稿生成，登记页走真实身份；`ContractDriftRule` 对草稿调用 `allowDraft:true`，仍以逐字节比较判 Error（布局改动后必须重生成契约）。`Diff` 对草稿契约同样比较；**页面段破坏性口径修正**（草稿→登记不应被误判）：`formId 0→真实` 非破坏、真实→真实破坏；`luaPath null→路径` 非破坏、路径→null/换路径破坏；`layer`/`fullScreen`/`cacheStrategy` 视为运行时/维护元数据，变化非破坏（仍进 Changed 报告）；`owner`/`designRef` 维持非破坏。夹具覆盖 `formId 0→真实非破坏` 与 `cacheStrategy 变化非破坏`。
4. **清单**：`UiPageManifestSync` 为保留的草稿条目刷新 `contractPath`/`contractPresent`（文件存在即真）——否则草稿契约永远不被漂移规则看到，登记门槛也无从判定。
5. **工作台**：契约透视的“生成”复用批量入口，报告新增“草稿 N”行；美术在起页后可对草稿页生成契约、跑 diff/评审，再交工程登记。
6. **登记联动**：登记成功（表行 + 清单状态）后按新条目重生成契约（真实 `formId`/layer/fullScreen/luaPath），写盘；失败显式报“表单已登记但契约未刷新，重跑 ui.contract”。

### 3) 起页流程（`UiPageScaffold.Scaffold`）

1. 校验：页名 `Page_<PascalCase>`；基线存在（默认首个可用）；`manifestPath` 在 `Assets/` 下；目标 prefab 不存在；清单同名条目若已登记（`forms` 非空）→ 拒绝。
2. 复制基线为自包含 prefab（`AssetDatabase.CopyAsset`，非 Variant）；**整体 try/finally**：此后任一步失败都删除新建 prefab，并把清单恢复到操作前字节（原文件写入或删除新建）。
3. 改名根名与文件名一致 → `EnsureRootContract`：
   - 四件套缺则补：`Canvas`（保留现渲染/排序字段）、`CanvasScaler`（`ScaleWithScreenSize`，1920×1080，`MatchWidthOrHeight=0.5`）、`GraphicRaycaster`、`CanvasGroup`（不重置 alpha/interactable）。
   - 安全区（内容归属优先）：已有 `SafeAreaReceiver` → 保留；否则若存在**直接子**拉伸 `Cot_*` 容器（`anchorMin≈0/ anchorMax≈1`）→ 在该容器挂 `SafeAreaReceiver`（`Tmp_BaselineA/B` → `Cot_Baseline`）；否则新建 `Cot_SafeArea`（拉伸）并把原有子节点按序移入（`Tmp_ListScreen` → `VirtualList_Rooms`）。
4. 清单：`LoadIfExists` → `EnsureDraft(prefab, 美术 owner)` → 原子保存；失败按第 2 步清理。
5. 可选打开 Prefab Stage；返回 `ScaffoldResult{Ok, PrefabPath, ManifestPath, Entry, Log}`。不碰 Lua/表/Luban。

### 4) 登记流程（`UiPageRegistration.Register`）

输入 `UiPageRegisterRequest`：`PageName`、`Id`、`Layer(0–3)`、`FullScreen`、`CacheStrategy`（默认 `Lru`）、`ManifestPath`、`LuaDirectory`、`TablePath`、`BindManifestPath`、`Codegen`（`IUiPageCodegenPipeline`）、`Table`（`IUiPageTablePort`，默认生产门面）、`ContractRefresher`（`IUiPageContractRefresher`，默认批量重生成+写盘）。

1. **注入一致性守卫**：`TablePath` 非空而 `Codegen` 为空 → 直接拒绝（真实 Luban 永远读生产表，混用会造成测试表与生产生成物错位）。
2. **预检（全在写面前）**：
   - 页名合法；prefab 在 `Screens/<page>.prefab` 存在；
   - 清单可读，条目存在且 `status=Draft`、`forms` 为空（否则“先起页”/“已登记”）；
   - **契约门槛**：`UiContractStore.Load(UiContractPaths.ForPage(page))` 成功、`page.name == pageName`、`page.formId == 0`、**`page.prefabGuid == prefab 当前 GUID`**（防同名旧契约）；无契约/不可读/已是真实 id/guid 不符 → 拒绝（提示“先生成草稿契约”或“该页已登记过”）；
   - id>0 且不在保留区、layer 0–3、`Table.Check` 通过；缓存策略在 `UICacheStrategy` 词表内。
3. Lua 骨架：目标不存在则写（UTF-8 无 BOM，`class/_viewName/OnShow/OnHide`）；已存在则保留并记录（不覆盖作者逻辑）。
4. 写表：`Table.Write`（**先备份后写**，返回 `{Ok, BackupPath, Error}`，失败也回传备份句柄）→ 失败时 `Table.TryFindRow` 核验——若进程已落行（含超时/假端口“已落行但报失败”）则 `Table.Rollback(backup)` 并再次核验；**复核仍有行＝回滚失败**，结果给“按备份手动恢复 <path>”指令，不声称已恢复；删除本次新建 Lua（若为本次创建）；返回 `FailedAt.WriteTable` 与核验结果。
5. 代码生成链：`Codegen.RunLuban()` → `Codegen.RefreshBuildHash()`（默认 `codegen.build-hash`，红线 1）。失败处理：
   - `Table.Rollback(backup)` + 核验行不存在；再 `RunLuban()` 复原生成读码面（尽力而为，结果记日志）；
   - 保留 prefab/Lua，清单仍 `Draft`；返回失败与恢复指令（含备份路径、两次生成结果）。
6. 清单：`Table.TryListPages` → `UiPageManifestSync.Sync(rows, existing)` → `SetCacheStrategy` → `SetStatus(Registered)` → 原子 `Save`。失败：显式错误 + “表/生成物已提交，重跑 `ui.page-manifest`”（不回滚表，避免生成物与表再次错位）。
7. 契约刷新：`ContractRefresher.TryRefresh(entry)`（默认 `UiContractBatch.Regenerate` + `UiContractStore.Save` 原子写；真实 formId/layer/fullScreen/luaPath/cacheStrategy）；失败：`FailedAt.ContractRefresh`、`Ok=false`，清单已 Registered，显式报“表单已登记但契约未刷新，重跑 ui.contract”（`ui.contract.drift` 会继续报红，不静默）。
8. 绑定 hash：**仅当** prefab 根挂 `BindRoot` 时 `UiBindGenerationHash.Record(prefab, bindManifestPath)`；无 `BindRoot`（路径 A）跳过并记录“不伪造 hash”（08 §6.2）。记录异常 → Warning，不判登记失败。
9. 返回 `RegisterResult{Ok, FormId, LuaPath, BackupPath, Status, CacheStrategy, Warnings, Log, FailedAt}`。

**重试语义**：预检失败可在修复后重跑；写表失败已回滚，id 可复用；Luban 失败清单仍 Draft；成功后重跑被“已登记”拒绝（不重复加行）。

### 5) 页面清单与缓存策略

- `Save` 原子化：同目录写 `.tmp` → `File.Replace`（无目标则 `File.Move`）→ `ImportAsset`；异常删除临时件。
- `Sync`：`cacheStrategy` 视为维护字段——条目已有非空合法值则保留；空/非法 → 默认 `Lru` + `Warnings`（不再无条件重置）。
- `UiPageManifestEditor.SetCacheStrategy`：按 `UICacheStrategy` 词表校验，成功重算 `sourceHash`。
- `ApplyManifestEditorial` 投影 `cacheStrategy`（批量生成与漂移重生成同口径，避免 `ui.contract.drift` 假红）。
- 工作台页面透视增加缓存策略下拉（维护字段，与 Owner/状态同权限）；登记窗口同字段。

### 6) 窗口与工作台接线

- 两窗口纯输入/呈现；`Open()` 与 `Open(prefill)` 两个入口（handler 注册无参重载）。
- 工作台页面透视按钮：`canRun` 为真时可点 → 触发 `ScaffoldRequested`/`RegisterRequested`；工作台打开窗口并预填（登记传当前选中页）。
- **版本冲突保护**：
  - 任一维护字段输入变化（`EditorGUI.BeginChangeCheck`）即 `MarkDirty`（未 Apply 的文本也算未保存）；
  - `Reload`/`ReloadIfChanged` 保留选中页与目标；记录加载时的磁盘 `sourceHash`；
  - `TrySave` 前比较磁盘 `sourceHash`：不等则**拒绝保存**并提示“清单已被外部修改，先刷新”，不覆盖；外部变更存在未保存编辑时 `ReloadIfChanged` 不重载，只置 `ExternalChanged` 提示。
- Lane 矩阵不变；登记窗口列出草稿页（`forms` 空且 `Draft`），无草稿时禁用并给“先起页”。

### 7) 失败与边界（可核验）

| 场景 | 结果与核验 |
|---|---|
| 起页 prefab 已存在/已登记 | 预检失败，不复制、不写清单 |
| 起页中任一写步失败 | 删除新建 prefab；清单恢复操作前字节；结果含失败步与两条路径 |
| 登记无草稿契约/契约不可读/formId≠0 | 预检失败，不建 Lua、不写表 |
| 登记无草稿/已登记/id 冲突/保留 id/layer 越界 | 预检失败，零产物 |
| 表写失败（含超时后可能已落行） | `TryFindRow` 核验 → 有行则回滚 + 复核；无行则只删本次 Lua |
| Luban/BuildHash 失败 | 回滚表 + 复核 → 重跑 Luban 复原生成物；清单仍 Draft；结果含两次结果与备份路径 |
| 清单保存失败（表已写） | 显式错误 + 重跑 `ui.page-manifest` 指令；不回滚表 |
| 契约刷新失败（表/清单已登记） | 显式错误 + 重跑 `ui.contract`；`ui.contract.drift` 继续报红，不静默 |
| 草稿契约漂移（改布局未重生成） | `ui.contract.drift` 判 Error（草稿也要求契约与 prefab 一致） |
| 缓存策略非法 | 预检失败（词表单源） |
| 工作台磁盘被外改 | 保存拒绝、提示刷新；外部变更未保存时不自动重载 |

## 通用性与扩展性

- **根因**：P2-1 把两个角色动作压成“未建”门控；本批拆内核与入口，不是给按钮加特判。
- **单源复用**：表读写只经 `UiPageTableHelper`；根契约只经 `EnsureRootContract`；缓存策略词表只经 `UICacheStrategy`；状态迁移只经 `UiPageManifestEditor`；Lane/阶段只经既有门控表；代码生成只经 `IUiPageCodegenPipeline`。
- **扩展点**：新增基线＝`Baselines` 加一行；新增语言/骨架＝登记请求的产物段（C# 本批显式拒绝并说明）；新增生成步骤＝pipeline 加一个方法；新增阶段＝`UiWorkbenchAvailability` 表项；新增契约阶段（草稿→登记）＝`UiPageMetadata.IsDraft` 与 `Regenerate(allowDraft)`；新增测试面＝路径注入，不复写写面。
- **PageRow 迁移表（破坏性编辑器 API 变更，随批全量改指）**：
  - `UiPageManifestSync.cs:40,47,67,69,84,92,96`
  - `UiContractGenerator.cs:131,139,142,152`
  - `UiContractBatch.cs:108,201,215,257,270,334-345`
  - `UiPageManifestEditModeTests.cs:59-61`
  - `UiContractReconciliationEditModeTests.cs:215`
  - 其余 `UiPageScaffold.LuaDir`/`TemplatePath`/`IsValidPageName` 保持不变。
- **避免硬编码/特判**：不按页名分支；保留 id 区间只由 Python 助手；表路径/命令在常量与 `toolchain.json`；窗口不拼 python 命令。
- **不新增静态可变状态**：跨窗口同步靠清单内容指纹（`sourceHash`）。

## 测试策略

### 层级

| 层 | 内容 |
|---|---|
| Python 单测 | 新增 `scripts/tests/test_uiform_row.py`：`--table` 副本上的 list/check/confirm/rollback/delete；真实表字节不变；layer 越界与 `Page_login` 负例 |
| EditMode（新） | `UiPageScaffoldRegisterEditModeTests`：起页正/负例与安全区归属；**草稿契约生成（批量含草稿、formId=0、确定性）**；草稿契约漂移与登记后刷新；契约门槛；缓存策略保留与契约投影；表写/Luban/BuildHash 失败注入与回滚复核；注入一致性守卫；工作台能力/事件/可用性/保存冲突/焦点重载 |
| EditMode（回归） | `UiWorkbench`（可用性/能力标签断言改绿）、`UiPreview`（新工具在册）、`UiPageManifest`、`UiValidation`、`UiContract*`（草稿契约纳入、批量 11+草稿、diff 基线不变、缓存策略投影不产生漂移）、`UiToolchainWiring`（`ReconcileToDisk` 新址）、`UiUimainRetirement`、`UiBindingTools` |
| 冒烟 | `unity command eval_file` 打开两窗口，断言标题/关键字段/Console 0 error；真实点击流程人工确认（红线 13，如实记录“自动只证窗口对象，不证可点”） |

### 故障注入

- `IUiPageCodegenPipeline` 假实现：可配置 Luban/BuildHash 成功或失败、记录调用序列（断言“Luban 后跑 BuildHash”“失败后重跑 Luban”）。
- `IUiPageTablePort` 假实现：包装真实临时表门面，可配置“先真的落行、再返回失败/超时”，用于确定性触发 `TryFindRow → Rollback → 复核`；也可配置“未落行返回失败”。
- `IUiPageContractRefresher` 假实现：可配置刷新失败，验证“表单已登记但契约未刷新”的显式失败路径。
- 表：复制真实 `#uiform.xlsx` 到 `Temp/ui-register-tests/`，`--table` 指副本；备份目录随表生成，结束清理。
- 清单写盘失败：目标父路径放一个同名文件，使原子写建目录/替换失败；断言 prefab 已删、清单字节不变。
- 清单外改冲突：临时清单 `Load` 后 `SetEditorial` + `Save` 模拟外部写；断言工作台保存被拒且磁盘不变。
- 所有负例断言 `FailedAt` 与关键错误片段（防止“骨架恒失败”假绿）。

### 先失败后通过

1. 落可编译骨架：`UiPageTableRow/Helper`（先搬既有实现，行为不变）、`Scaffold/Register` 返回显式“P2-2 未实现”（`FailedAt.NotImplemented`）、`Baselines` 空、窗口/工具未登记、Availability 仍挂起、`SetCacheStrategy` 缺失由骨架桩提供。
2. 写全部夹具并运行：正例与“断言具体失败语义”的负例均应**断言失败**（非编译错误、非环境错误），`wf verify --red` 记录。
3. 逐任务实现 → 定向夹具转绿 → 全量回归。

### 安全

- 临时表复制；临时清单/Lua/契约/hash 全部住 `Assets/Tests/TempUiPageSplit/`（契约不进真实 `Contracts/`）；夹具结束断言真实 `Contracts/` 文件清单与字节基线（既有 11 份）不变。
- 前后校验真实 `#uiform.xlsx`、`UiPageManifest.json`、`UiBindManifest.json` 字节一致；测试后临时目录无残留，`Contracts/` 无测试文件。
- 不跑真实 Luban/BuildHash 于测试（注入假 pipeline、假表端口、假契约刷新器）；收尾单独跑 `gen-build-hash.py --check-shared` 证明真实常量未漂移。
- 夹具页名固定且唯一（`Page_TestScaffold*`/`Page_TestRegister*`），`OneTimeTearDown` 删除 `Screens/Page_Test*.prefab`、临时契约与目录，并断言无残留。

## 验收标准

- [ ] AC1 起页：美术 Lane 下工作台「起页」可点（人工确认一次）；输入新页名并选 `Tmp_BaselineA` 后产出 `Screens/Page_*.prefab`（自包含、根 Canvas/CanvasScaler 1920×1080 Match0.5/GraphicRaycaster/CanvasGroup 齐备，**`SafeAreaReceiver` 挂在内容容器 `Cot_Baseline` 上**），页面清单新增 `Draft`（forms 空、owner 美术）；**不产生** Lua、**不修改** `#uiform.xlsx`（夹具以真实表字节比对）。
- [ ] AC2 登记：工程 Lane 选中草稿页、填 id/layer/全屏/缓存策略并登记后——**无草稿契约时被拒**；有草稿契约（`formId=0`）时写 `#uiform.xlsx` 一行且留备份，生成 `Assets/LiteGame/Lua/UI/Page_*.lua` 骨架，代码生成链依次调用 Luban 与 BuildHash（夹具以假 pipeline 断言调用序列；真实链路另以命令级验证），清单 `Registered` 且带 formId、缓存策略保留；**登记后契约重生成，`formId` 变真实 id，缓存策略与清单一致**；无 `BindRoot` 的页面**不写入**绑定 hash 清单。
- [ ] AC3 门控与入口：美术不能登记、工程不能起页（矩阵与原因可测；按钮实际置灰人工确认一次）；起页/登记不再显示“P2-2 未提供”，`UiWorkbenchAvailability` 两项已实现；两工具在 Hub/CLI 清单为 window，工作台为编排入口、两窗口可直开的口径写入 08 与设计。
- [ ] AC4 失败语义：起页目标已存在或写盘失败时无残留 prefab、清单字节不变；登记无草稿契约/guid 不符/无草稿/id 冲突/保留 id/layer 越界时零产物；**通过 `IUiPageTablePort` 假实现触发“已落行但报失败/超时”，登记必须核验后回滚并复核**；Luban 失败回滚表行、重跑 Luban、清单仍 `Draft`；BuildHash 失败同样回滚并复核；**`IUiPageContractRefresher` 注入失败时显式报红**；工作台在磁盘外改时拒绝保存。以上均有夹具断言具体 `FailedAt` 与磁盘状态。
- [ ] AC5 工具面：`ui.page-scaffold`、`ui.page-register` 在 Catalog 无 Error；`toolchain.py check` 0 error；`工具索引.md` 重生成且 `docs --check` 通过。
- [ ] AC6 测试与回归：新增 EditMode 夹具与 Python 单测全绿；**草稿契约生成/漂移/登记刷新用例全绿，真实契约基线 11 份不变**；既有 UI 夹具、L1、Doc、toolchain、`wf verify` 全绿；测试后真实表/清单/hash/契约零残留、真实 BuildHash `--check-shared` 通过、Validation 302 基线（11 页/12 行）不变。
- [ ] AC7 文档：08 手册（§1 入口、§3 起页与草稿契约、§5 登记、§6 hash、§9 排障）、修改指南工具表、技术总结 04、设计 §3.1/§3.5/§3.6/§5.2/§9.1/§9.2、`UiPageContract.schema.json`（formId 0）、施工进度第 9 章与工具索引同步；`doc-lint` 0/0。
- [ ] AC8 冒烟：`eval_file` 打开起页/登记窗口，断言标题、关键控件存在、Console 0 error；真实点击/输入流程人工确认一次并记入 build-log（不能自动化的部分如实登记，不写成可自动判定）。

## 风险与回滚

- **风险 1：工作台与外窗陈旧覆盖** → 磁盘 `sourceHash` 冲突拒绝保存 + 外部变更提示 + 保留选中重载；夹具覆盖两条路径。
- **风险 2：测试误写生产数据** → 表/清单/Lua/hash/契约全注入；真实表/清单/hash 字节比对；临时表备份目录隔离。
- **风险 3：安全区只补壳、内容不在容器内** → 优先挂现有 `Cot_*` 拉伸容器（BaselineA/B），无容器时新建并搬入子节点（ListScreen）；夹具断言内容节点是 Receiver 节点的后代。
- **风险 4：Luban/BuildHash 失败留下表与生成物错位** → 回滚表 + 复核 + 重跑 Luban 复原；两次结果与备份路径进结果；残余（进程被杀且备份不可用）显式报告，不静默。
- **风险 5：清单/契约缓存策略不一致造成假漂移** → 对账保留 + 契约投影同源；夹具覆盖。
- **风险 6：`formId=0` 草稿契约与既有契约消费面不兼容** → 只放宽 schema 最小值并把草稿处理收口在批量/漂移/登记三处；既有 11 份契约逐字节不变（基线断言）；`UiContractDiff` 的页面段破坏性口径按草稿晋升修正（0→真实/缓存策略/层级非破坏），并补 diff 夹具。
- **风险 7：同名旧草稿契约通过门槛** → 预检比对 `page.prefabGuid` 与当前 prefab GUID；漂移规则仍在事后兜底。
- **风险 8：契约文件原子性/清单导入分支** → `UiContractStore.Save` 同样临时文件+替换；清单原子写保留原 `AssetDatabase.IsValidFolder`/`Refresh` 分支，避免未知目录首次写盘导入失败；`File.Replace` 后 `ImportAsset` 失败按“盘已新、报失败”显式记录（可重跑导入）。
- **监控/发现**：新增夹具；`ui.contract.diff` 真实基线 11 份零破坏；`toolchain.py check`/`docs --check`；`gen-build-hash.py --check-shared`；Validation 302 基线。
- **回滚**：本批修改现有文件与删除 `Create`、迁移 `PageRow`，**不是纯增量**——回滚＝revert 本批提交；工具清单条目可独立摘除；注册工具失败时的操作级恢复由备份/复核路径保证。
