# 任务评审：大厅进房离房链路样板简化

## 评审输入

- 方案、验收标准与任务清单：`.workflow/active/lobby-room-flow/plan.md`、`acceptance.md`、`tasks.md`；方案复评：`reviews/plan-review.md`（独立子代理 Leibniz，PASS/0 阻塞）。
- 基线：HEAD `d4f284c79adb3852b3eb68f7303d439cb4def969`，工作区 3487 项既有改动；以 `evidence/baseline.json` 与 `baseline-files/` 的 17 个前快照为工作树基线，未把 HEAD 当工作树内容。
- 定点只读：`MetaEntrySession`、`MetaEntryService`、`ProcedureFlow/ProcedureSegment`、`ProcedureMatch/Battle`、`PageFlow/AccountReentry`、`AppComposition/MetaSegment`、`Page_RoomLogic/Page_SettingsLogic`、`GMTestModePage`、`MetaEntryEditModeTests`、`M3WiringGuardTests`、`ProcedureBoundaryTests`、`Core.Tests.csproj`、`StageMachine`、`unity-tests.py`，以及 `test-inventory.md`、`preflight.json`、`build-log.md`。
- 验证上下文：`workflow-context.json` 已指向主代理独立 root；本评审未访问该 root 配置、未改 state、未操作 Unity、未执行测试。任务 T1~T5 的 done 仍由主代理核验后落盘。
- 评审范围：任务与方案一致性、AC 映射、依赖、测试先行、测试/实现会话分离、T2/T3 重叠、两排队窗口、旧断言迁移、必要卸载/登录优先级、GM/账号重入、真实 UI 输入、Scope。未复做全仓方案审计。

## 覆盖与追溯

| 验收标准 | 对应任务 | 是否覆盖 | 说明 |
|---|---|---|---|
| AC1 | T2、T5 | 是 | 结构、Meta 去流程副作用、装配与文档 |
| AC2 | T1、T2、T4 | 是 | 三入口、参数保留、等待页；T1 只覆盖部分竞态 |
| AC3 | T1、T2、T3、T4 | 部分 | 两个排队窗口的新 API 用例未进入测试先行（B1） |
| AC4 | T1、T3、T4 | 是 | 失败注入、清理、错误确认后重进 |
| AC5 | T1、T3、T4 | 是 | Loading Back、等待/对局离房、幂等、登录落点 |
| AC6 | T1、T3、T4 | 是 | 所有权、订阅/输入/Back/遮罩、卸载完成信号 |
| AC7 | T2、T3 | 是 | 账号重入、GM sandbox、本地直入、结算、敏感日志 |
| AC8 | T1、T4、T5 | 部分 | red 只覆盖旧 API 一个核心竞态；新 API 的 red→green 不足 |
| AC9 | T4 | 是 | Raycast→pointer/键盘，不直调 API；具体执行命令/截图证据待落（m3） |

## 检查项

- [x] 依赖顺序为 T1→T2→T3→T4→T5，无循环；T2/T3 串行依赖合理。
- [x] AC1~9 均有任务映射；`acceptance.md` 的“对应任务”列尚未回填（m1）。
- [x] T1 的真实 red 可构造：静态核对 `MetaEntrySession.QuickEnterAsync` 在 await 后才检查 ct，而 `MetaEntryService.JoinWith` 在 await 内先 `RequestMatch`；可控延迟 `IMetaClient` 可让新增测试编译并在旧实现上断言失败。未运行，仅静态判断。
- [ ] T1 只要求“至少一个核心竞态”，任务又明确“尚缺新 API 的用例先记录后续补齐”；plan §测试策略却要求“生产修复前观察预期断言失败”（B1）。
- [ ] T2/T3 的测试迁移只写在 description；任务 `files` 同时包含生产与测试文件，验收/verify 未形成可核验的会话分离（B2）。
- [ ] T3 与 T2 共享 `RoomFlowLifetime`、`ProcedureFlow`、`ProcedureMatch`、`ProcedureBattle` 和同一 `RoomFlow` fixture，但 T3 verify 只写“生命周期 fixture”，未重跑 T2 入场 fixture/全量 RoomFlow（B3）。
- [x] 旧断言迁移方向明确：MetaEntry 保留参数/错误契约，流程测试承担阶段切换；M3 改为同实例接线；但“不降强度”没有门禁（B2）。
- [x] 两排队窗口有代码依据：`StageMachine` pending 为 last-wins；Match→Battle 在 JoinAck 后请求。T2/T3 所有权文字需点名（m2）。
- [x] 必要卸载 await、登录导航代次、GM/账号重入、本地直入/结算均在 T2/T3 验收中有落点。
- [x] T4 方法符合真实输入要求（Raycast 命中→pointer/键盘，不直调进入/离开 API）；命令/截图证据未具体化（m3）。
- [x] 未发现明显范围外重构；IAppFlow 退役在方案内，但 T5 文档面有遗漏（m4）。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 |
|---|---|---|---|
| B1 | major（阻塞） | T1 描述“尚缺新 API 的用例先记录后续补齐”，T4 才“补齐对最终端口的行为用例”。AC3 的 Main→Match、Match→Battle 两个排队窗口与 AC6 卸载完成信号等新行为，无法在 T2/T3 施工前取得真实断言失败；与 plan“生产修复前观察预期失败”不一致。 | 施工前拆出 T2T/T3T 独立测试子任务：先引入接口/端口骨架，再由测试会话写失败断言，最后实现；T1 穷尽当前 API 可测竞态，并把每个 deferred 用例登记到具体测试任务；T4 只负责最终 UI 操作与全量回归。 |
| B2 | major（阻塞） | T2/T3 的 `files` 含 `MetaEntryEditModeTests.cs`、`M3WiringGuardTests.cs`、`Assets/Tests/EditMode/Flow/RoomFlow/`、`PageFlowBattleMenuEditModeTests.cs`；测试迁移只在 description 中要求独立会话，未要求 test session 标识、未禁止生产会话改测试。T1 的 red 测试在 T2 移除 `IAppFlow` 后必须迁移，最容易被顺手改弱。 | 拆 T2T/T3T 测试适配任务；生产会话文件范围删除测试文件；验收增加“测试/实现两个 dsh 会话标识、旧断言逐条保留、主代理 diff 核验”；T1 red 测试迁移前后做断言对照。 |
| B3 | major（阻塞） | T2/T3 重叠四个生产文件与同一测试目录；T3 verify 只列“RoomFlow 生命周期 fixture”，未重跑 T2 入场 fixture 或全量 RoomFlow。T2/T3 的 verify 是复合散文，不是可重复命令，任务完成证据不足。 | T2/T3 verify 改为精确命令并记录结果：`python scripts/gate/unity-tests.py --mode editor --filter RoomFlow --timeout 600` 全量 + 各自 fixture；T3 追加 `AccountReentryTests`、`PageFlowBattleMenuEditModeTests`、`AccountSessionFactoryTests/RoomClientLeaveTests`；T3 完成前必须重跑 T1/T2 用例。 |
| m1 | minor | `acceptance.md` 的“对应任务”列仍为“确认验收后经CLI拆解”。 | 回填 T1~T5，或注明以 `tasks.md` 为权威，并在变更记录留痕。 |
| m2 | minor | T2“queued 前后”与 T3“两个 queued 窗口”所有权边界不够明确。 | 明确 T2=Main→Match（DisplayToken 脱离、无效排队先消费），T3=Match→Battle（接管前托管、Main 覆盖回收、完成信号）。 |
| m3 | minor | T4 verify 只写“实际 Play 真实输入脚本和副作用记录”，没有脚本入口、pipeline 命令或截图证据路径。 | 指定 `AgentScripts/LobbyRoomFlow/` 入口、`run_script/eval` 命令、`capture_game_view` 截图与断言输出；主代理按输出复核。 |
| m4 | minor | 若 T2 退役 `IAppFlow`，`Docs/说明/02`、`14`、`04`、`Docs/说明/架构图/图源-应用壳.json` 与 HTML 仍引用；T5 只列 03/08/17。 | T5 增加这些文件，或保留 `IAppFlow` 并标注过渡；`Docs/施工进度/` 写成精确卷名。 |
| m5 | minor | T2 若在 `Core.Tests` 加 ProcedureFlow 行为测试，`LiteFramework.Core.Tests.csproj` 需补 `ProcedureFlow/IRoomFlow/IRoomTicketSource/RoomFlowLifetime` 源链接；T3 verify 未列 `AccountReentryTests`。 | T2 verify 明示源集完整性；T3 verify 增加 `AccountReentryTests`。 |

## 评审结论

- 评审方式: 独立子代理
- 评审轮次: 第1轮（首轮独立任务评审）
- 复评范围与结果: 不适用（首轮）；B1~B3 修复后需复评任务与回归面
- 结论: FAIL
- 阻塞问题: 3
- 阻塞项: B1、B2、B3（major，施工前必须修复）
- 评审人: 独立子代理（本会话）
- 评审时间: 2026-11-08（Asia/Hong_Kong）
- 说明: T1 的真实 red 在静态层面可构造，AC 映射基本完整，依赖顺序正确，GM/账号重入/必要卸载/登录优先级已有落点；但测试先行的新 API 覆盖、测试/实现会话分离的可核验性、T2/T3 重叠范围的回归验证仍不足。未执行测试或 Unity，未访问独立 root 配置，未批准实施，也不修改门禁状态。

- 代理类型: dsh headless
- 会话标识: session-128282f0-318f-41e2-8ea3-121f44c7026b
