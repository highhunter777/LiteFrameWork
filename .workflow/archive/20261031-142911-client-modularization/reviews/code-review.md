# 代码评审（独立子代理）：客户端模块化（竖向切分 + 装配就地 + 边界机械化）

> 第 2 轮复审（评审整改闭环）。首轮独立评审 FAIL（blocker B1 + major M1~M5）；整改后第二轮复审先因 M4 文档旧路径残留 FAIL，清残后定向复审 PASS（0 阻塞）。本文件由主代理根据独立子代理返回整理落盘；不是主代理自评。

## 评审输入

- 代码基线：`59d971d8`（批 5 主体，220 文件）+ `b9412ea6`（提交状态回填）；本轮复审对象为工作区评审整改（Session 端口迁移、矩阵边登记、端口用例、文档纠偏、UiValidation 基线）。
- 子代理标识：独立子代理 2（首轮评审子代理亦已给出 FAIL，本轮为整改后复审）。
- 子代理约束：只读评审，不修改生产代码、测试、门禁与文档。
- 评审范围：B1 Session→Procedure 反向依赖、M1 失败回滚/视图清理、M2 矩阵未登记边、M3 IBattleRun 端口用例、M4 文档回写、M5 UI 基线/技术债，以及移动/新增测试是否引入新问题。

## 变更范围

- **Session 端口归位**：`MatchRulesSeeder`、`ILocalTransportFactory`、`LocalTransportRegistry` 从 `Modules/Procedure/{Setup,Transport}` 移到 `Modules/Session/{Setup,Transport}`；`ProductTransportFactory` 只消费本域件，`Session→Procedure` 类型直引归零。
- **失败路径**：`BattleRunFactory.Create` 在 `BattleSession` 构造失败时释放已建 `MatchSession`；`ProcedureBattle` 的视图组改为方法级 `rig`，factory/加载失败时 `finally` 显式 `Dispose`，成功 `AttachView` 后置空。
- **矩阵门禁**：`domain-matrix.py` 增加 `unregistered_edges`；`--check` 对未登记非零边直接失败；`ALLOWED_EDGES` 覆盖全部非零边，删除零计数 `Meta→Procedure` 噪声条目。
- **端口用例**：`BattleRunPortEditModeTests` 扩到 7 例，覆盖早到、等待挂起后 StartGame/Ended 唤醒、取消、Leave 幂等、Dispose 不触发 Ended、视图挂载/拆除；`BattleClient` 登记进 Account Scope。
- **文档**：路线图依赖图/接缝表、`Docs/说明/17` 5.1/9.1/10.1、`商业级通用客户端框架总设计` 的端口归属按 12 域/18 段与 `Modules/Session` 路径纠偏。
- **UI 基线**：`UiValidationEditModeTests` 文字档基线 104→128；`技术债.md` 测试面登记 24 条未标记文本；`final-gates.txt` 更新为 14/14。

## 正确性与边界

- **B1 已关闭**：域矩阵复算 `Session→Procedure=0`；Session 只保留 `Battle/Config/Dev/Input/Map/Meta` 等已登记边；移动后命名空间仍为 `LiteGame`，编译面无新增反向依赖。
- **M1 已关闭**：factory 构造失败释放 MatchSession；视图组在加载/工厂失败时由流程侧释放，成功移交后不再重复释放；静态检查未发现双重释放或漏放。
- **M2 已关闭**：`domain-matrix --check` PASS；所有非零边均有登记，未登记边直接红；零计数噪声边已移除。
- **M3 已关闭（含 R2）**：7 个端口用例覆盖早到、挂起后唤醒、取消、Leave 幂等、Dispose、视图生命周期；真实 `BattleSession` + 假传输，不依赖 PlayMode。
- **M4 已关闭（含 R3）**：路线图/数据流/总设计的旧 `LiteGame.Presentation`、`Modules.View/Product/Room/Procedure.Session`、`context.Tick`、`LiteGame.Core` Battle 端口描述已按现行代码修正。
- **M5 已关闭（含 R4）**：基线 128 与全量 EditMode 基线 1102/1102（整改前）及整改后定向 7/7/关键回归一致；24 条来源/样式债已在技术债登记，未把基线更新当作清理。
- **R6 保留**：`IBattleRun` 端口面仍偏宽、工厂仍接收具体 `AccountSession`、流程仍读 `AccountSession.Scope`；属后续接口隔离改造，不阻塞本轮。

## 测试充分性

- `BattleRunPortEditModeTests` 7/7：早到 StartGame/Ended、等待挂起后唤醒、取消、Leave 幂等、Dispose 不触发 Ended、视图挂载/拆除均有直接断言。
- `ProcedureBoundaryTests` 2/2：先去注释再断言 `_run.Tick`/`WaitStartedOrEndedAsync`，并禁止代码区出现 `BattleContext`；`DomainBoundaryTests` 5/5 钉 8 共享域命名空间。
- `BattleRankingConsistency` 1/1：增加独立期望序列（Kills/PlayerId/Name），不再只靠共用 helper 自证；`CameraAimBlend` 6/6 移除每帧 `SetAiming` 计数，只钉翻转序列。
- `UiValidation` 14/14：文字档基线 128 与真实面一致；全量 EditMode 基线 1102/1102（整改前），整改后端口 7/7 与关键回归通过。
- 残留：M1 的 factory 抛异常/视图释放尚无专门回归用例，靠代码审查；R6 端口面宽度未做替身测试。

## 通用性与扩展性

- B1 消除后 Session 可独立演进；矩阵门禁从“文档过期检查”升级为“未登记边即失败”，新增耦合会被机械拦截。
- 排行/相机仍保持单源（`BattleRankOrder`、`CameraBlendDefaults`）；移动后的播种器/本地传输端口归属 Session，职责更一致。
- 残留 R6：`IBattleRun` 事实/驱动/视图/房间会话混合，`IBattleRunFactory.Create(AccountSession)` 仍暴露具体会话；后续可拆 facts/driver 面，不影响本轮验收。

## 安全与性能

- 未发现新增凭据、权限、网络协议或输入校验问题；移动与异常清理不改变对局安全语义。
- 热路径无新增分配：`BattleSession.Tick` 维持原瞄准/采样顺序；`ProcedureBattle` 仅增加一个局部引用与异常路径 Dispose；矩阵扫描只在门禁运行。
- 测试使用假传输/空视图，无真实网络与磁盘副作用；`BattleClient` 已登记 Account Scope，清理不靠 GC。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| R1 | minor | `scripts/gate/domain-matrix.py` | `Meta→Procedure` 零计数允许边造成登记噪声 | 删除该条；非零边全部登记；`--check` 未登记边失败 | 已修 |
| R2 | minor | `Assets/Tests/EditMode/BattleRunPortEditModeTests.cs` | 首版只覆盖早到，未覆盖挂起后唤醒/取消；fixture 未登记 BattleClient | 增 3 例（唤醒/取消），`Account.Register(Battle)`；7/7 | 已修 |
| R3 | minor | `Docs/说明/17`、`商业级通用客户端框架总设计.md` | 5.1/10.1 仍写直接 Sample/context.Tick；总设计仍把 Battle 端口放 Core | 改为 `_run.Tick`/`BattleSession.Tick` 与 `Modules/Battle/Abstractions` | 已修 |
| R4 | minor | `evidence/final-gates.txt`、`build-log.md` | 证据与未决事项对全量 EditMode 口径不一致 | 证据改为“全量基线 1102/1102 + 整改后定向”；全量重跑受编辑器重启中止，如实留痕 | 已补（基线） |
| R5 | minor | `Modules/Session/Setup/MatchRulesSeeder.cs`、`Transport/ILocalTransportFactory.cs` | 移动后注释仍指 `ProcedureMatch` | 改为 `ProductTransportFactory`/`BattleSession` | 已修 |
| R6 | minor | `Modules/Session/Abstractions/IBattleRun.cs`、`IBattleRunFactory.cs` | 端口面宽、工厂暴露具体 AccountSession | 后续拆 facts/driver 面并收窄工厂 | 接受为后续债 |

## 复测记录

| 检查 | 命令/方式 | 结果 |
|---|---|---|
| 域矩阵 | `python scripts/gate/domain-matrix.py --check` | PASS；未登记非零边 0 |
| BuildHash | `python scripts/codegen/gen-build-hash.py --check` | PASS：shared=cf3c3453057b28a4 / meta=75e0e85fe3c33cf2 |
| Ui 生成物 | `python scripts/codegen/gen-ui-page.py --check` | PASS：12 pages |
| .NET 边界 | `dotnet test ... --filter DomainBoundaryTests|ProcedureBoundaryTests` | 7/7 PASS |
| 端口用例 | `unity-tests.py --mode editor --filter BattleRunPortEditModeTests` | 7/7 PASS |
| 定向回归 | `BattleRankingConsistency` 1/1、`CameraAimBlend` 6/6、`BattleSession` 12/12、`MatchLoopLeak` 1/1、`UiValidation` 14/14 | PASS |
| 全量 EditMode | `unity-tests.py --mode editor --filter LiteGame.EditModeTests --filter-type assembly` | 1102/1102 PASS（整改前全量）；整改后全量重跑因编辑器重启中止，定向 7/7 + 关键回归通过 |
| L1 | `python scripts/gate/test.py --suite L1 --profile PullRequest` | 1921/1921 PASS |

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 独立子代理 2
- 评审时间: 2026-10-31

B1/M1/M2/M3/M4/M5 均已闭环；R1～R5 已修，R6 作为后续接口隔离债保留。可以进入 writeback 与提交。
