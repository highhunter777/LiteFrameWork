using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>段运行期：持有模块/关闭句柄/模块作用域，负责拓扑初始化与启动失败回滚。</summary>
    internal sealed class SegmentRuntime
    {
        private readonly object _gate = new object();
        private readonly List<IClientSegment> _modules = new List<IClientSegment>();
        private readonly List<(IClientSegmentHandle handle, SegmentScope scope)> _handles =
            new List<(IClientSegmentHandle, SegmentScope)>();
        private List<IClientSegment> _ordered;

        internal int Count { get { lock (_gate) return _modules.Count; } }

        internal IReadOnlyList<string> OrderedNames
        {
            get
            {
                lock (_gate)
                {
                    if (_ordered == null) return Array.Empty<string>();
                    var names = new string[_ordered.Count];
                    for (int i = 0; i < _ordered.Count; i++) names[i] = _ordered[i].Name;
                    return names;
                }
            }
        }

        internal void Add(IClientSegment module)
        {
            lock (_gate) _modules.Add(module);
        }

        /// <summary>启动前图校验/排序（无副作用；失败由 Host 进终态）。</summary>
        internal void Prepare()
        {
            lock (_gate) _ordered = SegmentGraphValidator.Order(_modules);
        }

        internal async UniTask InitializeAsync(ClientContext context, ClientScope rootScope,
            Action<string, string> trace, CancellationToken ct, Func<bool> shutdownRequested)
        {
            List<IClientSegment> ordered;
            lock (_gate)
            {
                if (_ordered == null) _ordered = SegmentGraphValidator.Order(_modules);
                ordered = new List<IClientSegment>(_ordered);
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (shutdownRequested != null && shutdownRequested())
                    throw new OperationCanceledException("引导被宿主关闭打断（ShutdownAsync 与 InitializeAsync 并发）");

                IClientSegment module = ordered[i];
                trace?.Invoke(module.Name, "init:start");
                var moduleScope = new SegmentScope(rootScope, module.Name);
                var moduleContext = new SegmentContext(context, module.Name, moduleScope);
                try
                {
                    await module.InitializeAsync(moduleContext, ct);
                }
                catch
                {
                    moduleScope.Dispose();                 // 失败模块先释放自己的登记资源
                    throw;
                }
                finally
                {
                    moduleContext.Close();
                }
                trace?.Invoke(module.Name, "init:done");
                lock (_gate) _handles.Add((new SegmentHandle(module), moduleScope));
            }
        }

        /// <summary>启动失败回滚：逆序关闭已成功模块并释放各自作用域；异常吞掉（原异常优先）。</summary>
        internal async UniTask RollbackAsync(Action<string, string> trace, Action<Exception> onFailure = null)
        {
            IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)> handles = SnapshotHandles();
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    trace?.Invoke(handles[i].handle.GetType().Name, "rollback:shutdown");
                    await handles[i].handle.ShutdownAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    trace?.Invoke(handles[i].handle.GetType().Name, "rollback:failed:" + ex.GetType().Name);
                }
                finally
                {
                    if (!handles[i].scope.IsDisposed) handles[i].scope.Dispose();
                    IReadOnlyList<Exception> failures = handles[i].scope.DisposeFailures;
                    for (int f = 0; f < failures.Count; f++) onFailure?.Invoke(failures[f]);
                }
            }

            lock (_gate) _handles.Clear();
        }

        internal IReadOnlyList<(IClientSegmentHandle handle, SegmentScope scope)> SnapshotHandles()
        {
            lock (_gate) return new List<(IClientSegmentHandle, SegmentScope)>(_handles);
        }
    }
}
