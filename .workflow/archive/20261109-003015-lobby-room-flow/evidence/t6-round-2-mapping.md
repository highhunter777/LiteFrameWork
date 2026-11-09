# T6 第二轮：测试迁移对照与保留（接管后核对）

> 目的：证明重构只改了“调用适配”，没有删用例、降断言或跳过；并记录测试作者与生产作者是不同会话、写集不重叠。

## 会话与写集

| 角色 | dsh 会话 | 写集 |
|---|---|---|
| 测试作者（T1/T3 断言、T6 两轮迁移） | `session-1ae08d74-641c-4dab-beae-c439e3030248` | `Assets/Tests/EditMode/Flow/RoomFlow/**`、`Assets/Tests/EditMode/Meta/MetaEntryEditModeTests.cs`、`Assets/Tests/EditMode/UI/Page/PageFlowBattleMenuEditModeTests.cs`、`Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs`、`Tests/LiteFramework.Core.Tests/LiteFramework.Core.Tests.csproj` |
| 生产作者 T2（Meta 纯票据 + 流程/阶段/装配） | `session-72bdafc0-fd6b-4dfe-ae8a-76c1cacc5d99` | `Modules/Meta/**`、`Modules/Procedure/Flow/**`、`ProcedureSegment`、`AppComposition`、`MetaEntrySession`、`GMTestModePage`、`Page_LobbyLogic` |
| 生产作者 T2C1（适配器/程序集边） | `session-a393f27e-1c49-4596-b48e-9700e9e6e6d9` | 适配器与 asmdef 接线 |
| 生产作者 T2C2/C3/C4 | `session-985e3c76…` / `session-8e5f885e…` / `session-bfab2724…` | Match / Battle / Segment 片段 |
| 生产作者 T3（清理/恢复/停机） | `session-536138b6-6bd5-498f-9e29-8ca45efd817d` | `RoomFlowLifetime`、`ProcedureFlow`、`ProcedureMatch/Battle`、`Page_RoomLogic`、`Page_SettingsLogic` |
| 生产作者 T3 页面片 | `session-d82df91f-f36b-4af1-8345-982f74f2db72` | 房间/设置页提前 Replace 收口 |
| T4 操作脚本作者 | `session-05b57652-d8fd-400e-be75-369b76c745b9` | `AgentScripts/LobbyRoomFlow/RunRoomFlowUi.cs` |
| T4 本地环境脚本作者 | `session-ddf01ec9-e2be-4ba8-bd20-32a9ee3207c7` | `AgentScripts/LobbyRoomFlow/local_stack.py` |
| T5 文档作者 | `session-6218077b-8d0b-4bbc-8153-0ea1cf4fd140` | `Docs/**` |
| 接管后生产修复（致命终态自旋） | `bb93c2af-ee60-4bd0-bf17-9cf022b27743`（宿主子代理） | `ProcedureFlow.cs` 单行 |

测试文件最后修改时间 21:29–21:55；T2/T3 生产写入 22:0x–22:26，晚于全部测试改动，未回改测试。

## T1 冻结的 9 条断言逐条对照

| # | T1 断言（冻结名） | 现状（同名用例，仍在跑） | 迁移性质 | 结果 |
|---|---|---|---|---|
| 1 | 取票前已取消_零网络请求零RequestMatch | `RoomFlowEntryTests` 同名 | 仅 rig 改共享 Lifetime/Flow | green |
| 2 | 在途取消_晚到票据成功_零流程推进零建连接 | 同名 | 同上 | green |
| 3 | 重复请求_只发一次取票只建一次连接 | 同名 | 同上 | green |
| 4 | 旧A取消后新B成功_A晚到不得推进流程 | 同名 | 同上 | green |
| 5 | Main到Match排队后离房_零建连接 | 同名 | 同上 | green |
| 6 | 账号重入_在途取票晚到不得RequestMatch | 同名 | 仅 `new AccountReentry(rig.Meta→rig.Flow)` | green |
| 7 | JoinAck后Battle排队被Main覆盖_未接管会话恰好回收一次 | `RoomFlowLifecycleTests` 同名 | 文件未改（哈希与 T1 冻结一致） | green |
| 8 | 必要卸载未结束_再次入场零取票_完成后放行 | 同名 | 文件未改 | green |
| 9 | 账号重入后旧房间清理完成_不覆盖登录页 | 同名 | 文件未改 | green |

哈希对账：`RoomFlowLifecycleTests.cs` 与 T1 清单完全一致；`Entry/Rig/Doubles` 变化为 T6 第一轮 API 迁移与 T3 失败用例所需假件扩展（有报告 `dsh-t6-rig-wiring.report.md`、`dsh-t6-meta-tests.report.md` 逐条说明）。

## 旧 Meta 用例的新落点（T6 第一轮，已核对）

见 `evidence/dsh-t6-meta-tests.report.md` 的映射表：14 条旧用例逐条映射到“纯 Meta 同名用例 + 流程侧 `RoomFlowTicketTests`”，`流程端口未绑定_换票显性失败` 由 `流程构造_缺必要端口_当场fail_fast` 替代（后绑定机制已删除，属语义等价替代，不是降级）。

## M3 接线守卫

`Tests/LiteFramework.Core.Tests/M3WiringGuardTests.cs` 已改为验证最终装配：`ProcedureFlow` 同实例注册 `IRoomFlow`/`IAccountSessionControl`、`AccountReentry` 同 flow、Match/Battle/Flow 共享同一 `RoomFlowLifetime`、`MetaEntryService` 无 `BindFlow`/`IAppFlow`/`IAccountSessionControl`、`AppComposition` 直接 `Require<IRoomFlow>()`、无 `BindMetaEntry`。原约束未删除。

## 本批全量验证（接管后实跑）

| 套件 | 结果 | 证据 |
|---|---|---|
| RoomFlow（T1+T3 共 24 项） | 24/24 green | `evidence/t3-takeover-roomflow-1.txt` |
| MetaEntryEditModeTests | 13/13 green | `evidence/t3-takeover-metaentry.txt` |
| PageFlowBattleMenuEditModeTests | 6/6 green | `evidence/t3-takeover-pageflow.txt` |
| Core 账号重入/边界/同实例接线 | 8/8 green | `evidence/t3-takeover-core.txt` |
| Net AccountSessionFactory/RoomClientLeave | 8/8 green | `evidence/t3-takeover-net.txt` |

## 接管后新增定向用例（第二轮）

- `Cleanup/RoomFlowCleanupFailureTests.卸载抛异常_保留占用不报清理完成_错误反馈展示_不导航_再次入场被拒`：Battle 关键场景卸载抛异常 → 阶段安全回 Main、不落 ProcedureError；操作保持占用（`IsBusy`、`CleanupDone=false`、`Release` 被拒、`CleanupTask` 未完成）；错误反馈标题/说明/单按钮（retryable=false）可见；不进入 RecoveryStep、不开大厅/结算；再次入场返回 `room.flow-busy` 且零取票零建连。
- `Cleanup/RoomFlowLoadingCancelTests.等待JoinAck时取消加载遮罩_回Main_会话恰释放一次_观察者放行可重进`：取消 `Feedback.LoadingToken`（与加载面 Back 同源）→ 请求回 Main、不落 Error；会话恰好释放一次；清理完成前占用仍拒新入场；观察者释放后放行重新取票并排队 Match。
- 辅助件 `Cleanup/RoomFlowCleanupDoubles.cs`（真实 UIService/DialogService/FeedbackService+PageFlow 的测试装配、抛错版 ISceneService）；`RoomFlowRig` 仅新增可选注入，既有 24 例调用与断言不变。
- 目录预算：RoomFlow 直属 .cs 仍 5 个；新增件在 `Cleanup/` 子目录（3 个 .cs）。

（若本轮新增用例未通过，见 `build-log.md` 对应记录；不得计 T6 done。）

## 未覆盖/风险

- 卸载失败分支依赖注入的假 `ISceneService` 抛错，真实 Unity 场景卸载失败未做真机注入。
- `CancelWait` 真实 UI 并发受 pipeline 串行限制（见 T4 记录），该路径用自动化用例覆盖，不声称真实 UI 通过。
