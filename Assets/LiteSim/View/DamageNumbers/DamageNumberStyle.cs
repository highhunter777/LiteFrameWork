namespace LiteSim.View.DamageNumbers
{
    /// <summary>伤害数字样式档（纯数据——Unity 驱动换算 TMP 字号/颜色；零引擎）。</summary>
    public readonly struct DamageNumberStyle
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;
        /// <summary>TMP 字号（世界空间）。</summary>
        public readonly float FontSize;
        /// <summary>按值分带缩放（乘进出场缩放成终值）。</summary>
        public readonly float ScaleBoost;

        public DamageNumberStyle(float r, float g, float b, float fontSize, float scaleBoost)
        {
            R = r;
            G = g;
            B = b;
            FontSize = fontSize;
            ScaleBoost = scaleBoost;
        }
    }

    /// <summary>
    /// 样式解析单源接口（《命中反馈与伤害数字专项设计》§4.2）：**初版常量表实现**，样式编辑器批
    /// （《UI编辑器工具专项设计》§3）落地后换 token 来源——接口不变，消费者零改动。
    /// </summary>
    public interface IDamageNumberStyleResolver
    {
        /// <summary>值/角色/暴击 → 样式档。</summary>
        DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit);
    }

    /// <summary>
    /// 常量表初版（样式编辑器落地前的唯一单源）：**造成=白、造成暴击=橙、承受=红（染红区分口径）、
    /// 承受暴击=深红放大**；旁观档落造成色兜底（驱动已滤，不构成第二事实源）。按值分带缩放单独可测。
    /// </summary>
    public sealed class DamageNumberStyleResolver : IDamageNumberStyleResolver
    {
        public static readonly DamageNumberStyle CausedWhite = new DamageNumberStyle(1f, 1f, 1f, 5f, 1f);
        public static readonly DamageNumberStyle CausedCritOrange = new DamageNumberStyle(1f, 0.55f, 0.12f, 6.5f, 1f);
        public static readonly DamageNumberStyle ReceivedRed = new DamageNumberStyle(0.95f, 0.22f, 0.18f, 5f, 1f);
        public static readonly DamageNumberStyle ReceivedCritDeepRed = new DamageNumberStyle(1f, 0.12f, 0.10f, 6.5f, 1f);

        public DamageNumberStyle Resolve(int value, HitLocalRole role, bool crit)
        {
            DamageNumberStyle s = role == HitLocalRole.Received
                ? (crit ? ReceivedCritDeepRed : ReceivedRed)
                : (crit ? CausedCritOrange : CausedWhite);

            // 按值分带缩放与角色档正交（大伤害无论谁打都更大——"按值缩放"独立语义）
            return new DamageNumberStyle(s.R, s.G, s.B, s.FontSize, s.ScaleBoost * ValueScaleBoost(value));
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
