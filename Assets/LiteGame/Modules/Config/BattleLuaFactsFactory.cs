using LiteGame;
using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// B3b：把 Luban 战斗策略键值表投影成 Core 的不可变 <see cref="BattleLuaFacts"/>。
    /// 缺表/缺行一律回落 <see cref="BattleLuaFacts.Default"/>（表是热更面，不是启动硬依赖）。
    /// </summary>
    public static class BattleLuaFactsFactory
    {
        public static BattleLuaFacts Create(cfg.BattleStrategyTable table)
        {
            if (table == null) return BattleLuaFacts.Default;
            MatchRules fallback = MatchRules.Default;
            return new BattleLuaFacts(
                Int(table, "match.duration_frames", fallback.DurationFrames),
                Int(table, "match.kill_limit", fallback.KillLimit),
                Int(table, "match.respawn_delay_frames", fallback.RespawnDelayFrames),
                Int(table, "match.spawn_protection_frames", fallback.SpawnProtectionFrames),
                String(table, "rank.order", "kills_desc_entity_asc"),
                String(table, "result.name_prefix", "玩家"));
        }

        private static int Int(cfg.BattleStrategyTable table, string key, int fallback)
            => table.GetOrDefault(key) is { } row ? row.IntValue : fallback;

        private static string String(cfg.BattleStrategyTable table, string key, string fallback)
        {
            cfg.BattleStrategyRow row = table.GetOrDefault(key);
            return row != null && !string.IsNullOrEmpty(row.StringValue) ? row.StringValue : fallback;
        }
    }
}
