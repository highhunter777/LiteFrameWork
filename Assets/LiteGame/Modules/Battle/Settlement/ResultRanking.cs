using System.Collections.Generic;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 结算页排行投影（**冻结结果 → 行表**；口径单一裁决处）：
    /// - **全序** = 击杀降序 → EntityId 升序（与局内排行榜 <c>BattleRankboardDriver</c> 同一全序）；
    /// - **名次** = 1 + 击杀**严格大于**本行的人数（并列同名次——与本地行结算口径同式）；
    /// - **名字** = <c>玩家{k}</c>，k = <see cref="PlayerIdentity.OrdinalOfRows"/>（与局内身份序号**同一条规则**，
    ///   输入面 = 冻结行表）。
    /// 名单 ≤ 内核席位上限，插入排序即可（零比较器分配）。
    /// </summary>
    internal static class ResultRanking
    {
        /// <summary>排行行（Lua 侧取 Rank/Name/Kills/Deaths；PlayerId 供调用方定位本地行）。</summary>
        internal readonly struct Row
        {
            public readonly int PlayerId;
            public readonly int Rank;
            public readonly string Name;
            public readonly int Kills;
            public readonly int Deaths;

            public Row(int playerId, int rank, string name, int kills, int deaths)
            {
                PlayerId = playerId;
                Rank = rank;
                Name = name;
                Kills = kills;
                Deaths = deaths;
            }
        }

        /// <summary>构建排行行表（已按全序排好）。<paramref name="players"/> null/空 → 空表。</summary>
        internal static Row[] Build(IReadOnlyList<PlayerMatchResult> players)
        {
            if (players == null || players.Count == 0) return System.Array.Empty<Row>();

            int n = players.Count;
            var rows = new Row[n];
            var entityIds = new long[n];
            var kills = new int[n];
            for (int i = 0; i < n; i++)
            {
                PlayerMatchResult p = players[i];
                entityIds[i] = p.EntityId;
                kills[i] = p.Kills;
                rows[i] = new Row(p.PlayerId, 0,
                    PlayerIdentity.PlaceholderNameFor(PlayerIdentity.OrdinalOfRows(players, p.EntityId)),
                    p.Kills, p.Deaths);
            }

            // 全序与名次走单一实现（与局内排行榜共用 BattleRankOrder）。
            var order = new int[n];
            BattleRankOrder.SortDisplay(n, entityIds, kills, order);
            var sorted = new Row[n];
            var sortedKills = new int[n];
            for (int i = 0; i < n; i++)
            {
                sorted[i] = rows[order[i]];
                sortedKills[i] = sorted[i].Kills;
            }
            for (int i = 0; i < n; i++)
                sorted[i] = new Row(sorted[i].PlayerId, BattleRankOrder.RankAt(i, sortedKills),
                    sorted[i].Name, sorted[i].Kills, sorted[i].Deaths);
            return sorted;
        }
    }
}
