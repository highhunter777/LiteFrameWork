using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 组合根执行器（Host 装配末段的协作件）：持 <c>AppComposition.ComposeAfter</c> 回调，负责
    /// 开一个与模块同语义的**装配窗口**（<c>Put</c> 合法）+ 独立资源作用域，并在关闭/回滚时先于模块释放。
    ///
    /// 为什么独立成件：组合根语义（窗口开关、失败即回收自身作用域、逆序第一的释放位）属于装配主线，
    /// 但不必让 <see cref="ClientHost"/> 本体继续变胖（类行数冻结预算只许降）。
    /// </summary>
    internal sealed class ComposeRootRunner
    {
        private readonly Func<ModuleContext, UniTask> _compose;
        private ModuleScope _scope;

        internal ComposeRootRunner(Func<ModuleContext, UniTask> compose)
            => _compose = compose ?? throw new ArgumentNullException(nameof(compose));

        /// <summary>执行组合根：回调拿到打开的窗口与自己的作用域。失败时**先释放自身作用域**再抛出
        /// （模块回滚由 Host 负责）；成功后作用域留待关闭时释放。</summary>
        internal async UniTask RunAsync(ClientContext context, ClientScope rootScope,
            Action<string, string> trace, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var scope = new ModuleScope(rootScope, "AppComposition");
            var window = new ModuleContext(context, "AppComposition", scope);
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
            ModuleScope scope = _scope;
            _scope = null;
            if (scope == null || scope.IsDisposed) return Array.Empty<Exception>();
            scope.Dispose();
            return scope.DisposeFailures;
        }
    }
}
