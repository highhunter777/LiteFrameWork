namespace RoomServer.Application
{
    /// <summary>ACK 验证结果（R1：四道闸从 Room 迁到会话——ACK 只触连接级水位/ledger，属 App 层事实）。</summary>
    public enum AckResult
    {
        Accepted = 0,
        /// <summary>负值或未前进（回退/重复）。</summary>
        RejectedStale,
        /// <summary>超前于最近真实发送帧（伪造/时钟错乱）。</summary>
        RejectedFuture,
        /// <summary>落在发送 ledger 窗外（条目已被覆盖——保守不放行）。</summary>
        RejectedEvicted,
    }

    /// <summary>
    /// 会话（《状态同步实施方案》§4.5 SessionManager 行）：一条 KCP 连接的记账——**连接级**状态
    /// （背压水位 / ACK 水位 / 发送 ledger）。
    ///
    /// R1 分离（§9.2）：本类只表示 <c>Connection</c>（断线即弃、重绑时整体作废）；席位语义
    /// （playerId 归属、实体 Id、重连窗口保留）在 Runtime 层 <see cref="Runtime.PlayerSession"/>。
    /// <see cref="PlayerId"/> 仅作 App 层的"连接 → 席位"路由缓存（进房/重绑时由 App 写入），
    /// 不构成身份——身份事实在席位。
    ///
    /// 广播记账（水位/ledger/ACK/背压档）收在 <see cref="Broadcast"/>（<see cref="BroadcastAccount"/>）；
    /// 本类同时实现 <see cref="IBroadcastSeat"/>——宿主 owner 形态下会话即席位视图，管线直接吃它。
    /// 历史直用字段的调用面（<c>SendQueueBytes</c> 等）保留为转发成员，语义不变。
    ///
    /// 保活由 kcp2k 内建 Ping/Timeout 承担（超时 → OnDisconnected → 本表标记 Disconnected）；
    /// E3 的连接 cookie 亦由 kcp2k V1.41 内建握手承担（抗 UDP 放大——白得，记录于实施记录）。
    /// </summary>
    public sealed class Session : IBroadcastSeat
    {
        /// <summary>传输层连接 Id（kcp2k connectionId）。</summary>
        public readonly int ConnectionId;

        /// <summary>
        /// 连接代次。连接 Id 可能被底层复用；宿主把该值带进 Mailbox envelope，
        /// 消费时可丢弃旧连接的迟到命令。直接构造的历史测试会使用 0（不启用代次校验）。
        /// </summary>
        public readonly long Epoch;

        /// <summary>进房后分配的玩家号（房间内从 0 递增；未进房 = -1）。App 路由缓存——席位事实在 Runtime。</summary>
        public int PlayerId = -1;

        /// <summary>
        /// 本会话当前所在房间号（未进房 = null）。多房间下的**路由键**：输入/重连/断线都要先按它
        /// 找到房间，再按 <see cref="PlayerId"/> 找席位。
        ///
        /// 与 <see cref="PlayerId"/> 同为 App 层路由缓存——**不构成身份**（身份在
        /// <see cref="Principal"/>，席位事实在 Runtime）。会话断线即弃，本字段随会话消失。
        /// </summary>
        public string RoomId;

        /// <summary>客户端上报的构建哈希（Join 时校验，不符拒绝进房——版本红线 §4.5-5）。</summary>
        public string BuildHash;

        /// <summary>
        /// 验证通过的 Join 票据主体（§P0-6 身份接缝）。**null = 未走票据验证**（验证器未装配的原型形态）。
        /// 这是**身份事实**（谁），而 <see cref="PlayerId"/> 是**席位路由**（第几号位）——两者不可互推：
        /// 席位仍由 RoomRuntime 权威分配，本字段不参与分配。日志不得输出其中任何字段值。
        /// </summary>
        public JoinPrincipal Principal;

        /// <summary>最近一次收到该会话任何包的 monotonic 毫秒（诊断用；超时踢除走 kcp2k）。</summary>
        public long LastSeenMs;

        /// <summary>断线标记（掉线不停帧：权威循环对断线者沿用空输入 §4.5-2）。</summary>
        public bool Disconnected;

        /// <summary>Join 已验收并入 Mailbox、尚未由 Runtime 消费的 admission 闸门。</summary>
        public bool JoinPending;

        /// <summary>本连接的快照广播记账（水位/ledger/ACK/背压档；见 <see cref="BroadcastAccount"/>）。</summary>
        public readonly BroadcastAccount Broadcast = new BroadcastAccount();

        // ---- 广播记账转发成员（历史直用字段的调用面保持不变；语义见 BroadcastAccount）----

        /// <summary>E1 背压水位（累计已发快照字节数）。</summary>
        public long SendQueueBytes
        {
            get { return Broadcast.SendQueueBytes; }
            set { Broadcast.SendQueueBytes = value; }
        }

        /// <summary>客户端已确认消化的下行字节（累计口径）。</summary>
        public long AckedBytes
        {
            get { return Broadcast.AckedBytes; }
            set { Broadcast.AckedBytes = value; }
        }

        /// <summary>最近一次实际发出的快照帧号。</summary>
        public int LastSentSnapshotFrame
        {
            get { return Broadcast.LastSentSnapshotFrame; }
            set { Broadcast.LastSentSnapshotFrame = value; }
        }

        /// <summary>最近一次验证通过的 ACK 帧号（单调）。</summary>
        public int LastAcceptedAckFrame
        {
            get { return Broadcast.LastAcceptedAckFrame; }
            set { Broadcast.LastAcceptedAckFrame = value; }
        }

        /// <summary>快照发送 ledger（有界环）。</summary>
        public SnapshotLedger SnapshotLedger
        {
            get { return Broadcast.Ledger; }
        }

        /// <summary>广播器发送记账（转发）。</summary>
        public void RecordSnapshotSend(int frame, int bytes) => Broadcast.RecordSnapshotSend(frame, bytes);

        /// <summary>E1 降级计数。</summary>
        public int BackpressureDrops
        {
            get { return Broadcast.BackpressureDrops; }
            set { Broadcast.BackpressureDrops = value; }
        }

        /// <summary>当前背压档位（0 全速；1 抽帧 / 2 收缩 AOI / 3 裁实体）。</summary>
        public int BackpressureTier
        {
            get { return Broadcast.BackpressureTier; }
            set { Broadcast.BackpressureTier = value; }
        }

        /// <summary>档位恢复的滞留起点帧（-1 = 未在恢复观察中）。</summary>
        public int RecoverSinceFrame
        {
            get { return Broadcast.RecoverSinceFrame; }
            set { Broadcast.RecoverSinceFrame = value; }
        }

        /// <summary>客户端已收的最新快照帧号（Input.ackSnapshot 上报；-1 = 尚无）。</summary>
        public int LastAckSnapshot
        {
            get { return Broadcast.LastAckSnapshot; }
            set { Broadcast.LastAckSnapshot = value; }
        }

        /// <summary>广播记账视图（<see cref="IBroadcastSeat"/>；宿主 owner 形态下即本会话）。</summary>
        BroadcastAccount IBroadcastSeat.Account
        {
            get { return Broadcast; }
        }

        public Session(int connectionId, long nowMs)
            : this(connectionId, nowMs, 0)
        {
        }

        public Session(int connectionId, long nowMs, long epoch)
        {
            ConnectionId = connectionId;
            Epoch = epoch;
            LastSeenMs = nowMs;
        }

        public void Touch(long nowMs) => LastSeenMs = nowMs;

        /// <summary>连接号视图（<see cref="IBroadcastSeat"/>）；同 <see cref="ConnectionId"/>。</summary>
        int IBroadcastSeat.ConnectionId
        {
            get { return ConnectionId; }
        }

        /// <summary>断开标记视图（<see cref="IBroadcastSeat"/>）；同 <see cref="Disconnected"/>。</summary>
        bool IBroadcastSeat.Disconnected
        {
            get { return Disconnected; }
        }

        /// <summary>连接代次视图（<see cref="IBroadcastSeat"/>）；同 <see cref="Epoch"/>。</summary>
        long IBroadcastSeat.SessionEpoch
        {
            get { return Epoch; }
        }

        /// <summary>语义 ACK 验证（转发到 <see cref="Broadcast"/>；四道闸语义见 <see cref="BroadcastAccount.TryAcceptAck"/>）。</summary>
        public AckResult TryAcceptAck(int ackFrame) => Broadcast.TryAcceptAck(ackFrame);
    }
}
