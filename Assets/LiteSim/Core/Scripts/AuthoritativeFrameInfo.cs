namespace LiteSim
{
    /// <summary>
    /// 权威快照的和解材料（P2-A《联机同步残差收敛专项设计》§2.2 增量部分和解输入）：
    /// 帧内**实际携带的槽位集合** + 逐槽位公共口径校验值 + 可见摘要/Match 摘要。
    ///
    /// 由协议层（LiteNet）从 wire 快照提取（<c>SnapshotReassembler.Collect</c>），交
    /// <see cref="RollbackSim.OnAuthoritativeSnapshot(int, SimWorldState, in AuthoritativeFrameInfo)"/> 判定——
    /// Core 不依赖协议。数组由调用方持有并复用（<see cref="Slots"/>/<see cref="SlotChecksums"/> 长度须 ≥ <see cref="SlotCount"/>）。
    ///
    /// 语义边界：<see cref="IsFull"/> = true 时只读 <see cref="FullChecksum"/>（全图口径，与旧和解同构）；
    /// false 时读 <see cref="VisibleChecksum"/>/<see cref="MatchChecksum"/>/<see cref="Slots"/>/<see cref="SlotChecksums"/>
    /// （增量：只按携带槽位做部分和解，视野外槽位保留本地预测）。
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
