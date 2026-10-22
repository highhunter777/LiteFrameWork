# 施工记录：UI 系统 P1-3：页面清单 UiPageManifest（工具维护注册表）

> 运行 ui-p1-page-manifest · 创建于 2026-10-22T23:28:19+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-22T23:29:46+08:00 · 新增任务 T1「页面清单模型、状态词表、存储与 schema」
- 2026-10-22T23:29:46+08:00 · 新增任务 T2「对账同步：tbuiform + 已有清单 + 契约存在性」
- 2026-10-22T23:29:53+08:00 · 取消任务 T2：路径应为现有 Page/ 目录而非 Pages/，重建任务
- 2026-10-22T23:29:53+08:00 · 取消任务 T1：路径应为现有 Page/ 目录而非 Pages/，重建任务
- 2026-10-22T23:29:59+08:00 · 新增任务 T3「页面清单模型、状态词表、存储与 schema」
- 2026-10-22T23:29:59+08:00 · 新增任务 T4「对账同步：tbuiform + 已有清单 + 契约存在性」
- 2026-10-22T23:30:07+08:00 · 新增任务 T5「编辑 API：草稿/状态流转/维护字段」
- 2026-10-22T23:30:07+08:00 · 新增任务 T6「13 个真实页面基线落盘」
- 2026-10-22T23:30:18+08:00 · 新增任务 T7「先行测试与负例覆盖」
- 2026-10-22T23:30:18+08:00 · 新增任务 T8「门禁、文档冻结与回写」
- 2026-10-22T23:47:54+08:00 · 开始任务 T3「页面清单模型、状态词表、存储与 schema」
- 2026-10-22T23:47:54+08:00 · 完成任务 T3 · 证据: UiPageManifest/UiPageManifestEntry/UiPageFormEntry/UiPageStatus 落地；Store 走 UiDeterministicJson；UiPageManifest.schema.json；确定性/UiPageManifestHash
- 2026-10-22T23:47:54+08:00 · 开始任务 T4「对账同步：tbuiform + 已有清单 + 契约存在性」
- 2026-10-22T23:47:54+08:00 · 完成任务 T4 · 证据: Sync 按页面(prefab)分组、forms[] 承载多行；保留维护字段、刷新身份字段；重复 formId/非法状态 Error、孤儿 Draft 保留/非 Draft 告警、多行 lua 告警；表读失败经 TryListPages 显式报错；负例不写盘
- 2026-10-22T23:48:01+08:00 · 开始任务 T5「编辑 API：草稿/状态流转/维护字段」
- 2026-10-22T23:48:01+08:00 · 完成任务 T5 · 证据: EnsureDraft 幂等（校验 Assets/ .prefab 存在、Page_ 前缀）；SetStatus 按五态迁移表（同值无操作、非法迁移/未知页报错）；SetEditorial 更新维护字段并重算指纹
- 2026-10-22T23:48:01+08:00 · 开始任务 T6「13 个真实页面基线落盘」
- 2026-10-22T23:48:01+08:00 · 完成任务 T6 · 证据: 真实 tbuiform 生成基线：pages=12 forms=13 warnings=0 hash=sha256:bfbc67c7…（6,235 B，入库）
- 2026-10-22T23:48:01+08:00 · 开始任务 T7「先行测试与负例覆盖」
- 2026-10-22T23:48:02+08:00 · 完成任务 T7 · 证据: 先红：page-manifest-red 12/14 失败（断言失败）；实现后 15/15 全绿；回归 UiPreview 5/5、UiWidgetLibrary 6/6、WidgetSpec 3/3、WidgetManifest 11/11、Contract 10/10、Diff 5/5、StaticText 6/6
- 2026-10-22T23:50:25+08:00 · 开始任务 T8「门禁、文档冻结与回写」
- 2026-10-22T23:50:25+08:00 · 完成任务 T8 · 证据: L1 PASS 1819；doc-lint OK 0 violation；L2 meta PASS（14192）；定向 15/15 + 回归 7 套件；设计 §8.5 + 总设计 §5.1/协作规范 §5.3 五态统一 + README schema；进度卷第 3 章；审计挂账 1 条
- 2026-10-22T23:41:57+08:00 · 真实 tbuiform 基线生成：pages=12 forms=13 warnings=0（`Tmp_Dialog` 201/206 合并为一条两行）。
- 2026-10-22T23:45:00+08:00 · 真实对账暴露多行登记问题，模型从「一条目=一行」改为「一条目=一页面 + forms[]」；新增两条用例后 15/15 全绿。

## 关键决定

- **页面清单＝工具维护注册表**（方案 A），非纯生成物：支持草稿先于表行（§3.1）、维护字段跨对账保留；`sourceHash` 用于发现外部写入。
- **条目按页面（prefab）唯一 + `forms[]` 承载登记行**：真实表里 `Tmp_Dialog` 登记 201（系统错误面）/206（确认面）两行——页面级 Owner/状态只有一份，登记行多条；`luaPath` 多行不一致置 null + 告警。
- **五态词表合并两处设计表述**（草案/评审/冻结 + 草案/已登记/已接线/冻结）为 `Draft/InReview/Registered/Wired/Frozen`；迁移表在 `UiPageStatusRules` 单源，设计 §8.5 冻结，总设计 §5.1 与协作规范 §5.3 改为指针。
- **表读面区分失败与空表**：新增 `UiPageScaffold.TryListPages(out rows, out error)`；`ListPages` 委托保留旧行为（失败仍返回空 + warning）。对账必须用 Try 版，否则表读失败会把清单对成"全孤儿"。
- **基线入库**（与纯生成物不同）：清单承载 Owner/状态等维护态，必须跨人传递；13 行 → 12 页/13 表单，全部 `Registered`（`Wired` 是工程接线/评审动作，不自动判定）。
- **编辑 API 纯内存 + 显式保存**：每次编辑重算指纹；`SetStatus` 只认迁移表；`SetEditorial` 对设备档做 trim/去重/排序保证确定性。

## 偏差与处理

- 方案原模型（条目含 formId/layer/fullScreen 标量）在真实对账时暴露多行问题 → 收敛为 `forms[]`；偏差已回写设计 §8.5、模型与测试。
- 任务 T1/T2 因文件路径误写 `Pages/` 取消并重建为 T3/T4（取消理由记录在任务状态；未产生多余目录）。
- 方案原「重名页面 Error」在真实表下不成立（同 prefab 多行合法）→ 改为「同页多行合并」正例 + 「多行 lua 不一致」告警用例。
- 基线状态统一 `Registered`，未自动推断 `Wired`（方案假设，已写入进度卷边界）。

## 问题与修复

- **真实对账暴露多行登记**：首版按 formId 逐行建条目，真实 `tbuiform` 的 `Tmp_Dialog` 两行被误判「重名页面」（15 例中 1 红）。修：条目按 prefab 唯一 + `forms[]`；新增「同页多行合并」「多行 lua 不一致告警」两条用例。
- **表读失败不可见**：`UiPageScaffold.ListPages` 失败返回空表（仅 warning），对账会误判「清单全孤儿」。修：新增 `TryListPages` 显式错误，Sync 失败不写盘。
- **`??` 与 `List<T>`/`IReadOnlyList<T>` 类型不匹配**（编译期）：改为先赋局部变量再判空。
- **显式行对账误读磁盘基线**（全量 verify 暴露）：基线入库后，`Sync(rows, null)` 仍会 `LoadIfExists` ⇒ 显式夹具用例读到真实 12 页（`Expected: 2 / But was: 14`、`Single()` 抛多元素）；单跑绿、全量 `wf verify` 才红（15/16）。修：只有 `rows == null`（默认模式）才读磁盘清单，显式行时 `existing` 严格按入参。

## 验证结果

- 先行测试：`page-manifest-red` 12/14 失败（断言失败），其余 7 条 red 命令通过；证据 `evidence/red.json`。
- 定向夹具：`UiPageManifest` **15/15**；回归 `UiPreview` 5/5 · `UiWidgetLibrary` 6/6 · `WidgetSpec` 3/3 · `UiWidgetManifest` 11/11 · `UiContractGenerator` 10/10 · `UiContractDiff` 5/5 · `UiStaticText` 6/6。
- 门禁预跑：L1 **PASS 1819** · `doc-lint` **OK** · L2 meta **PASS（14192）**。
- 最终 `wf verify` 全量命令记录见下（`evidence/verify.json`）。
- 2026-10-22T23:37:54+08:00 · 先行测试 PASS · 1/8 · 证据 evidence/red.json
- 2026-10-22T23:53:57+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-22T23:58:26+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json

## 交付摘要

- **页面清单模型**：`UiPageManifest` / `UiPageManifestEntry` / `UiPageFormEntry`（页面唯一 + `forms[]` 多行登记）；五态词表 `Draft/InReview/Registered/Wired/Frozen` + `UiPageStatusRules` 迁移表单源。
- **存储与指纹**：`UiPageManifestStore`（`UiDeterministicJson`、UTF-8 无 BOM、LF）+ `UiPageManifestHash`（身份+维护字段，不含告警/版本/时间戳）+ `UiPageManifest.schema.json`。
- **对账**：`UiPageManifestSync` 刷新身份字段、保留维护字段；新增/孤儿/重复 formId/非法状态/多行 lua/prefab 缺失全部显式报告；表读失败经新增 `TryListPages` 硬失败、不写盘；输出限定 `Assets/`。
- **编辑 API**：`EnsureDraft`（起页草稿、幂等）、`SetStatus`（迁移校验、同值无操作）、`SetEditorial`（Owner/设计稿/设备档/备注，编辑后重算指纹）。
- **基线入库**：12 页 / 13 表单 / 0 告警 / `sourceHash=sha256:bfbc67c7…`（`UiPageManifest.json`，6,235 B；`Tmp_Dialog` 201/206 合并为一条两行）。
- **测试**：`UiPageManifestEditModeTests` 15 例，先红后绿；回归 7 套件全绿。
- **门禁**：`wf verify` **PASS 16/16**（L1 1819 · doc · l2-meta · 4 个 UI 夹具 · 其余）。

## 未决事项

- **P1-4～P1-6 未做**：校验规则族与 `UiRuleExceptions`、Hub/CLI/CI 接线、试点页挂 `LTextKeyRef`。
- **工具入口未接**：起页工具（`UiPageScaffold.Create`）尚未自动写草稿、登记流程尚未调用 `SetStatus`——P1-5 接线；设计 §9.1 也无独立页面清单工具 ID，建议挂工作台「页面」透视。
- **多行页面契约歧义**：`Tmp_Dialog` 201/206 两行，契约 `page.formId` 取首行；已在设计 §8.5 与审计表登记，P1-4/P1-5 明确处理。
- **手改检测未做**：`sourceHash` 已备，检测规则归 P1-4。
- **状态基线统一 `Registered`**：`Wired` 属工程接线/评审动作，未自动判定。
- **工作区未提交**：代码/清单 JSON/文档/配置均未 commit，提交与分批由用户裁决。

## 文件清单

- 编辑器：`Assets/LiteGame/Editor/UiTools/Page/{UiPageManifest,UiPageStatus,UiPageManifestJson,UiPageManifestHash,UiPageManifestSync,UiPageManifestEditor}.cs`；`Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（新增 `TryListPages`）
- 数据：`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`（基线，入库）
- 测试：`Assets/Tests/EditMode/UiPageManifestEditModeTests.cs`
- 文档：`Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md`（§8.5）、`Docs/设计文档/客户端/UI系统/UiPageManifest.schema.json`、`UI系统总设计.md`（§5.1）、`UI美术协作与交接规范.md`（§5.3）、`UI系统/README.md`、`Docs/施工进度/UI系统线.md`（第 3 章）、`Docs/施工进度/README.md`、`Docs/审计/代码质量审计-客户端.md`
- 工装/配置：`.workflow/config.json`（新增 `page-manifest-editmode` / `page-manifest-red`）、`.workflow/active/ui-p1-page-manifest/**`

## 后续建议

- **P1-4 校验规则**：直接消费清单 `warnings`/`sourceHash`；补「清单与磁盘指纹不一致＝手改」「缺 `WidgetSpec`=Error」「状态与实现一致性」「多行页面契约 formId」四条规则。
- **P1-5 接线**：起页成功即 `EnsureDraft`；页面登记后 `SetStatus(Registered)`；工程接线后 `SetStatus(Wired)`；在工作台「页面」透视给出清单视图与状态按钮（设计 §9.1 补工具 ID 或挂到 `ui.workbench`）。
- **P1-6 试点**：`Page_Login` 挂 `LTextKeyRef` 并跑一次 `Draft→Registered→Wired→Frozen` 状态演练，验证清单随状态流转可 review。
- **内容复核**：13 行页面的 Owner/设计稿/设备档由美术/工程补全（当前 Owner 统一 `art.ui`、设计稿/设备档为空）。
- **多行页面**：契约若需覆盖 201/206 两行的差异，按次版本追加 `formIds[]`，而不是让清单承载契约语义。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-22T23:28:19+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-22T23:29:46+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-22T23:30:51+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-22T23:30:52+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-22T23:38:16+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-22T23:50:51+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-22T23:58:34+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-22T23:58:34+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-22T23:59:23+08:00 · 完成「回写与收尾」→ 完成
