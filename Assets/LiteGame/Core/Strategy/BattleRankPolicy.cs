namespace LiteGame
{
    /// <summary>排行主序（表键 <c>rank.order</c> 的合法取值）。</summary>
    public enum BattleRankOrderKind
    {
        /// <summary>击杀降序（默认；第一名击杀最多）。</summary>
        KillsDescending = 0,

        /// <summary>击杀升序（少杀在前——仅策略表显式选择时生效）。</summary>
        KillsAscending = 1,
    }

    /// <summary>同击杀时的并列次序（表键 <c>rank.tie</c> 的合法取值）。</summary>
    public enum BattleRankTieBreak
    {
        /// <summary>EntityId 升序（默认；与旧口径一致）。</summary>
        EntityIdAscending = 0,

        /// <summary>EntityId 降序。</summary>
        EntityIdDescending = 1,
    }

    /// <summary>
    /// 对局排行策略（纯件、零引擎、零状态）：局内排行榜与结算页共用的**唯一全序/名次实现**。
    /// </summary>
    public readonly struct BattleRankPolicy
    {
        public readonly BattleRankOrderKind Order;
        public readonly BattleRankTieBreak TieBreak;

        public BattleRankPolicy(BattleRankOrderKind order, BattleRankTieBreak tieBreak)
        {
            Order = order;
            TieBreak = tieBreak;
        }

        /// <summary>缺表/缺行默认：击杀降序 → EntityId 升序（与历史静态门面同口径）。</summary>
        public static BattleRankPolicy Default => new BattleRankPolicy(
            BattleRankOrderKind.KillsDescending, BattleRankTieBreak.EntityIdAscending);

        /// <summary>全序比较：按主序比较击杀，相等时按并列键比较 EntityId。</summary>
        public bool Precedes(int leftKills, long leftId, int rightKills, long rightId)
        {
            if (leftKills != rightKills)
                return Order == BattleRankOrderKind.KillsDescending ? leftKills > rightKills : leftKills < rightKills;
            return TieBreak == BattleRankTieBreak.EntityIdAscending ? leftId < rightId : leftId > rightId;
        }

        /// <summary>把索引表排成展示序（<paramref name="order"/> 前 <paramref name="count"/> 项有效）。</summary>
        public void SortDisplay(int count, long[] ids, int[] kills, int[] order)
        {
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && Precedes(kills[current], ids[current], kills[order[j]], ids[order[j]]))
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = current;
            }
        }

        /// <summary>展示序第 <paramref name="index"/> 位的名次（1-based；同击杀并列）。</summary>
        public int RankAt(int index, int[] killsInOrder)
        {
            int rank = 1;
            for (int i = 1; i <= index; i++)
                if (killsInOrder[i] != killsInOrder[i - 1]) rank = i + 1;
            return rank;
        }

        /// <summary>策略事实串（默认 = <c>kills_desc_entity_asc</c>，与 B3b 既有值兼容）——策略值的规范文本。</summary>
        public string ToWireValue()
            => (Order == BattleRankOrderKind.KillsDescending ? "kills_desc" : "kills_asc")
               + (TieBreak == BattleRankTieBreak.EntityIdAscending ? "_entity_asc" : "_entity_desc");
    }
}
