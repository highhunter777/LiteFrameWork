namespace LiteFramework.Animation
{
    /// <summary>定义缺失或资源缺失时的策略（"拒绝、回退或保持已有姿态的明确策略"）。</summary>
    public enum FallbackPolicy
    {
        /// <summary>拒绝请求，保持当前姿态（默认）。</summary>
        Reject = 0,
        /// <summary>回退到同通道已登记的回退 ID（<see cref="AnimationProfile.RegisterFallback"/>），回退深度受限。</summary>
        UseFallback = 1,
    }
}
