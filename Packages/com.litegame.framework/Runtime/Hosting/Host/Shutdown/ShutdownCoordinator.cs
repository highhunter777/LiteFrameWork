using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>关闭协调器：刷新钩子 → 组合根 → 逆序模块 → 根释放；并持有并发关闭信号/失败聚合。</summary>
    internal sealed class ShutdownCoordinator
    {
        private readonly object _gate = new object();
        private readonly Func<ClientScope> _rootScope;
        private readonly Func<IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)>> _handles;
        private readonly Func<IReadOnlyList<(string name, Func<CancellationToken, UniTask> flush)>> _flushHooks;
        private readonly Func<ComposeRootRunner> _composeRoot;
        private readonly Func<ClientContext> _context;
        private readonly Func<Action<string, string>> _trace;
        private readonly Action<HostState> _setState;
        private readonly List<Exception> _failures = new List<Exception>();
        private bool _requested;
        private bool _rootFailuresReported;
        private UniTaskCompletionSource _signal;

        internal ShutdownCoordinator(
            Func<ClientScope> rootScope,
            Func<IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)>> handles,
            Func<IReadOnlyList<(string name, Func<CancellationToken, UniTask> flush)>> flushHooks,
            Func<ComposeRootRunner> composeRoot,
            Func<ClientContext> context,
            Func<Action<string, string>> trace,
            Action<HostState> setState)
        {
            _rootScope = rootScope ?? throw new ArgumentNullException(nameof(rootScope));
            _handles = handles ?? throw new ArgumentNullException(nameof(handles));
            _flushHooks = flushHooks ?? throw new ArgumentNullException(nameof(flushHooks));
            _composeRoot = composeRoot ?? throw new ArgumentNullException(nameof(composeRoot));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _trace = trace ?? throw new ArgumentNullException(nameof(trace));
            _setState = setState ?? throw new ArgumentNullException(nameof(setState));
        }

        internal bool IsShutdownRequested { get { lock (_gate) return _requested; } }

        internal IReadOnlyList<Exception> Failures { get { lock (_gate) return _failures; } }

        internal async UniTask ShutdownAsync(CancellationToken ct)
        {
            List<(string name, Func<CancellationToken, UniTask> flush)> flushes = null;
            UniTaskCompletionSource waitSignal;
            lock (_gate)
            {
                if (_requested)
                {
                    waitSignal = _signal;
                    if (waitSignal == null) return;
                }
                else
                {
                    _requested = true;
                    _setState(HostState.ShuttingDown);
                    _signal = new UniTaskCompletionSource();
                    flushes = new List<(string, Func<CancellationToken, UniTask>)>(_flushHooks());
                    waitSignal = null;
                }
            }

            if (waitSignal != null)
            {
                await waitSignal.Task;
                return;
            }

            try
            {
                _rootScope()?.Cancel();
                await RunFlushHooksAsync(flushes, ct);
                ReleaseComposeScope();

                IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)> handles = _handles();
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        await handles[i].handle.ShutdownAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddFailure(ex);
                    }
                    finally
                    {
                        ReleaseModuleScope(handles[i]);
                    }
                }
            }
            finally
            {
                IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)> remaining = _handles();
                for (int i = remaining.Count - 1; i >= 0; i--) ReleaseModuleScope(remaining[i]);
                DisposeRoot();

                UniTaskCompletionSource signal;
                lock (_gate)
                {
                    _setState(HostState.Closed);
                    signal = _signal;
                }
                signal?.TrySetResult();
            }
        }

        /// <summary>强制同步释放：只 Dispose Scope（组合根/模块/根），不调用模块 Shutdown；幂等。</summary>
        internal void ForceRelease()
        {
            lock (_gate) _requested = true;

            ReleaseComposeScope();
            IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)> handles = _handles();
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                Log.Warning($"ForceRelease 跳过模块优雅关闭：{handles[i].scope.Name}", LogTag.Bootstrap);
                ReleaseModuleScope(handles[i]);
            }
            DisposeRoot();

            lock (_gate)
            {
                _setState(HostState.Closed);
                _signal?.TrySetResult();
            }
        }

        internal void MarkClosed()
        {
            lock (_gate)
            {
                _requested = true;
                _setState(HostState.Closed);
            }
        }

        internal void ReleaseComposeScope()
        {
            IReadOnlyList<Exception> failures = _composeRoot()?.Release();
            if (failures == null || failures.Count == 0) return;
            lock (_gate) _failures.AddRange(failures);
        }

        internal void ReleaseModuleScope((IClientSegmentHandle handle, SegmentScope scope) entry)
        {
            if (entry.scope.IsDisposed) return;
            entry.scope.Dispose();
            lock (_gate) _failures.AddRange(entry.scope.DisposeFailures);
        }

        internal void DisposeRoot()
        {
            _rootScope()?.Dispose();
            lock (_gate)
            {
                if (_rootFailuresReported) return;
                _rootFailuresReported = true;
                ClientScope root = _rootScope();
                if (root != null) _failures.AddRange(root.DisposeFailures);
                ClientContext context = _context();
                if (context != null) _failures.AddRange(context.Services.DisposeFailures);
            }
        }

        private async UniTask RunFlushHooksAsync(
            List<(string name, Func<CancellationToken, UniTask> flush)> flushes, CancellationToken ct)
        {
            for (int i = 0; i < flushes.Count; i++)
            {
                try
                {
                    _trace()?.Invoke(flushes[i].name, "flush:start");
                    await flushes[i].flush(ct);
                    _trace()?.Invoke(flushes[i].name, "flush:done");
                }
                catch (Exception ex)
                {
                    AddFailure(ex);
                }
            }
        }

        internal void AddFailure(Exception ex)
        {
            lock (_gate) _failures.Add(ex);
        }
    }
}
