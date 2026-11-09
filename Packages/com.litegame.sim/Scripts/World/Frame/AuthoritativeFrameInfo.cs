namespace LiteSim
{
    /// <summary>
    /// 权威快照的和解材料（P2-A《联机同步残差收敛专项设计》§2.2 增量部分和解输入）： 帧内**实际携带的槽位集合** + 逐槽位公共口径校验值 + 可见摘要/Match 摘要。
    /// </summary>
    public readonly struct AuthoritativeFrameInfo
    {
        /// <summary>是否全量帧（全图覆盖/全图 checksum 判定）。</summary>
        public readonly bool IsFull;

        /// <summary>全量口径折叠（<see cref="SimChecksum.ComputePublicChecksum"/>）——仅 IsFull 时使用。</summary>
        public readonly uint FullChecksum;

        /// <summary>携带槽位集合的公共口径折叠（<see cref="SimChecksum.ComputeVisibleChecksum"/>）——仅增量使用。</summary>
        public readonly uint VisibleChecksum;

        /// <summary>携带 Match 的公共口径折叠（<see cref="SimChecksum.ComputeMatchChecksum"/>）——仅增量使用。</summary>
        public readonly uint MatchChecksum;

        /// <summary>携带槽位数（增量）。</summary>
        public readonly int SlotCount;

        /// <summary>携带槽位（升序；仅增量使用）。</summary>
        public readonly int[] Slots;

        /// <summary>与 <see cref="Slots"/> 平行的逐槽公共口径校验值（wire；仅增量使用）。</summary>
        public readonly uint[] SlotChecksums;

        public AuthoritativeFrameInfo(bool isFull, uint fullChecksum, uint visibleChecksum, uint matchChecksum,
            int slotCount, int[] slots, uint[] slotChecksums)
        {
            IsFull = isFull;
            FullChecksum = fullChecksum;
            VisibleChecksum = visibleChecksum;
            MatchChecksum = matchChecksum;
            SlotCount = slotCount;
            Slots = slots;
            SlotChecksums = slotChecksums;
        }
    }
}
