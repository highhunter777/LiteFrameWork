namespace LiteFramework.Animation
{
    /// <summary>
    /// 混合播放请求（"淡入淡出、层权重"的请求侧）： **语义 ID + 槽位权重**——与 <see cref="AnimationRequest"/> 同规矩，业务只给语义 ID。
    /// </summary>
    public readonly struct AnimationBlendRequest
    {
        public readonly AnimationId Id;
        /// <summary>请求通道（**解析后以定义通道为权威**——与 <see cref="AnimationRequest"/> 同规矩）。</summary>
        public readonly AnimationChannel Channel;
        /// <summary>各槽位权重（槽位序；要求有限、≥0、总和 &gt; 0——任一条不合法整组拒绝）。</summary>
        public readonly float[] Weights;
        /// <summary>开始位置（归一化 0..1；各输入按**自身时长**对齐到同一归一化相位）。</summary>
        public readonly float StartNormalized;
        /// <summary>速度倍率（全部输入共用；≤0 或非有限值由校验拒绝）。</summary>
        public readonly float Speed;

        public AnimationBlendRequest(AnimationId id, AnimationChannel channel, float[] weights,
            float startNormalized = 0f, float speed = 1f)
        {
            Id = id;
            Channel = channel;
            Weights = weights;
            StartNormalized = startNormalized;
            Speed = speed;
        }
    }
}
