using System.Text;
using System.Threading;
using LiteSim;
using RoomServer.Runtime;

namespace RoomServer.Application
{
    /// <summary>
    /// Ops 指标（《M10实施指导》§2.5 登记项："输出形式 = 控制台周期打印（5s），指标集 = 帧号/房间数/快照均长/丢包率/和解率汇总"）。
    /// 本类只做**汇总与格式化**（StringBuilder 复用）；计数由各处经 <c>Note*</c> 方法前推。
    ///
    /// **为什么是"只读属性 + Note* 方法"而不是 public 可变字段（P4 计数面封装）**：
    /// 字段公开时"该不该用 Interlocked、谁写谁读"只能靠注释纪律——编译器不设防，
    /// 而本类的计数**确实跨线程**（输入/ACK 类计数由多个房间 Worker 线程累加，见下）。
    /// 收成方法后：每个计数的**原子性在定义处一次定死**（而不是在每个调用点各写各的），
    /// 读面变成 `Interlocked.Read`（周期行不再可能读到撕裂的 64 位值）。
    /// **兼容面**：属性名与字段名一一对应，`Format`/`/metrics`/测试断言（`host.Ops.Rejects` 等）全部零改动；
    /// 唯一保留的可写状态是 <see cref="PrintEnabled"/>（输出开关，不是计数）。
    ///
    /// **并发形态**（R2 Worker 执行）：输入/ACK 类计数可能由多个房间 Worker 线程累加，
    /// 故这些计数用 <see cref="Interlocked"/> 前推；周期行读取亦经 <c>Interlocked.Read</c>——
    /// 观测行的定位仍是近似快照（不承诺跨计数一致性），但单值不再撕裂。
    /// </summary>
    public sealed class Ops
    {
        // ---- 单一可写开关 ----

        /// <summary>周期打印开关（对跑脚本可关；用例断言时不影响计数）——本类唯一可外部写的状态。</summary>
        public bool PrintEnabled { get; set; } = true;

        // ---- 后备字段（一律私有；递增只经下列 Note* 方法）----

        private long _lastPrintMs;
        private long _inputPackets;
        private long _ackObserved;
        private long _mismatchReports;
        private int _lastMismatchFrame = -1;
        private long _rejects;
        private long _reconnectsServed;
        private long _roomsRejectedAtCapacity;
        private long _roomsRejectedBadConfig;
        private long _roomsDestroyed;
        private long _rejectsWhileDraining;
        private long _roomsDrainTimedOut;
        private long _ticksCoalesced;
        private long _ticketRejected;
        private long _ticketRejectedBadSignature;
        private long _ticketRejectedExpired;
        private long _ticketRejectedReplayed;
        private long _ticketRejectedUnknownKey;
        private long _sessionsRejected;
        private long _sessionsCleaned;
        private long _matchStateChanges;
        private long _settlementsReady;
        private long _settlementsJournaled;
        private long _settlementsOutboxDuplicates;
        private long _settlementsOutboxRejected;
        private long _settlementsOutboxFailed;
        private long _restoresCompleted;
        private long _packetRejects;
        private long _packetOversized;
        private long _ackRejected;
        private long _ackRejectedStale;
        private long _ackRejectedFuture;
        private long _ackRejectedEvicted;

        // ---- 读面（名字与原字段一致：Format / 测试断言 / 诊断全不动）----

        /// <summary>最近一次周期打印时刻（单调毫秒）——写入只经 <see cref="TryBeginPrint"/>。</summary>
        public long LastPrintMs
        {
            get { return Interlocked.Read(ref _lastPrintMs); }
        }

        public long InputPackets
        {
            get { return Interlocked.Read(ref _inputPackets); }
        }

        public long AckObserved
        {
            get { return Interlocked.Read(ref _ackObserved); }
        }

        /// <summary>和解上报数（"和解触发率全程可观测"的分子；§9 M10 行）。</summary>
        public long MismatchReports
        {
            get { return Interlocked.Read(ref _mismatchReports); }
        }

        /// <summary>最近一次和解上报的帧号（-1 = 从未上报）——更新只经 <see cref="NoteMismatch"/>。</summary>
        public int LastMismatchFrame
        {
            get { return Volatile.Read(ref _lastMismatchFrame); }
        }

        public long Rejects
        {
            get { return Interlocked.Read(ref _rejects); }
        }

        public long ReconnectsServed
        {
            get { return Interlocked.Read(ref _reconnectsServed); }
        }

        /// <summary>因达房间容量上限被拒的建房请求数（§520/§600：上限来自配置，不写死）。</summary>
        public long RoomsRejectedAtCapacity
        {
            get { return Interlocked.Read(ref _roomsRejectedAtCapacity); }
        }

        /// <summary>因模板缺失/模板配置非法被拒的建房请求数。</summary>
        public long RoomsRejectedBadConfig
        {
            get { return Interlocked.Read(ref _roomsRejectedBadConfig); }
        }

        /// <summary>终态房间销毁数（§42"房间销毁后清理"：容量归还，同房号此后可重建）。</summary>
        public long RoomsDestroyed
        {
            get { return Interlocked.Read(ref _roomsDestroyed); }
        }

        /// <summary>排空期间被拒的新进房数（§12 第 2 步"停止接受新 Join"）。</summary>
        public long RejectsWhileDraining
        {
            get { return Interlocked.Read(ref _rejectsWhileDraining); }
        }

        /// <summary>排空超时被强制关闭的房间数（§12 第 3 步"超时则归档 Aborted 原因并安全关闭"）。</summary>
        public long RoomsDrainTimedOut
        {
            get { return Interlocked.Read(ref _roomsDrainTimedOut); }
        }

        /// <summary>Worker 执行形态下被合并节流跳过的 Tick 数（至多一条在途；防 Worker 落后时 Tick 堆积）。</summary>
        public long TicksCoalesced
        {
            get { return Interlocked.Read(ref _ticksCoalesced); }
        }

        /// <summary>票据拒绝总数（任何分类）。</summary>
        public long TicketRejected
        {
            get { return Interlocked.Read(ref _ticketRejected); }
        }

        /// <summary>签名不匹配（篡改）。</summary>
        public long TicketRejectedBadSignature
        {
            get { return Interlocked.Read(ref _ticketRejectedBadSignature); }
        }

        /// <summary>已过期。</summary>
        public long TicketRejectedExpired
        {
            get { return Interlocked.Read(ref _ticketRejectedExpired); }
        }

        /// <summary>重放（nonce 已消费）。</summary>
        public long TicketRejectedReplayed
        {
            get { return Interlocked.Read(ref _ticketRejectedReplayed); }
        }

        /// <summary>未知 kid（密钥轮换后旧 kid 下线）。</summary>
        public long TicketRejectedUnknownKey
        {
            get { return Interlocked.Read(ref _ticketRejectedUnknownKey); }
        }

        /// <summary>会话容量上限拒绝的连接数（超限断开计数）。</summary>
        public long SessionsRejected
        {
            get { return Interlocked.Read(ref _sessionsRejected); }
        }

        /// <summary>周期清理移除的会话数（累计）。</summary>
        public long SessionsCleaned
        {
            get { return Interlocked.Read(ref _sessionsCleaned); }
        }

        /// <summary>Match 状态迁移事件数（含幂等忽略与非法拒绝）。</summary>
        public long MatchStateChanges
        {
            get { return Interlocked.Read(ref _matchStateChanges); }
        }

        /// <summary>SettlementReady 产出数。</summary>
        public long SettlementsReady
        {
            get { return Interlocked.Read(ref _settlementsReady); }
        }

        /// <summary>SettlementReady 已入盒落盘数（§11.3 本地持久 Outbox；排空第 4 步的语义承载）。</summary>
        public long SettlementsJournaled
        {
            get { return Interlocked.Read(ref _settlementsJournaled); }
        }

        /// <summary>入盒幂等命中数（同 matchId 重复——重放/重试形态）。</summary>
        public long SettlementsOutboxDuplicates
        {
            get { return Interlocked.Read(ref _settlementsOutboxDuplicates); }
        }

        /// <summary>入盒容量满拒数（§6"有界 Outbox"；显式拒绝不静默丢）。</summary>
        public long SettlementsOutboxRejected
        {
            get { return Interlocked.Read(ref _settlementsOutboxRejected); }
        }

        /// <summary>入盒介质写失败数（不抛进权威循环，计数可见）。</summary>
        public long SettlementsOutboxFailed
        {
            get { return Interlocked.Read(ref _settlementsOutboxFailed); }
        }

        /// <summary>重连恢复完成数（SeatRestored：Restoring → Active；§9.3 步骤 6）。</summary>
        public long RestoresCompleted
        {
            get { return Interlocked.Read(ref _restoresCompleted); }
        }

        /// <summary>坏包（未知类型/截断/proto 解析失败）拒绝计数。</summary>
        public long PacketRejects
        {
            get { return Interlocked.Read(ref _packetRejects); }
        }

        /// <summary>超长入包（解析前长度上限）拒绝计数。</summary>
        public long PacketOversized
        {
            get { return Interlocked.Read(ref _packetOversized); }
        }

        /// <summary>语义 ACK 违纪合计（回退/重复/超前/超窗——不释放水位，见 <see cref="Session.TryAcceptAck"/>）。</summary>
        public long AckRejected
        {
            get { return Interlocked.Read(ref _ackRejected); }
        }

        /// <summary>ACK 回退/重复/负值拒绝计数。</summary>
        public long AckRejectedStale
        {
            get { return Interlocked.Read(ref _ackRejectedStale); }
        }

        /// <summary>ACK 超前（> 最近真实发送帧）拒绝计数。</summary>
        public long AckRejectedFuture
        {
            get { return Interlocked.Read(ref _ackRejectedFuture); }
        }

        /// <summary>ACK 落在 ledger 窗外拒绝计数。</summary>
        public long AckRejectedEvicted
        {
            get { return Interlocked.Read(ref _ackRejectedEvicted); }
        }

        // ---- 写面（Note*：原子性在定义处一次定死）----

        /// <summary>
        /// 周期打印节流（**判据收进 Ops**，原先散在宿主）：到点则前推时间戳并返回 true。
        /// 用 CAS 前推，宿主/其它线程并发调用不会重复打印。
        /// </summary>
        public bool TryBeginPrint(long nowMs, long intervalMs)
        {
            long last = Volatile.Read(ref _lastPrintMs);
            if (nowMs - last < intervalMs) return false;
            Interlocked.Exchange(ref _lastPrintMs, nowMs);
            return true;
        }

        /// <summary>连接/入包边界拒绝（一次计入 <see cref="Rejects"/>）。</summary>
        public void NoteReject() => Interlocked.Increment(ref _rejects);

        /// <summary>重连服务成功一次。</summary>
        public void NoteReconnectServed() => Interlocked.Increment(ref _reconnectsServed);

        /// <summary>建房被拒（容量）。</summary>
        public void NoteRoomRejectedAtCapacity() => Interlocked.Increment(ref _roomsRejectedAtCapacity);

        /// <summary>建房被拒（坏配置/模板缺失）。</summary>
        public void NoteRoomRejectedBadConfig() => Interlocked.Increment(ref _roomsRejectedBadConfig);

        /// <summary>终态房间销毁。</summary>
        public void NoteRoomDestroyed() => Interlocked.Increment(ref _roomsDestroyed);

        /// <summary>排空期新进房被拒（§12 第 2 步）。</summary>
        public void NoteRejectWhileDraining() => Interlocked.Increment(ref _rejectsWhileDraining);

        /// <summary>排空超时强制关闭一个房间（§12 第 3 步）。</summary>
        public void NoteRoomDrainTimedOut() => Interlocked.Increment(ref _roomsDrainTimedOut);

        /// <summary>Worker 形态下被合并节流跳过一条 Tick。</summary>
        public void NoteTickCoalesced() => Interlocked.Increment(ref _ticksCoalesced);

        /// <summary>票据拒绝总数（分类计数由 <see cref="NoteTicketRejection"/> 一并前推）。</summary>
        public void NoteTicketRejected() => Interlocked.Increment(ref _ticketRejected);

        /// <summary>
        /// 票据拒绝分类计数（P4 收口）：总数 + 分类一次前推。
        /// 调用方只传枚举，**分类到计数的映射只有这一份**（原先散在宿主的 switch 里）。
        /// </summary>
        public void NoteTicketRejection(JoinTicketRejection reason)
        {
            Interlocked.Increment(ref _ticketRejected);
            switch (reason)
            {
                case JoinTicketRejection.BadSignature: Interlocked.Increment(ref _ticketRejectedBadSignature); break;
                case JoinTicketRejection.Expired: Interlocked.Increment(ref _ticketRejectedExpired); break;
                case JoinTicketRejection.Replayed: Interlocked.Increment(ref _ticketRejectedReplayed); break;
                case JoinTicketRejection.UnknownKey: Interlocked.Increment(ref _ticketRejectedUnknownKey); break;
            }
        }

        /// <summary>会话容量拒绝一条连接。</summary>
        public void NoteSessionRejected() => Interlocked.Increment(ref _sessionsRejected);

        /// <summary>周期清理移除的会话数（累加本次清理结果）。</summary>
        public void NoteSessionsCleaned(int removed) => Interlocked.Add(ref _sessionsCleaned, removed);

        /// <summary>Match 状态迁移事件（含幂等忽略与非法拒绝）。</summary>
        public void NoteMatchStateChanged() => Interlocked.Increment(ref _matchStateChanges);

        /// <summary>SettlementReady 产出一次。</summary>
        public void NoteSettlementReady() => Interlocked.Increment(ref _settlementsReady);

        /// <summary>结算入盒落盘一次。</summary>
        public void NoteSettlementJournaled() => Interlocked.Increment(ref _settlementsJournaled);

        /// <summary>结算入盒幂等命中一次。</summary>
        public void NoteSettlementOutboxDuplicate() => Interlocked.Increment(ref _settlementsOutboxDuplicates);

        /// <summary>结算入盒容量满拒一次。</summary>
        public void NoteSettlementOutboxRejected() => Interlocked.Increment(ref _settlementsOutboxRejected);

        /// <summary>结算入盒介质写失败一次。</summary>
        public void NoteSettlementOutboxFailed() => Interlocked.Increment(ref _settlementsOutboxFailed);

        /// <summary>席位恢复完成一次（Restoring → Active）。</summary>
        public void NoteRestoreCompleted() => Interlocked.Increment(ref _restoresCompleted);

        /// <summary>坏包拒绝一次（未知类型/截断/proto 解析失败）。</summary>
        public void NotePacketRejected() => Interlocked.Increment(ref _packetRejects);

        /// <summary>超长入包拒绝一次（解析前长度上限）。</summary>
        public void NotePacketOversized() => Interlocked.Increment(ref _packetOversized);

        /// <summary>
        /// 和解上报（P4 收口）：总数递增 + 只记**最大帧号**——"最后在哪一帧发生过不一致"是比较语义，
        /// 原先由调用方写 `if (report.Frame > LastMismatchFrame) LastMismatchFrame = report.Frame;`（读-比-写三步，
        /// 多线程下会丢最大值）。
        /// </summary>
        public void NoteMismatch(long frame)
        {
            Interlocked.Increment(ref _mismatchReports);
            int target = frame > int.MaxValue ? int.MaxValue : (int)frame;
            int current = Volatile.Read(ref _lastMismatchFrame);
            while (target > current)
            {
                int prev = Interlocked.CompareExchange(ref _lastMismatchFrame, target, current);
                if (prev == current) break;
                current = prev;
            }
        }

        /// <summary>ACK 验证结果归类记账（会话只返回结果，计数归宿主；可被房间 Worker 线程调用）。</summary>
        public void CountAck(AckResult result)
        {
            switch (result)
            {
                case AckResult.RejectedStale: Interlocked.Increment(ref _ackRejectedStale); Interlocked.Increment(ref _ackRejected); break;
                case AckResult.RejectedFuture: Interlocked.Increment(ref _ackRejectedFuture); Interlocked.Increment(ref _ackRejected); break;
                case AckResult.RejectedEvicted: Interlocked.Increment(ref _ackRejectedEvicted); Interlocked.Increment(ref _ackRejected); break;
            }
        }

        /// <summary>
        /// 被输入闸门接受的包计数（输入包 + ACK 观测同一落点前推）。
        /// 宿主 owner 形态与 Worker 执行形态共用；Worker 形态可从多个房间线程调用。
        /// </summary>
        public void CountAcceptedInput()
        {
            Interlocked.Increment(ref _inputPackets);
            Interlocked.Increment(ref _ackObserved);
        }

        private readonly StringBuilder _sb = new StringBuilder(512);

        /// <summary>
        /// 周期汇总（房间号/帧号/快照/输入/和解率/背压/回溯/节拍债——一行式，便于日志抓取）。
        /// **逐房间**调用：多房间下按房间出一行，才能看出是哪个房间慢/过载（§514 隔离观测前提）。
        /// <paramref name="roomCount"/>、<paramref name="loop"/> 与 <paramref name="limiter"/> 属宿主/Worker 级——
        /// 只在首行给（<paramref name="limiter"/> = 限流计数，单源在 <see cref="RateLimiter"/>）。
        /// <paramref name="outboundRejected"/> = 本房间出站回传被拒数（在 <see cref="RoomInstance"/> 上；
        /// 原先该计数只增不读——P4 接进本行，观测面才闭环）。
        /// </summary>
        public string Format(string roomId, RoomRuntime room, SnapshotPipeline pipeline,
            SessionManager sessions, int roomCount, ServerLoop.LoopStats loop = null,
            RateLimiter limiter = null, long outboundRejected = 0)
        {
            _sb.Clear();
            _sb.Append("[Ops] rooms=").Append(roomCount)
               .Append(" room=").Append(roomId)
               .Append(" frame=").Append(room.AuthSim.Frame)
               .Append(" steps=").Append(room.StepsCount)
               .Append(" sessions=").Append(sessions.Count)
               .Append(" players=").Append(room.NextPlayerId)
               .Append(" | snap: sent=").Append(pipeline.SnapshotSent)
               .Append(" full=").Append(pipeline.SnapshotFullSent)
               .Append(" deltaSlots(last)=").Append(pipeline.Differ.LastDeltaCount)
               .Append(" | in: pkts=").Append(InputPackets)
               .Append(" acc=").Append(room.Gate.AcceptedCount)
               .Append(" drop(dup/range/stale/ack/btn)=")
               .Append(room.Gate.DroppedDuplicateFrame).Append('/')
               .Append(room.Gate.DroppedOutOfRange).Append('/')
               .Append(room.Gate.DroppedStaleFrame).Append('/')   // P4：原首位是恒零死计数（分类已并入 range），改列活的"已消费帧"
               .Append(room.Gate.DroppedAckSnapshot).Append('/')
               .Append(room.Gate.DroppedIllegalButtons)
               .Append(" | reconcile: reports=").Append(MismatchReports)
               .Append(" | lagcomp: fire=").Append(room.FireInputsProcessed)
               .Append(" comp=").Append(room.LagComp.CompensatedCount)
               .Append(" degr=").Append(room.LagComp.DegradedCount)
               .Append(" | bp: throttled=").Append(pipeline.BackpressureThrottled)
               .Append(" outRej=").Append(outboundRejected)
               .Append(" | match: phase=").Append(room.Phase)
               .Append(" transitions=").Append(MatchStateChanges)
               .Append(" settled=").Append(SettlementsReady)
               .Append(" obx(ok/dup/rej/fail)=").Append(SettlementsJournaled).Append('/')
               .Append(SettlementsOutboxDuplicates).Append('/')
               .Append(SettlementsOutboxRejected).Append('/')
               .Append(SettlementsOutboxFailed)
               .Append(" restored=").Append(RestoresCompleted)
               .Append(" | edge: rejects=").Append(Rejects)
               .Append(" sessRej=").Append(SessionsRejected)
               .Append(" sessClean=").Append(SessionsCleaned)
               .Append(" pktBad=").Append(PacketRejects)
               .Append(" pktBig=").Append(PacketOversized)
               .Append(" roomsFull=").Append(RoomsRejectedAtCapacity)
               .Append(" roomsBad=").Append(RoomsRejectedBadConfig)
               .Append(" roomsDown=").Append(RoomsDestroyed)
               .Append(" drainRej=").Append(RejectsWhileDraining)
               .Append(" drainTimeout=").Append(RoomsDrainTimedOut)
               .Append(" tickCoalesced=").Append(TicksCoalesced)
               .Append(" ackBad=").Append(AckRejected)
               .Append("(stale=").Append(AckRejectedStale)
               .Append(" future=").Append(AckRejectedFuture)
               .Append(" evict=").Append(AckRejectedEvicted).Append(')')
               .Append(" tktBad=").Append(TicketRejected)
               .Append("(bad=").Append(TicketRejectedBadSignature)
               .Append(" exp=").Append(TicketRejectedExpired)
               .Append(" rply=").Append(TicketRejectedReplayed)
               .Append(" kid=").Append(TicketRejectedUnknownKey).Append(')');
            if (limiter != null)
                _sb.Append(" | rl: ipConn=").Append(limiter.RejectedIpConnect)
                   .Append(" ipEntry=").Append(limiter.RejectedIpEntry)
                   .Append(" acct=").Append(limiter.RejectedAccountEntry)
                   .Append(" sess=").Append(limiter.RejectedSessionPackets)
                   .Append(" tblFull=").Append(limiter.RejectedTableFull)
                   .Append(" purged=").Append(limiter.BucketsPurged)
                   .Append(" buckets=").Append(limiter.BucketCount);
            if (loop != null)
                _sb.Append(" | loop: ticks=").Append(loop.Ticks)
                   .Append(" debt=").Append(loop.DroppedTimeMs).Append("ms")
                   .Append(" abandoned=").Append(loop.CatchUpAbandoned)
                   .Append(" pumpFail=").Append(loop.PumpFailures);   // >0 = 权威帧体在抛且已被吸收（P0-2）
            return _sb.ToString();
        }
    }
}
