namespace LiteGame
{
    /// <summary>
    /// 结算排行行（契约数据，零引擎）：页面侧取 Rank/Name/Kills/Deaths；PlayerId 供调用方定位本地行。
    /// 由 Battle 域投影产出（<see cref="ISettlementSummary"/>），结算页只读本结构。
    /// </summary>
    public readonly struct MatchSettlementRow
    {
        public readonly int PlayerId;
        public readonly int Rank;
        public readonly string Name;
        public readonly int Kills;
        public readonly int Deaths;

        public MatchSettlementRow(int playerId, int rank, string name, int kills, int deaths)
        {
            PlayerId = playerId;
            Rank = rank;
            Name = name;
            Kills = kills;
            Deaths = deaths;
        }
    }
}
