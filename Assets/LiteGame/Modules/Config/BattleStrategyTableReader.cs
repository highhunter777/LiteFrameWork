using System.IO;
using LiteSim;

namespace LiteGame.Modules.Config
{
    /// <summary>
    /// <c>battlestrategy</c> 键值表的读取口（**客户端与服务端源链接共编**——与 <see cref="TableLayout"/> 同链）：
    /// 只认键名与取值类型，不认消费语义；缺行回落调用方给的默认值（"缺行回落 C# 默认"契约的单点）。
    ///
    /// **键名单源**：表源 <c>Luban/Data/#battlestrategy.xlsx</c> 的行键必须落在这里；
    /// <c>BattleStrategyTableTests.键名单源_表产物逐键在册</c> 逐键核对表产物，防"表里改了键、代码还读旧键"的静默失效。
    /// </summary>
    public static class BattleStrategyTableReader
    {
        /// <summary>表行键（与 #battlestrategy.xlsx 的 id 列一一对应）。</summary>
        public static class Keys
        {
            public const string MatchDurationFrames = "match.duration_frames";
            public const string MatchKillLimit = "match.kill_limit";
            public const string MatchRespawnDelayFrames = "match.respawn_delay_frames";
            public const string MatchSpawnProtectionFrames = "match.spawn_protection_frames";

            public const string RankOrder = "rank.order";
            public const string RankTie = "rank.tie";
            public const string ResultNamePrefix = "result.name_prefix";

            public const string DamageStyleCausedTopColor = "damage.style.caused.top_color";
            public const string DamageStyleCausedBottomColor = "damage.style.caused.bottom_color";
            public const string DamageStyleCausedFontSize = "damage.style.caused.font_size";
            public const string DamageStyleCausedScaleBoost = "damage.style.caused.scale_boost";
            public const string DamageStyleCausedCritTopColor = "damage.style.caused_crit.top_color";
            public const string DamageStyleCausedCritBottomColor = "damage.style.caused_crit.bottom_color";
            public const string DamageStyleCausedCritFontSize = "damage.style.caused_crit.font_size";
            public const string DamageStyleCausedCritScaleBoost = "damage.style.caused_crit.scale_boost";
            public const string DamageStyleReceivedTopColor = "damage.style.received.top_color";
            public const string DamageStyleReceivedBottomColor = "damage.style.received.bottom_color";
            public const string DamageStyleReceivedFontSize = "damage.style.received.font_size";
            public const string DamageStyleReceivedScaleBoost = "damage.style.received.scale_boost";
            public const string DamageStyleReceivedCritTopColor = "damage.style.received_crit.top_color";
            public const string DamageStyleReceivedCritBottomColor = "damage.style.received_crit.bottom_color";
            public const string DamageStyleReceivedCritFontSize = "damage.style.received_crit.font_size";
            public const string DamageStyleReceivedCritScaleBoost = "damage.style.received_crit.scale_boost";

            public const string DamageValueBand1Threshold = "damage.value_scale.band1.threshold";
            public const string DamageValueBand1Boost = "damage.value_scale.band1.boost";
            public const string DamageValueBand2Threshold = "damage.value_scale.band2.threshold";
            public const string DamageValueBand2Boost = "damage.value_scale.band2.boost";
            public const string DamageValueBand3Threshold = "damage.value_scale.band3.threshold";
            public const string DamageValueBand3Boost = "damage.value_scale.band3.boost";

            public const string DamageMotionHoverHeight = "damage.motion.hover_height";
            public const string DamageMotionRiseSpeed = "damage.motion.rise_speed";
            public const string DamageMotionRiseDecel = "damage.motion.rise_decel";
            public const string DamageMotionFadeStart = "damage.motion.fade_start";
            public const string DamageMotionFadeEnd = "damage.motion.fade_end";
            public const string DamageMotionMergeWindow = "damage.motion.merge_window";
            public const string DamageMotionPopSeconds = "damage.motion.pop_seconds";
            public const string DamageMotionPushDistance = "damage.motion.push_distance";
            public const string DamageMotionAnchorFollowSpeed = "damage.motion.anchor_follow_speed";
            public const string DamageMotionCritShakeSeconds = "damage.motion.crit_shake_seconds";
            public const string DamageMotionCritShakeAmplitude = "damage.motion.crit_shake_amplitude";
            public const string DamageMotionCritShakeFrequency = "damage.motion.crit_shake_frequency";
        }

        /// <summary>读整数行；缺表/缺行/空串 → <paramref name="fallback"/>。</summary>
        public static int Int(cfg.Tbbattlestrategy table, string key, int fallback)
        {
            cfg.battlestrategy row = table?.GetOrDefault(key);
            return row == null ? fallback : row.IntValue;
        }

        /// <summary>读浮点行；缺表/缺行 → <paramref name="fallback"/>。</summary>
        public static float Float(cfg.Tbbattlestrategy table, string key, float fallback)
        {
            cfg.battlestrategy row = table?.GetOrDefault(key);
            return row == null ? fallback : row.FloatValue;
        }

        /// <summary>读字符串行；缺表/缺行/空串 → <paramref name="fallback"/>。</summary>
        public static string String(cfg.Tbbattlestrategy table, string key, string fallback)
        {
            cfg.battlestrategy row = table?.GetOrDefault(key);
            return row == null || string.IsNullOrEmpty(row.StringValue) ? fallback : row.StringValue;
        }

        /// <summary>
        /// 规则四值投影（客户端本地测试房与服务端房间默认共用同一读取口径）。
        /// 表值越界/非法 → <see cref="InvalidDataException"/>（配置错误 fail-fast，不静默回落）。
        /// </summary>
        public static MatchRules ReadRules(cfg.Tbbattlestrategy table, MatchRules fallback)
        {
            try
            {
                return new MatchRules(
                    Int(table, Keys.MatchDurationFrames, fallback.DurationFrames),
                    Int(table, Keys.MatchKillLimit, fallback.KillLimit),
                    Int(table, Keys.MatchRespawnDelayFrames, fallback.RespawnDelayFrames),
                    Int(table, Keys.MatchSpawnProtectionFrames, fallback.SpawnProtectionFrames));
            }
            catch (System.ArgumentOutOfRangeException ex)
            {
                throw new InvalidDataException("battlestrategy 的 match.* 规则行非法：" + ex.Message, ex);
            }
        }
    }
}
