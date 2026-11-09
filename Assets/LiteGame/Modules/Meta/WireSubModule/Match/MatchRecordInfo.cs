namespace MetaWire
{
    /// <summary>单局战绩行（结束原因值域与服务端枚举同源编号）。</summary>
    public sealed class MatchRecordInfo
    {
        public string MatchId { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int EndReason { get; set; }
        public int GameplayEndReason { get; set; }
        public long FinishedAtMs { get; set; }
    }
}
