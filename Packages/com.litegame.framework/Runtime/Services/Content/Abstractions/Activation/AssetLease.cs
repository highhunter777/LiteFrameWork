using System;

namespace LiteFramework
{
    /// <summary>
    /// 资源租约：对已加载资产的一份**可释放持有权**。
    /// </summary>
    public sealed class AssetLease<T> : IDisposable where T : class
    {
        private readonly Action<AssetLease<T>> _releaseCallback;
        private bool _released;

        /// <summary>租约标识（location 或缓存键；诊断/断言用）。</summary>
        public string Key { get; }

        /// <summary>持有的资产对象（实现方保证 Release 后不可再依赖；是否置 null 由实现方决定）。</summary>
        public T Asset { get; }

        /// <summary>是否已释放。</summary>
        public bool IsReleased { get { lock (this) return _released; } }

        /// <summary>实现方工厂缝（IContentService 实现方创建租约；消费方只 Dispose 不构造）。</summary>
        public AssetLease(string key, T asset, Action<AssetLease<T>> releaseCallback)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Asset = asset;
            _releaseCallback = releaseCallback ?? throw new ArgumentNullException(nameof(releaseCallback));
        }

        /// <summary>释放租约（幂等；仅首次触发回调通知服务方递减引用）。</summary>
        public void Dispose()
        {
            lock (this)
            {
                if (_released) return;
                _released = true;
            }
            _releaseCallback(this);
        }
    }
}
