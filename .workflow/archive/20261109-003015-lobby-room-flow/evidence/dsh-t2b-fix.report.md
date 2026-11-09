三个问题已局部修复，仍只在 T2-B 写集内：`RoomFlowOperation.cs`、`RoomFlowLifetime.cs`、`ProcedureFlow.cs`。未改测试/装配/阶段，未运行 Unity。

## 1. 操作终结与释放修复

- `RoomFlowOperation` 新增缓存不可变 `CancellationToken _token`；`Token` 不再访问可能已释放的 CTS。
- `Dispose()` 幂等、单次：解除页面注册 → Dispose CTS（释放 root 链接注册）→ Dispose 尚未接管的会话；**Release 路径不再 Cancel**，不会取消已移交的在用连接。
- 失效/释放占用与“晚到任务终结”分离：取消先 `Invalidate()`（Cancel），旧 await 用缓存 token 安全观察取消；句柄 Dispose 不负责发取消信号。

每条终态 owner：

| 终态 | 释放 busy | 句柄 Dispose owner |
|---|---|---|
| Requesting 页面 ct 取消 | 取消回调立即清 `_current` | 页面回调内 Dispose；flow finally 再幂等调用 |
| Requesting Leave/Logout/root 取消 | `InvalidateCurrent` 立即清 `_current` | 同一路径 Dispose；晚到 await 用缓存 token 收口 |
| 票据失败/取消/端点非法/账号失效 | flow finally | flow finally；即使底层请求永不返回，取消路径已先行 Dispose |
| 排队 `Request(Match)` 抛错/返回 false | `AbandonQueued` | `AbandonQueued` 立即 Dispose |
| QueuedMatch 正常/无效消费后回 Main | flow 唯一观察者 Release | 同一观察者 `Release` Dispose |
| Joining / QueuedBattle / Running 被 Leave/覆盖 | 保持占用 | 唯一观察者等待清理信号 + Main 后 Dispose |
| 宿主关闭/`ProcedureFlow.Dispose` | `RoomFlowLifetime.Dispose` | lifetime 统一 `Invalidate + MarkCleaned + Dispose` |

## 2. 票据成功值校验

`call.Ok` 后先取 `JoinTicketInfo ticket = call.Value`，以下任一即返回稳定 `MetaFailure.Invalid`（InvalidResponse），零排队/零连接、不打印票据内容：

- `Value == null`
- `Address` 空
- `RoomId` 空
- `Ticket` 空

之后才解析地址并排队。

## 3. 单一完成观察入口

- 成功提交 `Request(Match)` 后立即注册唯一观察者（`ReleaseObserved` 最多一次）；不再依赖 Leave 事件，正常结束/失败同样能释放。
- `TryConsumeMatch` 遇到 invalid 排队：**不再清 `_current`**，只 `MarkCleaned()`；占用保留到阶段实际回 Main，再由观察者 Release。
- 观察路径固定为：`CleanupTask` → 等待阶段回 Main → `RoomFlowLifetime.RecoveryStep` → Release。
- 新增内部可重用扩展点 `RoomFlowLifetime.RecoveryStep`（`Func<RoomFlowOperation, UniTask>`）：T3 只在这里追加页面恢复/错误确认；必须在同一观察者内、Main 已成立后执行，不得另起一条观察者或提前 Release。`RecoveryStep` 返回并再次校验当前句柄后才释放占用。

## 文件

- `Assets/LiteGame/Modules/Procedure/Flow/RoomFlowOperation.cs`
- `Assets/LiteGame/Modules/Procedure/Flow/RoomFlowLifetime.cs`
- `Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs`

## T3 接线约束

T3 设置 `lifetime.RecoveryStep = async op => { if (!op.RecoverySuppressed) ...恢复/错误确认...; }`，不得在内部 Request 阶段、不得直接调 `Release`；`NotifyCleaned` 仍由 Match/Battle 执行体在清理（含 await 卸载）完成后调用。未接管 QueuedBattle 会话由失效路径恰好释放一次；Running 会话只取消工作，等 Battle 清理。

未执行编译/测试，不能声称 green；宿主编译与 T6 迁移仍待主代理安排。