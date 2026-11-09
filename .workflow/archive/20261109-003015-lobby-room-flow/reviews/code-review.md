# 代码评审（主代理）：大厅进房离房链路样板简化

> 运行 lobby-room-flow · 2026-11-09 接管后复审
>
> 代码由施工子代理撰写；本文件由主代理基于实际 diff、文件实读与验证证据评审落盘。

## 评审输入

- 本批前快照：`evidence/baseline-files/**`（方案 §现状与约束 8：工作区有大量既有未提交改动，评审必须对比批前内容，不只对比 HEAD）。
- 当前工作树：`git status --porcelain` + 逐文件实读；本批新增 121 个文件（Meta 目录收口、协议/契约分文件、RoomFlow 测试）。
- 施工证据：`evidence/t3-takeover-*.txt`、`evidence/t6-roomflow-final.txt`、`evidence/t4-ui-acceptance-2026-11-08.md`、`evidence/t6-round-2-mapping.md`、`evidence/red.json`。
- 验证证据：`evidence/verify.json`（wf verify，11 条命令）。
- 评审角色：主代理（代码作者为 dsh 施工子代理，测试作者为独立 dsh 会话）。

## 变更范围

| 任务 | 主要文件 | 结果 |
|---|---|---|
| T1/T6 测试 | `Assets/Tests/EditMode/Flow/RoomFlow/**`（Entry/Lifecycle/Ticket/Failure/Cleanup + Rig/Doubles）、`MetaEntryEditModeTests`、`PageFlowBattleMenuEditModeTests`、`M3WiringGuardTests`、`LiteFramework.Core.Tests.csproj` | 先 9/9 断言 red，现 26/26 + 13/13 + 6/6 + 8/8 |
| T2 入场 | `Meta/Abstractions/{IRoomFlow,IRoomTicketSource}.cs`、`Meta/Client/MetaEntryService.cs`、`Procedure/Flow/{ProcedureFlow,RoomFlowLifetime,RoomFlowOperation}.cs`、`ProcedureSegment`、`AppComposition`、`MetaEntrySession`、`Page_LobbyLogic`、`GMTestModePage`、`MetaSegment`、`Meta/Call|Config|WireSubModule/**` 收口 | AC1/AC2/AC3/AC7 |
| T3 离场 | `ProcedureMatch`、`ProcedureBattle`、`PageFlow`、`Page_RoomLogic`、`Page_SettingsLogic` | AC4/AC5/AC6 |
| T4 真实 UI | `AgentScripts/LobbyRoomFlow/{RunRoomFlowUi,local_stack}.py|cs`、`HolderClient/**` | 真实页面 10 步 + 未验证披露 |
| T5 文档 | `Docs/设计文档/架构/*`、`Docs/说明/{02,03,08,14,17}`、`Docs/施工进度/客户端重构线.md`、审计登记 | doc 门禁 0 violation |

## 正确性与边界

- **结构（AC1）**：页面只经 `IMetaSession` → `IRoomFlow`（`ProcedureFlow`）；`EnterCoreAsync` 顺序为 ct/登录态/Main/占用检查 → 取票 → await 后复验 token/代次/登录态/IsCurrent → 票据端点校验 → `Request(Match, args.WithRoomOperation)`。`MetaEntryService` 已无 `BindFlow`/`IAppFlow`/阶段请求（M3 文本守卫 + 实读）。`AppComposition` 从后绑定改为 `Require<IRoomFlow>()`，删 `BindMetaEntry`（对批前快照仅 12 行变化）。
- **无新程序集边**：`LiteGame.Modules.Meta.asmdef`/`LiteGame.App.asmdef` 对批前只有“移除 LuaBridge/xLuaMain”的删减（属 Lua 退出线），本批未新增引用；Procedure asmdef 未改。
- **取消隔离（AC3）**：单操作占用（`RoomFlowLifetime`），重复请求稳定 `room.flow-busy`；页面 DisplayToken 只在 Requesting 阶段生效，入队前 `DetachPage`；`Main→Match` 无效排队由 `TryConsumeMatch` 原子消费、零建连回 Main；`JoinAck→QueuedBattle` 的会话由句柄持有，Main 覆盖时 `InvalidateCurrent` 释放恰一次（用例断言）。页面代次（`Form.DisplayGeneration`）与 `PageFlow.RecoveryGeneration`（Battle 接管签发、ShowLogin/Shutdown 递增）覆盖旧恢复覆盖登录页。
- **清理（AC5/AC6）**：关闭序 `BeginCleanup → 输入/视图/运行/账号 → Request(next) → await UnloadBattleScenesAsync(RootToken) → NotifyCleaned`；卸载未完成保持 busy；Match 未移交会话在 finally 释放一次，Battle 接管后仅 Battle 释放。卸载失败走 `MarkRoomFailure + SuppressRecovery + Feedback.ShowErrorAsync(retryable:false)`，不 `NotifyCleaned`、不自动放行（新增用例覆盖）。观察者在 `ProcedureId.Error` 终态停止等待（本轮修复的自旋）。
- **失败恢复（AC4）**：Join 失败/超时/取消 → `MarkRoomFailure` → `Request(Main)` → 唯一观察者 `RecoveryStep`（`RecoverRoomAsync`：错误确认 → 大厅）；启动/装配 Fatal 仍走 `ProcedureError` 与 `ErrorRecoveryService`。
- **兼容（AC7）**：账号重入 `AccountReentry(flow, pageFlow)` 用同一 `ProcedureFlow` 实例；GM sandbox 建房走 `IRoomFlow.CreateRoomAsync(mapId, players, template, gmKey)`；本地测试直入复用同一 `RoomFlowLifetime` 句柄。错误文案不含密钥；`deploy.args` 只在本地验收临时改动并逐字节恢复。

## 测试充分性

- 真实断言 red：T1 9/9 NUnit 断言失败（`evidence/red.json`，非编译错误），随后转绿；T3 失败基线 3 项真实 red 后转绿。
- 覆盖：三类入口、重复/取消/晚到/旧 A 新 B、Main→Match 排队失效、JoinAck→Battle 排队被覆盖、必要卸载未完成阻断、登录页优先、卸载失败停机、加载面取消、RoomBridge 解绑、M3 最终装配。
- 测试作者与生产作者为不同 dsh 会话，写集不重叠（`evidence/t6-round-2-mapping.md`）；9 条冻结断言同名保留，`RoomFlowLifecycleTests` 哈希与 T1 清单一致，Entry/Rig 仅调用适配。
- 未削弱：无 skip、无删除断言、无改门禁。

## 通用性与扩展性

- 根因（Meta 请求混合阶段切换、取消判定晚于副作用、UI 先于异步清理）由既有流程域统一收口；未新增通用请求框架/FSM/事件总线。
- 仅两个窄契约（`IRoomFlow`/`IRoomTicketSource`）；新入口只需页面经 `IMetaSession` 调用，新票据源实现 `IRoomTicketSource`，恢复动作集中在 `RecoverRoomAsync`。
- 无硬编码房号/场景/玩家数；错误文案与 mergeKey 为常量；状态全为实例字段，无新增 public static 可变状态。
- 未过度抽象：`RoomFlowLifetime/Operation` 只承载本条链路需要的占用、取消、移交与完成信号。

## 安全与性能

- 凭据不进入日志/证据（证据目录扫描无 token/JWT/key 值）；`deploy.args` 临时改动已恢复。
- 热路径无新增周期性 Warning/Log；观察者只在清理完成时运行一次，致命终态不再每帧 Yield。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 阻塞 | `ProcedureFlow.ReleaseAfterCleanupAsync` | 阶段机落 `ProcedureId.Error` 时等待循环永不退出，每帧留 Forever 任务（泄漏） | 循环条件增加 `Current != Error`，Error 即退出并保持占用（独立生产子代理单行修复） | 已修复并复测 |
| C-2 | 阻塞（门禁） | `Modules/Meta/{Call,Config,WireSubModule/Join}` | `MetaCall/MetaFailure/MetaFailureKind/RoomEndpoint/JoinTicketInfo` 被 Procedure 消费却不在 `Abstractions/`，L1 跨模块守卫红 | 移至 `Meta/Abstractions/Room/`（AssetDatabase 迁移）；同步 `MetaServer/RoomServer/Core.Tests` csproj 源链接 | 已修复，L1 1961 绿 |
| C-3 | 说明（批次外） | `Packages/.../Services/UI/Hud/*` | 并行 WIP 的两个 public interface 不在 `Abstractions/`，L1 接口自持守卫红 | 纯位置迁移到 `Hud/Abstractions/`（不改内容），属批次外门禁修复，不计入本重构设计产出 | 已修复并披露 |
| C-4 | 轻微 | `ProcedureBoundaryTests` 文案 | 注释字符串仍提 `IAppFlow`（非实际引用，测试通过） | 记录不阻塞；测试文件归测试作者维护 | 遗留（不影响门禁） |

## 复测记录

- 评审轮次：第 1 轮（接管后复审）。
- 修复后执行：`RoomFlow 26/26`、`MetaEntry 13/13`、`PageFlowBattleMenu 6/6`、`Core 8/8`、`Net 8/8`、`L1 1961/1961 PASS`、`comment-lint 0`、`dir-flat OK`、`l0 PASS`、`toolchain OK(2 warning)`、`Doc 0/0`；`wf verify` 结果见 `evidence/verify.json`。
- 复评范围：C-1 的终态路径（Main 正常路径逐字未变）、C-2 的文件位置与源链接、C-3 的纯迁移；未发现新阻塞。
- `wf verify`：**PASS**，11/11 命令 exit=0（core-room-regression / net-room-regression / room-flow-editmode / meta-entry-editmode / page-flow-editmode / L1 / dependency / comments / directories / documents / toolchain），见 `evidence/verify.json`。

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 主代理（DSH 会话）
- 评审时间: 2026-11-09T00:30+08:00
- 残留（不计阻塞）：卸载失败仅假件注入、CancelWait 真实 UI 并发受限改由自动化覆盖、GM sandbox/账号重入真实页面未验、`RecoverRoomAsync.mergeKey` 未断言。
