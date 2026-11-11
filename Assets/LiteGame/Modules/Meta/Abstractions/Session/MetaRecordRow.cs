namespace LiteGame.Modules.Meta.Abstractions.Session
{
    /// <summary>战绩行（最近 N 局；时间已按本地时区格式化）。</summary>
    public sealed class MetaRecordRow
    {
        public readonly string MatchId;
        public readonly int Kills;
        public readonly int Deaths;
        public readonly string Reason;
        public readonly string TimeText;

        public MetaRecordRow(string matchId, int kills, int deaths, string reason, string timeText)
        {
            MatchId = matchId ?? string.Empty;
            Kills = kills;
            Deaths = deaths;
            Reason = reason ?? string.Empty;
            TimeText = timeText ?? string.Empty;
        }
    }
}
