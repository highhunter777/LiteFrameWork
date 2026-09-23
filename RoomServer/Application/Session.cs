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
    /// 保活由 kcp2k 内建 Ping/Timeout 承担（超时 → OnDisconnected → 本表标记 Disconnected）；
    /// E3 的连接 cookie 亦由 kcp2k V1.41 内建握手承担（抗 UDP 放大——白得，记录于实施记录）。
    /// </summary>
    public sealed class Session
    {
        /// <summary>传输层连接 Id（kcp2k connectionId）。</summary>
        public readonly int ConnectionId;

        /// <summary>进房后分配的玩家号（房间内从 0 递增；未进房 = -1）。App 路由缓存——席位事实在 Runtime。</summary>
        public int PlayerId = -1;

        /// <summary>客户端上报的构建哈希（Join 时校验，不符拒绝进房——版本红线 §4.5-5）。</summary>
        public string BuildHash;

        /// <summary>最近一次收到该会话任何包的 monotonic 毫秒（诊断用；超时踢除走 kcp2k）。</summary>
        public long LastSeenMs;

        /// <summary>断线标记（掉线不停帧：权威循环对断线者沿用空输入 §4.5-2）。</summary>
        public bool Disconnected;

        /// <summary>E1 背压水位（累计已发快照字节数）——按此降档；释放只能经 ACK ledger（R0-P0-4）。</summary>
        public long SendQueueBytes;

        /// <summary>客户端已确认消化的下行字节（累计口径）——**只能**由验证过的 ACK 经 ledger 前推（P0-4：
        /// 旧实现"ack 到达即视为全清"可被伪造 ack 清空水位 = 慢客户端伪装成快客户端，降级机制失效）。</summary>
        public long AckedBytes;

        /// <summary>最近一次实际发出的快照帧号（广播器记账；ACK 验证的上界——P0-4：ack 只能确认真实已发送的帧）。</summary>
        public int LastSentSnapshotFrame = -1;

        /// <summary>最近一次**验证通过**的 ACK 帧号（单调；-1 = 尚无）。</summary>
        public int LastAcceptedAckFrame = -1;

        /// <summary>快照发送 ledger：有界环，条目 = (帧号, 发送后累计字节)。ACK 按帧号查累计值释放水位。</summary>
        public readonly SnapshotLedger SnapshotLedger = new SnapshotLedger();

        /// <summary>
        /// 广播器发送记账（唯一入口——测试播种同一入口 = 诚实模拟）：
        /// 累计字节 + ledger 记录 + <see cref="LastSentSnapshotFrame"/> 前移。
        /// </summary>
        public void RecordSnapshotSend(int frame, int bytes)
        {
            SendQueueBytes += bytes;
            SnapshotLedger.Record(frame, SendQueueBytes);
            LastSentSnapshotFrame = frame;
        }

        /// <summary>E1 降级计数（水位超限被跳过/降档的发送次数——Ops 观测）。</summary>
        public int BackpressureDrops;

        /// <summary>当前背压档位（0 = 全速；1 抽帧 / 2 收缩 AOI / 3 裁实体——《服务端架构设计》§10-E1）。</summary>
        public int BackpressureTier;

        /// <summary>档位恢复的滞留起点帧（-1 = 未在恢复观察中；连续达标 RecoverHoldMillis 才降一档）。</summary>
        public int RecoverSinceFrame = -1;

        /// <summary>客户端已收的最新快照帧号（Input.ackSnapshot 上报；-1 = 尚无）。全量兜底判据与回溯对齐都用它。</summary>
        public int LastAckSnapshot = -1;

        public Session(int connectionId, long nowMs)
        {
            ConnectionId = connectionId;
            LastSeenMs = nowMs;
        }

        public void Touch(long nowMs) => LastSeenMs = nowMs;

        /// <summary>
        /// 语义 ACK 验证（P0-4：**旧实现"ack 到达即 AckedBytes=SendQueueBytes 全清"可被任意伪造 ack 清空水位**——
        /// 慢客户端伪装成快客户端，E1 降级形同虚设）。四道闸全过才释放：
        /// 非负且单调前进（> <see cref="LastAcceptedAckFrame"/>）→ 不超前（≤ <see cref="LastSentSnapshotFrame"/>，
        /// 只认真实发送过的帧）→ 命中发送 ledger（取该帧发送后的累计字节）。释放 = <c>AckedBytes = ledger.累计值</c>
        /// （天然单调）。<see cref="LastAckSnapshot"/>（NeedsFull 判据）**只在验证后**更新——差分器只吃可信值。
        /// 返回分类结果，App 层据此记 Ops（本类不自持计数器）。
        /// </summary>
        public AckResult TryAcceptAck(int ackFrame)
        {
            if (ackFrame < 0 || ackFrame <= LastAcceptedAckFrame) return AckResult.RejectedStale;
            if (ackFrame > LastSentSnapshotFrame) return AckResult.RejectedFuture;
            if (!SnapshotLedger.TryGet(ackFrame, out long cumulativeAfter)) return AckResult.RejectedEvicted;

            AckedBytes = cumulativeAfter;               // ledger 累计口径：一次到位
            LastAcceptedAckFrame = ackFrame;
            LastAckSnapshot = ackFrame;                 // 验证过才更新（P0-4 红线）
            return AckResult.Accepted;
        }
    }
}
