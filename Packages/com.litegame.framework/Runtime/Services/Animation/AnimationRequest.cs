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
}
