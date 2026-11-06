using System.Collections.Generic;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 结算页排行投影（**冻结结果 → 行表**）。
    /// </summary>
    internal static class ResultRanking
    {
        /// <summary>构建排行行表（已按全序排好）。<paramref name="players"/> null/空 → 空表。
        /// 行类型＝契约 <see cref="MatchSettlementRow"/>（端口 <see cref="ISettlementSummary"/> 的返回值）。</summary>
        internal static MatchSettlementRow[] Build(IReadOnlyList<PlayerMatchResult> players,
            BattleRankPolicy policy)
        {
            if (players == null || players.Count == 0) return System.Array.Empty<MatchSettlementRow>();

            int n = players.Count;
            var rows = new MatchSettlementRow[n];
            var entityIds = new long[n];
            var kills = new int[n];
            for (int i = 0; i < n; i++)
            {
                PlayerMatchResult p = players[i];
                entityIds[i] = p.EntityId;
                kills[i] = p.Kills;
                rows[i] = new MatchSettlementRow(p.PlayerId, 0,
                    PlayerIdentity.PlaceholderNameFor(PlayerIdentity.OrdinalOfRows(players, p.EntityId)),
                    p.Kills, p.Deaths);
            }

            // 全序与名次走单一实现（与局内排行榜共用 BattleRankPolicy——策略表投影）。
            var order = new int[n];
            policy.SortDisplay(n, entityIds, kills, order);
            var sorted = new MatchSettlementRow[n];
            var sortedKills = new int[n];
            for (int i = 0; i < n; i++)
            {
                sorted[i] = rows[order[i]];
                sortedKills[i] = sorted[i].Kills;
            }
            for (int i = 0; i < n; i++)
                sorted[i] = new MatchSettlementRow(sorted[i].PlayerId, policy.RankAt(i, sortedKills),
                    sorted[i].Name, sorted[i].Kills, sorted[i].Deaths);
            return sorted;
        }
    }
}
