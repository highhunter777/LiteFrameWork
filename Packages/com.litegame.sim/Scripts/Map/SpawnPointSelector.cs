namespace LiteSim
{
    /// <summary>
    /// 出生点确定性选择（开局与复活共用）：在**未被占用**的点中按 hash 选一个；
    /// 调用方按槽位顺序逐个占用，保证同帧/同局互斥。不消费 <see cref="SimWorldState.RngState"/>
    /// （该随机流是射击系统的确定性审计面）。
    /// </summary>
    public static class SpawnPointSelector
    {
        /// <summary>选点：<paramref name="used"/> 为已占用点位掩码；全部占满时退化为全体可用（不阻塞复活/开局）。</summary>
        public static int Pick(SimMapData map, uint used, uint hash)
        {
            int count = map.SpawnPointCount;
            if (count <= 0) return 0;

            uint free = 0u;
            int freeCount = 0;
            for (int i = 0; i < count; i++)
            {
                uint bit = 1u << i;
                if ((used & bit) == 0u) { free |= bit; freeCount++; }
            }
            if (freeCount == 0)
            {
                free = (1u << count) - 1u;
                freeCount = count;
            }

            int pick = (int)(hash % (uint)freeCount);
            for (int i = 0; i < count; i++)
            {
                if ((free & (1u << i)) == 0u) continue;
                if (pick-- == 0) return i;
            }
            return 0;
        }

        /// <summary>开局选点 hash：MatchId（每局唯一）优先混入，Seed 为 0/固定值也能逐局变化。</summary>
        public static uint InitialHash(ulong seed, string matchId, int playerIndex)
        {
            uint h = (uint)playerIndex ^ (uint)seed ^ (uint)(seed >> 32);
            if (!string.IsNullOrEmpty(matchId))
                for (int i = 0; i < matchId.Length; i++) h = (h ^ matchId[i]) * 16777619u;
            return Mix(h);
        }

        public static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }
    }
}
