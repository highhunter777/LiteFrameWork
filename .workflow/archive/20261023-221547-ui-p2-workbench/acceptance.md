# 验收标准确认：UI 系统 P2-1 单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | **单一入口**：`scripts/gate/toolchain.json`、`EditorToolHandlers`、工具索引、`08-UI 制作与调试` 手册中制作/起页入口只有 `ui.workbench`（起页按钮 P2-2 接入）；`ui.compose` 与 `ui.page-new` 在代码/清单/测试断言中退役，无第二个制作窗口或起页向导 | `python scripts/toolchain.py check` 0 error；`UiPreviewEditModeTests`/`UiWorkbenchEditModeTests` 断言 `ui.workbench` 在册且 `ui.compose`/`ui.page-new` 不在册；全仓 grep 无活引用（归档/历史文档除外） | T7、T8 |
| AC2 | **五透视可用**：页面/控件库/绑定/契约/校验在同一窗口切换，共享页面选择、目标 prefab 与 Lane；切换不丢上下文 | `UiWorkbenchEditModeTests` 状态切换用例；手动冒烟：Hub 打开工作台、逐透视切换、选中真实页面后切回不丢目标（Console 无 error，状态行有 `[ui/workbench]`） | T6（T2/T3/T4/T5 供内核） |
| AC3 | **Lane 门控**：美术不可登记/绑生成，工程不可起页；禁用项有原因文案；矩阵覆盖全部动作×Lane | `UiWorkbenchEditModeTests` 矩阵用例（全动作×三角色，断言允许/禁止与原因非空） | T2、T6 |
| AC4 | **页面透视**：真实清单 11 页/12 行只读展示；Owner/设计稿/备注/状态编辑落盘确定性（重复保存逐字节一致、`sourceHash` 重算） | `UiWorkbenchEditModeTests` 清单投影用例（只读真实清单）+ 临时路径保存确定性用例；不写真实清单 | T4 |
| AC5 | **契约透视**：全量生成（写临时目录 11 份、无 Error、重复逐字节一致）与基线 diff 可跑；报告含页数/跳过/破坏性/输出目录；失败显式 | `UiWorkbenchEditModeTests`：报告投影 + **真实 `UiContractBatch.Generate(临时目录)` 集成用例**；手动冒烟跑一次 `ui.contract-diff`（只读） | T5、T6 |
| AC6 | **退役与文档**：`08` 手册去掉旧工具过渡说明；技术总结 04、修改指南工具表、`开发工具链平台专项设计`、工具索引、施工进度新章同步；`toolchain check` 0 error、`doc-lint` 0/0、`ui-workbench-editmode` 全绿、L1 全绿 | `python scripts/gate/doc-lint.py`；`python scripts/toolchain.py check`；`python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests`；`python scripts/gate/test.py --lane L1 --profile PullRequest` | T7、T8、T9、T11 |

## 提审记录

- 提审时间：2026-10-23 20:05（Asia/Hong_Kong）
- 提审方式：对话中结构化提问（单题列出 AC1–AC6 + 判定方式，选项「确认 AC1–AC6」/「AC 需调整」）
- 用户原话或选择：**选择「确认 AC1–AC6（推荐）」**；此前同轮已就范围裁决答复「同意『本批只做宿主、透视和 Lane，起页登记拆分留给 P2-2』」

## 用户确认

- 确认结论: 通过
- 确认人: 用户
- 确认时间: 2026-10-23 20:05
- 变更要求：无

## 变更记录

- 2026-10-23 20:20 · AC1 口径按独立评审与用户裁决收窄：**本批连 `ui.page-new` 一并退役**（用户选择「本批就退役 ui.page-new」），AC1 由“制作入口只有 ui.workbench”改为“制作/起页入口只有 ui.workbench（起页按钮 P2-2 接入）”。理由：`ui.page-new` 写表违反设计 §3.1/§3.6，保留会与“单一制作入口”自相矛盾；代价（P2-2 前无起页工具）用户已知情接受。其余 AC2–AC6 不变。
- 范围裁决 A1 已由用户确认：起页/登记拆分留 P2-2；样式令牌/预览矩阵留 P2-3。
