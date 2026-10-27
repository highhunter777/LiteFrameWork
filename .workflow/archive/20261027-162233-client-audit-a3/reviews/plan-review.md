# 方案评审：客户端审计 A3——三大件拆分

> 运行 client-audit-a3 · 评审对象 `plan.md`

## 评审输入

- 代码基线：`0837cc0d`（评审时工作区含并行线服务端审计文档改动；A3 相关源文件未改动）
- 方案：`.workflow/active/client-audit-a3/plan.md`
- 代码与依据：`BattleViewRig.cs`（1058 行）、`UIBindIndex.cs`（697 行）、`StageMachineCore.cs`（374 行）、`HierarchicalStageMachine.cs`（481 行）、消费面与 `Assets/Tests`、`Tests/LiteFramework.Core.Tests`、`Docs/审计/客户端/批次计划.md`、`Docs/修改指南.md` §1.6
- 评审清单：`.agents/skills/staged-delivery/references/artifact-quality.md` 的「方案（plan.md）」「方案评审」
- 先行红测：`.workflow/active/client-audit-a3/red-structure-check.py` 与 `evidence/red.json`
- 子代理标识：Qoder 独立子代理会话（只读，共 6 轮：5 轮 FAIL 收敛、第 6 轮 PASS）

## 方案检查

- 目标可观察：三组协作件外提、主类职责收敛、公共 API 不破、行为零变化；范围/非目标清晰。
- 现状与根因有代码依据：三个目标文件行数、职责清单、消费面与测试面均已核对；`BattleViewRig` 仅 `ProcedureBattle` 消费（internal），`UIBindIndex` 公共面与 4 个测试类、`StageMachineCore` 仅两派生类。
- 取舍充分：门面 + 内部协作件（推荐）对比端口接口化、原地分区；拒绝过度抽象的理由成立。
- 设计覆盖失败与边界：取消透传、逐项降级/整组不注入/逐条降级、HUD 孤儿清理、UIBind 门面转发、FSM 构造序与初始化守卫、`.meta` 生成、并行线文件隔离。
- 相位表逐行对应 `BattleViewRig.cs` Tick(431-488)/Dispose(535-613)（6 相位 / 20 步；Tick⑥ 指针锁定在末尾、Dispose⑪ HUD 根件在 laser/protection 之后、⑯ 传送先于诊断件）。
- 通用性与扩展点：新增表现件挂对应装配器；新增绑定域挂 `UIBindEvents`；树语义只动层级机；取件助手统一降级口径。
- 风险与回滚：五类风险均有监控；按子批代码文件集回退（含新增件 `.meta`），文档回写单独回退。

## 验收标准可判定性

| AC | 判定 | 判定方式 |
|---|---|---|
| AC1 | 可判定 | 结构探针（63 项，基线红/施工后绿）+ 代码评审 |
| AC2 | 可判定 | `git diff` 逐项核对 public 成员；派生类 grep；编译 |
| AC3 | 可判定 | `unity command recompile` + `recompile_status failed=false` + `console` |
| AC4 | 可判定 | L1（PullRequest）+ 27 个定向 EditMode 类；失败项 pristine 对照 |
| AC5 | 可判定 | 相位表逐行对 diff + Play 冒烟日志/截图 + `find_gameobjects` Dispose 正证据 |
| AC6 | 可判定 | 5 条登记删除/归档追加；批次计划 A3 4→5、A5 10→9、未结清 127→122；doc-lint |
| AC7 | 可判定 | 每子批 `git status` 快照差集（`evidence/a3-N-diff.txt`）无交集 |

## 发现的问题与处理

| 轮 | 结论 | 阻塞问题 | 处理 |
|---:|---|---|---|
| 1 | FAIL | 相位端口缺失、A3-1 无验证实据、A3-2 依赖未定义、取件语义未说明、A3-3 readonly/静默退化、回写漏条、AC 口径 | 相位表、Play 冒烟、构造契约、取件助手、初始化守卫、5 条回写与 A5 计数、AC 重写 |
| 2 | FAIL | red 门禁无红测、相位表 3 处不符、Play 无 Dispose 正证据、AC6 计数、工作区/定向类 minor | 结构探针（真红）、相位表逐行核对、GM 请求桥 + `find_gameobjects`、127→122、定向类另跑 |
| 3 | FAIL | 探针漏字段、无转绿步骤、A3-2 归属、AC6、基线/守卫 minor | 补 63 项断言、`structure-green.txt` 步骤、`internal` 化、计数、Snapshot/ExportGraph 守卫 |
| 4 | FAIL | 探针覆盖不足且只查 private | 补视角域/租约/HUD 根件字段 |
| 5 | FAIL | 探针与方案命名冲突（`_hud`/`_locale`）、只按 private/protected readonly 匹配 | 统一协作件命名（`_viewAssembler` 等）；探针改声明式正则；AC7 改差集 |
| 6 | **PASS** | 无 | 探针真红 63 项、可红转绿；相位表逐行相符；可实施 |

非阻塞附注（施工时留意）：`BattleViewAssets` 的持有状态与 Dispose ⑨–⑮⑳ 清除归属按“取件后移交”写明；⑳ 租约清空经 `_assets`；AC7 差集建议叠文件哈希。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- **可实施性**：三子批文件集互斥、结构探针可红转绿、回滚边界清晰；按方案可开工。
