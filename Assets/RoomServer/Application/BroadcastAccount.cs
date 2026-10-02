namespace RoomServer.Application
{
    /// <summary>
    /// 每席位快照广播记账（《状态同步实施方案》§4.5 SessionManager 行；R0-P0-4 ACK 语义四道闸）。
    ///
    /// **归属**：广播记账是**房间侧**状态——发送水位 / 发送 ledger / 已验证 ACK 水位 / 背压档位
    /// 只由驱动该房间的线程读写（单线程纪律）。宿主 owner 形态下记账挂在 <see cref="Session"/> 上
    /// （连接即席位视图同一体）；Worker 执行形态下挂在房间本地的每席位槽上——两种形态共用本类，
    /// 保证"ACK 只释放真实发送过的量"等语义只有一份实现。
    ///
    /// 连接级身份（ConnectionId/断开标记）不在这里——见 <see cref="IBroadcastSeat"/>。
    /// </summary>
    public sealed class BroadcastAccount
    {
        /// <summary>E1 背压水位（累计已发快照字节数）——按此降档；释放只能经 ACK ledger（R0-P0-4）。</summary>
        public long SendQueueBytes;

        /// <summary>客户端已确认消化的下行字节（累计口径）——**只能**由验证过的 ACK 经 ledger 前推（P0-4：
        /// 旧实现"ack 到达即视为全清"可被伪造 ack 清空水位 = 慢客户端伪装成快客户端，降级机制失效）。</summary>
        public long AckedBytes;

        /// <summary>最近一次实际发出的快照帧号（广播器记账；ACK 验证的上界——P0-4：ack 只能确认真实已发送的帧）。</summary>
        public int LastSentSnapshotFrame = -1;

        /// <summary>最近一次**验证通过**的 ACK 帧号（单调；-1 = 尚无）。</summary>
        public int LastAcceptedAckFrame = -1;

        /// <summary>客户端已收的最新快照帧号（Input.ackSnapshot 上报；-1 = 尚无）。全量兜底判据与回溯对齐都用它。</summary>
        public int LastAckSnapshot = -1;

        /// <summary>E1 降级计数（水位超限被跳过/降档的发送次数——Ops 观测）。</summary>
        public int BackpressureDrops;

        /// <summary>当前背压档位（0 = 全速；1 抽帧 / 2 收缩 AOI / 3 裁实体——《服务端架构设计》§10-E1）。</summary>
        public int BackpressureTier;

        /// <summary>档位恢复的滞留起点帧（-1 = 未在恢复观察中；连续达标 RecoverHoldMillis 才降一档）。</summary>
        public int RecoverSinceFrame = -1;

        /// <summary>快照发送 ledger：有界环，条目 = (帧号, 发送后累计字节)。ACK 按帧号查累计值释放水位。</summary>
        public readonly SnapshotLedger Ledger = new SnapshotLedger();

        /// <summary>
        /// 广播器发送记账（唯一入口——测试播种同一入口 = 诚实模拟）：
        /// 累计字节 + ledger 记录 + <see cref="LastSentSnapshotFrame"/> 前移。
        /// </summary>
        public void RecordSnapshotSend(int frame, int bytes)
        {
            SendQueueBytes += bytes;
            Ledger.Record(frame, SendQueueBytes);
            LastSentSnapshotFrame = frame;
        }

        /// <summary>
        /// 清空记账（重绑换连接：新连接没有历史发送记录——与宿主 owner 形态"换新会话即新记账"
        /// 等价；不重置会让旧连接的 ack/水位影响新连接的全量兜底与背压判据）。
        /// </summary>
        public void Reset()
        {
            SendQueueBytes = 0;
            AckedBytes = 0;
            LastSentSnapshotFrame = -1;
            LastAcceptedAckFrame = -1;
            LastAckSnapshot = -1;
            BackpressureDrops = 0;
            BackpressureTier = 0;
            RecoverSinceFrame = -1;
            Ledger.Clear();
        }

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
            if (!Ledger.TryGet(ackFrame, out long cumulativeAfter)) return AckResult.RejectedEvicted;

            AckedBytes = cumulativeAfter;               // ledger 累计口径：一次到位
            LastAcceptedAckFrame = ackFrame;
            LastAckSnapshot = ackFrame;                 // 验证过才更新（P0-4 红线）
            return AckResult.Accepted;
        }
    }
}