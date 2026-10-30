namespace LiteGame
{
    /// <summary>
    /// 对局排行**全序单源**（局内排行榜 <c>BattleRankboardDriver</c> 与结算页 <c>ResultRanking</c> 共用）：
    /// 击杀降序 → EntityId 升序。并列同名次的判据也在此收口（名次 = 1 + 击杀严格大于本行的人数）——
    /// 两处各写一份排序/名次会在同分边界漂移。
    ///
    /// 名单规模 ≤ 内核席位上限，插入排序零比较器分配；调用方持有缓冲（排行榜每帧复用，结算一次性）。
    /// </summary>
    internal static class BattleRankOrder
    {
        /// <summary>全序比较：击杀多者在前；同击杀按 EntityId 升序（全序防同分抖动）。</summary>
        internal static bool Precedes(int leftKills, long leftId, int rightKills, long rightId)
            => leftKills != rightKills ? leftKills > rightKills : leftId < rightId;

        /// <summary>把索引表排成展示序（<paramref name="order"/> 前 <paramref name="count"/> 项有效）。</summary>
        internal static void SortDisplay(int count, long[] ids, int[] kills, int[] order)
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

        /// <summary>展示序第 <paramref name="index"/> 位的名次（1-based；同击杀并列——与旧结算口径同式）。</summary>
        internal static int RankAt(int index, int[] killsInOrder)
        {
            int rank = 1;
            for (int i = 1; i <= index; i++)
                if (killsInOrder[i] != killsInOrder[i - 1]) rank = i + 1;
            return rank;
        }
    }
}
