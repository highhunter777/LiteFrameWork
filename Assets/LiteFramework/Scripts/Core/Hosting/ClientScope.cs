using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteFramework
{
    /// <summary>
    /// 客户端作用域原语（《商业级通用客户端框架总设计》§6.2）：Cancellation、Disposable 和
    /// 资源租约的 **LIFO 释放**。这是 Root/Account/Match/Scene/UI 五层 Scope 树的轻量底座——
    /// 本批只落原语，各层语义树与"短生命周期依赖不进根容器"的装配约束归 C1 后续批次。
    ///
    /// 语义：
    /// - <see cref="Token"/> 随 <see cref="Cancel"/> 或**父 Scope 取消**而失效（链接 CTS——
    ///   根取消必须级联到全部子孙，跨帧异步绑定它是 §4 原则 4 的落点）。
    /// - <see cref="Register"/> 登记可释放资源，<see cref="Dispose"/> 按**登记逆序**释放
    ///   （后创建的先释放——先建者可能被后建者依赖）。
    /// - 单项释放异常**不阻断其余**（聚合上报）；重复 Dispose 幂等。
    /// - Dispose 只做本 Scope 的释放与取消；父 Scope 的释放不由子触发（所有权单向）。
    /// </summary>
    public sealed class ClientScope : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<IDisposable> _owned = new List<IDisposable>();
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        /// <summary>作用域名（诊断/日志定位用，非唯一键）。</summary>
        public string Name { get; }

        /// <summary>本作用域的取消令牌：Cancel/Dispose/父级取消都会使其失效。</summary>
        public CancellationToken Token { get; }

        /// <summary>父作用域（null = 根）。仅作诊断链路展示，不做双向引用。</summary>
        public ClientScope Parent { get; }

        /// <summary>已登记未释放的资源数（诊断/测试断言用）。</summary>
        public int OwnedCount { get { lock (_gate) { return _owned.Count; } } }

        /// <summary>是否已完成释放（幂等标记）。</summary>
        public bool IsDisposed { get { lock (_gate) { return _disposed; } } }

        /// <summary>聚合释放期捕获的异常（按释放顺序；不抛出——上报语义）。</summary>
        public IReadOnlyList<Exception> DisposeFailures { get { lock (_gate) { return _disposeFailures; } } }
        private readonly List<Exception> _disposeFailures = new List<Exception>();

        public ClientScope(string name, ClientScope parent = null, CancellationToken externalCancellationToken = default)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parent = parent;

            if (parent != null && externalCancellationToken != default)
                throw new ArgumentException("父作用域与外部令牌不可同时指定（取消来源必须唯一可解释）", nameof(externalCancellationToken));

            if (parent != null)
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(parent.Token);
            }
            else if (externalCancellationToken != default)
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            }
            else
            {
                _cts = new CancellationTokenSource();
            }

            Token = _cts.Token;
        }

        /// <summary>创建子作用域（§6.2 五层 Scope 树的建树语法糖）：
        /// 子 Scope 链接父级 CTS（父取消级联子），子 Dispose 不影响父。</summary>
        public ClientScope CreateChild(string childName)
        {
            return new ClientScope(childName, this);
        }

        /// <summary>登记一个可释放资源（返回资源本身，便于链式使用）。
        /// 已释放后登记：立即释放并抛 <see cref="ObjectDisposedException"/>（登记进已死作用域 = 生命周期错误，必须显性失败）。</summary>
        public T Register<T>(T disposable) where T : IDisposable
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            lock (_gate)
            {
                if (_disposed)
                {
                    disposable.Dispose();
                    throw new ObjectDisposedException(nameof(ClientScope), $"作用域 {Name} 已释放——登记进已死作用域的资源被就地释放");
                }
                _owned.Add(disposable);
            }
            return disposable;
        }

        /// <summary>主动取消本作用域（不释放资源——释放只走 Dispose；取消只切断异步）。</summary>
        public void Cancel()
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// 逆序释放全部登记资源并取消令牌。单项异常不阻断其余（聚合进 <see cref="DisposeFailures"/>）；
        /// 幂等；释放顺序 = 登记逆序（LIFO）。
        /// </summary>
        public void Dispose()
        {
            List<IDisposable> toRelease;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toRelease = new List<IDisposable>(_owned);
                _owned.Clear();
            }

            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }

            for (int i = toRelease.Count - 1; i >= 0; i--)
            {
                try
                {
                    toRelease[i].Dispose();
                }
                catch (Exception ex)
                {
                    lock (_gate) _disposeFailures.Add(ex);
                }
            }

            _cts.Dispose();
        }
    }
}
