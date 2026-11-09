using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>一次入场操作的寿命阶段（由流程/阶段按顺序推进，不引入独立状态机）。</summary>
    internal enum RoomFlowPhase
    {
        Requesting,
        QueuedMatch,
        Joining,
        QueuedBattle,
        Running,
        Cleaning,
        Cleaned,
    }

    /// <summary>一次入场操作的句柄：阶段、独立取消、页面注册、待接管会话与清理完成信号。</summary>
    internal sealed class RoomFlowOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationToken _token;
        private readonly Action<RoomFlowOperation> _pageCanceled;
        private readonly UniTaskCompletionSource _cleanup = new();
        private CancellationTokenRegistration _pageRegistration;
        private IAccountSession _pendingSession;
        private bool _pageRegistered;
        private bool _disposed;

        internal RoomFlowOperation(CancellationToken rootToken, Action<RoomFlowOperation> pageCanceled)
        {
            _cancellation = rootToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(rootToken)
                : new CancellationTokenSource();
            _token = _cancellation.Token;
            _pageCanceled = pageCanceled ?? throw new ArgumentNullException(nameof(pageCanceled));
        }

        internal RoomFlowPhase Phase { get; private set; }
        internal bool Invalidated { get; private set; }
        internal bool Submitted { get; private set; }
        internal bool RecoverySuppressed { get; private set; }
        internal bool RecoveryDone { get; private set; }
        internal bool CleanupDone { get; private set; }
        internal bool ReleaseObserved { get; set; }
        internal MetaJoin Join { get; private set; }

        /// <summary>恢复代次：入口（PageFlow）在操作开始时签发；旧代次不得覆盖新入口（登录页）。</summary>
        internal long RecoveryGeneration { get; private set; } = long.MinValue;
        internal bool RecoveryCompleted { get; private set; }
        internal LiteNet.Proto.MatchEnded RecoveryResult { get; private set; }
        internal int RecoveryPlayerId { get; private set; } = -1;
        internal bool RecoveryFailed { get; private set; }
        internal string RecoveryErrorTitle { get; private set; }
        internal string RecoveryErrorMessage { get; private set; }

        /// <summary>不可变令牌副本：句柄释放后晚到任务仍可安全读取取消态。</summary>
        internal CancellationToken Token => _token;
        internal UniTask CleanupTask => _cleanup.Task;

        /// <summary>登记页面取消：只在 Requesting 阶段生效，入队前必须解除。</summary>
        internal void AttachPage(CancellationToken pageToken)
        {
            if (!pageToken.CanBeCanceled || _pageRegistered) return;
            _pageRegistered = true;
            _pageRegistration = pageToken.Register(
                static state => ((RoomFlowOperation)state).OnPageCanceled(), this);
        }

        /// <summary>解除页面取消注册（已接受的操作不再随页面隐藏取消）。</summary>
        internal void DetachPage()
        {
            if (!_pageRegistered) return;
            _pageRegistered = false;
            _pageRegistration.Dispose();
            _pageRegistration = default;
        }

        internal void MarkQueuedMatch(MetaJoin join)
        {
            Join = join;
            Submitted = true;
            Phase = RoomFlowPhase.QueuedMatch;
        }

        /// <summary>进入 Joining（本地直入由阶段调用；排队路径已有 Submitted）。</summary>
        internal void MarkJoining()
        {
            Submitted = true;
            Phase = RoomFlowPhase.Joining;
        }

        internal void MarkQueuedBattle(IAccountSession session)
        {
            _pendingSession = session;
            Phase = RoomFlowPhase.QueuedBattle;
        }

        internal IAccountSession TakePendingSession()
        {
            IAccountSession session = _pendingSession;
            _pendingSession = null;
            return session;
        }

        internal void MarkRunning() => Phase = RoomFlowPhase.Running;

        internal void MarkCleaning() => Phase = RoomFlowPhase.Cleaning;

        internal void MarkCleaned()
        {
            if (CleanupDone) return;
            Phase = RoomFlowPhase.Cleaned;
            CleanupDone = true;
            _cleanup.TrySetResult();
        }

        internal void Invalidate()
        {
            if (Invalidated) return;
            Invalidated = true;
            try { _cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        internal void SuppressRecovery() => RecoverySuppressed = true;

        internal void MarkRecoveryDone() => RecoveryDone = true;

        /// <summary>记录操作开始时的入口代次；恢复时与 PageFlow 当前代次比对。</summary>
        internal void MarkRecoveryGeneration(long generation) => RecoveryGeneration = generation;

        /// <summary>记录对局终态事实（正常结束→结算页；否则→大厅）。</summary>
        internal void MarkBattleOutcome(bool completed, LiteNet.Proto.MatchEnded result, int playerId)
        {
            RecoveryCompleted = completed;
            RecoveryResult = result;
            RecoveryPlayerId = playerId;
        }

        /// <summary>记录房间侧失败；RecoveryStep 先显示错误反馈再回大厅。</summary>
        internal void MarkRoomFailure(string title, string message)
        {
            RecoveryFailed = true;
            RecoveryErrorTitle = string.IsNullOrEmpty(title) ? "进入房间失败" : title;
            RecoveryErrorMessage = string.IsNullOrEmpty(message) ? "房间连接已中断，请重试" : message;
        }

        private void OnPageCanceled() => _pageCanceled(this);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachPage();
            _cancellation.Dispose();
            TakePendingSession()?.Dispose();
        }
    }
}
