# 方案A：未开局席位释放复用 ＋ Abort 终态客户端信令

> **来源**：用户裁决（2026-10-17，房间服审查报告问题清单）——问题1 选**方案 A（释放＋复用）**、问题2 选**复用 MatchEnded**，本批一并落地。
> **状态**：**已完成（附验证证据）**——工作区未提交。

## 交付

| 面 | 落点 |
| --- | --- |
| 席位释放（内核） | `RoomRuntime.ExecuteDisconnect` 按相位分流：`WaitingForPlayers` → `ReleaseSeat`（owner 先迁移再摘席；席位名单缩容广播；全员释放照旧 `Abort` 收尾）；`Running` 照旧保留断线席位（重连窗口）。释放席位无输入水位残留（未开局输入本就不被消费） |
| 空位复用（内核） | `ExecuteJoin` 改**最低空位扫描**（满员判据＝无空位，不再 `NextPlayerId` 顺延只增）；`NextPlayerId` 降格为高水位迭代上界（只升不降，新增注释）；owner 判据改"房内尚无 owner"（`AnySeatOwner`）——空位复用后 0 号位不等价"首入座"，复用者不二次称主 |
| 上报口径 | `RoomRuntime.AllocatedSeats`（在册席位——未开局离场即释放、对局中含保留席位）；`RoomSummaries().Players` 改用之（此前 `NextPlayerId` 会把已离场者虚报） |
| 宿主侧同步 | `ApplySignal(SeatsChanged)`：内核已释放的席位，宿主会话槽一并清空＋复位路由字段（与 `TryDestroyTerminalRoom` 同款）——幽灵会话不再虚占 `OccupiedPlayerCount`；对局中断线席位仍在列表（Connected=false）不受影响 |
| Abort 信令（内核） | 新增 `RoomAbortedOutput`（Reason/MatchId/Frame；`Abort` 成功迁移后产出）——未开局收尾（等待超时/运维关闭/排空）无结果可结算，向在座客户端通告散场 |
| Abort 信令（宿主） | `ApplyOutput(RoomAbortedOutput)` → 对在座连接发 `MatchEnded{EndReason=随因, GameplayEndReason=None, Players 空}`（Reliable） |
| Abort 信令（客户端×3） | ①`RoomClient.MatchEnded` 未开局接收面放宽：无 StartGame 时判据＝"本次进房已应答"（`_joinAttemptPending`/PlayerId）——重新 Join 未决窗口的旧终局照旧拒；②`BattleContext.EndReason` 增 `Aborted`（未开局终局〔Sim 未建〕不进结算页，走"房间页替换回大厅"）；③`ProcedureBattle.WaitStartGameOrEnded`：等开局与终局**双源竞速**（先到先得、败者随 finally 退订）——不竞速则散场后永等 StartGame（房间已销毁信令不再来） |
| 房间页注释 | `Page_Room.lua` 等待人数注释同步（未开局离场名单缩容、计数回落；对局中断线保留占座）——逻辑零改动（分子＝名单条数自动跟随） |
| 设计回写 | 服务端总设计 §9.1：全员离场收尾段重写＋新增"未开局离场即释放席位（空位复用）"与"终态中止的客户端信令"两条 |

## 用例

| 类型 | 内容 |
| --- | --- |
| 新增（运行时） | `未开局离场释放席位_空位可复用_号位不再等价房主`（满员拒→释放→缩容→复用 0 号位不称主→在座 owner 开局）；`未开局离场_名单缩容而非保留离线态` |
| 新增（宿主级） | `未开局离场释放席位_新玩家复用空位_容量与上报口径不泄漏`（Players/OccupiedPlayerCount 随释放回落、第三者复用 1 号位、房间不收尾）；`未开局等待超时_中止并通知在座客户端_房间销毁`（真等待超时→MatchEnded{WaitingTimeout, Players 空}→Pump 销毁） |
| 新增（客户端） | `未开局收到中止终局_也接受并通告`（局号未知也接受；不可靠通道照旧拒；重新 Join 后旧结果已清） |
| 补断言 | `未开局关闭_走中止而非结算` 补 `RoomAbortedOutput`（Reason/MatchId） |
| 重钉（5 处） | `旧房主重连不夺回`（Rebind→**重新 Join 拿回号位**——释放后未开局已无重绑通道）；`独房主…全断线转最低席位`→**对局中变体**（未开局已无保留席位可转）；`优先转在线席位`→对局中变体（两轮遍历只剩对局中可达）；`RoomSeatsTests.进房断线恢复`（断线保留/重连语义挪对局中）；`MultiRoomIsolation.断线只影响本房间`（A 房加第二人，隔离判据留席位面＋释放断言） |

## 验证证据

| 项 | 结果 |
| --- | --- |
| LiteNet 全量 | **607/607**（602 + 5 新用例；Release 车道） |
| L1 门禁 | `pwsh scripts/gate/test.ps1 -Lane L1 -Profile PullRequest` → **[PASS] 1733**（1728 + 5） |
| Unity | refresh **0 错误**（26 警告全为旧文件既有）；EditMode 定向 `BattleSession`＋`RoomBridge` **25/25** |
| BuildHash | `--check` 双 OK（shared `12674a5f40c43358` / meta `6391768fdd5b1f2e`）——改动件（RoomRuntime/RoomOutput/ServerHost/RoomClient/BattleContext/ProcedureBattle/Lua）均不在闭包 |
| 红证 | `RoomClient` 判据首版（`PlayerId >= 0`）被既有钉 `NewJoinClearsPreviousResultAndRequiresNewStart` 当场咬红（重新 Join 未决窗口误收旧终局）→ 改"本次进房已应答"后复绿——既有纪律用例护住了接收面放宽的边界 |

## 如实说明（边界）

| 项 | 说明 |
| --- | --- |
| 散场提示文案未加 | 散场后客户端回大厅（房间页替换）无 toast/文案提示——需 UI 文案键与提示基建，归后续 UI 批；服务器信令面已完备 |
| ProcedureBattle 竞速无流程级用例 | `WaitStartGameOrEnded` 为流程私有件，证据面＝两端钉子（服务端发信令＋客户端收信令）＋编译；真机"等待期被服务器关闭"全链归手测 |
| L2/L3 未跑 | 按节律留夜间流水线；本批未触 proto（MatchEnded 复用现有面）与 Meta 侧 |
| 并行线同场 | `Page_Room.lua`/`ProcedureBattle.cs` 为在途批触碰文件——本批仅注释/等待段 hunk，diff 逐文件核过归属；提交时 hunk 级隔离或随先行批一并落 |
