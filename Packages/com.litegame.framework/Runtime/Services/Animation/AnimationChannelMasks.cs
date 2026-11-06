namespace LiteFramework.Animation
{
    /// <summary>通道 ↔ 掩码位映射帮助器（唯一映射点，见 <see cref="AnimationChannelMask"/>）。</summary>
    public static class AnimationChannelMasks
    {
        public static AnimationChannelMask Of(AnimationChannel channel)
            => channel switch
            {
                AnimationChannel.Base => AnimationChannelMask.Base,
                AnimationChannel.Overlay => AnimationChannelMask.Overlay,
                AnimationChannel.Override => AnimationChannelMask.Override,
                _ => AnimationChannelMask.None,
            };
    }
}
