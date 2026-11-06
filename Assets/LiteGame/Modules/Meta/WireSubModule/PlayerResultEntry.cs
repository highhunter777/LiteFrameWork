namespace MetaWire
{
    /// <summary>单玩家的结算增量项 + 结果事实（非 sealed 理由同上）。</summary>
    public class PlayerResultEntry
    {
        public string PlayerId { get; set; }
        public string AccountId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }

        /// <summary>奖励增量（-1,000,000..1,000,000；首版允许负值承载惩罚语义）。</summary>
        public long Delta { get; set; }

        /// <summary>修订号模式（缺省 -1 = 追加模式；≥0 = CAS 期望，不符即 conflict）。</summary>
        public long ExpectedRevision { get; set; } = -1L;

        /// <summary>幂等键（可空 = 服务端按 <c>match:{matchId}:{playerId}</c> 确定性派生）。</summary>
        public string OperationId { get; set; }
    }
}
