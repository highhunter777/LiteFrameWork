using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// B3 Bridge.battle 只读事实（Lua 只读门面）：本局规则 + 排行/结算口径的**不可变快照**。
    /// 生产绑定由装配点/流程提供；未绑定时 <see cref="Default"/> 用 <see cref="MatchRules.Default"/>，
    /// 保证 Lua 侧永远可读且不会写入战斗状态。
    /// </summary>
    public readonly struct BattleLuaFacts
    {
        public readonly int DurationFrames;
        public readonly int KillLimit;
        public readonly int RespawnDelayFrames;
        public readonly int SpawnProtectionFrames;
        public readonly string RankOrder;
        public readonly string ResultNamePrefix;

        public BattleLuaFacts(int durationFrames, int killLimit, int respawnDelayFrames, int spawnProtectionFrames,
            string rankOrder, string resultNamePrefix)
        {
            DurationFrames = durationFrames;
            KillLimit = killLimit;
            RespawnDelayFrames = respawnDelayFrames;
            SpawnProtectionFrames = spawnProtectionFrames;
            RankOrder = rankOrder ?? string.Empty;
            ResultNamePrefix = resultNamePrefix ?? string.Empty;
        }

        /// <summary>生产默认事实（与 <see cref="MatchRules.Default"/> / BattleRankOrder / ResultRanking 同口径）。</summary>
        public static BattleLuaFacts Default => FromRules(MatchRules.Default);

        public static BattleLuaFacts FromRules(MatchRules rules)
            => new BattleLuaFacts(rules.DurationFrames, rules.KillLimit, rules.RespawnDelayFrames,
                rules.SpawnProtectionFrames, "kills_desc_entity_asc", "玩家");
    }
}
