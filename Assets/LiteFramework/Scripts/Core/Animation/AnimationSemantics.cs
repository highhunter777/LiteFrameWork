using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 动画语义 ID（《动画模块专项设计》§4"AnimationId：稳定语义 ID；Idle/Reload/Death 等枚举或常量
    /// 由游戏层定义，框架只处理类型化 ID"）。框架不认识具体语义，只做类型化传递与相等判定。
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
    /// 播放通道（§6"首版按消费者设置基础移动、上半身动作、全身动作通道"）。
    /// 通道是**视觉归属**，不是业务优先级——业务优先级由 Sim/Driver 解释，播放器只解决谁占用通道。
    /// </summary>
    public enum AnimationChannel
    {
        /// <summary>基础移动（全身底层）。</summary>
        Locomotion = 0,
        /// <summary>上半身动作（叠加层）。</summary>
        UpperBody = 1,
        /// <summary>全身动作（覆盖移动）。</summary>
        FullBody = 2,
    }

    /// <summary>
    /// 通道集合掩码（§7"Graph Evaluate 只由一个驱动器调用"）——单次 <c>Tick</c> 采样后，
    /// 用它回报"本次采样中自然到达结束边界的通道集合"，避免逐通道驱动导致时间被重复推进。
    /// <b>语义位与 <see cref="AnimationChannel"/> 枚举序解耦</b>：禁止散写 <c>1 &lt;&lt; channel</c>，
    /// 统一走 <see cref="AnimationChannelMasks.Of(AnimationChannel)"/>。
    /// </summary>
    [Flags]
    public enum AnimationChannelMask
    {
        None = 0,
        Locomotion = 1 << 0,
        UpperBody = 1 << 1,
        FullBody = 1 << 2,
    }

    /// <summary>通道 ↔ 掩码位映射帮助器（唯一映射点，见 <see cref="AnimationChannelMask"/>）。</summary>
    public static class AnimationChannelMasks
    {
        public static AnimationChannelMask Of(AnimationChannel channel)
            => channel switch
            {
                AnimationChannel.Locomotion => AnimationChannelMask.Locomotion,
                AnimationChannel.UpperBody => AnimationChannelMask.UpperBody,
                AnimationChannel.FullBody => AnimationChannelMask.FullBody,
                _ => AnimationChannelMask.None,
            };
    }
}