using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using MetaWire;

namespace LiteGame
{
    /// <summary>
    /// 流程域的房间动作实现：单操作占用、取票/建房、票据校验与 Match 排队；不持 Meta 具体服务。
    /// </summary>
    internal sealed class ProcedureFlow : IRoomFlow, IAccountLoginControl, IDisposable
    {
        private const string BusyCode = "room.flow-busy";
        private const string InvalidCode = "room.flow-invalid";

        private readonly StageMachine<ProcedureId, ProcedureArgs> _machine;
        private readonly IRoomTicketSource _tickets;
        private readonly RoomFlowLifetime _lifetime;
        private readonly CancellationToken _rootToken;
        private readonly int _leaveTimeoutMs;
        private bool _disposed;

        public ProcedureFlow(StageMachine<ProcedureId, ProcedureArgs> machine, IRoomTicketSource tickets,
            RoomFlowLifetime lifetime, CancellationToken rootToken = default, int leaveTimeoutMs = 10000)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _rootToken = rootToken;
            _leaveTimeoutMs = leaveTimeoutMs;
            _lifetime.OperationStarted = ObserveRelease;
        }

        public string AccountId => _tickets.AccountId;

        public bool IsLoggedIn => _tickets.IsLoggedIn;

        public UniTask<MetaCall<JoinTicketInfo>> EnterRoomAsync(string roomId, CancellationToken ct = default)
            => EnterCoreAsync(roomId, 0, 0, null, null, create: false, ct);

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId = 0, int expectedPlayers = 0,
            CancellationToken ct = default)
            => EnterCoreAsync(string.Empty, mapId, expectedPlayers, null, null, create: true, ct);

        public UniTask<MetaCall<JoinTicketInfo>> CreateRoomAsync(int mapId, int expectedPlayers,
            string template, string gmKey, CancellationToken ct = default)
            => EnterCoreAsync(string.Empty, mapId, expectedPlayers, template, gmKey, create: true, ct);

        /// <summary>离房：先使当前操作失效；Main 排队只由 Match 消费，不在 OnLeave/重入窗口反向 Request。</summary>
        public bool LeaveMatch()
        {
            ThrowIfDisposed();
            PumpRelease();
            RoomFlowOperation operation = _lifetime.Current;
            bool hadOperation = operation != null;
            if (hadOperation) _lifetime.InvalidateCurrent(releaseRequesting: true);

            if (!_machine.Started) return hadOperation;

            if (_machine.Current == ProcedureId.Main) return hadOperation;

            _machine.Request(ProcedureId.Main);
            return true;
        }

        /// <summary>离房并等清理终态：Main+CleanupDone→true；Error 且未清理/卸载失败→false；超时→false。</summary>
        public UniTask<bool> LeaveMatchAsync(CancellationToken ct = default)
        {
            LeaveMatch();
            if (!_machine.Started || _rootToken.IsCancellationRequested) return UniTask.FromResult(true);
            RoomFlowOperation operation = _lifetime.Current;
            if (operation == null) return UniTask.FromResult(true);
            return WaitLeaveTerminalAsync(operation, ct);
        }

        private async UniTask<bool> WaitLeaveTerminalAsync(RoomFlowOperation operation, CancellationToken ct)
        {
            long deadline = DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(_leaveTimeoutMs).Ticks;
            while (true)
            {
                if (_rootToken.IsCancellationRequested || ct.IsCancellationRequested) return true;
                if (operation.RecoveryFailed && !operation.CleanupDone) return false;  // 卸载失败：保留 busy
                if (_machine.Started && _machine.Current == ProcedureId.Main && operation.CleanupDone) return true;
                if (_machine.Started && _machine.Current == ProcedureId.Main && operation.RecoveryFailed) return false;
                if (_machine.Started && _machine.Current == ProcedureId.Error) return operation.CleanupDone;
                if (DateTime.UtcNow.Ticks >= deadline) return false;             // 超时：保留 busy
                await UniTask.Yield();
            }
        }

        /// <summary>登出：先同步抑制旧恢复许可，再取消在途操作，最后委托票据源清登录态。</summary>
        public void Logout(string reason)
        {
            ThrowIfDisposed();
            _lifetime.SuppressRecovery();
            _lifetime.InvalidateCurrent(releaseRequesting: true);
            _tickets.Logout(reason);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.OperationStarted = null;
            _lifetime.Dispose();
        }

        private async UniTask<MetaCall<JoinTicketInfo>> EnterCoreAsync(string roomId, int mapId,
            int expectedPlayers, string template, string gmKey, bool create, CancellationToken ct)
        {
            ThrowIfDisposed();
            ct.ThrowIfCancellationRequested();
            PumpRelease();
            if (_machine.Current != ProcedureId.Main) return Invalid();
            if (_lifetime.IsBusy) return Busy();

            RoomFlowOperation operation = _lifetime.Begin();
            try
            {
                _lifetime.AttachPage(operation, ct);
                MetaCall<JoinTicketInfo> call = create
                    ? await CreateTicketAsync(operation, mapId, expectedPlayers, template, gmKey)
                    : await _tickets.RequestJoinTicketAsync(roomId, operation.Token);

                ct.ThrowIfCancellationRequested();
                operation.Token.ThrowIfCancellationRequested();
                if (!_lifetime.IsCurrent(operation))
                    throw new OperationCanceledException(operation.Token);
                if (!call.Ok) return call;

                JoinTicketInfo ticket = call.Value;
                if (ticket == null
                    || string.IsNullOrEmpty(ticket.Address)
                    || string.IsNullOrEmpty(ticket.RoomId)
                    || string.IsNullOrEmpty(ticket.Ticket))
                    return MetaCall<JoinTicketInfo>.Failed(
                        MetaFailure.Invalid("票据缺少实例地址/房号/票据号"));

                if (!_tickets.IsConfigured || !_tickets.IsLoggedIn
                    || string.IsNullOrEmpty(_tickets.AccountId))
                    return Invalid();

                if (!RoomEndpoint.TryParse(ticket.Address, out RoomEndpoint endpoint, out string reason))
                    return MetaCall<JoinTicketInfo>.Failed(
                        MetaFailure.Invalid("票据地址不可解析（" + reason + "）——联系部署侧核对 AdvertiseHost"));

                if (_machine.Current != ProcedureId.Main) return Invalid();

                _lifetime.DetachPage(operation);
                if (!_lifetime.IsCurrent(operation) || operation.Invalidated) return Invalid();
                var join = new MetaJoin(endpoint.Host, endpoint.Port, ticket.RoomId, ticket.Ticket);
                operation.MarkQueuedMatch(join);
                bool queued;
                try
                {
                    queued = _machine.Request(ProcedureId.Match,
                        new ProcedureArgs(metaJoin: join).WithRoomOperation(operation));
                }
                catch (InvalidOperationException)
                {
                    _lifetime.AbandonQueued(operation);
                    return Invalid();
                }
                if (!queued)
                {
                    _lifetime.AbandonQueued(operation);
                    return Invalid();
                }
                return call;
            }
            finally
            {
                if (!operation.Submitted)
                {
                    if (_lifetime.IsCurrent(operation))
                        _lifetime.InvalidateCurrent(releaseRequesting: true);
                    operation.Dispose();
                }
            }
        }

        private UniTask<MetaCall<JoinTicketInfo>> CreateTicketAsync(RoomFlowOperation operation,
            int mapId, int expectedPlayers, string template, string gmKey)
            => template == null && gmKey == null
                ? _tickets.CreateRoomTicketAsync(mapId, expectedPlayers, operation.Token)
                : _tickets.CreateRoomTicketAsync(mapId, expectedPlayers, template, gmKey, operation.Token);

        private void ObserveRelease(RoomFlowOperation operation)
        {
            if (operation.ReleaseObserved) return;
            operation.ReleaseObserved = true;
            ReleaseAfterCleanupAsync(operation).ForgetLogged("ProcedureFlow");
        }

        /// <summary>唯一观察者：清理完成 → 等阶段真正回 Main → 恢复导航 → 释放；未提交取消不做恢复。</summary>
        private async UniTask ReleaseAfterCleanupAsync(RoomFlowOperation operation)
        {
            await operation.CleanupTask;
            if (_rootToken.IsCancellationRequested || _disposed) return;
            if (!operation.Submitted || !_lifetime.IsCurrent(operation)) return;

            // 清理可能在 OnLeave 同步完成，此时阶段机仍停在 Battle：必须等实际 Current==Main，
            // 不能只让一帧或按 pending 猜。Error/Fatal 或阶段机停止时保持占用，不抢导航。
            while (!_rootToken.IsCancellationRequested && !_disposed
                && _lifetime.IsCurrent(operation)
                && _machine.Started
                && _machine.Current != ProcedureId.Error
                && _machine.Current != ProcedureId.Main)
            {
                await UniTask.Yield();
            }

            if (_rootToken.IsCancellationRequested || _disposed
                || !_lifetime.IsCurrent(operation)
                || !_machine.Started
                || _machine.Current != ProcedureId.Main)
                return;

            if (!operation.RecoverySuppressed && !operation.RecoveryDone)
            {
                Func<RoomFlowOperation, UniTask> recovery = _lifetime.RecoveryStep;
                if (recovery != null) await recovery(operation);
            }
            operation.MarkRecoveryDone();

            if (_rootToken.IsCancellationRequested || _disposed
                || !_lifetime.IsCurrent(operation)
                || !_machine.Started
                || _machine.Current != ProcedureId.Main) return;
            _lifetime.Release(operation);
        }

        /// <summary>无帧推进时的终态补检：恢复已完成且已回 Main 的操作在此释放。</summary>
        private void PumpRelease()
        {
            RoomFlowOperation operation = _lifetime.Current;
            if (operation == null || !operation.Submitted) return;
            if (!operation.CleanupDone || !operation.RecoveryDone) return;
            if (!_machine.Started || _machine.Current != ProcedureId.Main) return;
            _lifetime.Release(operation);
        }

        private static MetaCall<JoinTicketInfo> Busy()
            => MetaCall<JoinTicketInfo>.Failed(
                MetaFailure.Rejected(0, BusyCode, "入场操作进行中（取票/排队/接管/清理）"));

        private static MetaCall<JoinTicketInfo> Invalid()
            => MetaCall<JoinTicketInfo>.Failed(
                MetaFailure.Rejected(0, InvalidCode, "入场请求已失效（取消/登录态变化/阶段不允许）"));

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ProcedureFlow));
        }
    }
}
