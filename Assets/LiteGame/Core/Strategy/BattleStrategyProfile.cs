using LiteSim;
using LiteGame.Feedback.DamageNumbers;

namespace LiteGame
{
    /// <summary>
    /// B3 战斗策略的**不可变投影**（纯件、零引擎）：规则四值 + 排行策略 + 伤害数字样式/轨道 + 结算名前缀。
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
    }
}
