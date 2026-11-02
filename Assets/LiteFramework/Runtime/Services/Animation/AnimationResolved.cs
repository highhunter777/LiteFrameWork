namespace LiteFramework.Animation
{
    /// <summary>
    /// 已解析的播放方案（Profile/Resolver 的输出；后端只认这个，不认 AnimationId）。
    /// <see cref="Binding"/> = Controller 状态完整路径/层（Animator 后端）或后续 Clip 资源键。
    /// </summary>
    public readonly struct AnimationResolvedPlayback
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>后端绑定（状态路径/层，或资源键）。</summary>
        public readonly string Binding;
        public readonly float StartNormalized;
        public readonly float Speed;
        /// <summary>是否循环（Completed 的判定方式必须写入定义——判定归定义，不读资产 loop 设置）。</summary>
        public readonly bool Loop;
        /// <summary>是否需要等待资源装载（false = 预加载命中，提交即生效）。</summary>
        public readonly bool RequiresLoad;

        public AnimationResolvedPlayback(AnimationId id, AnimationChannel channel, string binding,
            float startNormalized, float speed, bool requiresLoad, bool loop = false)
        {
            Id = id;
            Channel = channel;
            Binding = binding;
            StartNormalized = startNormalized;
            Speed = speed;
            RequiresLoad = requiresLoad;
            Loop = loop;
        }
    }

    /// <summary>
    /// 已解析的混合方案（Profile/Resolver 的输出；后端只认这个，不认 AnimationId）。
    /// <see cref="Bindings"/> 来自 Profile 登记（不可变共享数据，**不复制**）；
    /// <see cref="Weights"/> 来自请求（**调用方数组，只在本次提交内有效、不保留**）——两者同一槽位序。
    /// 混合集合**恒为循环形态**：没有单一结束边界，不产生 <c>Completed</c>，故没有 Loop 字段。
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