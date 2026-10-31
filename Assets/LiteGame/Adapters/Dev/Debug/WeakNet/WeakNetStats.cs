#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteNet.Diagnostics
{
    /// <summary>注入统计只读快照（HUD/测试共用；不参与判定）。</summary>
    public readonly struct WeakNetStats
    {
        public readonly uint Seed;
        public readonly int RollCount;
        public readonly int Queued;
        public readonly int Dropped;
        public readonly int DroppedUp;
        public readonly int DroppedDown;
        public readonly int Duplicated;
        public readonly int Reordered;
        public readonly int Overflow;
        public readonly int Released;

        public WeakNetStats(uint seed, int rollCount, int queued, int dropped, int droppedUp, int droppedDown,
            int duplicated, int reordered, int overflow, int released)
        {
            Seed = seed;
            RollCount = rollCount;
            Queued = queued;
            Dropped = dropped;
            DroppedUp = droppedUp;
            DroppedDown = droppedDown;
            Duplicated = duplicated;
            Reordered = reordered;
            Overflow = overflow;
            Released = released;
        }

        public static readonly WeakNetStats Empty = new WeakNetStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
#endif
