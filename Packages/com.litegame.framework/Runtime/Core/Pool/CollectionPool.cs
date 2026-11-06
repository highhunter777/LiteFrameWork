using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 集合池内核（《对象池专项设计》§4）：组合 <see cref="ObjectPool{T}"/>。
    /// </summary>
    public sealed class CollectionPool<TCollection, TItem> where TCollection : class, ICollection<TItem>, new()
    {
        private readonly ObjectPool<TCollection> _pool;

        public CollectionPool(int maxIdle = 8, string statsName = null, bool diagnoseDoubleRelease = false)
        {
            _pool = new ObjectPool<TCollection>(
                create: () => new TCollection(),
                onRelease: c => c.Clear(),
                maxIdle: maxIdle,
                statsName: statsName ?? $"CollectionPool.{typeof(TCollection).Name}",
                diagnoseDoubleRelease: diagnoseDoubleRelease);
        }

        /// <summary>取一个已清空的集合（状态未定义契约同内核：内容由调用方填充）。</summary>
        public TCollection Acquire() => _pool.Acquire();

        /// <summary>归还（Clear 由池执行）。nowMs 语义同内核（供按龄收缩，可选）。</summary>
        public void Release(TCollection collection, long nowMs = 0) => _pool.Release(collection, nowMs);

        public int MaxIdle => _pool.MaxIdle;
        public int UnusedCount => _pool.UnusedCount;
        public long AcquiredCount => _pool.AcquiredCount;
        public long PoolHits => _pool.PoolHits;
    }

}
