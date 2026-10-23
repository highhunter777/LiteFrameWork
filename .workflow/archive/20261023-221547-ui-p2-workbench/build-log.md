# 施工记录：UI 系统 P2-1：单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23T18:55:29+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-23T18:58:10+08:00 · 新增任务 T1「先红：工作台守卫夹具与最小编译骨架」
- 2026-10-23T18:58:17+08:00 · 新增任务 T2「Lane 策略内核（动作×角色矩阵单源）」
- 2026-10-23T18:58:17+08:00 · 新增任务 T3「工作台状态与偏好（上下文不丢）」
- 2026-10-23T18:58:26+08:00 · 新增任务 T4「页面透视模型（清单投影与编辑保存门面）」
- 2026-10-23T18:58:26+08:00 · 新增任务 T5「契约透视模型（生成与 diff 报告投影）」
- 2026-10-23T18:58:34+08:00 · 新增任务 T6「宿主窗口装配（零 Sirenix IMGUI）」
- 2026-10-23T18:58:34+08:00 · 新增任务 T7「工具面接线与 ui.compose 退役」
- 2026-10-23T18:58:44+08:00 · 新增任务 T8「文档回写（设计/手册/索引/进度）」
- 2026-10-23T18:58:44+08:00 · 新增任务 T9「验证与收尾（全量夹具/回归/门禁/施工记录）」
- 2026-10-23T20:20:05+08:00 · 开始任务 T1「先红：工作台守卫夹具与最小编译骨架」
- 2026-10-23T20:20:05+08:00 · 完成任务 T1 · 证据: UiWorkbenchEditModeTests 10/10 断言失败（编译 0 错）；wf verify --red PASS，ui-workbench-red exit=1（evidence/red.json）
- 2026-10-23T20:20:05+08:00 · 开始任务 T2「Lane 策略内核（动作×角色矩阵单源）」
- 2026-10-23T20:20:05+08:00 · 开始任务 T3「工作台状态与偏好（上下文不丢）」
- 2026-10-23T20:26:53+08:00 · 新增任务 T10「退役波及面补遗：page-new/向导/引用/文档入口」
- 2026-10-23T20:26:53+08:00 · 新增任务 T11「验证补遗：UI EditMode + doc + toolchain 显式证据」
- 2026-10-23T21:22:46+08:00 · 完成任务 T2 · 证据: UiLanePolicy 11x3 矩阵+穷举夹具绿；UiWorkbenchEditModeTests 13/13
- 2026-10-23T21:22:46+08:00 · 完成任务 T3 · 证据: UiWorkbenchState 切换上下文夹具绿；IWorkbenchSettings 双实现（EditorPrefs/内存）
- 2026-10-23T21:22:56+08:00 · 开始任务 T4「页面透视模型（清单投影与编辑保存门面）」
- 2026-10-23T21:22:56+08:00 · 完成任务 T4 · 证据: 页面清单 11 页/12 行只读、缺清单显式错误、临时路径双写逐字节一致（夹具绿）
- 2026-10-23T21:22:56+08:00 · 开始任务 T5「契约透视模型（生成与 diff 报告投影）」
- 2026-10-23T21:22:56+08:00 · 完成任务 T5 · 证据: 契约报告投影 + Generate(临时目录) 11 份/无 Error/重复一致（夹具绿）
- 2026-10-23T21:22:56+08:00 · 开始任务 T6「宿主窗口装配（零 Sirenix IMGUI）」
- 2026-10-23T21:22:56+08:00 · 完成任务 T6 · 证据: 窗口打开：title=UI 工作台、Perspective=Page、五透视 5、Console 0 error（eval_file 冒烟）
- 2026-10-23T21:23:04+08:00 · 开始任务 T7「工具面接线与 ui.compose 退役」
- 2026-10-23T21:23:04+08:00 · 完成任务 T7 · 证据: Catalog：ui.workbench 在册(Kind=window/无 Error)、ui.compose 与 ui.page-new 为 null（夹具绿）
- 2026-10-23T21:23:05+08:00 · 开始任务 T8「文档回写（设计/手册/索引/进度）」
- 2026-10-23T21:23:05+08:00 · 完成任务 T8 · 证据: 08/技术总结04/修改指南/平台设计/工具索引/进度第8章 已改；doc-lint 0/0
- 2026-10-23T21:23:05+08:00 · 开始任务 T10「退役波及面补遗：page-new/向导/引用/文档入口」
- 2026-10-23T21:23:05+08:00 · 完成任务 T10 · 证据: 两窗口类删除、UiPreviewEditModeTests 6/6、平台设计条目改；全仓无 ui.compose/ui.page-new 活引用（夹具+grep）
- 2026-10-23T21:48:05+08:00 · 开始任务 T9「验证与收尾（全量夹具/回归/门禁/施工记录）」
- 2026-10-23T21:48:05+08:00 · 完成任务 T9 · 证据: 代码冻结后全量 wf verify 复跑（31 命令；证据 evidence/verify.json）；新增 21 例 + 回归 + L1 + doc/toolchain
- 2026-10-23T21:48:05+08:00 · 开始任务 T11「验证补遗：UI EditMode + doc + toolchain 显式证据」
- 2026-10-23T21:48:05+08:00 · 完成任务 T11 · 证据: UiWorkbench 21/21、UiPreview 6/6、doc-lint 0/0、toolchain check OK；最终 wf verify 证据见 evidence/verify.json

## 关键决定

1. **宿主纯 IMGUI（零 Sirenix）**：上下文条 + 透视页签 + 状态行自绘；复用分区不动（`UiWidgetLibrarySection`/`UiWidgetPropertySection` 仍是存量 Odin 实现）——避免在本批重写 Odin 面板。
2. **双层门控**：Lane（角色该不该做，`UiLanePolicy` 矩阵 11×3）× 阶段（工具建没建，`UiWorkbenchAvailability`）；禁用提示**阶段原因优先**，避免“全局置灰掩盖角色差异”。
3. **五透视全启用**：页面/控件库/绑定/契约/校验；样式/预览在 P2-3 以新增项接入，不放“禁用槽位”（与夹具一致）。
4. **`ui.compose` 与 `ui.page-new` 同批退役**（用户 2026-10-23 裁决）：严格单入口，代价＝P2-2 前无窗口起页工具；`UiPageScaffold.Create` 留作 P2-2 内核并注批次号。
5. **页面清单编辑显式保存**：只改内存 + 显式“保存清单”；真实清单只读，夹具写临时路径并断言重复保存逐字节一致。
6. **AC5 生成半用真实集成证据**：`UiContractBatch.Generate(临时目录)` 跑两次，断言 11 份 + 无 Error + 逐字节一致，而不是只测报告投影。
7. **透视拆 Section**：页面/契约各自成 `UiWorkbenchPageSection`/`UiWorkbenchContractSection`，宿主 <300 行，符合单一职责与 >400 行红线。

## 偏差与处理

1. **方案 v2（评审后修订）**：首轮独立评审 FAIL → 收窄 AC1（制作/起页入口唯一）、补 `GenerateBinding` 动作与 11×3 矩阵、补透视能力标签、补真实生成证据；用户追加裁决“本批退役 `ui.page-new`”。修订记录见 `plan.md` 头部与 `acceptance.md` 变更记录。
2. **任务清单补遗**：原 T7 只写 `ui.compose`，按裁决新增 T10（page-new/向导/引用/平台文档）与 T11（UI EditMode + doc + toolchain 显式证据），避免“删宿主断编译/证据不足”。
3. **测试例数 10→13**：红基线为 10 例；实现期补 3 例（阶段可用性、能力标签映射、契约真实生成）。补例不回改红证据，最终以 13/13 绿为验收。
4. **页面/契约透视拆 Section**：方案只写“透视”，实现按单一职责拆成两个 Section 类（宿主更薄），行为不变。
5. **`UiPageScaffold.Create` 死码风险**：向导删除后无窗口消费者；按红线 10 保留并注“P2-2 接线”，不做删除（P2-2 直接复用）。

## 问题与修复

| 问题 | 修复 |
|---|---|
| PowerShell 不支持 `python - <<'PY'` heredoc（多次 ParserError） | 改“写临时 `.py` 文件 → 执行 → 删除”，路径含中文/`#` 时也稳定 |
| `unity-tests.py` 首跑报“compilation failed”但 `console_status` 已 false（状态滞后/陈旧日志） | 先 `unity command recompile` + `recompile_status` 确认，再跑夹具；必要时读 Editor.log 末尾 `error CS` 区分新旧 |
| 删除 `UiComposeWindow`/`UiPageWizardWindow` 后 Unity 仍尝试编译旧文件（CS2001）与 `UiWorkbenchWindow` 未解析 | `unity command recompile` 触发资产库刷新后恢复；删除统一走 unity-pipeline `delete_asset` |
| 本仓 NUnit 无 `StringAssert.IsNotEmpty`；且 `Assert.IsNotEmpty(null)` 抛 `ArgumentException` 而非断言失败 | 断言改为 `Assert.IsNotNull` + `Assert.IsNotEmpty` 两步（封装 `AssertForbidden`），红仍是断言失败 |
| 页面清单 `Store.Save` 需要 `Assets/` 下路径（AssetDatabase.ImportAsset） | 夹具临时目录固定在 `Assets/LiteGame/Editor/UiTools/TempWorkbenchTests`，`OneTimeTearDown` 删除 |
| 契约生成集成用例可能污染真实 `Contracts/` | 生成到临时子目录，断言 11 份与逐字节一致后由 TearDown 清理 |

## 验证结果

- **先行测试（红）**：2026-10-23T20:18:54 · `wf verify --red` PASS（20 命令；`ui-workbench-red` exit=1，10/10 断言失败）· `evidence/red.json`。
- **首次全量（受污染）**：21:20 · FAIL 30/31——`ui-contract-editmode` 1/10，控制台残留一条工具层错误（`Failed to handle /api/exec request ... 5000ms`，由本会话早期 `eval` 超时产生）被 Test Framework 计入；`clear_console` 后单跑 10/10。**非产品缺陷**。
- **第二次全量**：21:19 · PASS 31/31（L1 40583 ms + UI EditMode 17 条 + PlayMode + doc/l0/toolchain）。
- **代码评审修复后**：21:55 · PASS 31/31（B1–B4 修复、扩展性/夹具加强后）。
- **Stage 跟随修复后（最终）**：全量 `wf verify` **PASS 31/31**（`ui-workbench-editmode` 21/21）· `evidence/verify.json`。
- **窗口冒烟**：`title=UI 工作台 | Perspective=Page | 透视 5`，窗口实例 1、Console error=0。限制：编辑器窗口未聚焦时主循环挂起，逐透视点击/截图未自动化，留人工一次确认（逻辑由 21 例夹具覆盖）。
- 2026-10-23T22:05:23+08:00 · 验证 PASS · 31/31 · 证据 evidence/verify.json
- 2026-10-23T22:15:10+08:00 · 验证 PASS · 31/31 · 证据 evidence/verify.json

## 交付摘要

P2-1 收敛 UI 制作入口：新增 `ui.workbench`（纯 IMGUI 宿主 + 页面/控件库/绑定/契约/校验五透视 + Lane/阶段双层门控 + 共享页面/目标/Lane 上下文），`ui.compose` 与 `ui.page-new` 同批退役；页面清单编辑（原子应用、显式保存、关闭提示）与契约生成/diff 进窗口；绑定透视保留路径 B 生成/hash、控件库保留受控属性，迁移未丢动作。新增 `UiWorkbenchEditModeTests` **21 例**（矩阵逐项/能力标签/选中绑定/缺清单/原子迁移/真实契约生成与 diff/Stage 目标解析/设置）；首红证据 10/10；全量 `wf verify` PASS。独立评审三轮（方案 3 轮、代码 4 轮）发现并修复 6 个实质问题。

## 未决事项

- **起页/登记归 P2-2**：P2-1 无窗口起页工具（用户裁决退役 `ui.page-new`），手册已写明手工路径；`UiPageScaffold.Create` 保留为 P2-2 内核。
- **样式令牌/预览矩阵归 P2-3**：设备档为占位（工具栏「设备档：P2-3」）；样式族遗留（令牌 SO 管线、状态色 token 映射）不变。
- **控件库/受控属性分区仍用 Odin**：宿主零 Sirenix，但复用分区是存量 Odin 实现；纯 IMGUI 化与 `Compose/`→`Workbench/` 目录迁移留清理批。
- **窗口级 Stage 跟随无自动夹具**：`UiComposeTarget.Resolve` 的 Stage 语义有夹具；窗口 `FollowTarget/OpenTargetStage` 调用点靠评审+手动冒烟覆盖。
- **手工逐透视点击未自动化**：受编辑器窗口主循环限制；留人工一次确认。
- **并行线未提交**：工作区另有 DevMenu/调参/测试房语义批；本批提交按 hunk 拆分（共享文件只取本批改动）。

## 文件清单

- 新增：`Assets/LiteGame/Editor/UiTools/Workbench/`（`UiWorkbenchWindow`、`UiWorkbenchLane`、`UiLanePolicy`、`UiWorkbenchAvailability`、`UiWorkbenchPerspective`、`UiWorkbenchState`、`UiWorkbenchSettings`、`UiWorkbenchPageList`、`UiWorkbenchPageEditor`、`UiWorkbenchPageSection`、`UiWorkbenchContractSection`、`UiContractReportView` 及 .meta）、`Assets/Tests/EditMode/UiWorkbenchEditModeTests.cs`(+meta)
- 删除：`Assets/LiteGame/Editor/UiTools/Compose/UiComposeWindow.cs`(+meta)、`Assets/LiteGame/Editor/UiTools/Page/UiPageWizardWindow.cs`(+meta)
- 修改：`Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`、`Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（P2-2 注释）、`Assets/Tests/EditMode/UiPreviewEditModeTests.cs`、`scripts/gate/toolchain.json`、`.workflow/config.json`
- 文档：`Docs/项目说明/08-UI工作台.md`、`Docs/技术总结/04-UI系统与Lua脚本层.md`、`Docs/修改指南.md`、`Docs/设计文档/架构/开发工具链平台专项设计.md`、`Docs/工具链/工具索引.md`、`Docs/施工进度/UI系统线.md`、`Docs/施工进度/README.md`

## 后续建议

1. **P2-2**：拆 `ui.page-scaffold`（仅 prefab + 草稿清单）与 `ui.page-register`（表/Lua/Luban/hash），把页面透视两个按钮接线；契约门槛（未生成契约不可登记）。
2. **P2-3**：令牌 JSON→`UiThemeAsset`、样式透视、设备档/语言/截图矩阵、`ui.preview` 登记。
3. **清理批**：`Compose/` 分区迁入 `Workbench/`、控件库/受控属性去 Odin、窗口级 Stage 跟随补测试缝（注入 target binder）。
4. **基线维护**：页面数变化时同步 21 例中的 11 页/12 行断言与契约生成份数。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-23T18:55:29+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-23T18:57:37+08:00 · 完成「提出方案」→ 「实施任务清单」
- 2026-10-23T18:59:07+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-23T20:01:14+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-23T20:19:44+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-23T22:06:11+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-23T22:15:25+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-23T22:15:39+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-23T22:15:47+08:00 · 完成「回写与收尾」→ 完成
