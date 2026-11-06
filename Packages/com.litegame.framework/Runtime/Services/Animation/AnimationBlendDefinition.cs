namespace LiteFramework.Animation
{
    /// <summary>
    /// 混合定义：语义 ID → 有序的绑定槽位集合（槽位序 = 请求权重次序）。
    /// </summary>
    public readonly struct AnimationBlendDefinition
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>槽位绑定（槽位序；构造时克隆，视为只读）。</summary>
        public readonly string[] Bindings;
        /// <summary>合法速度区间（含端点）；越界请求按 <see cref="FallbackPolicy.Reject"/> 拒绝。</summary>
        public readonly float MinSpeed;
        public readonly float MaxSpeed;

        public AnimationBlendDefinition(AnimationId id, AnimationChannel channel, string[] bindings,
            float minSpeed = 0.01f, float maxSpeed = 4f)
        {
            Id = id;
            Channel = channel;
            Bindings = bindings == null ? null : (string[])bindings.Clone();
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
        }

        public int SlotCount => Bindings?.Length ?? 0;

        public bool IsValid => Id.IsValid && SlotCount > 0;
    }
}