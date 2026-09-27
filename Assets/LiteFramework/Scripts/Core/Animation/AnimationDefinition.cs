namespace LiteFramework.Animation
{
    /// <summary>
    /// 动画定义（《动画模块专项设计》§4）：只读定义，**播放实例另存**当前位置/层权重/Handle/结束状态——
    /// 本类是不可变的共享数据，不在其上存角色状态。
    /// </summary>
    public readonly struct AnimationDefinition
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>后端绑定：Controller 状态完整路径/层，或后续 Clip 资源键。</summary>
        public readonly string Binding;
        /// <summary>是否循环（§5"循环播放不会自然 Completed"）。</summary>
        public readonly bool Loop;
        /// <summary>合法速度区间（含端点）；越界请求按 <see cref="FallbackPolicy.Reject"/> 拒绝。</summary>
        public readonly float MinSpeed;
        public readonly float MaxSpeed;
        /// <summary>是否需要装载（false = 预加载集合，提交即生效）。</summary>
        public readonly bool RequiresLoad;

        public AnimationDefinition(AnimationId id, AnimationChannel channel, string binding,
            bool loop = false, float minSpeed = 0.01f, float maxSpeed = 4f, bool requiresLoad = false)
        {
            Id = id;
            Channel = channel;
            Binding = binding;
            Loop = loop;
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
            RequiresLoad = requiresLoad;
        }

        public bool IsValid => Id.IsValid && !string.IsNullOrEmpty(Binding);
    }
}