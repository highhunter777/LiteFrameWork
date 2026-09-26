namespace RoomServer.Runtime
{
    /// <summary>关闭原因（《商业级通用服务端框架总设计》§8.1 RoomCommand.Shutdown / §9.1）。</summary>
    public enum ShutdownReason
    {
        None = 0,
        /// <summary>对局时限到达（Running 期间由 Tick 时间轴驱动）。</summary>
        TimeLimit,
        /// <summary>运维主动终止（进程排空 / 人工干预）。</summary>
        Operator,
        /// <summary>
        /// **限时排空**（§12 优雅关闭第 3 步："在配置时限内完成对局；**超时则归档 Aborted 原因并安全关闭**"）。
        /// 与 <see cref="Operator"/> 的区别在语义而非机制：Operator 是立即停，DrainTimeout 是
        /// "已等满排空时限仍未自然收敛"。归档/日志据此区分"正常排空"与"排空超时被强停"。
        /// </summary>
        DrainTimeout,
        /// <summary>不可恢复错误（内部状态损坏、连续异常）。</summary>
        Error,
        /// <summary>等待玩家超时（房间始终未满员）。</summary>
        WaitingTimeout,
        /// <summary>全员离场。</summary>
        AllPlayersLeft,
    }

    /// <summary>命令种类（§8.1：RoomRuntime 的**唯一**入口——Transport/时钟/IO 一概不得旁路进入）。</summary>
    public enum RoomCommandKind
    {
        AuthenticatedJoin = 0,
        ClientInput,
        Disconnect,
        Rebind,
        Tick,
        Shutdown,
        /// <summary>恢复完成 ACK（R1 批③，§9.3 步骤 6）：席位 Restoring → Active 的门闩。</summary>
        RestoreAck,
    }

    /// <summary>
    /// 房间命令（§8.1 RoomCommand）：App 层完成解码、票据/buildHash 校验后构造本命令送入 Runtime。
    ///
    /// 形态裁定：**struct 判别联合 + 静态工厂**，非 record 族——ClientInput 与 Tick 是每包/每帧的
    /// 热路径（R0 起零分配纪律），多态 record 每条一次堆分配不可接受；命令种类有限且字段面窄，
    /// 联合 struct 一处可览。R2 引入有界 Mailbox 时命令即队列元素，本形态零改动可序列入队。
    ///
    /// 语义边界：AuthenticatedJoin 只在 App 层验证（版本/票据/房间号/字节上限）通过后才会构造——
    /// Runtime 信任命令来源，不再重复验证协议细节（§5-5"可测与可诊断"：失败源可注入）。
    /// </summary>
    public readonly struct RoomCommand
    {
        public RoomCommandKind Kind { get; }

        /// <summary>Join/Disconnect/Rebind 携带的传输连接 Id。</summary>
        public int ConnectionId { get; }

        /// <summary>ClientInput/Rebind 的目标席位（playerId）。</summary>
        public int PlayerId { get; }

        /// <summary>Rebind 的新连接 Id。</summary>
        public int NewConnectionId { get; }

        /// <summary>Tick/Shutdown 的时间戳（App 由 IMonotonicClock 派生注入；L1 用虚拟时钟）。</summary>
        public long NowMs { get; }

        public ShutdownReason Reason { get; }

        /// <summary>ClientInput 的输入载荷（纯数据——Runtime 不见 proto，§8.1 禁止项）。</summary>
        public ClientInputBatch Input { get; }

        private RoomCommand(RoomCommandKind kind, int connectionId, int playerId,
            int newConnectionId, long nowMs, ShutdownReason reason, in ClientInputBatch input)
        {
            Kind = kind;
            ConnectionId = connectionId;
            PlayerId = playerId;
            NewConnectionId = newConnectionId;
            NowMs = nowMs;
            Reason = reason;
            Input = input;
        }

        /// <summary>已验证的进房（App 完成票据/版本校验后构造）。</summary>
        public static RoomCommand Join(int connectionId)
            => new RoomCommand(RoomCommandKind.AuthenticatedJoin, connectionId, -1, -1, 0, ShutdownReason.None, default);

        /// <summary>已过闸会话的输入包（App 解 proto → 纯数据后构造）。</summary>
        public static RoomCommand ClientInput(int playerId, in ClientInputBatch input)
            => new RoomCommand(RoomCommandKind.ClientInput, -1, playerId, -1, 0, ShutdownReason.None, input);

        /// <summary>传输连接断开（席位是否保留由重连窗口决定，本命令只通报连接事实）。</summary>
        public static RoomCommand Disconnect(int connectionId)
            => new RoomCommand(RoomCommandKind.Disconnect, connectionId, -1, -1, 0, ShutdownReason.None, default);

        /// <summary>重连重绑：把新连接原子绑定到既有席位（旧连接立即失效）。</summary>
        public static RoomCommand Rebind(int playerId, int newConnectionId)
            => new RoomCommand(RoomCommandKind.Rebind, -1, playerId, newConnectionId, 0, ShutdownReason.None, default);

        /// <summary>权威步进（60Hz；NowMs 为 App 派生的单调毫秒——超时/时限判定的唯一时间源）。</summary>
        public static RoomCommand Tick(long nowMs)
            => new RoomCommand(RoomCommandKind.Tick, -1, -1, -1, nowMs, ShutdownReason.None, default);

        /// <summary>关闭请求（重复投递幂等——Finishing/Settling/Closed 状态下记事件不重迁移，§9.1）。</summary>
        public static RoomCommand Shutdown(ShutdownReason reason)
        {
            if (reason == ShutdownReason.None)
                throw new System.ArgumentException("Shutdown 必须携带原因", nameof(reason));
            return new RoomCommand(RoomCommandKind.Shutdown, -1, -1, -1, 0, reason, default);
        }

        /// <summary>
        /// 恢复完成 ACK（§9.3 步骤 6）：客户端已应用重连响应的权威快照与输入历史，席位
        /// <see cref="SeatPhase.Restoring"/> → Active——服务器自此恢复该席位增量广播。
        /// 幂等：非 Restoring 席位（未在恢复中）静默忽略，重复 ACK 无副作用。
        /// </summary>
        public static RoomCommand RestoreAck(int playerId)
            => new RoomCommand(RoomCommandKind.RestoreAck, -1, playerId, -1, 0, ShutdownReason.None, default);
    }
}
