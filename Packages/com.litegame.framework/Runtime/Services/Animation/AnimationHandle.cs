using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放句柄（至少能验证播放器身份、Owner 代次与请求身份）：
    /// <b>旧 Handle 不能停止复用对象的新播放</b>——三个身份分量共同决定它是否仍指向"那一次播放"。
    /// </summary>
    public readonly struct AnimationHandle : IEquatable<AnimationHandle>
    {
        /// <summary>播放器实例身份（同一 Owner 重建播放器后代次不同）。</summary>
        public readonly int PlayerId;
        /// <summary>Owner 代次（复用对象换代后旧句柄全体失效）。</summary>
        public readonly int OwnerGeneration;
        /// <summary>请求序号（播放器内单调递增）。</summary>
        public readonly int RequestSequence;

        public AnimationHandle(int playerId, int ownerGeneration, int requestSequence)
        {
            PlayerId = playerId;
            OwnerGeneration = ownerGeneration;
            RequestSequence = requestSequence;
        }

        public bool IsValid => PlayerId != 0 && RequestSequence != 0;

        public bool Equals(AnimationHandle other)
            => PlayerId == other.PlayerId && OwnerGeneration == other.OwnerGeneration && RequestSequence == other.RequestSequence;

        public override bool Equals(object obj) => obj is AnimationHandle other && Equals(other);
        public override int GetHashCode() => (PlayerId * 397) ^ (OwnerGeneration * 31) ^ RequestSequence;
        public override string ToString() => $"P{PlayerId}/G{OwnerGeneration}/#{RequestSequence}";
    }
}
