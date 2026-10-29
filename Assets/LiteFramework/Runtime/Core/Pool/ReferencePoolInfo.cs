using System;

namespace LiteFramework
{
    /// <summary>开发 HUD 数据源（Debug 三宏内由 <see cref="ReferencePoolService.GetAllInfos"/> 产出）。</summary>
    public readonly struct ReferencePoolInfo
    {
        public readonly Type Type;
        public readonly int Unused;
        public readonly int Using;
        public readonly long CreatedCount;    // Acquire 未命中转 new 的次数（Misses = Acquired − PoolHits）
        public readonly long AcquireCount;
        public readonly long ReleaseCount;
        public readonly int MaxSize;
        public readonly int PeakUnused;
        public readonly long DroppedCount;
        public readonly long EvictedCount;
        public readonly long PoolHits;

        public ReferencePoolInfo(Type type, int unused, int using_, long created, long acquire, long release,
                                 int maxSize = ReferencePoolService.DefaultMaxSize,
                                 int peakUnused = 0, long droppedCount = 0,
                                 long evictedCount = 0, long poolHits = 0)
        {
            Type = type; Unused = unused; Using = using_;
            CreatedCount = created; AcquireCount = acquire; ReleaseCount = release;
            MaxSize = maxSize; PeakUnused = peakUnused; DroppedCount = droppedCount;
            EvictedCount = evictedCount; PoolHits = poolHits;
        }
    }
}
