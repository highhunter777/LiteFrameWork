namespace LiteGame.View.Animation
{
    /// <summary>
    /// 俯仰瞄准姿态映射（第三人称自由视角）：相机 pitch（弧度）→ 上半身姿态俯仰角（弧度）。
    /// 程序化姿态的纯数学面——写序与 <c>WeaponGripConstraint</c> 同款（动画 Evaluate 后置写、
    /// 单一写者、退出即停写）。aim up/down 素材批（Overlay 上半身混合）落地后本映射退役为素材驱动。
    /// </summary>
    public static class AimPitchPosture
    {
        /// <summary>上半身俯仰下限（弧度；机制初档常量单源，调参面后续迁 GM）——
        /// 前俯超过 -50° 后头胸贴枪身/穿模风险陡增，超出部分由相机 pitch 承担。</summary>
        public const float MinPitchRad = -50f * UnityEngine.Mathf.Deg2Rad;

        /// <summary>上半身俯仰上限（弧度；与下限同推导口径）——后仰超过 +60° 视觉失衡。</summary>
        public const float MaxPitchRad = 60f * UnityEngine.Mathf.Deg2Rad;

        /// <summary>相机 pitch → 上半身姿态俯仰角（区间内恒等、越界钳制）。
        /// 恒等映射依据：躯干俯仰直接采用相机俯仰量（所见即所俯仰——素材批落地前无比例衰减）。</summary>
        public static float UpperBodyPitch(float cameraPitchRad)
        {
            return UnityEngine.Mathf.Clamp(cameraPitchRad, MinPitchRad, MaxPitchRad);
        }
    }
}
