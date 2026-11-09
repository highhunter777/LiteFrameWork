using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 每实例共享的房间操作寿命：唯一当前操作、排队/接管、清理通知与恢复抑制；不做页面导航。
    /// </summary>
    internal sealed class RoomFlowLifetime : IDisposable
    {
        private readonly CancellationToken _rootToken;
        private readonly CancellationTokenRegistration _rootRegistration;
        private RoomFlowOperation _current;
        private bool _disposed;

        /// <summary>T3 扩展点：清理完成后的页面恢复/错误确认；与 release 共用同一个观察者。</summary>
        internal Func<RoomFlowOperation, UniTask> RecoveryStep { get; set; }

        /// <summary>唯一观察者入口：Begin 即通知当前 flow 观察该操作的清理完成（含本地直入）。</summary>
        internal Action<RoomFlowOperation> OperationStarted { get; set; }

        internal RoomFlowLifetime(CancellationToken rootToken = default)
        {
            _rootToken = rootToken;
            if (rootToken.CanBeCanceled)
                _rootRegistration = rootToken.Register(
                    static state => ((RoomFlowLifetime)state).SuppressAndCancel(), this);
        }

        internal RoomFlowOperation Current => _current;
        internal bool IsBusy => _current != null;
        internal bool IsDisposed => _disposed;

        /// <summary>占用本实例并返回新操作句柄（调用方保证当前空闲）。</summary>
        internal RoomFlowOperation Begin()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomFlowLifetime));
            if (_rootToken.IsCancellationRequested) throw new System.OperationCanceledException(_rootToken);
            var operation = new RoomFlowOperation(_rootToken, OnPageCanceled);
            _current = operation;
            OperationStarted?.Invoke(operation);
            return operation;
        }

        internal bool IsCurrent(RoomFlowOperation operation)
            => !_disposed && ReferenceEquals(_current, operation);

        internal void AttachPage(RoomFlowOperation operation, CancellationToken pageToken)
            => operation?.AttachPage(pageToken);

        internal void DetachPage(RoomFlowOperation operation)
            => operation?.DetachPage();

        /// <summary>同步抑制旧恢复（登出/宿主关闭），后续 Leave 不清除该许可。</summary>
        internal void SuppressRecovery() => _current?.SuppressRecovery();

        /// <summary>使当前操作失效；Requesting 无已接受副作用可立即释放占用。</summary>
        internal RoomFlowOperation InvalidateCurrent(bool releaseRequesting)
        {
            RoomFlowOperation operation = _current;
            if (operation == null) return null;

            if (releaseRequesting && operation.Phase == RoomFlowPhase.Requesting)
            {
                _current = null;
                operation.DetachPage();
                operation.Invalidate();
                operation.MarkCleaned();
                operation.Dispose();
                return operation;
            }

            operation.Invalidate();
            if (operation.Phase == RoomFlowPhase.QueuedBattle)
            {
                try { operation.TakePendingSession()?.Dispose(); }
                finally { operation.MarkCleaned(); }
            }
            return operation;
        }

        /// <summary>Match 实际入场：原子验活并一次接管排队参数；无效排队在这里被消费。</summary>
        internal bool TryConsumeMatch(RoomFlowOperation operation, out MetaJoin join)
        {
            join = default;
            if (!ReferenceEquals(_current, operation)) return false;
            if (operation.Phase != RoomFlowPhase.QueuedMatch) return false;

            if (operation.Invalidated)
            {
                operation.MarkCleaned();
                return false;
            }

            join = operation.Join;
            operation.MarkJoining();
            return true;
        }

        /// <summary>JoinAck 后把会话托管给操作句柄，等待 Battle 实际接管；失败时调用方仍持有会话。</summary>
        internal bool TryQueueBattle(RoomFlowOperation operation, IMatchSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!ReferenceEquals(_current, operation)
                || operation.Invalidated
                || operation.Phase != RoomFlowPhase.Joining)
                return false;

            operation.MarkQueuedBattle(session);
            return true;
        }

        /// <summary>Battle 实际接管：取得会话并进入 Running；无效排队不交出会话。</summary>
        internal bool TryTakeSession(RoomFlowOperation operation, out IMatchSession session)
        {
            session = null;
            if (!ReferenceEquals(_current, operation)
                || operation.Invalidated
                || operation.Phase != RoomFlowPhase.QueuedBattle)
                return false;

            session = operation.TakePendingSession();
            if (session == null) return false;
            operation.MarkRunning();
            return true;
        }

        internal void BeginCleanup(RoomFlowOperation operation)
        {
            if (ReferenceEquals(_current, operation)) operation.MarkCleaning();
        }

        /// <summary>阶段清理完成（会话/视图/卸载收口后由执行体调用）。</summary>
        internal void NotifyCleaned(RoomFlowOperation operation)
        {
            if (ReferenceEquals(_current, operation)) operation.MarkCleaned();
        }

        /// <summary>释放占用：只接受当前句柄且清理已完成；调用方须先确认 Main 条件。</summary>
        internal bool Release(RoomFlowOperation operation)
        {
            if (!ReferenceEquals(_current, operation)) return false;
            if (!operation.CleanupDone) return false;
            _current = null;
            operation.Dispose();
            return true;
        }

        internal void AbandonQueued(RoomFlowOperation operation)
        {
            if (!ReferenceEquals(_current, operation)) return;
            _current = null;
            operation.MarkCleaned();
            operation.Dispose();
        }

        private void OnPageCanceled(RoomFlowOperation operation)
        {
            if (!ReferenceEquals(_current, operation)) return;
            if (operation.Phase != RoomFlowPhase.Requesting) return;
            _current = null;
            operation.Invalidate();
            operation.MarkCleaned();
            operation.Dispose();
        }

        private void SuppressAndCancel()
        {
            _current?.SuppressRecovery();
            InvalidateCurrent(releaseRequesting: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _rootRegistration.Dispose();
            RoomFlowOperation operation = _current;
            _current = null;
            if (operation == null) return;
            operation.SuppressRecovery();
            operation.Invalidate();
            operation.MarkCleaned();
            operation.Dispose();
        }
    }
}
