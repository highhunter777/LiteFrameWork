using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 组合根执行器（Host 装配末段的协作件）：持 <c>AppComposition.ComposeAfter</c> 回调，负责 开一个与模块同语义的**装配窗口**（<c>Put</c> 合法）+ 独立资源作用域。
    /// </summary>
    internal sealed class ComposeRootRunner
    {
        private readonly Func<SegmentContext, UniTask> _compose;
        private SegmentScope _scope;

        internal ComposeRootRunner(Func<SegmentContext, UniTask> compose)
            => _compose = compose ?? throw new ArgumentNullException(nameof(compose));

        /// <summary>执行组合根：回调拿到打开的窗口与自己的作用域。失败时**先释放自身作用域**再抛出
        /// （模块回滚由 Host 负责）；成功后作用域留待关闭时释放。</summary>
        internal async UniTask RunAsync(ClientContext context, ClientScope rootScope,
            Action<string, string> trace, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var scope = new SegmentScope(rootScope, "AppComposition");
            var window = new SegmentContext(context, "AppComposition", scope);
            try
            {
                trace?.Invoke("AppComposition", "compose:start");
                await _compose(window);
                trace?.Invoke("AppComposition", "compose:done");
            }
            catch
            {
                scope.Dispose();                           // 组合根自己的登记资源就地回收（模块回滚在外层）
                throw;
            }
            finally
            {
                window.Close();
            }
            _scope = scope;
        }

        /// <summary>释放组合根作用域（幂等）：返回释放失败列表（由 Host 聚合上报，不抛出）。</summary>
        internal IReadOnlyList<Exception> Release()
        {
            SegmentScope scope = _scope;
            _scope = null;
            if (scope == null || scope.IsDisposed) return Array.Empty<Exception>();
            scope.Dispose();
            return scope.DisposeFailures;
        }
    }
}
