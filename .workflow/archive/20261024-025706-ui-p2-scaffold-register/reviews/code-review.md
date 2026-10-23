# 代码评审（独立子代理）：UI 系统 P2-2：起页/登记拆分（ui.page-scaffold / ui.page-register）

> 运行 ui-p2-scaffold-register · 创建于 2026-10-23T22:34:41+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 代码基线：HEAD `c92da267e466ddf057b958190ff202f2d9a1eacf`；工作区 32 改 + 22 新（`git diff --stat` 约 +850/−1050），核心在 `Assets/LiteGame/Editor/UiTools/{Page,Contract,Workbench}`、`UiAtomicFile.cs`、新夹具、`scripts/codegen/_uiform_row.py`、`scripts/tests/test_uiform_row.py`、`scripts/gate/{toolchain.json,unity-tests.py}`、`.workflow/config.json`
- 子代理标识：`3d611bd1-61f5-4068-8cce-895a0e06ee35`（首轮）；同一子代理继续第 2 轮修复复评
- 子代理约束：只读评审，不修改生产代码、测试与门禁；限读 14 次（首轮）/8 次（复评）

## 变更范围

- 对照任务与方案：T1–T10 全部交付；方案 `.workflow/active/ui-p2-scaffold-register/plan.md` v4、验收 AC1–AC8、测试计划 S1–S5/C1–C5/R1–R12/W1–W7/P1–P8
- 变更文件/模块：起页内核/窗口、登记内核/窗口、表门面与 Python 助手、草稿契约链、清单/契约原子写、工作台接线、工具注册面、夹具与文档

## 正确性与边界

- 主路径：起页（基线复制→根契约/安全区→清单草稿→失败清理）与登记（契约门槛→Lua→表写→Luban/BuildHash→清单→契约刷新→hash 规则）均落到确定性断言；29/29 新夹具覆盖。
- 边界与错误路径：首轮发现 4 个阻塞（diff 真实→0、Python 写失败丢备份、行核验三态、测试自比较），已修复并加/改用例；另外修了工作台未应用编辑、Empty JSON 吞错、BindRoot NRE、注入守卫、Check fullScreen、起页异常归因、`UiContractBatch` 拆分等。
- 回归风险：契约 diff 口径变化仅影响“真实→0”场景（正确方向）；`Check` 端口签名变化已同步实现/夹具；`UiPageTablePort` 三态替换 `TryFindRow` 已全调用点迁移。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：AC1–AC8 主/负路径均有断言；表写“已落行但报失败”通过假端口注入；Luban/BuildHash 顺序/失败回滚；契约刷新失败；Unknown 读表失败；真实表/清单/绑定清单/11 份契约字节基线。
- 是否存在迎合实现的测试：首轮指出 1 处恒真断言（已改快照）；其余负例断言 `FailedAt`/错误文本/字节不变。

## 通用性与扩展性

- 根因与复用边界：三端口 + 请求注入，故障路径可扩展；基线为数据表；草稿层/全屏/缓存策略单源。
- 扩展点与接入方式：新增基线加一行；新增校验步骤加端口或 `FailedAt` 分支；契约新段加 `CompareXxx`。
- 硬编码、特判、魔法值：`ProcessTimeoutMs` 常量化；默认缓存策略改用 `UiPageContract.DefaultCacheStrategy`；layer/页名 C#/Python 双套规则与表路径双份仍存（工具边界，已知）。
- 重复逻辑与过度抽象：清单/契约原子写收敛到 `UiAtomicFile`；`UiContractBatch` DTO 拆分。
- 相关场景复用与回归影响：20 套 UI 回归逐套全绿。
- 命名、结构与复杂度：`UiContractBatch` 393 行（<400）；登记内核约 360 行线性流程，新增步骤需改方法体（已知）。

## 安全与性能

- 安全（注入、权限、敏感信息、输入边界）：表助手参数经 `Quote`；`--table` 副本隔离、备份目录随表、回滚只允许本表备份目录；不写真实清单/绑定清单/契约。
- 性能与资源：表助手/生成链为编辑器低频操作；夹具 29 例约 210s（进程调用占大头），可接受。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| B1 | 阻塞 | `UiContractDiff.cs:71` | 真实 formId→0 漏判破坏 | 只豁免 baseline=0；补断言 | 已修复 |
| B2 | 阻塞 | `_uiform_row.py:203` | save 异常丢 backup | 错误分支回传 backup + 原子写；Python 10/10 | 已修复 |
| B3 | 阻塞 | `UiPageTableHelper.cs:106` | 读失败与无行同值，复核假绿 | 三态 `UiPageRowLookup` + Unknown 显式报错/用例 | 已修复 |
| B4 | 阻塞 | 新夹具:369 | 表字节自比较恒真 | 写前快照比较 | 已修复 |
| C-5 | 重要 | `UiPageScaffold.cs` | Copy/Root 异常归因错、文案过度承诺 | 三段 try + 条件化文案 | 已修复 |
| C-6 | 重要 | `UiWorkbenchPageSection.cs` | 未应用编辑被保存静默丢弃 | 拒绝保存并提示先 Apply | 已修复 |
| C-7 | 重要 | `UiPageRegistration.cs` | 自定义 Table 绕过注入守卫 | 守卫覆盖 Table 端口 | 已修复 |
| C-8 | 重要 | `UiPageToolProcess.cs` | 空输出吞 stderr | 只接受含 `"ok"` 的 JSON | 已修复 |
| C-9 | 重要 | `UiPageRegistration.cs` | BindRoot NRE | prefab 判空 | 已修复 |
| C-10 | 重要 | `UiWorkbenchPageSection.cs` | ExternalChanged 无出路 | 提供放弃/重读按钮 | 已修复 |
| C-11 | 重要 | `UiPageTableHelper.cs` | Check 固定 fullScreen | 增参数传实际值 | 已修复 |
| C-12 | 重要 | `UiContractBatch.cs` | 479 行超红线 | DTO 拆出，393 行 | 已修复 |
| C-13 | 重要 | 进度章/夹具 | “已完成”超前、AC6 缺基线 | 状态降级 + 真实清单/绑定清单字节断言 | 已修复 |

## 复测记录

- 评审轮次: 2（首轮 FAIL → 修复 → 复评）
- 修复后执行的命令与结果：`UiPageScaffoldRegisterEditModeTests` **31/31**（约 210s）；Python `test_uiform_row` **10/10**（约 25s）；受影响 UI 回归全绿；随后 `wf verify` PASS。
- 复评子代理与复评范围：同一独立评审者，逐轮只核修复项（第 2 轮 B1–B4 + I1/I3/I5/I6/I7/I9；第 3 轮 R1/R2/R4/R5/R7；第 4 轮 R3 + 两处 residual）。
- 验证证据：evidence/verify.json

## 第 2 轮结论

**FAIL**（B1–B4 通过；I2 修复引入 R1 新阻塞，I1 文案未条件化，另 6 项残留）

| id | 级别 | 问题 | 处置 |
|---|---|---|---|
| R1 | 阻塞 | `HasUnappliedEdits` 比对 `_rows` 旧投影；`Apply` 只改 `_manifest` → “应用→保存”永远失败 | 改为对照 `_manifest` 条目；`TryApplyEdits` 同步编辑器字段与行投影；新增 Apply→Save 用例（已绿） |
| R2 | 重要 | `CleanupText` 常量仍无条件宣称已清理/恢复 | `CleanupFailed` 返回按实际结果拼的文案（prefab/清单各自成功与否），三处 catch 使用 |
| R3 | 重要 | 进度章仍写 28 例/Python 6 例 | 改 29 例 / Python 10 例（当前 31 个 `[Test]`：29 原 + 2 新增） |
| R4 | 重要 | RollbackAndVerify 的 Unknown 分支无用例 | 新增 `UnknownAfterRollbackTablePort` + “回滚后核验读表失败”用例 |
| R5 | 中 | `default` 落 Absent，枚举扩展会误报“已复核” | 显式 `case Absent`，`default` 报“结果未知” |
| R6 | 中 | Python chmod 用例跨平台不确定 | 标注 `skipUnless(win32)`（仓库/门禁为 Windows） |
| R7 | 中 | I6 按钮 `Reload(state)` 丢注入路径 | 改 `Reload(state, _manifestPath)`（错误态按钮同改） |
| R8 | 低 | `Contains("\"ok\"")` 子串误判 | 改 `TrimStart().StartsWith("{\"ok\"")` |

## 第 3 轮（复评）

- 范围：R1/R2/R4/R5/R7 + 新用例。
- 复评结果：**FAIL（仅 R3 文档计数；代码项全部 PASS，无代码阻塞）**。进度章仍写 29 例/29/29（实为 31 个 `[Test]`），要求改 31/31 并补“应用→保存”“回滚后 Unknown”描述、标注 23/25 为历史骨架口径。
- 非阻塞 residual：R1 回填原始入参（trim 后可能再误判）、R2 “已删除/已恢复”措辞、R6 Windows 口径、R8 BOM 边界。

## 第 4 轮（最终确认）

- 范围：仅核 R3 文档计数 + R1/R2 两处 residual 小改（`TryApplyEdits` 用 `applied` 回填；`CleanupFailed` 文案改“无残留”）。
- 复评结果：**PASS**。指定三段静态文本一致；无残留阻塞。
- 说明：第 3→4 轮代码只改了两处 residual；新夹具在改后实跑 **31/31**；第 4 轮评审者未重跑（其声明）。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 结论说明: 4 轮 FAIL → FAIL → FAIL(仅文档) → PASS；代码阻塞 B1–B4/R1 已修复，文档计数已闭合
- 阻塞问题: 无
- 历史阻塞: B1–B4、R1（均已修复并复评）
- 通用性: PASS
- 通用性说明: 三端口/基线表/草稿单源成立；表路径与页名/layer 双套规则为已知工具边界
- 扩展性: PASS
- 扩展性说明: 新增基线一行、契约段加 CompareXxx；登记为线性流程，新增步骤需改方法体
- 评审人: 独立子代理 `3d611bd1-61f5-4068-8cce-895a0e06ee35`
- 评审时间: 2026-10-24T02:45+08:00（第 4 轮）
