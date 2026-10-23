# UI 系统 P1-5/P1-6：Hub/CLI 接线与试点页静态文案

> 运行：`ui-p1-hub-pilot`（2026-10-23）
> 设计依据：[UI 编辑器工具链专项设计](../../../Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md) §5/§6/§8.3/§9 · [UI 系统总设计](../../../Docs/设计文档/客户端/UI系统/UI系统总设计.md) §12 · [UI 美术协作与交接规范](../../../Docs/设计文档/客户端/UI系统/UI美术协作与交接规范.md) §3

## 背景与目标

P1-0～P1-4 已落地并提交（`e3f6888e` / `2389f804` / `249714e3` / `3e40b33e`）：页面契约内核、静态文案组件、`WidgetSpec`/控件清单、页面清单、校验规则族与例外登记。P1 的收口项只剩：

- **P1-5 Hub/CLI 接线**：契约、契约 diff、控件清单、页面清单四类能力只有 C# API，没有工具入口；`ui.validate` 是唯一已登记的 UI 动作。
- **P1-6 试点页挂 key**：`LTextKeyRef` 已在运行期与编辑器面就位，但真实页面 0 挂载，契约 `text[]` 恒空。

可观察目标（全部可用命令/运行态验证）：

1. `scripts/gate/toolchain.json` 登记四个新动作，Hub 目录可打开并执行；`python scripts/toolchain.py check`、`docs --check` 通过，无孤儿 handler。
2. 四个动作各自输出可读摘要：生成数量 / 跳过项 / 错误项 / 破坏性变更。
3. 起页工具（`UiPageScaffold.Create`）产页成功后，页面清单自动对账落盘；失败显式记录，不静默。
4. 试点页 `Page_Login` 的三个纯静态文案节点由 `LTextKeyRef` 声明，Lua 不再动态写；契约 `text[]` 产出 3 条，运行期中/英切换正确。
5. 生成物基线入库：11 个页面契约 + 控件清单 + 页面清单（`contractPresent=true`），`ui.contract-diff` 具备可比基线。

## 需求澄清与假设

已确认（来自设计文档与施工记录）：

- P1 exit＝「美术可生成契约；工程可消费；CI 能红；正负例齐」（总设计 §12）。
- 工具入口一律走 Hub/CLI，manifest 是唯一注册源（工具链 §9）。
- 页面契约按**主表单行**生成，多行登记差异由页面清单 `forms[]` 承担（§8.5，审计 L251）。
- 静态文案只存 key，参数/复数归动态文案；同节点只允许一个文案写者（§5.2）。

本批采用假设（写入方案并承担影响）：

| 假设 | 影响 | 若不成立 |
| --- | --- | --- |
| `UIMain` 是常驻壳，不产页面契约（根名不符 `Page_/Hud_/Tmp_`） | `ui.contract` 跳过并报显式 Warning，不计失败 | 需要给 UIMain 冻结根契约并放开命名规则——另批 |
| 契约/清单生成物**入库**作为对账基线 | 契约 diff 跨提交可用；页面清单 `contractPresent` 转真 | 不入库则 diff 只有"无基线"提示，CI 无法判破坏性变更 |
| 试点页选 `Page_Login` | 三个纯静态文案（标题/游客进入/设置）迁移；`Txt_Version`/`Txt_Status` 保持 Lua 动态 | `Page_Settings` 全部文案都带参数或状态，无法做到零双写 |
| 起页后做**对账**（不是草稿） | 创建流程已写 `tbuiform` 行，对账得到 `Registered` 与真实 `forms[]` | 若起页改为"只建 prefab 不登记"，同一钩子改用 `EnsureDraft` |

## 范围与非目标

范围内：

- `EditorToolHandlers` 注册四个动作 + `scripts/gate/toolchain.json` 登记 + 工具索引重生成。
- 新增契约批处理服务（生成 / diff），表只读一次、按 prefab 去重、确定性输出。
- 契约生成器补「多行登记提示」与「按行批量」入口（不改单页语义）。
- `UiPageScaffold.Create` 成功后页面清单对账。
- `Page_Login` 三个静态节点挂 `LTextKeyRef`，同步删 Lua 的三处 `SetTextKey`。
- 生成 11 份契约并入库；重新对账页面清单与控件清单。

非目标（本批不做，明确挂账）：

- P2 工作台合并（工作区已有在途未提交改动：`UiComposeWindow`/`UiBindingEditor`/`UiPageWizardWindow`/`Preview`/`Templates`——本批**不碰**）。
- 样式/文本/布局/性能/行为五族校验规则（§6.2 中标"待接入"）。
- 例外登记的版本号到期与评审流程（§6.3 明确 v1.0 不含）。
- HUD/UIMain 根契约冻结（审计 L252）。
- 契约生成期 schema 文法强校验（审计 L248；现存校验规则 `ui.binding-naming` 承担，不在生成器造第二套判定）。
- 控件状态/尺寸的语义一致性校验（审计 L250）。

## 现状与约束

代码事实：

- `EditorToolRegistry` 只允许「清单登记 ⇔ handler 注册」1:1；`EditorToolCatalog` 把注册了但清单没登记视为错误（孤儿 handler）。
- `ToolchainManifestLoader`（C#）与 `_toolchain_manifest.py` 双端校验：`kind=action` 必须 `plane=editor` 且不得声明 `requires`；写入者必须登记 `outputs`；`docs` 路径必须存在。
- CLI 的编辑器工具入口是 `python scripts/toolchain.py open <id>`（经 Hub），动作不另开 CLI 通道。
- `UiContractGenerator` 已有 `Generate`（prefab+metadata）/`GenerateRegistered`（查表）/`GenerateToDisk`；`GenerateRegistered` 每调一次读一次表（≈2s）。
- 生成物口径：固定字段序、LF、UTF-8 无 BOM、无时间戳，重复生成零 diff。
- `UIForm` 已接 `UiStaticTextBinder`：`PrepareForShow` 建绑定器，`ApplyLocale` 在 `OnShow` 前应用，关闭退订。
- `LTextKeyRef` 只存 `Key`；生成器把 `LTextKeyRef` 节点写进契约 `text[]`（`args` 固定 0）。

现状基线（本批探测，未写盘）：13 条登记行 / 12 个唯一 prefab；11 个可生成契约；`UIMain` 报「根名不符 Page_/Hud_/Tmp_」；全部页面 `text[]` 为空。

## 候选方案与取舍

### 方案 A（推荐）：批处理服务 + 薄 handler

新增 `UiContractBatch`（生成/对账两入口，可注入输出目录与行集），四个 handler 只做参数→调用→`ToolResult` 映射。

- 优点：一次读表；可单测（不触碰真实 `Contracts/`）；handler 无业务逻辑；后续工作台透视可直接复用。
- 缺点：新增一个协作件（一类一文件，职责单一）。

### 方案 B：handler 内联循环

handler 直接遍历 `ListPages()` 调 `GenerateRegisteredToDisk`。

- 缺点：12 次读表（≈24s）；无独立测试面；Hub 与未来工作台各写一遍循环。

### 方案 C：CLI 侧 Python 直接生成契约

由 Python 解析 prefab？不可行——契约真相在 Unity 序列化资产，必须经 AssetDatabase。

**取舍**：A。生成器保持单页语义，批处理承担编排；diff 复用既有 `UiContractDiff`。

## 设计要点

### 1) 契约批处理（`UiContractBatch`）

```text
Generate(outputDirectory = null)
  rows = UiPageScaffold.TryListPages()            // 失败 → 显式错误，不伪造空表
  prefabs = rows 按 prefabPath 去重（保序）
  for each prefab:
    root = prefab.name
    若 root 不匹配 Page_|Hud_|Tmp_ → Skipped("根名不在契约面"), Warning
    多行登记 → Warning("多行登记 201/206，契约取首行 201；其余行见页面清单 forms[]")
    Generate(prefabPath, metadata from 首行)
      失败 → Errors（继续下一页，聚合报告）
      成功 → 写 <out>/<Page>.contract.json（确定性）
Diff(outputDirectory = null)
  对每个可生成页：baseline = 磁盘契约（缺失 → Warning「无基线」并跳过）
  current = Generate(...)；UiContractDiff.Compare
  汇总 Breaking / 非破坏变更；Breaking → 失败
```

输出目录默认 `UiContractPaths.ContractDirectory`；测试注入临时目录。**不引入时间戳**，重复执行零 diff。

### 2) 四个工具（handler 薄适配）

| id | 行为 | writes | 失败判据 |
| --- | --- | --- | --- |
| `ui.contract` | 批量生成/刷新全部登记页契约 | asset | 任一页生成失败或表读失败 |
| `ui.contract-diff` | 基线 vs 现生成 | none | 存在破坏性变更；无基线只告警 |
| `ui.widget-manifest` | `UiWidgetManifestGenerator.GenerateToDisk` | asset | 生成失败（负例不写盘） |
| `ui.page-manifest` | `UiPageManifestSync.SyncToDisk` | asset | 表读失败 / 对账错误 |

约定：错误逐条进 `ToolResult.Details`；成功摘要给数量（生成 N / 跳过 M / 告警 K）；`ToolResult.Failed` 只用于「必须修」的情形。

### 3) 起页接线（`UiPageScaffold.Create`）

Luban 重生成成功、`SaveAssets` 之后，调用一次页面清单对账并落盘：

- 成功 → `result.Log` 记「页面清单已对账：N 页」。
- 失败 → `result.Log` 记「页面清单对账失败：…（页面已创建，可重跑 ui.page-manifest）」；**不回滚页面**（清单是注册表，不是页面事务的一部分），也不静默。
- `openStage=false`（测试形态）同样对账（保持两种形态一致）。

### 4) 试点页 `Page_Login`

| 节点 | key | 处理 |
| --- | --- | --- |
| `Txt_Title` | `UI.Login.Title` | 挂 `LTextKeyRef`；删 `Page_Login.lua` 的 `SetTextKey` |
| `Txt_GuestLogin` | `UI.Login.GuestEnter` | 同上 |
| `Txt_Settings` | `Common.Settings` | 同上 |
| `Txt_Version` | `Common.Version` | 保留 Lua（带参） |
| `Txt_Status` | 多变 | 保留 Lua（状态机） |

prefab 改动只走 `PrefabUtility.LoadPrefabContents` → 加组件 → `SaveAsPrefabAsset` → `UnloadPrefabContents`（不直改 YAML）。

### 5) 生成物入库与清单同步

- 生成 11 份契约 → `Assets/LiteGame/Editor/UiTools/Contracts/`。
- 重生成 `UiWidgetManifest.json`（30 控件）与 `UiPageManifest.json`（12 页 / 13 行）→ `contractPresent` 转真。
- 三份生成物入库；手改仍禁（重生成即覆盖）。

## 通用性与扩展性

- **编排与生成分离**：`UiContractBatch` 只编排，遍历/过滤/哈希全部复用 `UiBindSurfaceWalker` 与既有生成器，不产生第二份接口面定义。
- **数据驱动接入**：新增页面只需 `tbuiform` 登记 + prefab，批量生成/对账自动覆盖；没有按页硬编码清单。
- **工具面机制化**：新动作只加「handler + manifest 行」，Hub 侧栏、CLI `open`、文档索引、孤儿检查自动生效（清单是唯一注册源）。
- **失败语义统一**：表读失败、负例、无基线都走显式报告，不伪造空表/空契约。
- **多行登记**：差异统一由页面清单 `forms[]` 表达，契约侧只警告不猜测。

## 测试策略

- **先红**：新增 `UiToolchainWiringEditModeTests`（工具登记面、批量生成、UIMain 跳过、diff 无基线/破坏性、试点契约 text[]），未实现时断言失败（非环境错误）。
- **回归**：`UiContractGeneratorEditModeTests`、`UiContractDiffEditModeTests`、`UiPageManifestEditModeTests`、`UiWidgetManifestEditModeTests`、`UiStaticTextEditModeTests`、`UiValidationRulesEditModeTests`、`EditorToolCatalogEditModeTests`。
- **静态门禁**：`python scripts/toolchain.py check`、`docs --check`、`doc-lint.py`、L1。
- **运行期**：进入 Play Mode，读 `Page_Login` 三个节点的 TMP 文本（zh），切 en（`Bridge.Text.SetLocale`）后复查；同时确认 `Txt_Status`/`Txt_Version` 仍由 Lua 写作。
- 证据落 `evidence/red.json`、`evidence/verify.json`，运行期结论写 `build-log.md`。

## 验收标准

1. 四个动作在 Hub 可打开、可执行；`toolchain.py check` 0 error；`docs --check` 通过；无孤儿 handler。
2. `ui.contract` 产出 11 份契约（UIMain 显式跳过），重复执行零 diff；`ui.contract-diff` 有基线时零变更、无基线时告警。
3. `Page_Login` 契约 `text[]` 恰 3 条（key 与节点如上）；`UiStaticTextEditModeTests` 与新增试点用例全绿。
4. Play 运行期：登录页中文文案正确；切 en 后三节点同步英文；`Txt_Status` 不被静态绑定覆盖。
5. 起页后页面清单自动出现对应条目（测试可用注入路径验证对账调用）。
6. 生成物入库且 `contractPresent=true`；`ui.validate` 零新增 Error。
7. L1 / Doc 全绿；定向 EditMode 夹具全绿。

## 风险与回滚

| 风险 | 缓解 | 回滚 |
| --- | --- | --- |
| 删 Lua 静态写后，语言服务未就绪导致回退作者文本 | `ApplyLocale` 在 `OnShow` 前执行；作者文本即源语言兜底 | 还原 prefab 三节点与 Lua 三行 |
| 批量生成把 `UIMain` 判失败导致工具恒红 | 根名分类 + 显式 Warning，不计失败 | 改回逐页生成（临时） |
| 契约入库后与旧生成器版本漂移 | 生成无时间戳、重复零 diff；diff 工具可判 | 删除 `Contracts/` 重生成 |
| 起页对账写清单带来额外失败面 | 失败只记日志不回滚；清单可重生成 | 删除钩子调用 |
| 工作区已有 P2 在途未提交文件被误提交 | 本批按文件清单显式 `git add`，不 `-A` | `git restore --staged` 指定文件 |
