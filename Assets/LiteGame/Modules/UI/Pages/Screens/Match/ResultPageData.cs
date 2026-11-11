using System;
using System.Collections.Generic;
using LiteFramework.UI;

namespace LiteGame.Modules.UI.Pages.Screens.Match
{
    /// <summary>
    /// 结算页数据（强类型）：排行行 ＋ 本机摘要。本机行按 localPlayerId 从同一份行表定位，页面不重算。
    /// </summary>
    public sealed class ResultPageData : IUIData
    {
        /// <summary>排行行（名次/玩家/击杀/阵亡；序＝名次序）。</summary>
        public readonly struct Row
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
                Name = name ?? string.Empty;
                Kills = kills;
                Deaths = deaths;
            }
        }

        /// <summary>本局是否有本机行（无 = 页面隐藏本机摘要）。</summary>
        public readonly bool HasResult;
        public readonly int Kills;
        public readonly int Deaths;
        public readonly int Rank;
        /// <summary>结束原因枚举名（页面按值域拼文案键）。</summary>
        public readonly string Reason;
        public readonly IReadOnlyList<Row> Rows;

        public ResultPageData(IReadOnlyList<Row> rows, int localPlayerId, string reason)
        {
            Rows = rows ?? Array.Empty<Row>();
            Reason = reason ?? string.Empty;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].PlayerId != localPlayerId) continue;
                HasResult = true;
                Kills = Rows[i].Kills;
                Deaths = Rows[i].Deaths;
                Rank = Rows[i].Rank;
                break;
            }
        }
    }
}
