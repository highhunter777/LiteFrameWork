namespace LiteFramework.Animation
{
    /// <summary>
    /// 已解析的混合方案（Profile/Resolver 的输出；后端只认这个，不认 AnimationId）。
    /// </summary>
    public readonly struct AnimationResolvedBlend
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>槽位绑定（槽位序；后端按此取片段，**视为只读**）。</summary>
        public readonly string[] Bindings;
        /// <summary>槽位权重（槽位序；与 <see cref="Bindings"/> 等长）。</summary>
        public readonly float[] Weights;
        public readonly float StartNormalized;
        public readonly float Speed;

        public AnimationResolvedBlend(AnimationId id, AnimationChannel channel, string[] bindings, float[] weights,
            float startNormalized, float speed)
        {
            Id = id;
            Channel = channel;
            Bindings = bindings;
            Weights = weights;
            StartNormalized = startNormalized;
            Speed = speed;
        }

        /// <summary>槽位数（= 绑定数 = 权重数；上限见 <see cref="AnimationProfile.MaxBlendSlots"/>）。</summary>
        public int SlotCount => Bindings?.Length ?? 0;
    }
}
