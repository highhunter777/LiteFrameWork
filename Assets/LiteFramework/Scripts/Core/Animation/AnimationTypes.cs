using System;

namespace LiteFramework
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
    /// 播放请求（§5）。<b>业务不能传任意资源路径绕过 Profile</b>——只有 <see cref="AnimationId"/>，
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
        /// <summary>是否需要等待资源装载（false = 预加载命中，提交即生效）。</summary>
        public readonly bool RequiresLoad;

        public AnimationResolvedPlayback(AnimationId id, AnimationChannel channel, string binding,
            float startNormalized, float speed, bool requiresLoad)
        {
            Id = id;
            Channel = channel;
            Binding = binding;
            StartNormalized = startNormalized;
            Speed = speed;
            RequiresLoad = requiresLoad;
        }
    }
}
