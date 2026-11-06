using System;

namespace LiteGame.Feedback.DamageNumbers
{
    /// <summary>
    /// 伤害数字材质档（(本地角色, 爆头档) 四档——逐档材质调色板查表键；零引擎）。
    /// **顺序即装配取件序约定**（Caused=0 … ReceivedCrit=3，见 <c>ProcedureBattle</c> 取件循环）。
    /// </summary>
    public enum DamageNumberTier
    {
        /// <summary>造成·普通。</summary>
        Caused = 0,
        /// <summary>造成·爆头。</summary>
        CausedCrit = 1,
        /// <summary>承受·普通。</summary>
        Received = 2,
        /// <summary>承受·爆头。</summary>
        ReceivedCrit = 3,
    }

    /// <summary>伤害数字样式档（纯数据——Unity 驱动换算 TMP 颜色/字号/渐变；零引擎）。
    /// 颜色为**顶→底双端**（顶点渐变，批C"逐档材质+渐变"；顶亮底暗出体积感——DNP Gold 同向；
    /// 上下同值即平色）。**描边/光晕不在本档**——归 <see cref="Tier"/> 对应材质（调色板查表）。</summary>
    public readonly struct DamageNumberStyle
    {
        /// <summary>渐变顶色·红。</summary>
        public readonly float TopR;
        /// <summary>渐变顶色·绿。</summary>
        public readonly float TopG;
        /// <summary>渐变顶色·蓝。</summary>
        public readonly float TopB;
        /// <summary>渐变底色·红。</summary>
        public readonly float BottomR;
        /// <summary>渐变底色·绿。</summary>
        public readonly float BottomG;
        /// <summary>渐变底色·蓝。</summary>
        public readonly float BottomB;
        /// <summary>TMP 字号（世界空间）。</summary>
        public readonly float FontSize;
        /// <summary>按值分带缩放（乘进出场缩放成终值）。</summary>
        public readonly float ScaleBoost;
        /// <summary>材质档位（描边/光晕材质查表键——见 <see cref="DamageNumberTier"/>）。</summary>
        public readonly DamageNumberTier Tier;

        public DamageNumberStyle(float topR, float topG, float topB,
            float bottomR, float bottomG, float bottomB,
            float fontSize, float scaleBoost, DamageNumberTier tier)
        {
            TopR = topR;
            TopG = topG;
            TopB = topB;
            BottomR = bottomR;
            BottomG = bottomG;
            BottomB = bottomB;
            FontSize = fontSize;
            ScaleBoost = scaleBoost;
            Tier = tier;
        }
    }

    /// <summary>
    /// 常量表初版（样式编辑器落地前的唯一单源）：**造成=白、爆头=亮红、承受=暗砖红、承受暴击=深红**。
    /// </summary>
    public sealed class DamageNumberStyleResolver : IDamageNumberStyleResolver
    {
        /// <summary>造成普通 = 白（顶纯白 → 底浅冷灰——微微出体积，不改变"白"档语义）。</summary>
        public static readonly DamageNumberStyle CausedWhite =
            new DamageNumberStyle(1f, 1f, 1f, 0.84f, 0.87f, 0.94f, 5f, 1f, DamageNumberTier.Caused);

        /// <summary>造成爆头 = **亮红**（顶近纯红 → 底深红；顶部亮度显著高于承受档的暗砖红）。</summary>
        public static readonly DamageNumberStyle CausedCritRed =
            new DamageNumberStyle(1f, 0.36f, 0.24f, 0.88f, 0.05f, 0.04f, 6.5f, 1f, DamageNumberTier.CausedCrit);

        /// <summary>承受 = **暗砖红**（顶暗砖 → 底更深——整体明度低于爆头档、仍有清晰读数）。</summary>
        public static readonly DamageNumberStyle ReceivedRed =
            new DamageNumberStyle(0.95f, 0.30f, 0.24f, 0.62f, 0.10f, 0.08f, 5f, 1f, DamageNumberTier.Received);

        /// <summary>承受爆头 = **深红**（四档中最深——靠字号 + 抖动 + 深红光晕材质识别）。</summary>
        public static readonly DamageNumberStyle ReceivedCritDeepRed =
            new DamageNumberStyle(0.78f, 0.16f, 0.12f, 0.45f, 0.02f, 0.02f, 6.5f, 1f, DamageNumberTier.ReceivedCrit);

        private readonly bool _usesProfile;
        private readonly DamageNumberStyleProfile _profile;

        /// <summary>缺省构造 = 代码常量档（表未装载/测试装配；行为与表驱动前一致）。</summary>
        public DamageNumberStyleResolver() { }

        /// <summary>表驱动构造：四档样式与按值分带由策略表投影给出（缺行已由投影回落默认）。</summary>
        public DamageNumberStyleResolver(DamageNumberStyleProfile profile)
        {
            _profile = profile;
            _usesProfile = true;
        }

        public DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit)
            => _usesProfile ? _profile.Resolve(value, role, crit) : ResolveDefaults(value, role, crit);

        private static DamageNumberStyle ResolveDefaults(int value, HitLocalRole role, bool crit)
            => DamageNumberStyleProfile.Default.Resolve(value, role, crit);   // 默认档同样走 profile 单实现

        /// <summary>按值分带缩放（默认档；表驱动走 <see cref="DamageNumberStyleProfile.ValueScaleBoost"/>）。</summary>
        public static float ValueScaleBoost(int value) => DamageNumberStyleProfile.Default.ValueScaleBoost(value);
    }

    /// <summary>
    /// 伤害数字四档样式 + 按值分带（纯数据、零引擎）：battlestrategy 表 damage.style.* / damage.value_scale.* 行的不可变投影。
    /// </summary>
    public readonly struct DamageNumberStyleProfile
    {
        public readonly DamageNumberStyle Caused;
        public readonly DamageNumberStyle CausedCrit;
        public readonly DamageNumberStyle Received;
        public readonly DamageNumberStyle ReceivedCrit;

        public readonly float Band1Threshold;
        public readonly float Band1Boost;
        public readonly float Band2Threshold;
        public readonly float Band2Boost;
        public readonly float Band3Threshold;
        public readonly float Band3Boost;

        public DamageNumberStyleProfile(DamageNumberStyle caused, DamageNumberStyle causedCrit,
            DamageNumberStyle received, DamageNumberStyle receivedCrit,
            float band1Threshold, float band1Boost, float band2Threshold, float band2Boost,
            float band3Threshold, float band3Boost)
        {
            RequireStyle(caused, nameof(caused));
            RequireStyle(causedCrit, nameof(causedCrit));
            RequireStyle(received, nameof(received));
            RequireStyle(receivedCrit, nameof(receivedCrit));
            RequireBand(band1Threshold, band1Boost, nameof(band1Threshold));
            RequireBand(band2Threshold, band2Boost, nameof(band2Threshold));
            RequireBand(band3Threshold, band3Boost, nameof(band3Threshold));
            if (!(band1Threshold < band2Threshold && band2Threshold < band3Threshold))
                throw new ArgumentOutOfRangeException(nameof(band1Threshold), "按值分带阈值必须严格递增");

            Caused = caused;
            CausedCrit = causedCrit;
            Received = received;
            ReceivedCrit = receivedCrit;
            Band1Threshold = band1Threshold;
            Band1Boost = band1Boost;
            Band2Threshold = band2Threshold;
            Band2Boost = band2Boost;
            Band3Threshold = band3Threshold;
            Band3Boost = band3Boost;
        }

        /// <summary>缺表/缺行默认（与 <see cref="DamageNumberStyleResolver"/> 常量档同值）。</summary>
        public static DamageNumberStyleProfile Default => new DamageNumberStyleProfile(
            DamageNumberStyleResolver.CausedWhite, DamageNumberStyleResolver.CausedCritRed,
            DamageNumberStyleResolver.ReceivedRed, DamageNumberStyleResolver.ReceivedCritDeepRed,
            20f, 1.05f, 50f, 1.12f, 100f, 1.20f);

        public DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit)
        {
            DamageNumberStyle s = role == HitLocalRole.Received
                ? (crit ? ReceivedCrit : Received)
                : (crit ? CausedCrit : Caused);
            return new DamageNumberStyle(s.TopR, s.TopG, s.TopB, s.BottomR, s.BottomG, s.BottomB,
                s.FontSize, s.ScaleBoost * ValueScaleBoost(value), s.Tier);
        }

        /// <summary>按值分带缩放：≥band3 → band3 boost；≥band2 → band2；≥band1 → band1；否则 1。</summary>
        public float ValueScaleBoost(int value)
        {
            if (value >= Band3Threshold) return Band3Boost;
            if (value >= Band2Threshold) return Band2Boost;
            if (value >= Band1Threshold) return Band1Boost;
            return 1f;
        }

        private static void RequireStyle(DamageNumberStyle style, string name)
        {
            if (!(style.FontSize > 0f)) throw new ArgumentOutOfRangeException(name, "字号必须大于 0");
            if (!(style.ScaleBoost > 0f)) throw new ArgumentOutOfRangeException(name, "缩放必须大于 0");
        }

        private static void RequireBand(float threshold, float boost, string name)
        {
            if (!(threshold > 0f)) throw new ArgumentOutOfRangeException(name, "分带阈值必须大于 0");
            if (!(boost > 0f)) throw new ArgumentOutOfRangeException(name, "分带倍率必须大于 0");
        }
    }
}
