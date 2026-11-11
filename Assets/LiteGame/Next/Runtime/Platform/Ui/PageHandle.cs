using System;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>页面句柄：页面实例、元数据、展示代次与页面级取消源；页面异步任务一律用 Lifetime.Token。</summary>
    public sealed class PageHandle
    {
        private readonly Func<PageHandle, UniTask> _close;
        private readonly Lifetime _lifetime;

        internal PageHandle(PageBase page, PageSpec spec, int displayGeneration, Func<PageHandle, UniTask> close)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _close = close ?? throw new ArgumentNullException(nameof(close));
            DisplayGeneration = displayGeneration;
            _lifetime = new Lifetime();
        }

        /// <summary>页面实例（页面缓存复用时同一实例跨展示代次保持）。</summary>
        public PageBase Page { get; }

        /// <summary>打开时的页面元数据。</summary>
        public PageSpec Spec { get; }

        /// <summary>页面级生命周期：关闭页面即取消；已关闭后再取 Token 会抛（先捕获 Token）。</summary>
        public Lifetime Lifetime => _lifetime;

        /// <summary>是否已关闭（关闭后 CloseAsync 为空操作）。</summary>
        public bool IsClosed { get; private set; }

        /// <summary>展示代次：同一页面实例每次展示 +1，用于丢弃旧展示的异步回调。</summary>
        public int DisplayGeneration { get; }

        /// <summary>该展示代次是否仍是当前代次（旧代次或已关闭返回 false）。</summary>
        public bool IsCurrent(int displayGeneration) => !IsClosed && DisplayGeneration == displayGeneration;

        /// <summary>关闭本页；已关闭或运行时已关闭时为空操作。</summary>
        public UniTask CloseAsync() => IsClosed ? UniTask.CompletedTask : _close(this);

        internal void MarkClosed()
        {
            if (IsClosed) return;
            IsClosed = true;
            _lifetime.Dispose();
        }
    }
}
