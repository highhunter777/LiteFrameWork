using System;
using System.Globalization;
using System.IO;
using LiteGame.Feedback.DamageNumbers;
using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// <c>battlestrategy</c> 表 → <see cref="BattleStrategyProfile"/> 的唯一投影点（客户端装载链）。
    /// 缺表/缺行回落 <see cref="BattleStrategyProfile.Default"/>；**行存在但值非法**（颜色格式/阈值次序/
    /// 规则越界）一律抛 <see cref="InvalidDataException"/>——启动期配置错误不静默带进对局。
    ///
    /// 服务端只复用 <see cref="BattleStrategyTableReader.ReadRules"/> 投影规则四值（不引 Core 表现类型）；
    /// 本类只服务客户端表现/策略面。
    /// </summary>
    public static class BattleStrategyProfileFactory
    {
        public static BattleStrategyProfile Create(cfg.Tbbattlestrategy table)
        {
            BattleStrategyProfile fallback = BattleStrategyProfile.Default;
            MatchRules rules = BattleStrategyTableReader.ReadRules(table, fallback.Rules);
            BattleRankPolicy rank = ReadRankPolicy(table, fallback.Rank);
            DamageNumberStyleProfile style = ReadStyle(table, fallback.DamageStyle);
            DamageNumberMotionProfile motion = ReadMotion(table, fallback.DamageMotion);
            string prefix = BattleStrategyTableReader.String(
                table, BattleStrategyTableReader.Keys.ResultNamePrefix, fallback.ResultNamePrefix);
            return new BattleStrategyProfile(rules, rank, style, motion, prefix);
        }

        private static BattleRankPolicy ReadRankPolicy(cfg.Tbbattlestrategy table, BattleRankPolicy fallback)
        {
            string orderText = BattleStrategyTableReader.String(
                table, BattleStrategyTableReader.Keys.RankOrder, null);
            string tieText = BattleStrategyTableReader.String(
                table, BattleStrategyTableReader.Keys.RankTie, null);

            BattleRankOrderKind order = orderText switch
            {
                null => fallback.Order,
                "kills_desc" => BattleRankOrderKind.KillsDescending,
                "kills_asc" => BattleRankOrderKind.KillsAscending,
                _ => throw new InvalidDataException($"rank.order 非法：'{orderText}'（合法 kills_desc/kills_asc）"),
            };
            BattleRankTieBreak tie = tieText switch
            {
                null => fallback.TieBreak,
                "entity_id_asc" => BattleRankTieBreak.EntityIdAscending,
                "entity_id_desc" => BattleRankTieBreak.EntityIdDescending,
                _ => throw new InvalidDataException($"rank.tie 非法：'{tieText}'（合法 entity_id_asc/entity_id_desc）"),
            };
            return new BattleRankPolicy(order, tie);
        }

        private static DamageNumberStyleProfile ReadStyle(cfg.Tbbattlestrategy table,
            DamageNumberStyleProfile fallback)
        {
            DamageNumberStyle caused = ReadTier(table, fallback.Caused,
                BattleStrategyTableReader.Keys.DamageStyleCausedTopColor,
                BattleStrategyTableReader.Keys.DamageStyleCausedBottomColor,
                BattleStrategyTableReader.Keys.DamageStyleCausedFontSize,
                BattleStrategyTableReader.Keys.DamageStyleCausedScaleBoost);
            DamageNumberStyle causedCrit = ReadTier(table, fallback.CausedCrit,
                BattleStrategyTableReader.Keys.DamageStyleCausedCritTopColor,
                BattleStrategyTableReader.Keys.DamageStyleCausedCritBottomColor,
                BattleStrategyTableReader.Keys.DamageStyleCausedCritFontSize,
                BattleStrategyTableReader.Keys.DamageStyleCausedCritScaleBoost);
            DamageNumberStyle received = ReadTier(table, fallback.Received,
                BattleStrategyTableReader.Keys.DamageStyleReceivedTopColor,
                BattleStrategyTableReader.Keys.DamageStyleReceivedBottomColor,
                BattleStrategyTableReader.Keys.DamageStyleReceivedFontSize,
                BattleStrategyTableReader.Keys.DamageStyleReceivedScaleBoost);
            DamageNumberStyle receivedCrit = ReadTier(table, fallback.ReceivedCrit,
                BattleStrategyTableReader.Keys.DamageStyleReceivedCritTopColor,
                BattleStrategyTableReader.Keys.DamageStyleReceivedCritBottomColor,
                BattleStrategyTableReader.Keys.DamageStyleReceivedCritFontSize,
                BattleStrategyTableReader.Keys.DamageStyleReceivedCritScaleBoost);

            return new DamageNumberStyleProfile(caused, causedCrit, received, receivedCrit,
                BattleStrategyTableReader.Int(table, BattleStrategyTableReader.Keys.DamageValueBand1Threshold,
                    (int)fallback.Band1Threshold),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageValueBand1Boost,
                    fallback.Band1Boost),
                BattleStrategyTableReader.Int(table, BattleStrategyTableReader.Keys.DamageValueBand2Threshold,
                    (int)fallback.Band2Threshold),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageValueBand2Boost,
                    fallback.Band2Boost),
                BattleStrategyTableReader.Int(table, BattleStrategyTableReader.Keys.DamageValueBand3Threshold,
                    (int)fallback.Band3Threshold),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageValueBand3Boost,
                    fallback.Band3Boost));
        }

        private static DamageNumberStyle ReadTier(cfg.Tbbattlestrategy table, DamageNumberStyle fallback,
            string topKey, string bottomKey, string fontSizeKey, string scaleBoostKey)
        {
            (float topR, float topG, float topB) = ReadColor(
                BattleStrategyTableReader.String(table, topKey, null), fallback.TopR, fallback.TopG, fallback.TopB);
            (float bottomR, float bottomG, float bottomB) = ReadColor(
                BattleStrategyTableReader.String(table, bottomKey, null),
                fallback.BottomR, fallback.BottomG, fallback.BottomB);
            float fontSize = BattleStrategyTableReader.Float(table, fontSizeKey, fallback.FontSize);
            float scaleBoost = BattleStrategyTableReader.Float(table, scaleBoostKey, fallback.ScaleBoost);
            return new DamageNumberStyle(topR, topG, topB, bottomR, bottomG, bottomB,
                fontSize, scaleBoost, fallback.Tier);
        }

        private static DamageNumberMotionProfile ReadMotion(cfg.Tbbattlestrategy table,
            DamageNumberMotionProfile fallback)
            => new DamageNumberMotionProfile(
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionHoverHeight,
                    fallback.HoverHeight),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionRiseSpeed,
                    fallback.RiseSpeed),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionRiseDecel,
                    fallback.RiseDecel),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionFadeStart,
                    fallback.FadeStartSeconds),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionFadeEnd,
                    fallback.FadeEndSeconds),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionMergeWindow,
                    fallback.MergeWindowSeconds),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionPopSeconds,
                    fallback.PopSeconds),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionPushDistance,
                    fallback.PushDistance),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionAnchorFollowSpeed,
                    fallback.AnchorFollowSpeed),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionCritShakeSeconds,
                    fallback.CritShakeSeconds),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionCritShakeAmplitude,
                    fallback.CritShakeAmplitude),
                BattleStrategyTableReader.Float(table, BattleStrategyTableReader.Keys.DamageMotionCritShakeFrequency,
                    fallback.CritShakeFrequency));

        /// <summary>#RRGGBB → 0..1 三通道；缺省走 fallback 通道。</summary>
        private static (float R, float G, float B) ReadColor(string value, float fallbackR, float fallbackG,
            float fallbackB)
        {
            if (string.IsNullOrEmpty(value)) return (fallbackR, fallbackG, fallbackB);
            string hex = value.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
            if (hex.Length != 6
                || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int packed))
                throw new InvalidDataException($"颜色值非法：'{value}'（期望 #RRGGBB）");
            return (((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
        }
    }
}
