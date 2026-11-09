T2-B 已交付：操作寿命 + 实质 `ProcedureFlow` + payload 句柄；未改装配/页面/GM/StageMatch/Battle/测试/.workflow，未运行 Unity 编译或测试，不能声称 green。

## 实际修改/新增文件

| 文件 | 动作 |
|---|---|
| `Assets/LiteGame/Modules/Procedure/Flow/RoomFlowOperation.cs` | 新建，操作句柄/阶段/取消/清理信号/待接管会话 |
| `Assets/LiteGame/Modules/Procedure/Flow/RoomFlowLifetime.cs` | 新建，每实例唯一操作、排队/接管/清理/恢复抑制 |
| `Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs` | 由转发件改为实质取票→校验→排队 Match |
| `Assets/LiteGame/Modules/Procedure/Shared/ProcedureArgs.cs` | 新增 internal 操作句柄与 `WithRoomOperation` |
| `Assets/LiteGame/Modules/Meta/Abstractions/IRoomFlow.cs` | 仅修正 `LeaveMatch` 文档 |

Flow 目录现为 5 个直接 .cs（原 3 + 2），未超预算；无 public 可变字段、无新增 public static 状态。未改 `MetaErrorCodes.cs`，busy/invalid 错误码作为 `ProcedureFlow` 单点常量。

## 精确新构造/API

```csharp
internal sealed class ProcedureFlow : IRoomFlow, IAccountSessionControl, IDisposable
{
    internal ProcedureFlow(
        IStageHost<ProcedureId, ProcedureArgs> machine,
        IRoomTicketSource tickets,
        RoomFlowLifetime lifetime,
        CancellationToken rootToken = default);

    public UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default);
    public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0, CancellationToken ct = default);
    public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers, string template, string gmKey, CancellationToken ct = default);
    public bool LeaveMatch();
    public void Logout(string reason);
    public string AccountId { get; }
    public bool IsLoggedIn { get; }
    public void Dispose();
}

internal sealed class RoomFlowLifetime : IDisposable
{
    internal RoomFlowLifetime(CancellationToken rootToken = default);
    internal RoomFlowOperation Current { get; }
    internal bool IsBusy { get; }
    internal RoomFlowOperation Begin();
    internal bool IsCurrent(RoomFlowOperation operation);
    internal void AttachPage(RoomFlowOperation operation, CancellationToken pageToken);
    internal void DetachPage(RoomFlowOperation operation);
    internal void SuppressRecovery();
    internal RoomFlowOperation InvalidateCurrent(bool releaseRequesting);

    internal bool TryConsumeMatch(RoomFlowOperation operation, out MetaJoin join);
    internal bool TryQueueBattle(RoomFlowOperation operation, IAccountSession session);
    internal bool TryTakeSession(RoomFlowOperation operation, out IAccountSession session);
    internal void BeginCleanup(RoomFlowOperation operation);
    internal void NotifyCleaned(RoomFlowOperation operation);
    internal bool Release(RoomFlowOperation operation);
    internal void AbandonQueued(RoomFlowOperation operation);
}
```

`ProcedureArgs` 新增：

```csharp
internal readonly RoomFlowOperation RoomOperation;
internal ProcedureArgs WithRoomOperation(RoomFlowOperation operation);
```

原 public 纯参数构造保留，internal 构造不与 public 可访问性冲突。

## 关键所有权/取消规则（已实现）

- 每实例仅一个 `Current`；重复请求在取票前返回稳定 `room.flow-busy`（已同步完成，不发第二票、不等待）。
- 取票前检查页面 ct；Requesting 期间页面 ct 取消由独立注册立即释放占用并取消操作 token，允许 B 开始。A 晚到仅以 `ReferenceEquals(Current, A)` 判活，A 的 finally 不会清 B。
- 页面 ct 不进入操作 CTS 链；入队前 `DetachPage`，之后页面隐藏/取消不再影响已接受操作。操作 CTS 仅链接 root。
- 成功排队前复验 ct、账号事实（`IsConfigured/IsLoggedIn/AccountId`）、当前操作身份、Main 阶段；失败返回票据源原错误或 `room.flow-invalid`，零排队。
- 票据地址在本片解析；非法/空地址零排队、零连接。成功排队只是 `Request(Match)`，不等于 JoinAck。
- QueuedMatch 取消后仍占用，直到 Match 通过 `TryConsumeMatch` 消费；无效排队在消费处清空占用并零建连接。
- Joining 会话由 Match 持有；JoinAck 后 `TryQueueBattle` 把会话托管给句柄。QueuedBattle 被取消/被 Main 覆盖时由句柄回收未接管会话并只回收一次；Running 后只取消操作 token，不提前 Dispose 在用会话。
- `NotifyCleaned` 完成清理信号；`Release` 必须同时满足当前句柄 + 清理完成 + 阶段已回 Main，由 flow 的观察协程释放，不在 `OnLeave`/取消同步窗口 Request。
- `LeaveMatch`：先失效当前操作；Main 且只有 Requesting/QueuedMatch 时不 `Request(Main)`；QueuedMatch 留给 Match 消费。当前非 Main 才请求 Main；无在途入场且无房间返回 false。
- `Logout`：先同步 `SuppressRecovery()`，再失效/取消，最后委托 `IRoomTicketSource.Logout`；后续 Leave 不清除旧恢复抑制。宿主 root 取消同样抑制当前操作恢复。
- `Dispose` 后公开调用抛 `ObjectDisposedException`；lifetime/操作句柄由 flow 单一 owner 释放，未接管会话随句柄释放。

## 下一片（T2-C / T3）接线点

- `ProcedureSegment`：同窗创建 `RoomFlowLifetime(rootToken)` → `ProcedureFlow(machine, IRoomTicketSource, lifetime, rootToken)` → 同实例登记 `IRoomFlow` 与 `IAccountSessionControl`。
- `ProcedureMatch.OnEnter`：建连前 `TryConsumeMatch(req.RoomOperation, out join)`；false 立即 `Request(Main)`，禁止 `_accounts.Create`。JoinAck 后 `TryQueueBattle(op, account)`；false 自行 Dispose 会话。
- `ProcedureBattle.OnEnter`：`TryTakeSession(op, out session)`；false 不建运行并回 Main。清理开始 `BeginCleanup`，会话/视图/运行/卸载完成后 `NotifyCleaned`；T3 的 await 卸载与错误确认应在 `BeginCleanup` 与 `NotifyCleaned` 之间。
- Match 取消路径 Dispose 会话后必须 `NotifyCleaned(op)`；T3 页面恢复读取 `op.RecoverySuppressed`，抑制时不覆盖登录页。
- 本地测试直入路径需由 T2-C 用同一 lifetime 建立句柄，不能形成第二套离场收口。

## 未执行/已知缺口

- 未执行 Unity 编译、未跑任何测试；未验证 `UniTask.Yield()`/`ForgetLogged` 在当前宿主编译面，按现有源码同型调用。
- 当前旧调用方（`MetaEntrySession`、`GMTestModePage`、`AppComposition`、RoomFlow 测试 rig）仍引用 T2-A 已移除 API，宿主编译会报旧签名错误，需 T2-C/T6 迁移；本片未加兼容副作用。
- 未改 `PageFlow` 导航代次（T3 写入范围），未改 `ProcedureMatch/Battle`，未删除旧 `IAppFlow`。