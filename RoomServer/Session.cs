using System.Diagnostics;

namespace RoomServer
{
    /// <summary>
    /// 会话（《状态同步实施方案》§4.5 SessionManager 行）：一条 KCP 连接的记账。
    /// 保活由 kcp2k 内建 Ping/Timeout 承担（超时 → OnDisconnected → 本表标记 Disconnected）；
    /// E3 的连接 cookie 亦由 kcp2k V1.41 内建握手承担（抗 UDP 放大——白得，记录于实施记录）。
    /// E1 背压水位为骨架字段：批③快照广播按它降档，本批先计数。
    /// </summary>
    public sealed class Session
    {
        /// <summary>传输层连接 Id（kcp2k connectionId）。</summary>
        public readonly int ConnectionId;

        /// <summary>进房后分配的玩家号（房间内从 0 递增；未进房 = -1）。</summary>
        public int PlayerId = -1;

        /// <summary>所属房间（未进房 = null）。</summary>
        public Room Room;

        /// <summary>客户端上报的构建哈希（Join 时校验，不符拒绝进房——版本红线 §4.5-5）。</summary>
        public string BuildHash;

        /// <summary>最近一次收到该会话任何包的 monotonic 毫秒（诊断用；超时踢除走 kcp2k）。</summary>
        public long LastSeenMs;

        /// <summary>断线标记（掉线不停帧：权威循环对断线者沿用空输入 §4.5-2）。</summary>
        public bool Disconnected;

        /// <summary>E1 背压水位（累计已发快照字节数）——批③ 按此降档；释放只能经 ACK ledger（R0-P0-4）。</summary>
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

        /// <summary>档位恢复的滞留起点帧（-1 = 未在恢复观察中；连续达标 <see cref="ProtocolConstants.RecoverHoldMillis"/> 才降一档）。</summary>
        public int RecoverSinceFrame = -1;

        /// <summary>客户端已收的最新快照帧号（Input.ackSnapshot 上报；-1 = 尚无）。全量兜底判据与回溯对齐都用它。</summary>
        public int LastAckSnapshot = -1;

        public Session(int connectionId, long nowMs)
        {
            ConnectionId = connectionId;
            LastSeenMs = nowMs;
        }

        public void Touch(long nowMs) => LastSeenMs = nowMs;

        private static long Now() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
    }
}
