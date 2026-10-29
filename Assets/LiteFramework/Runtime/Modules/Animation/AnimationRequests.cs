namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放请求。<b>业务不能传任意资源路径绕过 Profile</b>——只有 <see cref="AnimationId"/>，
    /// 资源由 Profile/Resolver 解析。
    /// </summary>
    public readonly struct AnimationRequest
    {
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>开始位置（归一化 0..1；0 = 从头）。</summary>
        public readonly float StartNormalized;
        /// <summary>速度倍率（≤0 或非有限值由校验拒绝）。</summary>
        public readonly float Speed;

        public AnimationRequest(AnimationId id, AnimationChannel channel, float startNormalized = 0f, float speed = 1f)
        {
            Id = id;
            Channel = channel;
            StartNormalized = startNormalized;
            Speed = speed;
        }
    }

    /// <summary>
    /// 混合播放请求（"淡入淡出、层权重"的请求侧）：
    /// **语义 ID + 槽位权重**——与 <see cref="AnimationRequest"/> 同规矩，业务只给语义 ID，
    /// 绑定由 Profile 解析（不能传任意资源路径绕过 Profile）。
    ///
    /// **权重按定义槽位序给出**（长度必须等于 <see cref="AnimationBlendDefinition.SlotCount"/>）：
    /// 权重只表达"混合比例"，不表达"增删片段"——集合形态由 Profile 登记固定（节点有界）。
    /// **权重由 Driver/消费者裁决**（业务优先级由 Sim/Driver 解释），播放器只仲裁与校验。
    ///
    /// <see cref="Weights"/> 是**调用方数组**：播放器与后端只在本次提交内读取、不保留引用
    /// （零分配路径 = 调用方复用同一数组逐帧调权重）。
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