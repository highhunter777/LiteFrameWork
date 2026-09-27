using System;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 停止原因（§5 终态表的非 Completed 项；<see cref="AnimationTerminalState"/> 由其派生）。
    /// </summary>
    public enum AnimationStopReason
    {
        /// <summary>被接受的新播放或表现接管替换。</summary>
        Interrupted = 0,
        /// <summary>调用方主动取消。</summary>
        Cancelled = 1,
        /// <summary>所属角色/展示作用域/场景结束。</summary>
        OwnerDisposed = 2,
        /// <summary>装载或后端执行失败（带稳定错误码见 <see cref="AnimationStartResult"/>）。</summary>
        Failed = 3,
    }

    /// <summary>
    /// 播放终态（§5"一个已接受请求只产生一次终态"）。
    /// </summary>
    public enum AnimationTerminalState
    {
        /// <summary>尚无终态（进行中或装载中）。</summary>
        None = 0,
        /// <summary>满足定义的视觉结束条件。</summary>
        Completed = 1,
        Interrupted = 2,
        Cancelled = 3,
        OwnerDisposed = 4,
        Failed = 5,
    }

    /// <summary>
    /// 播放状态快照（<c>TryGetState</c> 的输出）。终态记录**有界保留**——过期后查询返回未找到，
    /// 不把所有已完成 Handle 永久留在表里（§5）。
    /// </summary>
    public readonly struct AnimationPlaybackState
    {
        public readonly AnimationHandle Handle;
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>是否仍在装载（已接受但后端尚未提交成功）。</summary>
        public readonly bool IsLoading;
        /// <summary>是否仍在播放（已提交且无终态）。</summary>
        public readonly bool IsPlaying;
        public readonly AnimationTerminalState Terminal;

        public AnimationPlaybackState(AnimationHandle handle, AnimationId id, AnimationChannel channel,
            bool isLoading, bool isPlaying, AnimationTerminalState terminal)
        {
            Handle = handle;
            Id = id;
            Channel = channel;
            IsLoading = isLoading;
            IsPlaying = isPlaying;
            Terminal = terminal;
        }
    }

    /// <summary>
    /// 播放句柄（§5"至少能验证播放器身份、Owner 代次与请求身份"）：
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

    /// <summary>
    /// 启动结果（§5"区分接受与拒绝"）。拒绝**不改变现有播放**（提交前失败保持原姿态）。
    /// </summary>
    public readonly struct AnimationStartResult
    {
        /// <summary>拒绝原因（稳定可诊断；Accepted 时为 <see cref="None"/>）。</summary>
        public enum Reason
        {
            None = 0,
            /// <summary>AnimationId 无对应定义（Profile 缺失）。</summary>
            InvalidDefinition,
            /// <summary>当前后端不支持该能力（§4"不把不支持的能力静默降级"）。</summary>
            UnsupportedCapability,
            /// <summary>通道容量/混合尾部超限（§12）。</summary>
            CapacityExceeded,
            /// <summary>Owner 已失效（代次过期/已释放）。</summary>
            OwnerUnavailable,
            /// <summary>请求字段非法（空 ID、非有限速度、越界开始位置等）。</summary>
            InvalidRequest,
        }

        public readonly bool Accepted;
        public readonly AnimationHandle Handle;
        public readonly Reason RejectReason;

        private AnimationStartResult(bool accepted, AnimationHandle handle, Reason reason)
        {
            Accepted = accepted;
            Handle = handle;
            RejectReason = reason;
        }

        public static AnimationStartResult Accept(AnimationHandle handle) => new AnimationStartResult(true, handle, Reason.None);
        public static AnimationStartResult Reject(Reason reason) => new AnimationStartResult(false, default, reason);
    }
}