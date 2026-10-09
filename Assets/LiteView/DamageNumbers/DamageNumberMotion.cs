using System;

namespace LiteView.DamageNumbers
{
    /// <summary>
    /// 伤害数字轨道数学（纯函数、零引擎——《命中反馈与伤害数字专项设计》§4.2）：每渲染帧由驱动喂入
    /// 两个时钟（出场钟/最后活动钟），输出 (升浮、推挤、淡出、抖动、出场缩放)。
    ///
    /// **双钟口径**：升浮/抖动/出场用**出场钟**（自 Spawn 起持续——合并不重置，数字不"跳回起点"）；
    /// 淡出用**最后活动钟**（合并即重置——连击驻留：连击不停数字不消）。
    ///
    /// **表现参数纪律**：常量全在本类（样式编辑器批落地后迁 token 单源）；**禁入 <c>CombatConfig</c>**
    /// ——它在 BuildHash/digest 源集，表现参数不得搅联机身份。
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

        /// <summary>暴击抖动窗（秒，自出场起）。</summary>
        public const float CritShakeSeconds = 0.22f;

        /// <summary>暴击抖动幅度（米）。</summary>
        public const float CritShakeAmplitude = 0.06f;

        /// <summary>暴击抖动频率（Hz）。</summary>
        public const float CritShakeFrequency = 46f;

        /// <summary>
        /// 逐帧求值（无状态——驱动按条目两钟喂入）。推挤用种子黄金角散布（确定性：同种子同散布，
        /// 合并条目保持原种子 → 运动不漂）。
        /// </summary>
        public static void Evaluate(float sinceSpawn, float sinceLastMerge, bool crit, int seed,
            out float riseY, out float pushX, out float alpha, out float shakeX, out float popScale)
        {
            // 升浮：v0·t − ½·a·t²，峰值后钳住（缓升——"飘起后缓停"读感）
            float peakT = RiseSpeed / RiseDecel;
            float t = Math.Min(sinceSpawn, peakT);
            riseY = RiseSpeed * t - 0.5f * RiseDecel * t * t;

            // 推挤：种子 → [0,1) 黄金角分数 → [−1,1] × 距离（散开防叠）
            float frac = (seed * 0.61803398875f) % 1f;
            pushX = (frac * 2f - 1f) * PushDistance;

            // 淡出：最后活动钟起线性 1 → 0（窗口内合并重置即驻留）
            alpha = sinceLastMerge <= FadeStartSeconds
                ? 1f
                : Math.Max(0f, 1f - (sinceLastMerge - FadeStartSeconds) / (FadeEndSeconds - FadeStartSeconds));

            // 抖动：仅暴击、出场窗内，包络线性衰减正弦（种子定相位）
            if (crit && sinceSpawn < CritShakeSeconds)
            {
                float envelope = 1f - sinceSpawn / CritShakeSeconds;
                shakeX = MathF.Sin(sinceSpawn * CritShakeFrequency + seed) * CritShakeAmplitude * envelope;
            }
            else
            {
                shakeX = 0f;
            }

            // 出场缩放：0.6 → 1 线性拉伸（0.09s）——与按值分带缩放（样式解析器）相乘成终值
            popScale = sinceSpawn >= PopSeconds ? 1f : 0.6f + 0.4f * (sinceSpawn / PopSeconds);
        }
    }
}
