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
            for (int i = 0; i < n; i++)
            {
                PlayerMatchResult p = players[i];
                entityIds[i] = p.EntityId;
                rows[i] = new Row(p.PlayerId, 0,
                    PlayerIdentity.PlaceholderNameFor(PlayerIdentity.OrdinalOfRows(players, p.EntityId)),
                    p.Kills, p.Deaths);
            }

            // 全序：击杀降序 → EntityId 升序（插入排序——名单小，零比较器分配）
            for (int i = 1; i < n; i++)
            {
                Row row = rows[i];
                long id = entityIds[i];
                int j = i - 1;
                while (j >= 0 && (rows[j].Kills < row.Kills
                        || (rows[j].Kills == row.Kills && entityIds[j] > id)))
                {
                    rows[j + 1] = rows[j];
                    entityIds[j + 1] = entityIds[j];
                    j--;
                }
                rows[j + 1] = row;
                entityIds[j + 1] = id;
            }

            // 名次：1 + 击杀严格大于本行者数（并列同名次）
            for (int i = 0; i < n; i++)
            {
                int better = 0;
                for (int k = 0; k < n; k++)
                    if (rows[k].Kills > rows[i].Kills) better++;
                rows[i] = new Row(rows[i].PlayerId, better + 1, rows[i].Name, rows[i].Kills, rows[i].Deaths);
            }
            return rows;
        }
    }
}
