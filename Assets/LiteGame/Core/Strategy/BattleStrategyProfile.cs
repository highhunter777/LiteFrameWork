using LiteSim;
using LiteGame.Feedback.DamageNumbers;

namespace LiteGame
{
    /// <summary>
    /// B3 战斗策略的**不可变投影**（纯件、零引擎）：规则四值 + 排行策略 + 伤害数字样式/轨道 + 结算名前缀。
    /// 生产来源 = <c>battlestrategy</c> 表（<c>ConfigService</c> 装载后经
    /// <c>BattleStrategyProfileFactory</c> 投影一次）；缺表/缺行回落本类 <see cref="Default"/>。
    ///
    /// **边界**：本类只承载"可热更的表现/策略参数"，不写对局状态；权威规则仍由服务端随 StartGame 下发
    /// （本地测试房与 Bridge.battle 事实消费本投影的规则值，不构成第二权威）。伤害数字/排行参数不进
    /// <c>CombatConfig</c>/判定摘要——它们是单端表现面。
    /// </summary>
    public readonly struct BattleStrategyProfile
    {
        public readonly MatchRules Rules;
        public readonly BattleRankPolicy Rank;
        public readonly DamageNumberStyleProfile DamageStyle;
        public readonly DamageNumberMotionProfile DamageMotion;
        public readonly string ResultNamePrefix;

        public BattleStrategyProfile(MatchRules rules, BattleRankPolicy rank,
            DamageNumberStyleProfile damageStyle, DamageNumberMotionProfile damageMotion,
            string resultNamePrefix)
        {
            Rules = rules;
            Rank = rank;
            DamageStyle = damageStyle;
            DamageMotion = damageMotion;
            ResultNamePrefix = string.IsNullOrEmpty(resultNamePrefix) ? "玩家" : resultNamePrefix;
        }

        /// <summary>缺表/缺行默认（与表驱动前的 C# 常量同值）。</summary>
        public static BattleStrategyProfile Default => new BattleStrategyProfile(
            MatchRules.Default, BattleRankPolicy.Default,
            DamageNumberStyleProfile.Default, DamageNumberMotion.DefaultProfile, "玩家");

        /// <summary>Bridge.battle 只读事实（与策略表投影同源）。</summary>
        public BattleLuaFacts ToLuaFacts()
            => new BattleLuaFacts(Rules.DurationFrames, Rules.KillLimit, Rules.RespawnDelayFrames,
                Rules.SpawnProtectionFrames, Rank.ToWireValue(), ResultNamePrefix);
    }
}
