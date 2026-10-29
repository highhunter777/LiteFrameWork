using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 动画语义 ID：稳定语义 ID；Idle/Reload/Death 等枚举或常量
    /// 由游戏层定义，框架只处理类型化 ID。框架不认识具体语义，只做类型化传递与相等判定。
    /// </summary>
    public readonly struct AnimationId : IEquatable<AnimationId>
    {
        public readonly string Value;

        public AnimationId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(AnimationId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AnimationId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "(nil)";
    }

    /// <summary>
    /// 播放通道（视觉分层归属——基础层/叠加层/覆盖层，按消费者配置使用）。
    /// 通道是**视觉归属**，不是业务优先级——业务优先级由 Sim/Driver 解释，播放器只解决谁占用通道。
    /// </summary>
    public enum AnimationChannel
    {
        /// <summary>基础层（底层持续姿态——默认动画所在，其余通道在其上叠加/覆盖）。</summary>
        Base = 0,
        /// <summary>叠加层（Mask 限定作用域的局部动作，叠在基础层之上——需后端构造出该层 Mask）。</summary>
        Overlay = 1,
        /// <summary>覆盖层（整体接管——动作期间压过基础层，释放即回到基础层当前姿态）。</summary>
        Override = 2,
    }

    /// <summary>
    /// 通道集合掩码（Graph Evaluate 只由一个驱动器调用）——单次 <c>Tick</c> 采样后，
    /// 用它回报"本次采样中自然到达结束边界的通道集合"，避免逐通道驱动导致时间被重复推进。
    /// <b>语义位与 <see cref="AnimationChannel"/> 枚举序解耦</b>：禁止散写 <c>1 &lt;&lt; channel</c>，
    /// 统一走 <see cref="AnimationChannelMasks.Of(AnimationChannel)"/>。
    /// </summary>
    [Flags]
    public enum AnimationChannelMask
    {
        None = 0,
        Base = 1 << 0,
        Overlay = 1 << 1,
        Override = 1 << 2,
    }

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