# 代码评审：UI 系统 P2-1 单工作台与角色 Lane（宿主 + 透视迁移）

> 运行 ui-p2-workbench · 创建于 2026-10-23
> 评审方式: 独立子代理（四轮；主代理只落盘子代理结论与修复记录）

## 变更范围

- 基线 HEAD=`4bb74642`；工作区未提交（提交时按 hunk 拆分，共享文件只取本批改动）。
- 新增：`Assets/LiteGame/Editor/UiTools/Workbench/`（宿主/内核/页面透视/契约透视/报告视图/设置）、`Assets/Tests/EditMode/UiWorkbenchEditModeTests.cs`。
- 删除：`UiComposeWindow.cs`、`UiPageWizardWindow.cs`（及 meta）。
- 修改：`EditorToolHandlers`、`UiPageScaffold`（P2-2 注释）、`UiPreviewEditModeTests`、`toolchain.json`、`.workflow/config.json`、7 份文档。
- 范围外改动未纳入评审（并行 DevMenu/调参/测试房语义批）。

## 正确性与边界

- 经四轮评审确认的主路径：选页 → `PageSelected` → 宿主 `BindTarget`（目标单源）；打开/关闭 Prefab Stage 的目标回绑；页面清单编辑原子（副本应用，失败不改内存）；显式保存与关闭提示；契约生成/diff 复用 `UiContractBatch`。
- 边界：缺清单/损坏显式错误；无目标 prefab 占位；保存失败不写盘；Stage 关闭回绑资产根；另一个 Stage 打开时不覆盖已选目标（选页时切到所选页 Stage）。

## 测试充分性

- 21 例 EditMode：矩阵逐项允许集、能力标签精确集合、状态上下文、真实清单 11 页/12 行、缺清单、原子迁移失败、选中回调与目标绑定、契约生成到临时目录（11 份/逐字节一致）、真实 `UiContractBatch.Diff`、Stage 目标解析、内存设置往返、工具面与退役。
- 先红证据：10/10 断言失败（`evidence/red.json`）；后加 11 例为评审期间补强（不改变红基线）。
- 局限：窗口 `FollowTarget/OpenTargetStage` 调用点无测试缝（靠评审 + 冒烟）；逐透视点击未自动化。

## 通用性与扩展性

- **通用性 PASS**：Lane 矩阵表驱动、原因与 UI/夹具同源；状态纯类；清单/契约内核复用，无第二份生成逻辑。
- **扩展性 PASS**：透视绘制表 `_drawers`（无 switch）、`Enabled` 参与过滤、能力标签进页签 tooltip、`SettingsFactory` 可注入；新增透视/动作改表即可。
- 扣分项（非阻塞）：`Ordered` 静态初始化一次；窗口级 target binder 未抽成可测端口。

## 安全与性能

- 写盘路径限定 `Assets/` 下（清单/契约/临时目录）；删除资产走 unity-pipeline；无敏感信息；不引入运行期依赖（编辑器程序集）。
- 兼容：`ui.compose`/`ui.page-new` 退役属破坏性 UI 工具变更，已同批改测试断言、工具索引、手册与修改指南；`UiPageScaffold` 保留给 P2-2。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| B1 | blocker | 选页只改 state，窗口目标不更新 → 控件库/绑定/校验拿 null | 页面透视加 `PageSelected` 事件；窗口订阅并 `BindTarget`；夹具 `页面透视_选中回调与目标绑定` | 已修，第 2 轮复评闭环 |
| B2 | major | 页面编辑未受 `EditMetadata` 门控（TA 可保存）；`OpenPage/Validate/EditBinding` 未接线 | 编辑区 DisabledScope + 原因；OpenPage `GuardedButton`；绑定/校验 DisabledScope | 已修，第 2 轮闭环 |
| B3 | major | 关闭/刷新无未保存提示 | `OnDisable` 保存/丢弃（排除编译/Play）；刷新前确认；`TrySave` 统一入口 | 已修，第 2 轮闭环 |
| B4 | major | 编辑非原子（状态失败留半改） | `TryApplyEdits` 在 JSON 副本上应用，全成才替换+标脏；夹具覆盖非法迁移 | 已修，第 2 轮闭环 |
| B5 | major | 工作区混并行线，整文件提交会缺依赖编不过 | 提交时按 hunk 拆分（共享文件只取本批改动） | 提交阶段处理 |
| B6 | major | 验证证据 stale / 任务未完成 / build-log 自相矛盾 | 代码冻结后重跑全量 verify；T9/T11 完成；build-log 按事实重写 | 已处理 |
| R1 | blocker（回归） | 打开 Stage 后目标不回绑（ViaStage 仍 false，属性/生成写资产根） | `OpenTargetStage` 后 `BindTarget`；`FollowTarget` 补回绑；`OnFocus` 跟随；夹具 `目标解析_Stage打开时指向Stage根` | 已修，第 3 轮复核通过 |
| R2 | blocker（回归） | 关 Stage 后目标悬空（DisplayRoot 已销毁、头显“Stage 已打开”） | `FollowTarget` 无 Stage 且 `ViaStage` 时回绑资产根；另开 Stage 且目标悬空（`DisplayRoot==null`）时回绑自己的资产根，不跳转到 B | 已修，第 4 轮 FAIL → 第 5 轮复核 |

## 复测记录

| 轮次 | 子代理 | 结论 | 关键发现 | 复测命令/结果 |
|---|---|---|---|---|
| 1 | `a057d042` | FAIL | B1–B6；通用性 PASS / 扩展性 FAIL / 测试有效性 FAIL | 修复后 `UiWorkbenchEditModeTests` 20/20 |
| 2 | `7473f088` | FAIL | B1–B4 闭环；新阻塞 R1（Stage 不回绑） | 修复后 21/21（含 Stage 解析） |
| 3 | `f45ca661` | FAIL | R1 修复成立；新阻塞 R2（关 Stage 悬空）；1–3、5 通过 | 修复后 21/21 |
| 4 | `001da56d` | FAIL | R2 修复只覆盖「无 Stage」；另开 Stage 时目标 DisplayRoot 已销毁仍悬空（头显/属性树误报） | 修复后 21/21 |
| 5 | `a808150a` | **PASS** | 目标悬空分支成立（DisplayRoot 空/ViaStage 回绑自己的资产根，不跳 B）；无递归；B1–B4/哨兵/21 例未受影响 | 修复后 21/21；全量 verify PASS |

- 全量验证：`wf verify` **PASS 31/31**（代码冻结后最终一次，证据 `evidence/verify.json`；`ui-workbench-editmode` 21/21）。
- 门禁：`doc-lint` 0/0；`toolchain.py check` OK（1 条既存未注册脚本告警）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 测试有效性: PASS（窗口调用点覆盖为已知局限，非阻塞）
