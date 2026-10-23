# 方案评审：UI 系统 P2-1 单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23
> 评审方式: 独立子代理（三轮）
> 评审输入: `plan.md`（v2 修订稿）· `acceptance.md` · `tasks.md` · 设计 §2/§3/§9 · 代码事实（`EditorToolHandlers`/`toolchain.json`/`UiPageWizardWindow`/`UiComposeWindow`/`UiPreviewEditModeTests`）

## 评审轮次与结论

| 轮次 | 子代理标识 | 结论 | 阻塞问题 |
|---:|---|---|---|
| 1 | `65be5816`（独立会话） | **FAIL** | 5 条：AC1 与保留 `ui.page-new` 自相矛盾且删宿主会断向导调用；透视迁移漏 `UiGenerateSection`（绑定生成/hash）与 `UiWidgetPropertySection`（受控属性）；Lane 矩阵缺 `GenerateBinding` 且无矩阵表；夹具与方案冲突（5 透视 vs 样式/预览槽位、State 返回值、透视工厂）；AC5 无真实 Generate 证据、AC6 命令不自足 |
| 2 | `be506f48`（独立会话） | **FAIL** | 收敛为：T7 未随“本批退役 page-new”更新（未列两窗口类与双向调用、测试断言、平台设计文档）；AC5 判定仍只写 diff；AC6 漏 `ui-workbench-editmode`；另指出预览入口无落点、存量分区用 Odin 与“零 Sirenix”口径冲突、`UiPageScaffold.Create` 死码 |
| 3 | `67f20f6c`（独立会话，定点复核） | 见下「第三轮」 | 仅核对上述闭环项 |

## 首轮问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | 阻塞 | AC1 要求“入口只剩 ui.workbench”但 `ui.page-new` 仍写表；删 `UiComposeWindow` 会断 `UiPageWizardWindow:105` 调用 | 用户裁决**本批退役 `ui.page-new`**；AC1 收窄为“制作/起页入口唯一”；T10 增列两窗口类/双向调用/测试断言/文档 | 已闭环（第 3 轮核对） |
| P-2 | 阻塞 | 透视迁移漏绑定生成/hash 与受控属性 | 范围表补映射：绑定透视＝`UiBindingsSection`+`UiGenerateSection`；控件库＝`UiWidgetLibrarySection`+`UiWidgetPropertySection`；新增能力标签并由夹具断言 | 已闭环 |
| P-3 | 阻塞 | 矩阵缺 `GenerateBinding`、无全动作×角色表 | 动作枚举补 `GenerateBinding`；plan 增 11×3 矩阵（含禁止原因）；`UiLanePolicy` 表驱动 + 夹具穷举 | 已闭环 |
| P-4 | 高 | 夹具与方案冲突（槽位/返回值/工厂） | 统一为 5 透视全启用；State 方法返回“是否变化”；透视不带工厂（宿主 switch 装配） | 已闭环 |
| P-5 | 高 | AC5 无真实生成证据；AC6 命令不自足 | AC5 增 `UiContractBatch.Generate(临时目录)` 集成用例（11 份/无 Error/逐字节一致）；AC6 增 `ui-workbench-editmode` 与显式命令 | 已闭环 |

## 第二轮问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-6 | 阻塞 | T7 未覆盖 page-new/两窗口/双向调用/预览测试/平台设计 | 新增 T10（退役波及面补遗）与 T11（验证补遗）；`UiPreviewEditModeTests` 改为 `ui.workbench` 在册、两者 null；平台设计条目改写 | 已闭环 |
| P-7 | 中 | 预览入口无落点 | 范围表补“上下文条「预览」按钮（`Preview` 动作）”，08 手册入口表同步 | 已闭环 |
| P-8 | 中 | 零 Sirenix 与存量 Odin 分区冲突 | 口径改为“宿主自身零 Sirenix；控件库/受控属性为存量 Odin，本批不改写”，风险表登记 | 已闭环 |
| P-9 | 低 | `UiPageScaffold.Create` 死码 | 保留为 P2-2 内核，注释挂批次号（红线 10） | 已闭环 |

## 第三轮（定点复核）

- 子代理：`67f20f6c`（独立会话，只读）
- 核对结果：
  - **T7 退役波及面**：PASS——handler/清单/预览测试均已改；两窗口类删除后无活引用；T10 覆盖预览断言与平台设计条目。
  - **AC5/AC6 证据**：FAIL（**证据态**，非方案缺陷）——AC5 真实 Generate 与 AC6 四命令已入验收，但当时 `state` 的验证已过期、T9/T11 未完成、build-log 存在 30/30 与 31/31 的两条历史记录冲突。处理后：最终代码冻结后复跑全量 `wf verify`，T9/T11 完成，build-log 按事实改写。
  - **方案与实现口径**：PASS——5 透视全启用无禁用槽；State 四方法返回 bool；预览入口在上下文条；Odin 口径写明。
- 遗留（第三轮点出）：`UiPageScaffold.Create` 缺 P2-2 注释 → 已补（`UiPageScaffold.cs`）。

## 结论

- 方案结论: **PASS**（首轮/二轮阻塞项已闭环；第三轮仅剩证据态问题，已由最终验证与 T9/T11 收口）
- 阻塞问题: 0（方案层）
- 验收标准可判定性: AC1–AC6 均映射到夹具/命令/人工冒烟（`acceptance.md`），逐条可判定
- 说明: 本项目 `.workflow/config.json` 为 8 阶段配置（无独立的 plan-review / accept 阶段门），本文件与 `acceptance.md` 按技能要求补记，评审仍由独立子代理执行
