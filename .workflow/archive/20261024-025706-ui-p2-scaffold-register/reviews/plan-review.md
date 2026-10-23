# 方案评审（独立子代理）：UI 系统 P2-2：起页/登记拆分

> 运行 ui-p2-scaffold-register · 创建于 2026-10-23
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。
> 评审轮次：第 1 轮（方案 v1）→ FAIL；方案 v2 修复后待复评。

## 评审输入

- 评审对象：`plan.md`（方案 v1，2026-10-23T22:34 稿）
- 代码/文档基线：`git rev-parse HEAD` = c92da267e466ddf057b958190ff202f2d9a1eacf，工作区干净（仅本运行目录未跟踪）；P2-1 提交 92361185
- 评审范围：方案全文；设计 §3.1/§3.6/§9.1/§9.2、UI 总设计 §12 P2、08 手册 §1/§3/§5/§6、修改指南相关行、P2-1 归档方案；相关编辑器代码与既有夹具（`UiPageScaffold`、`UiPageManifestSync/Editor/Json`、`UiWorkbenchPageSection/State`、`UiContractBatch/Generator`、`UiBindGenerationHash`、`_uiform_row.py`、`PageStructureRule`、toolchain 清单）
- 不在范围：未运行 Unity EditMode/PlayMode/wf verify/eval_file；未做 UI 点击/视觉/安全区运行验证；未逐节通读全部手册；未执行新 test/真实 Luban；未实际编译新增 handler
- 子代理标识：`ed381cc5-3c9b-428c-93b8-3968cd70ee43`
- 子代理约束：只读评审，不修改生产代码、测试与门禁；未改文件（`git status` 仅本方案目录未跟踪）

## 方案检查

- 目标可观察、范围与非目标清晰：**部分满足**。起页/登记产物边界清楚，但 AC1/AC3/AC8 含“可点/置灰/真实点击”等不可自动判定项，需拆成“脚本可证 + 人工确认”。
- 现状与根因有代码/配置/文档依据：**满足**。方案引用的现状（P2-1 置灰、`Create` 一键、`_uiform_row.py` 硬编码、基线缺根组件）经核对成立。
- 候选方案取舍充分，推荐理由回应约束：**基本满足**；但“唯一制作入口”与“两个 window 进 Hub/CLI”口径冲突未解释（I4）。
- 设计覆盖接口、数据、关键流程、失败与边界：**不满足**（B1 契约门槛缺失、B2 设计输入裁剪、B4 原子性不成立）。
- 通用性与扩展点明确，非特判式补丁：**部分满足**。表/基线/根契约/路径注入方向成立；C#/页面类型/缓存未建模，`EnsureRootContract` 只覆盖 Page 根，`PageRow` 是破坏性类型移动。
- 测试策略能先失败后通过，覆盖主路径、边界与错误路径：**不足**。负例在“骨架恒失败”下会假绿；缺故障注入（清单写盘、表进程被杀、同步冲突）与契约/缓存/安全区用例。
- 风险、监控与回滚可执行：**部分满足**。Luban 回滚方向正确；清单无原子替换/快照；“纯增量回滚”表述不实。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 起页可点、产物、不写表/Lua | 门控 + EditMode + 表字节 | 部分成立 | 产物/根组件/零变化可测；“按钮可点”不可自动判定；安全区“内容在容器内”未断言 |
| AC2 登记写表/Lua/Luban/清单/hash | 临时表 + 注入 + 假 Luban | 部分成立 | 表/备份/Lua/清单可测；真实 Luban 只能命令/人工；缺契约门槛；无 BindRoot 仍记 hash 与 08 冲突 |
| AC3 Lane 门控与置灰 | 矩阵/原因 | 部分成立 | 矩阵/原因可测；实际 DisabledScope/tooltip 不可读 |
| AC4 失败语义 | 夹具 | 不成立 | 清单写盘失败、Scaffold 中途失败、表进程被杀无注入点 |
| AC5 工具在册 | Catalog/check/docs | 成立 | 现有 Catalog 无精确计数，新增不预期破坏 |
| AC6 夹具/回归/数据零变化 | wf verify + 字节 | 成立（有前提） | 需清理 Screens 测试 prefab，避免污染 302 基线 |
| AC7 文档回写 | doc-lint | 成立（文档面） | 只能证格式/索引，不能证设计未被反向修改 |
| AC8 窗口冒烟 + 人工点击 | eval_file + 人工 | 不成立（自动化） | 只能断言窗口对象/标题；真实点击只能人工 |

判定不了的条目：AC1“可点”、AC3 实际置灰/tooltip、AC4 清单写盘失败、AC2 真实 Luban、AC8 真实点击。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| B1 | 阻塞 | 登记缺“契约门槛”：预检/AC2 未查契约文件或 `ContractPresent`；设计 §3.6 输入含已评审契约、§3.5“无契约不交接”，P2-1 归档把“契约门槛生效”列为 P2-2 退出条件 | 方案 v2：预检用 `UiContractStore.Load` 校验契约存在/可读/`page.name` 匹配，失败零产物；AC2/AC4 加正负例 | 已修（待复评） |
| B2 | 阻塞 | 设计输入被单方面裁剪：只做 Lua/默认 Lru/无页面类型/目标模块，却称“无待确认问题” | 已提审用户并获裁决 **B**：缓存策略可选、写清单、对账保留；C# 与页面类型/目标模块作为明确边界写入设计与进度（不反向改设计迎合实现） | 已修（用户已裁决） |
| B3 | 阻塞 | 工作台/外窗同步丢更新：未 Apply 文本不标脏；`TrySave` 不比较磁盘 `sourceHash`，可覆盖登记结果；`Reload` 重置选中 | 方案 v2：输入变化即标脏；保存前磁盘指纹不一致则拒绝并提示；`ReloadIfChanged` 保留选中/目标；外部变更未保存时不重载；补冲突/重载夹具 | 已修（待复评） |
| B4 | 阻塞 | 原子性不成立：清单非原子写；`Scaffold` 只覆盖一种失败；表进程保存后被杀会“判表未变但实际留行” | 方案 v2：清单临时文件+原子替换；Scaffold 全流程 try/finally 清理；表失败 `TryFindRow` 核验→回滚→复核；Luban/BuildHash 失败回滚+复核+重跑 Luban；清单失败显式给恢复指令 | 已修（待复评） |
| I1 | 重要 | 安全区可能只补空 `Cot_SafeArea`，内容仍不在容器内 | 方案 v2：优先挂现有 `Cot_*` 拉伸容器（BaselineA/B→`Cot_Baseline`），无容器则新建并搬入子节点（ListScreen）；夹具断言内容为 Receiver 后代 | 已修 |
| I2 | 重要 | `PageRow` 搬迁调用点/夹具未列全 | 方案 v2 列出完整迁移表（Sync/ContractGenerator/ContractBatch/两个夹具） | 已修 |
| I3 | 重要 | `--table` 隔离不足（rollback/delete 仍用生产表）；真实 Luban 默认危险；测试 prefab 落生产 Screens | 方案 v2：`--table` 覆盖全部分支、备份目录随表；注入表路径必须同时注入 codegen；夹具 try/finally 清理并断言无残留 | 已修 |
| I4 | 重要 | 两 window 与“唯一制作入口”口径冲突 | 方案 v2 明确：工作台＝唯一编排入口；两窗口为设计 §9.1 的独立工具、可直开；08/设计同步改写 | 已修 |
| I5 | 重要 | layer 只校验 ≥0 | 方案 v2：C#/Python 统一 0–3，加越界负例 | 已修 |
| I6 | 重要 | 无 BindRoot 仍记 `bindroot|none`，与 08 §6.2 冲突 | 方案 v2：仅挂 `BindRoot` 才记录；路径 A 跳过并记录“不伪造 hash”；AC2 区分 | 已修 |
| I7 | 重要 | 真实 Luban 后未列 BuildHash 复算 | 方案 v2：代码生成链 Luban → `codegen.build-hash`；收尾/AC6 跑 `gen-build-hash.py --check-shared` | 已修 |
| I8 | 重要 | “纯增量回滚”不实 | 方案 v2 改为 revert 本批提交，并说明操作级恢复路径 | 已修 |
| S1 | 建议 | `Open(string prefill = null)` 不能直接转 `Action` | 提供无参 `Open()` 与 `Open(prefill)`，handler 注册无参重载 | 已采纳 |
| S2 | 建议 | `SuggestId` 注释与 Python“最小空闲 id”不一致 | 统一注释/口径（Python 仍为单源） | 已采纳 |
| S3 | 建议 | `ReservedIdFloor` 未使用 | 删除（保留 id 只由 Python 助手单源） | 已采纳 |
| S4 | 建议 | `--delete-id` 未纳入能力表 | `UiPageTableHelper.Delete` 纳入；Python 测试覆盖 | 已采纳 |
| S5 | 建议 | `PAGE_RE` 允许小写 | Python 改 `Page_[A-Z][A-Za-z0-9]*`，C#/Python 一致 | 已采纳 |
| S6 | 建议 | AC8 把“窗口打开”等同“可点” | 拆为脚本可证（标题/字段/Console）与人工点击记录 | 已采纳 |

## 评审结论（第 1 轮，方案 v1）

- 评审方式: 独立子代理
- 结论: **FAIL**
- 验收标准: 部分成立（AC1/AC2/AC3 部分、AC4 不成立、AC5–AC7 成立、AC8 自动化不成立）
- 阻塞问题: 4（B1–B4）
- 通用性: 部分满足（C#/页面类型/缓存未建模需裁决；`PageRow` 破坏性移动）
- 扩展性: 部分满足（表/基线/根契约方向成立；`EnsureRootContract` 只覆盖 Page）
- 测试策略: 覆盖不足（负例假绿、缺故障注入/同步冲突用例）
- 回滚: 仅提交级 revert 可执行；操作级缺清单快照/原子替换
- 评审人: 独立子代理 `ed381cc5-3c9b-428c-93b8-3968cd70ee43`
- 评审时间: 2026-10-23T22:59+08:00

## 复评记录

### 第 2 轮（方案 v3，子代理 `c8c6446f-aa5e-4d8b-87fb-d828946ff697`）

- 结论：**FAIL（接近通过）**；6 点中 3 点已闭环、3 点部分。
- 仍阻塞：
  1. 草稿契约 `layer` 默认值在方案内自相矛盾（0 vs 1）。
  2. 表写“已落行但报失败/超时”与契约刷新失败无注入缝，AC4 不可确定性验证。
- 其他：契约门槛未绑 `prefabGuid`；契约 `Save` 非原子；清单原子写需保留 `IsValidFolder/Refresh` 分支；嵌套 `Cot_Baseline` 组件 override 需写明验收口径；`--check` 为 dry-run 标志需保持同源校验。
- 处理：方案 v4 已修订——`DraftLayer/DraftFullScreen` 单源；新增 `IUiPageTablePort`（`Write` 返回 `{Ok,BackupPath,Error}`，备份先于写）与 `IUiPageContractRefresher`；`FailedAt.ContractRefresh`；回滚复核失败分支给出恢复指令；预检比对 `prefabGuid`；`UiContractStore.Save` 原子化。

### 第 3 轮（方案 v4 两处修复，子代理 `d0800f64-bf4a-4d29-ac8b-73f930f9183c`）

- 结论：两处阻塞均 **PASS（已闭环）**；A（layer/fullScreen 单源）与 B（故障注入缝）均有位置证据。
- 新建议：`Write` 失败回传备份句柄（已采纳：`UiPageTableWriteResult`）；`FailedAt.ContractRefresh`（已采纳）；回滚仍见行分支（已写恢复指令）；常量可追溯（已写）。
- 复评未覆盖：未运行 Unity/Python 测试，未实际编译（施工后由 `wf verify` 与代码评审覆盖）。

## 最终评审结论（方案 v4）

- 评审方式: 独立子代理（3 轮）
- 结论: **PASS**（阻塞 0；第 3 轮确认两处修复闭环）
- 验收标准: AC1–AC8 可判定性经用户确认（`acceptance.md`）
- 阻塞问题: 0
- 残余风险: 已记入 plan “风险与回滚”7 条
- 评审人: `ed381cc5-3c9b-428c-93b8-3968cd70ee43`（首轮）、`c8c6446f-aa5e-4d8b-87fb-d828946ff697`（第 2 轮）、`d0800f64-bf4a-4d29-ac8b-73f930f9183c`（第 3 轮）
- 评审时间: 2026-10-23T23:34+08:00
