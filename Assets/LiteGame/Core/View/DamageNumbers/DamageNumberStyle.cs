namespace LiteGame.View.DamageNumbers
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
    /// 常量表初版（样式编辑器落地前的唯一单源）：**造成=白、爆头=亮红、承受=暗砖红、承受暴击=深红**；
    /// 每档带**顶→底渐变**（顶亮底暗）＋**材质档键**（逐档描边/光晕材质，见调色板）。
    /// 暴击档放大字号并带抖动（见 <see cref="DamageNumberMotion"/>）。旁观档落造成色兜底（驱动已滤，
    /// 不构成第二事实源）。按值分带缩放单独可测。
    ///
    /// **爆头取红（玩家口径"爆头飘字要红"，批C 复核保留）**：<see cref="CausedCritRed"/> 用**亮红**
    /// （顶色近纯红、绿分量低、顶亮度显著高于承受档），与 <see cref="ReceivedRed"/> 的**暗砖红**靠
    /// "明度 + 字号 + 抖动 + 红光晕材质"四维识别。**代价须知**：红被爆头与承受两档共用，敌我维度不靠
    /// 颜色单独区分。若后续要完全解耦，应把承受档迁到另一色相（如青/紫）——须单独裁决。
    ///
    /// **渐变取值口径**（批C）：顶亮底暗（光源在上，DNP Gold 同向）；颜色语义（白/亮红/暗砖/深红）
    /// 两端一致不换色相，渐变为同色系明度差。
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

        public DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit)
        {
            DamageNumberStyle s = role == HitLocalRole.Received
                ? (crit ? ReceivedCritDeepRed : ReceivedRed)
                : (crit ? CausedCritRed : CausedWhite);

            // 按值分带缩放与角色档正交（大伤害无论谁打都更大——"按值缩放"独立语义）
            return new DamageNumberStyle(s.TopR, s.TopG, s.TopB, s.BottomR, s.BottomG, s.BottomB,
                s.FontSize, s.ScaleBoost * ValueScaleBoost(value), s.Tier);
        }

        /// <summary>按值分带缩放：≥100 → 1.20；≥50 → 1.12；≥20 → 1.05；否则 1（边界含左端）。</summary>
        public static float ValueScaleBoost(int value)
        {
            if (value >= 100) return 1.20f;
            if (value >= 50) return 1.12f;
            if (value >= 20) return 1.05f;
            return 1f;
        }
    }
}
