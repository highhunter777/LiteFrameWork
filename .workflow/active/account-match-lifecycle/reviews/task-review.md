# 任务评审（独立子代理）：Account/Match 名称与生命周期纠偏（第二批）

> 本文件由主代理根据三轮独立子代理返回整理落盘；不是主代理自评。

## 评审输入

- 评审对象：`tasks.md`（T1～T9）、`test-plan.md`（21 条）、`plan.md` v7、`acceptance.md` v7、运行配置。
- 基线：工作区快照；T1 契约与 T2 改名已落（评审时部分任务已完成，证据一并核对）。
- 子代理：第一轮 `73976624-d799-401b-a1f1-15c457dac966`；第二轮 `241984b2-f809-4c5a-8f45-632af0c553af`；第三轮 `70d34fab-920e-46da-aadd-49a9fc8a17b5`（均只读）。
- 约束：不修改生产/测试/门禁，不运行 Unity。

## 任务检查

- 粒度与依赖：FAIL → 修正（T1 契约、T2 仅生产改名、T9 测试机械同步、T8 门禁配置、T3 测试先行、T4/T5 生产、T6 文档、T7 验证）。
- 验收映射：AC1→T2/T9/T6；AC2→T3/T4；AC3/AC4/AC5→T3/T5；AC6→T7/T8；AC7→T6；AC8→T4/T6。
- 测试覆盖：21 条用例分 L1/EditMode；域生命周期在真实 `MetaSegment/RootScope`，L1 只测 `AccountReentry` 顺序。
- 写集分离：T9 仅符号替换；T3 只写测试；T4/T5 只写生产；T6 文档。

## 验收映射

| AC | 任务 | 判定 | 说明 |
|---|---|---|---|
| AC1 | T2/T9/T6 | PASS | 生产 rg 词边界 0（已实测）；文档/units 归 T6 |
| AC2 | T3/T4 | PASS | 真实装配 + 401×4 + 门面 + 软硬关闭 |
| AC3 | T3/T5 | PASS | Match 域 + LeaveMatchAsync 四终态（含 !Started） |
| AC4 | T3/T5 | PASS | 100 局 + 无重名断言 |
| AC5 | T3/T5/T7 | PASS | 除 401 外行为不变；真实 UI 不做 |
| AC6 | T7/T8 | PASS | 19 commands / 5 red；`--root` 统一 |
| AC7 | T6 | PASS | 文档语义回写 + domain-matrix |
| AC8 | T4/T6 | PASS | 消费者/释放者 + 守卫 |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| B1 | blocker | T2/T9 验收闭环断裂（测试同步与依赖相反） | T2 收敛仅生产改名 + rg/GUID；T9 独占测试同步 + 编译；T3 依赖 T9 | 关闭 |
| B2 | blocker | T1 契约缺 `AccountReentry` async 形状 | T1 补新构造（`IRoomFlow` + `IAccountDomain`）与 `LeaveMatchAsync` 调用；旧 2 参构造保留 | 关闭 |
| B3 | blocker | T3 缺 `AccountDomainTests.cs`、非法路径、L1 假域 | 新文件归属 T3；域生命周期移 EditMode 真实装配；L1 仅重入顺序 | 关闭 |
| B4 | blocker | 配置单源不一致（仓库/外部 root） | T3/T7/T8 统一 `--root`；修 `net-session-regression` 命令（csproj + 新过滤）；T8 用 `config show --json` | 关闭 |
| B5 | blocker | T9 类名“保留”与“旧名零命中”矛盾 | 类名随文件改 `MatchSessionFactoryTests`，net 过滤同步；T9/T8 元数据已改 | 关闭 |
| B6 | blocker | T3 用例 19 的 Close 不可观察 | T1 新构造注入 `IAccountDomain`，`ReturnToLoginAsync` 在 LeaveMatchAsync 后 Close（桩） | 关闭 |
| B7 | blocker | 外部 config `net-session-regression` 损坏 | 重建命令（`dotnet test Tests/LiteNet.Tests.csproj --filter MatchSessionFactoryTests|...`） | 关闭 |
| M1-M7 | major | !Started、真实装配、T9 边界、生产 rg、过滤名、AC8 守卫、red-manifest | 已落 tasks/test-plan | 关闭 |
| m-1..m-5 | minor | test-plan 401 计数/18-19 落点/17b 注入、T5 移除 LeaveMatch、T4 验收补 token | 已修 | 关闭 |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: `73976624…` / `241984b2…` / `70d34fab…`
- 评审时间: 2026-11-09T03:20+08:00
- 备注：第三轮给出最小修复集（B5/B6/B7 + test-plan 矛盾），主代理逐条落实并复核（T9/T8 元数据重开更新、config 重建、T1 构造补 `IAccountDomain`、test-plan 修正）；按“最小修复集落实即收口”处理，不再开第四轮。

## 检查项

- 任务粒度：T1 契约、T2 生产改名、T9 测试同步、T8 门禁、T3 测试先行、T4 账户域、T5 Match/Battle、T6 文档、T7 验证；单任务可独立验收。
- 依赖顺序：T1→T2→T9→T3→T4→T5→T6→T7；T8 独立、T3/T7 依赖；无环、无反向。
- 写集：T2/T4/T5 生产，T3 测试，T9 机械同步，T6 文档；无重叠。
- 测试先行：T3 在空桩上写断言级红；红命令五条（account-domain/meta-entry/room-flow/battle/match-loop）。
- 验收命令：可在干净 shell 执行；T7 用完整 `workflow.py --root ... verify`；T3 含 `verify --red`。

## 覆盖与追溯

| AC | 任务 | 用例（test-plan） | 证据 |
|---|---|---|---|
| AC1 | T2/T9/T6 | 21 | rg 词边界 0、GUID 回执、domain-matrix |
| AC2 | T3/T4 | 1-9 | MetaEntry EditMode + 反射/Log sink |
| AC3 | T3/T5 | 12-14/17/20 | RoomFlow 26 + 终态用例 |
| AC4 | T3/T5 | 15/16 | MatchLoopLeak + 无重名 |
| AC5 | T3/T5/T7 | 7/8/10/11/18-20 | 自动化 + diff |
| AC6 | T7/T8 | red/green | verify.json / red.json |
| AC7 | T6 | — | Doc + 实读 |
| AC8 | T4/T6 | 21 | 守卫 + 装配 |
