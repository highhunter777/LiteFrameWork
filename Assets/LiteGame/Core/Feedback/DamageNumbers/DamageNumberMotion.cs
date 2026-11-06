using System;

namespace LiteGame.Feedback.DamageNumbers
{
    /// <summary>
    /// 伤害数字轨道数学（纯函数、零引擎——《命中反馈与伤害数字专项设计》§4.2）：每渲染帧由驱动喂入 两个时钟（出场钟/最后活动钟），输出 (升浮、推挤、淡出、抖动、出场缩放)。
    /// </summary>
    public static class DamageNumberMotion
    {
        /// <summary>受击点上方抬升（米）——飘字**锚定受击部位**（用户裁决："飘字应该在受击部位飘"），
        /// 本值只把文字抬离受击点一点避免压住命中处（原 1.55 是"实体脚下锚 + 头顶以上"时代的基高，
        /// 锚点改为受击点后不再适用）。</summary>
        public const float HoverHeight = 0.25f;

        /// <summary>上浮初速（m/s）。</summary>
        public const float RiseSpeed = 1.8f;

        /// <summary>上浮减速（m/s²）——峰值 t = 1.5s 在寿命外（淡出终点 1.1s 前一直缓升），钳制仅防御。</summary>
        public const float RiseDecel = 1.2f;

        /// <summary>淡出起点（自最后活动起）。</summary>
        public const float FadeStartSeconds = 0.55f;

        /// <summary>淡出终点＝条目寿命（自最后活动起）。</summary>
        public const float FadeEndSeconds = 1.10f;

        /// <summary>同 (目标, 角色) 合并窗口（连击累加；窗口外命中起新条目）。</summary>
        public const float MergeWindowSeconds = 0.45f;

        /// <summary>出场缩放时长（0.6 → 1）。</summary>
        public const float PopSeconds = 0.09f;

        /// <summary>水平推挤距离（种子确定性散开——防叠字；俯视角垂直上浮＋横向散布即 DNP"推挤"的对应物）。</summary>
        public const float PushDistance = 0.28f;

        /// <summary>锚点追随速度（m/s——合并时锚点目标改最新受击点，驱动以恒速靠近：数字平滑滑向
        /// 最新命中处，不瞬跳）。</summary>
        public const float AnchorFollowSpeed = 14f;

        /// <summary>暴击抖动窗（秒，自出场起）。</summary>
        public const float CritShakeSeconds = 0.22f;

        /// <summary>暴击抖动幅度（米）。</summary>
        public const float CritShakeAmplitude = 0.06f;

        /// <summary>暴击抖动频率（Hz）。</summary>
        public const float CritShakeFrequency = 46f;

        /// <summary>
        /// 逐帧求值（无状态——驱动按条目两钟喂入）。
        /// </summary>
        public static void Evaluate(float sinceSpawn, float sinceLastMerge, bool crit, int seed,
            out float riseY, out float pushX, out float alpha, out float shakeX, out float popScale)
            => DefaultProfile.Evaluate(sinceSpawn, sinceLastMerge, crit, seed,
                out riseY, out pushX, out alpha, out shakeX, out popScale);

        /// <summary>缺表/缺行默认（常量即表值默认；表驱动时由策略表覆盖）。</summary>
        public static DamageNumberMotionProfile DefaultProfile => new DamageNumberMotionProfile(
            HoverHeight, RiseSpeed, RiseDecel, FadeStartSeconds, FadeEndSeconds, MergeWindowSeconds,
            PopSeconds, PushDistance, AnchorFollowSpeed, CritShakeSeconds, CritShakeAmplitude,
            CritShakeFrequency);
    }

    /// <summary>
    /// 伤害数字轨道参数（纯数据、零引擎）：<c>battlestrategy</c> 表 <c>damage.motion.*</c> 行的不可变投影。
    /// 缺行由装配点回落 <see cref="DamageNumberMotion.DefaultProfile"/>；本结构只做参数校验与逐帧求值。
    /// </summary>
    public readonly struct DamageNumberMotionProfile
    {
        public readonly float HoverHeight;
        public readonly float RiseSpeed;
        public readonly float RiseDecel;
        public readonly float FadeStartSeconds;
        public readonly float FadeEndSeconds;
        public readonly float MergeWindowSeconds;
        public readonly float PopSeconds;
        public readonly float PushDistance;
        public readonly float AnchorFollowSpeed;
        public readonly float CritShakeSeconds;
        public readonly float CritShakeAmplitude;
        public readonly float CritShakeFrequency;

        public DamageNumberMotionProfile(float hoverHeight, float riseSpeed, float riseDecel,
            float fadeStartSeconds, float fadeEndSeconds, float mergeWindowSeconds, float popSeconds,
            float pushDistance, float anchorFollowSpeed, float critShakeSeconds, float critShakeAmplitude,
            float critShakeFrequency)
        {
            RequireNonNegative(hoverHeight, nameof(hoverHeight));
            RequirePositive(riseDecel, nameof(riseDecel));
            RequireNonNegative(riseSpeed, nameof(riseSpeed));
            RequireNonNegative(fadeStartSeconds, nameof(fadeStartSeconds));
            if (fadeEndSeconds <= fadeStartSeconds)
                throw new ArgumentOutOfRangeException(nameof(fadeEndSeconds), "淡出终点必须大于起点");
            RequireNonNegative(mergeWindowSeconds, nameof(mergeWindowSeconds));
            RequirePositive(popSeconds, nameof(popSeconds));
            RequireNonNegative(pushDistance, nameof(pushDistance));
            RequireNonNegative(anchorFollowSpeed, nameof(anchorFollowSpeed));
            RequireNonNegative(critShakeSeconds, nameof(critShakeSeconds));
            RequireNonNegative(critShakeAmplitude, nameof(critShakeAmplitude));
            RequirePositive(critShakeFrequency, nameof(critShakeFrequency));

            HoverHeight = hoverHeight;
            RiseSpeed = riseSpeed;
            RiseDecel = riseDecel;
            FadeStartSeconds = fadeStartSeconds;
            FadeEndSeconds = fadeEndSeconds;
            MergeWindowSeconds = mergeWindowSeconds;
            PopSeconds = popSeconds;
            PushDistance = pushDistance;
            AnchorFollowSpeed = anchorFollowSpeed;
            CritShakeSeconds = critShakeSeconds;
            CritShakeAmplitude = critShakeAmplitude;
            CritShakeFrequency = critShakeFrequency;
        }

        /// <summary>逐帧求值（字段版本——表驱动参数；算法与静态口同源）。</summary>
        public void Evaluate(float sinceSpawn, float sinceLastMerge, bool crit, int seed,
            out float riseY, out float pushX, out float alpha, out float shakeX, out float popScale)
        {
            float peakT = RiseSpeed / RiseDecel;
            float t = Math.Min(sinceSpawn, peakT);
            riseY = RiseSpeed * t - 0.5f * RiseDecel * t * t;

            float frac = (seed * 0.61803398875f) % 1f;
            pushX = (frac * 2f - 1f) * PushDistance;

            alpha = sinceLastMerge <= FadeStartSeconds
                ? 1f
                : Math.Max(0f, 1f - (sinceLastMerge - FadeStartSeconds) / (FadeEndSeconds - FadeStartSeconds));

            if (crit && sinceSpawn < CritShakeSeconds)
            {
                float envelope = 1f - sinceSpawn / CritShakeSeconds;
                shakeX = MathF.Sin(sinceSpawn * CritShakeFrequency + seed) * CritShakeAmplitude * envelope;
            }
            else
            {
                shakeX = 0f;
            }

            popScale = sinceSpawn >= PopSeconds ? 1f : 0.6f + 0.4f * (sinceSpawn / PopSeconds);
        }

        private static void RequirePositive(float value, string name)
        {
            if (!(value > 0f)) throw new ArgumentOutOfRangeException(name, value, "必须大于 0");
        }

        private static void RequireNonNegative(float value, string name)
        {
            if (!(value >= 0f)) throw new ArgumentOutOfRangeException(name, value, "不得为负");
        }
    }
}
