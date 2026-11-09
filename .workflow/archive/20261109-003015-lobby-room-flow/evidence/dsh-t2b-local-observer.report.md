已补齐所有 `Begin` 操作的统一观察入口，只改 `Flow` 两处文件（`ProcedureFlow.cs`、`RoomFlowLifetime.cs`）及句柄小改（`RoomFlowOperation.cs`），未改阶段/装配/测试，未运行 Unity。

## 改动

### 1. `Begin` 即注册唯一观察者（覆盖本地 testRoom 直入）

`RoomFlowLifetime` 新增单 owner 回调：

```csharp
internal Action<RoomFlowOperation> OperationStarted { get; set; }
```

`Begin()` 创建并设为 `_current` 后立即 `OperationStarted?.Invoke(operation)`。

`ProcedureFlow` 构造时：

```csharp
_lifetime.OperationStarted = ObserveRelease;
```

`Dispose` 时清空后 `_lifetime.Dispose()`。这样任何 `Lifetime.Begin()`（含 C 的本地直入）都由当前唯一 flow 启动观察；`EnterCoreAsync` 与 `LeaveMatch` 已移除第二次 `ObserveRelease` 调用，`ReleaseObserved` 仍兜底最多一次。

### 2. 统一观察语义

唯一观察者现在固定为：

1. `await operation.CleanupTask`
2. root 关闭 / flow disposed → 直接返回，不恢复
3. `!operation.Submitted` → 直接返回（Requesting 取消、票据失败不触发页面恢复）
4. `!IsCurrent(operation)` → 旧操作返回，不恢复
5. `RecoverySuppressed` → 跳过恢复仍可正常释放；否则调用 `RecoveryStep`
6. `MarkRecoveryDone()`
7. 等待阶段回 Main
8. 再次校验 current → `Release`（Dispose 句柄、解除页面/root 注册、释放未接管会话）

取消取票的 busy 仍由 `InvalidateCurrent(releaseRequesting: true)` / 页面取消回调立即清 `_current`；旧 await 用缓存 token 观测取消，句柄由取消路径或 flow finally 幂等 Dispose。

### 3. 无真实帧的补泵

新增 `ProcedureFlow.PumpRelease()`：

- 仅当 `Submitted && CleanupDone && RecoveryDone && Machine.Started && Current == Main`
- 才 `lifetime.Release(op)`

调用点：`EnterCoreAsync` 前置检查、`LeaveMatch` 开头。它不会绕过 Main/清理/恢复条件，只补“测试连续 `Advance`、没有帧驱动 Yield 协程”的同一终态；真实帧下观察者仍会自行完成释放。

### 4. 本地直入句柄契约（供 C 接线作者）

C 侧本地 testRoom 路径：

```csharp
RoomFlowOperation op = lifetime.Begin();   // 观察者已启动
op.MarkJoining();                          // 置 Submitted + Joining（本地直入不走 EnterCore）
machine.Request(ProcedureId.Match,
    new ProcedureArgs(testRoom: true).WithRoomOperation(op));
```

- Match 建连前必须校验 `lifetime.IsCurrent(op) && !op.Invalidated`，否则零建连接。
- 无效本地排队：`lifetime.NotifyCleaned(op)`；仅当阶段不是 Main 才 `Request(Main)`。
- JoinAck 后：`lifetime.TryQueueBattle(op, account)`；失败方自行 Dispose 会话。
- Battle 接管：`lifetime.TryTakeSession(op, out session)`。
- 结束清理：`BeginCleanup(op)` → await 必要清理/卸载 → `NotifyCleaned(op)`；阶段执行体照旧推进 Main。观察者随后完成恢复/释放。
- 本地直入**不要**调用 `TryConsumeMatch`（它只接受 `QueuedMatch`）。

## 新增/变更的内部成员

```csharp
// RoomFlowLifetime
internal Action<RoomFlowOperation> OperationStarted { get; set; }

// RoomFlowOperation
internal bool RecoveryDone { get; }
internal void MarkRecoveryDone();
internal void MarkJoining();   // 现在同时置 Submitted=true
```

`RecoveryStep` 仍为 T3 唯一恢复扩展点，与 release 共用同一观察者。

## 说明

- 未跑 Unity 编译/测试，不能声称编译或 green。
- 当前 `ProcedureFlow.cs` 中已存在的 `IsAtMain/RequestMatch/RequestMain` 兼容块未改动，不属于本次新 `IRoomFlow` 观察路径；若它非主代理有意保留，请复核其生产消费者。