using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteFrameworkNext
{
    /// <summary>生命周期小工具：一个取消源 + 逆序释放的资源列表；关闭后登记立即释放并抛 ObjectDisposedException。</summary>
    public sealed class Lifetime : IDisposable
    {
        private readonly CancellationTokenSource _source = new CancellationTokenSource();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private bool _disposed;

        /// <summary>关闭信号：Dispose 时取消，异步任务只认这个 token。</summary>
        public CancellationToken Token => _source.Token;

        /// <summary>是否已关闭（关闭后不再接受登记）。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>登记随本生命周期释放的资源；已关闭时立即释放该资源并抛异常。</summary>
        public void Add(IDisposable disposable)
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            if (_disposed)
            {
                disposable.Dispose();
                throw new ObjectDisposedException(nameof(Lifetime));
            }

            _disposables.Add(disposable);
        }

        /// <summary>发关闭信号，不释放已登记资源（Dispose 负责逆序释放）。</summary>
        public void Cancel()
        {
            if (_disposed) return;
            _source.Cancel();
        }

        /// <summary>取消 → 逆序释放 → 聚合异常抛出（后续资源仍会释放）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            List<Exception> errors = null;
            try
            {
                _source.Cancel();
            }
            catch (AggregateException ex)
            {
                (errors ??= new List<Exception>()).Add(ex);
            }

            for (int index = _disposables.Count - 1; index >= 0; index--)
            {
                try
                {
                    _disposables[index].Dispose();
                }
                catch (Exception ex)
                {
                    (errors ??= new List<Exception>()).Add(ex);
                }
            }

            _disposables.Clear();
            _source.Dispose();
            if (errors != null) throw new AggregateException("Lifetime 释放期异常", errors);
        }
    }
}
