# 施工记录：UI 系统 P2-2：起页/登记拆分（ui.page-scaffold / ui.page-register）

> 运行 ui-p2-scaffold-register · 创建于 2026-10-23T22:34:41+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-23T23:23:14+08:00 · 新增任务 T1「先红：夹具骨架与可编译桩」
- 2026-10-23T23:23:14+08:00 · 新增任务 T2「表门面与路径注入（UiPageTableHelper/ToolProcess）」
- 2026-10-23T23:23:14+08:00 · 新增任务 T3「清单原子写与缓存策略维护字段」
- 2026-10-23T23:23:14+08:00 · 新增任务 T4「草稿契约链（formId=0 · 批量 · 漂移 · diff 口径）」
- 2026-10-23T23:23:29+08:00 · 新增任务 T5「起页内核（基线·根契约·安全区归属·原子清理）」
- 2026-10-23T23:23:29+08:00 · 新增任务 T6「登记内核（契约门槛·表核验·代码生成链·状态/缓存/契约刷新）」
- 2026-10-23T23:23:29+08:00 · 新增任务 T7「窗口与工作台接线（事件·门控·冲突保护·焦点重载）」
- 2026-10-23T23:23:29+08:00 · 新增任务 T8「工具面与索引（handlers + toolchain.json + 工具索引）」
- 2026-10-23T23:23:30+08:00 · 新增任务 T9「文档回写（08/修改指南/技术总结/设计/schema/进度）」
- 2026-10-23T23:23:30+08:00 · 新增任务 T10「全量验证与冒烟（UI 回归·L1·Doc·toolchain·wf verify·eval_file）」
- 2026-10-23T23:23:30+08:00 · 新增任务 T11「独立代码评审与修复复评」
- 2026-10-23T23:58:10+08:00 · 开始任务 T1「先红：夹具骨架与可编译桩」
- 2026-10-23T23:58:10+08:00 · 完成任务 T1 · 证据: UiPageScaffoldRegisterEditModeTests 25 例编译通过、24 失败 1 通过（断言失败，非编译/环境错误）；Python test_uiform_row 6 例 4 失败 1 错误（--table 未实现，usage 失败按 plan 记录）；编译 compilationFailed…
- 2026-10-24T00:24:05+08:00 · 开始任务 T2「表门面与路径注入（UiPageTableHelper/ToolProcess）」
- 2026-10-24T00:24:05+08:00 · 完成任务 T2 · 证据: UiPageTableRow/Helper/ToolProcess 落地；PageRow 全调用点迁移（Sync/ContractGenerator/Batch/3 夹具）；_uiform_row.py --table/备份目录随表/layer 0-3/PascalCase/delete；Python 6/6；回归 …
- 2026-10-24T00:24:05+08:00 · 开始任务 T3「清单原子写与缓存策略维护字段」
- 2026-10-24T00:28:33+08:00 · 完成任务 T3 · 证据: UiPageManifestStore.Save 临时件+File.Replace（保留 folderKnown/Refresh）；SetCacheStrategy + UiPageContract.IsValidCacheStrategy 单源；Sync 保留 cacheStrategy 并刷新草稿 contrac…
- 2026-10-24T00:28:33+08:00 · 开始任务 T4「草稿契约链（formId=0 · 批量 · 漂移 · diff 口径）」
- 2026-10-24T00:53:34+08:00 · 完成任务 T4 · 证据: 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warning）；Catalog 12/12
- 2026-10-24T00:53:34+08:00 · 开始任务 T5「起页内核（基线·根契约·安全区归属·原子清理）」
- 2026-10-24T00:53:34+08:00 · 完成任务 T5 · 证据: 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warning）；Catalog 12/12
- 2026-10-24T00:53:34+08:00 · 开始任务 T6「登记内核（契约门槛·表核验·代码生成链·状态/缓存/契约刷新）」
- 2026-10-24T00:53:34+08:00 · 完成任务 T6 · 证据: 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warning）；Catalog 12/12
- 2026-10-24T00:53:34+08:00 · 开始任务 T7「窗口与工作台接线（事件·门控·冲突保护·焦点重载）」
- 2026-10-24T00:53:34+08:00 · 完成任务 T7 · 证据: 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warning）；Catalog 12/12
- 2026-10-24T00:53:35+08:00 · 开始任务 T8「工具面与索引（handlers + toolchain.json + 工具索引）」
- 2026-10-24T00:53:35+08:00 · 完成任务 T8 · 证据: 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warning）；Catalog 12/12
- 2026-10-24T01:11:25+08:00 · 开始任务 T9「文档回写（08/修改指南/技术总结/设计/schema/进度）」
- 2026-10-24T01:11:25+08:00 · 完成任务 T9 · 证据: 文档回写：08/修改指南/技术总结/设计 §3.1/§3.5/§3.6/§5.2/§9/schema/施工进度第9章/README/工具索引；doc-lint 0/0、Doc lane PASS。全量验证：wf verify PASS（L1/doc/toolchain/docs/契约/工作台/新夹具 28/28/Py…
- 2026-10-24T01:11:25+08:00 · 开始任务 T10「全量验证与冒烟（UI 回归·L1·Doc·toolchain·wf verify·eval_file）」
- 2026-10-24T01:11:26+08:00 · 完成任务 T10 · 证据: 文档回写：08/修改指南/技术总结/设计 §3.1/§3.5/§3.6/§5.2/§9/schema/施工进度第9章/README/工具索引；doc-lint 0/0、Doc lane PASS。全量验证：wf verify PASS（L1/doc/toolchain/docs/契约/工作台/新夹具 28/28/Py…
- 2026-10-24T01:11:26+08:00 · 开始任务 T11「独立代码评审与修复复评」
- 2026-10-24T02:50:25+08:00 · 完成任务 T11 · 证据: 独立评审 4 轮：FAIL(4 阻塞 B1-B4) → FAIL(I2 修复引入 R1) → FAIL(仅文档计数) → PASS；新夹具 31/31、Python 10/10、wf verify PASS；证据 reviews/code-review.md + evidence/verify.json

## 关键决定

- **契约门槛补“草稿契约”链（用户裁决 A）**：设计要求登记前有契约，但 `ui.contract` 原只认 `tbuiform` 行、schema `formId≥1`，草稿页无法生成契约。改为 schema 允许 `formId=0`、批量纳入清单草稿、登记后重生成真实身份；不把门槛降级、不删设计输入。
- **缓存策略进清单维护字段（用户裁决 B）**：`tbuiform` 无列，登记窗口选 `Resident/Lru/DestroyOnClose` 写 `UiPageManifest.cacheStrategy`，对账保留、契约投影同值；运行时暂不消费，C# 骨架留后续批次。
- **安全区优先挂现有内容容器**：实测 `Tmp_BaselineA/B` 内容在嵌套 `Cot_Baseline`、`Tmp_ListScreen` 已有 `SafeAreaReceiver`；起页改为“已有保留 / 优先 `Cot_*` 拉伸容器 / 都没有才新建 `Cot_SafeArea` 并搬入”，夹具断言内容归属。
- **原子写共用 `UiAtomicFile`**：清单与契约都走临时件+替换（`File.Replace` 失败重试、退化共享读覆盖），避免 Unity 导入句柄偶发 `IOException` 留下半截 JSON。
- **故障注入端口化**：`IUiPageTablePort`（“已落行但报失败”）、`IUiPageCodegenPipeline`（Luban/BuildHash 顺序与失败）、`IUiPageContractRefresher`（刷新失败）——把 AC4 从“尽量覆盖”变成可确定性断言。
- **`unity-tests.py` 显式 `--project-path`**：本机同时打开 WindFrameWork/Test 两个 Editor，异步 `run_tests` 不带工程会报 “Multiple Unity Editor instances”；属环境修正，非本批功能。

## 偏差与处理

- **`.workflow/config.json` 8 阶段变体**：缺 `review-plan`/`confirm-acceptance` 门禁；并行 TPS 运行处于 `implement`，插入前置阶段会破坏其归档校验。本批手工执行独立方案评审（3 轮）与用户验收确认并落盘；红证据与最终验证用精简命令集（L1/doc/toolchain/docs/契约/工作台/新夹具/Python），其余 UI 套件逐套件跑过全绿。**归档后已把 `commands` 恢复为 HEAD 全量 + 两个新命令（33 条），`red_commands` 恢复 HEAD 20 条**，避免共享配置被本批收窄；因此归档运行的状态页会把验证标为“命令已变更”（证据文件本身不变）。
- **`unity-tests.py` 修改**：不属于方案文件清单，但为恢复本机测试能力所必需（多 Editor 环境 + 异步启动缺工程参数）；已在进度章“边界与遗留”注明。
- **`UiPageScaffold.Create` 提前删除**：原计划 T5 删除；T2 迁移表门面时已无消费者，随批删除，T5 验收相应完成。
- **`UiPageScaffold` 任务桩→实现**：T1 桩返回 `NotImplemented`，红测为断言失败（非编译错误）；实现后同一批夹具转绿。

## 问题与修复

1. **Python 单测 0 用例**：原写 pytest 风格函数，仓库走 `unittest discover`；改 `unittest.TestCase` 类后 6/6。
2. **`JsonUtility` 读不到表行**：`HelperResult/HelperRow` 缺 `[Serializable]`，导致 `rows` 为空、清单“11 页变 0 行”；补 `[Serializable]` 后真实表 16/16 回归。
3. **`File.Replace` 偶发 `IOException`（无法删除要被替换的文件）**：Unity 导入持有句柄；改为重试 + 共享读覆盖，并抽 `UiAtomicFile` 单源。
4. **安全区测试期望错误**：`Tmp_ListScreen` 内容节点已带 `SafeAreaReceiver`，原测“必须新建 `Cot_SafeArea`”与实际不符；改为“保留既有、不重复添加”。
5. **测试 id 冲突**：初版用 `ValidId=16`，与真实表 `Page_Records` 冲突；改 17、冲突用例用既有 11。
6. **异步 `run_tests` 卡“stale”**：`launch` 未带 `--project-path`，发射被多实例歧义拦下、轮询等超时；补工程参数后红/绿均正常（37s / 149s）。
7. **文档整文件误覆盖**：追加第 9 章时 `write` 覆盖了 1–8 章；从 `git show HEAD` 恢复并按行插入索引 + 追加第 9 章，`doc-lint` 复检 0/0。
8. **代码评审批次**：首轮 4 阻塞（diff 真实→0 / Python 丢 backup / 核验三态 / 测试自比较）已修；第 2 轮发现 I2 修复引入 R1（应用后保存恒失败）、清理文案未条件化等 → 再修（`HasUnappliedEdits` 对照清单条目、`TryApplyEdits` 同步字段、`CleanupFailed` 返回实际状态、Unknown 回滚用例、计数/重载路径/JSON 判据）；第 3 轮复评见 `reviews/code-review.md`。

## 验证结果

- 先行测试：`evidence/red.json`（新夹具 23/25 断言失败；Python 4 failures + 1 error）
- 最终验证：`evidence/verify.json`（9/9 PASS：L1 / doc / toolchain-check / docs-check / 契约 / 契约 diff / 工作台 / 新夹具 31/31 / Python 10/10）
- UI 回归 20 套件逐套全绿（清单见进度章第 9 章）
- 数据面：真实 `#uiform.xlsx` 与 11 份契约字节不变；`gen-build-hash.py --check-shared` OK
- 冒烟：两窗口 `eval_file` 打开、Console 0 error（真实点击留人工）
- 2026-10-24T01:19:02+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json
- 2026-10-24T02:50:19+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json
- 2026-10-24T02:54:25+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json

## 交付摘要

P2-2 把“起页/登记”从 P2-1 的置灰占位拆成两个真实工具：`ui.page-scaffold`（美术：视觉基线→自包含 prefab+根契约/安全区+页面清单草稿）与 `ui.page-register`（工程：草稿契约门槛→表行/Lua/`Luban+BuildHash`→清单 Registered+缓存策略→契约按真实身份刷新；表写失败回滚复核、生成链失败回滚并重跑 Luban、契约刷新失败显式报红）。补了草稿契约链（schema `formId=0`、批量/漂移/diff 口径）、清单/契约原子写、工作台与外窗的指纹冲突保护与焦点重载、两个 window 工具与工具索引。新增 EditMode 31 例 + Python 10 例，UI 回归全绿，`wf verify` PASS。

## 未决事项

- C# 骨架生成（依赖 `BindRoot`/命名空间配置）与运行时缓存策略消费（等 `tbuiform` 列）留后续批次。
- 契约评审（G2）仍是人工门；工具只机检“草稿契约存在/同名/guid/`formId=0`”。
- 窗口按钮的真实点击/输入流程需人工确认一次（自动化只证窗口对象与 Console）。
- 工作区含并行线改动（DevMenu/调参/测试房语义），提交范围待用户裁决。
- `red_commands` 精简与验证命令收敛属流程偏差，已在“偏差与处理”注明；后续可把完整 UI 回归集放回配置。

## 文件清单

- 新增：`Assets/LiteGame/Editor/UiTools/Page/{UiPageTableRow,UiPageTableHelper,UiPageTablePort,UiPageToolProcess,UiPageCodegen,UiPageRegistration,UiPageScaffoldWindow,UiPageRegisterWindow}.cs`、`Assets/LiteGame/Editor/UiTools/UiAtomicFile.cs`、`Assets/Tests/EditMode/UiPageScaffoldRegisterEditModeTests.cs`、`scripts/tests/test_uiform_row.py`
- 修改：`Page/{UiPageScaffold,UiPageManifestEditor,UiPageManifestJson,UiPageManifestSync}.cs`、`Contract/{UiPageContract,UiPageMetadata,UiContractGenerator,UiContractBatch,UiContractDiff,UiContractJson}.cs`、`Workbench/{UiWorkbenchWindow,UiWorkbenchPageSection,UiWorkbenchAvailability,UiWorkbenchPerspective}.cs`、`Toolchain/EditorToolHandlers.cs`、`Validation/Rules/ContractDriftRule.cs`、`scripts/gate/{toolchain.json,unity-tests.py}`、`scripts/codegen/_uiform_row.py`、测试 `{UiWorkbench,UiPageManifest,UiToolchainWiring,UiContractReconciliation}EditModeTests.cs`
- 文档：`Docs/设计文档/客户端/UI系统/{UI编辑器工具链专项设计.md,UiPageContract.schema.json}`、`Docs/项目说明/08-UI工作台.md`、`Docs/修改指南.md`、`Docs/技术总结/04-UI系统与Lua脚本层.md`、`Docs/施工进度/{UI系统线.md,README.md}`、`Docs/工具链/工具索引.md`
- 流程：`.workflow/config.json`（commands/red_commands）、本运行目录

## 后续建议

- 恢复完整验证命令集（把逐套件 UI 回归合并为一个 gate 入口，避免 30 条命令的 `wf verify`）。
- 为草稿契约补一条 `ContractDriftRule` 级夹具（当前在批量/重生成层覆盖）。
- 把 `UiAtomicFile` 推广到其它编辑器生成物（widget manifest/bind manifest）以统一“生成物不半写”口径。
- 并行 TPS 线归档后迁移 workflow 配置（补独立方案评审/验收门禁阶段），恢复 `red_commands` 的新测试语义。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-23T22:34:41+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-23T23:22:56+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-23T23:24:12+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-23T23:29:18+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-24T01:19:11+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-24T02:50:25+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-24T02:54:25+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-24T02:56:56+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-24T02:57:05+08:00 · 完成「回写与收尾」→ 完成
