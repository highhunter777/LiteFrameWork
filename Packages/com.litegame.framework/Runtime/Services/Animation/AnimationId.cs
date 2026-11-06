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
}
