using System;
using System.Collections.Generic;
using System.Threading;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>
    /// 一个房间的**全部宿主侧状态**（《商业级通用服务端框架总设计》§6"一个 roomId 只能映射一个独立
    /// RoomActor"、§8.2"一个 Worker 顺序驱动多个 RoomActor"）。
    ///
    /// 为什么独立成类：RoomRuntime / 快照管线 / 席位表若并列在 <see cref="ServerHost"/> 上，
    /// "每房间一份"在类型上无处表达——加第二个房间就得让每个字段各复制一遍。
    /// 收成一个对象后，多房间 = 一张 roomId → RoomInstance 的表，"每房间一份"由类型保证。
    ///
    /// **单线程约束**：本类不自我同步。驱动它的执行者必须单线程（§8.2"每个 RoomActor 仍是单线程"）
    /// ——与 <see cref="RoomRuntime"/> 同一纪律。两种执行形态：
    /// - **宿主 owner 形态**（<see cref="WorkerExecution"/> = false）：宿主主线程驱动，
    ///   会话即广播席位视图（管线吃 <see cref="Seats"/>）；
    /// - **Worker 执行形态**（true）：hash 归属的 Worker 驱动房间命令与广播，
    ///   <see cref="WorkerSeats"/> 是房间本地的广播席位槽（连接事实由 Worker 从 Runtime 席位刷新），
    ///   产出经 Mailbox Outbound lane 回传宿主应用/发送。
    ///
    /// **所有权**：<see cref="Seats"/> 的会话引用由宿主在席位变更时写入（见
    /// <see cref="ServerHost"/> 的 ApplySignal / HandleReconnect）；房间本身不创建也不释放会话。
    /// </summary>
    internal sealed class RoomInstance
    {
        /// <summary>未显式传容量时的每房间三 lane 默认容量，与宿主配置默认值保持一致。</summary>
        public const int DefaultMailboxCapacity = 1024;

        /// <summary>房间号（= <see cref="RoomConfig.RoomId"/>；路由键，创建后不变）。</summary>
        public readonly string RoomId;

        /// <summary>是否由动态 Join 预留；仅这类尚未入局的空房可在 admission 失败时回滚。</summary>
        public readonly bool IsDynamic;

        /// <summary>是否为 Worker 执行形态（房间命令/广播在归属 Worker 上执行，产出经 Outbound 回传）。</summary>
        public readonly bool WorkerExecution;

        /// <summary>权威运行时（Sim + 闸门 + 席位）。</summary>
        public readonly RoomRuntime Runtime;

        /// <summary>快照管线（增量差分 + 广播节流 + 背压降档）。</summary>
        public readonly SnapshotPipeline Pipeline;

        /// <summary>席位 → 连接会话映射（广播与信号按此定位连接；与 Runtime 的席位一一对应）。
        /// 仅宿主线程读写（admission/重连/销毁）。</summary>
        public readonly Session[] Seats;

        /// <summary>
        /// Worker 执行形态的广播席位槽（宿主 owner 形态为 null——那种形态管线直接吃 <see cref="Seats"/>）。
        /// 连接号/代次/断开事实由归属 Worker 从 Runtime 席位与本房间命令流刷新（单线程写）。
        /// </summary>
        public readonly RoomBroadcastSeat[] WorkerSeats;

        /// <summary>本房间的固定配置（创建时定，此后不变——§P0-5/§4"房间创建时固定不可变玩法配置"）。</summary>
        public readonly RoomConfig Config;

        /// <summary>
        /// 房间宿主消息盒。入站 Control/Input 已由 ServerHost 按房间路由；宿主 owner 形态下由宿主
        /// 单线程消费，Worker 执行形态下由归属 Worker 消费并把产出**回传**到 Outbound lane
        /// （宿主是 Outbound 的唯一消费者）。
        /// </summary>
        public readonly RoomMailbox<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope> Mailbox;

        // ---- 宿主 ↔ Worker 同步协议（P4：状态私有，只经下列方法访问）----
        //
        // **为什么不再用 public 字段**：这组值**跨线程**（宿主主线程 ↔ 归属 Worker），同步原语必须与
        // 状态绑死。字段公开时，"这里该用 Interlocked 还是裸 ++、谁写谁读"只能靠注释纪律——编译器不设防
        // （审计 §五实例：`OutboundSequence` 裸写、同类字段部分 Interlocked 部分裸 ++）。
        // 收成方法后：**哪些操作是原子 RMW**（CAS/Inc/Exchange）、**哪些值只有单侧线程可见**（普通读写），
        // 由 API 与文档承载；改名/漏改会编译不过，而不是静默退化。

        private int _pumpScheduled;                 // 0/1（宿主登记 ↔ Worker 复位，双向 CAS）
        private int _workerInFlight;                // 0/1（Worker 写，宿主判静默时读）
        private int _pendingTicks;                  // 0/1（宿主入盒，Worker 消费）
        private readonly List<RoomOutput> _workerOutputs = new List<RoomOutput>();
        private int _staleJoinRollbackPending;      // 宿主自写自读（迟到 Join 回收待办）
        private long _outboundSequence;             // Worker 写、宿主读（诊断）
        private long _outboundRejected;             // Worker 递增、宿主观测
        private long _drainDeadlineMs = -1;         // 宿主自写自读（-1 = 未排空）
        private int _drainTimeoutIssued;            // 宿主自写自读（超时 Shutdown 只投一次）

        /// <summary>
        /// Worker 驱动入口（缓存委托，避免每帧分配；仅 Worker 执行形态装配，装配后不再变）。
        /// 读取即 <c>!= null</c> 判定"本房间是否已绑定驱动"。
        /// </summary>
        public Action DriveAction { get; private set; }

        /// <summary>绑定 Worker 驱动入口（建房时一次性；重复绑定 = 装配错误，直接抛）。</summary>
        public void BindDriveAction(Action drive)
        {
            if (drive == null) throw new ArgumentNullException(nameof(drive));
            if (DriveAction != null)
                throw new InvalidOperationException("房间驱动入口已绑定（每房间只绑定一次——重复绑定会静默丢弃旧委托）");
            DriveAction = drive;
        }

        /// <summary>
        /// 尝试登记一次房间驱动（0→1）。返回 false = **已有在途驱动**，调用方直接跳过
        /// （见 ServerHost.ScheduleRoomPump：去重靠这次 CAS，不靠调用方先读后写）。
        /// </summary>
        public bool TrySchedulePump() => Interlocked.CompareExchange(ref _pumpScheduled, 1, 0) == 0;

        /// <summary>撤销登记（池已停/队列满）——留给下一次命令再唤醒。</summary>
        public void ClearPumpScheduled() => Interlocked.Exchange(ref _pumpScheduled, 0);

        /// <summary>驱动体收尾复位（Worker 侧）：登记与在途计数一并归零，房间才可能被判定静默。</summary>
        public void CompletePump()
        {
            Interlocked.Exchange(ref _pumpScheduled, 0);
            Interlocked.Decrement(ref _workerInFlight);
        }

        /// <summary>进入 Worker 驱动体（在途计数 +1）。</summary>
        public void EnterWorkerDrive() => Interlocked.Increment(ref _workerInFlight);

        /// <summary>
        /// 是否仍有在途驱动（登记位或在途计数非零）——**销毁前提**的静默判据
        /// （见 ServerHost.TryDestroyTerminalRoom）。两个值都在这里读，避免调用方各读各的漏一个。
        /// </summary>
        public bool HasPendingWork
        {
            get
            {
                return Volatile.Read(ref _pumpScheduled) != 0 || Volatile.Read(ref _workerInFlight) != 0;
            }
        }

        /// <summary>
        /// 合并节流：宿主每 Pump 至多入盒一条 Tick。返回 false = 本 Pump 已有在途 Tick，跳过。
        /// </summary>
        public bool TryQueueTick() => Interlocked.CompareExchange(ref _pendingTicks, 1, 0) == 0;

        /// <summary>消费 Tick 标记（Worker 执行后清零；也用于丢弃未入盒的那条）。</summary>
        public void ClearPendingTick() => Interlocked.Exchange(ref _pendingTicks, 0);

        /// <summary>
        /// 跑一条房间命令并把产出留在本房间的输出缓冲里（**Worker 侧单线程**：缓冲的 Clear 与 Execute
        /// 必须成对且无重入，故收进此处而不暴露缓冲本身）。返回的列表即本房间的复用缓冲，
        /// 调用方应当次即刻消费、不得留存。
        /// </summary>
        public IReadOnlyList<RoomOutput> ExecuteCommand(RoomCommand command)
        {
            _workerOutputs.Clear();
            Runtime.Execute(command, _workerOutputs);
            return _workerOutputs;
        }

        /// <summary>取下一个出站回传序号（诊断用；原为裸 <c>++</c>，P4 收进类内）。</summary>
        public long NextOutboundSequence() => Interlocked.Increment(ref _outboundSequence);

        /// <summary>累计出站回传拒绝数（Outbound lane 满/关闭；Worker 递增）。</summary>
        public long OutboundRejectedCount => Interlocked.Read(ref _outboundRejected);

        /// <summary>记一次出站回传被拒（lane 满/关闭）。</summary>
        public void NoteOutboundRejected() => Interlocked.Increment(ref _outboundRejected);

        /// <summary>置迟到 Join 的回滚待办（宿主写读）——见 ServerHost.TryRollbackStaleReservation。</summary>
        public void MarkStaleJoinRollback() => _staleJoinRollbackPending = 1;

        /// <summary>是否有待办的迟到 Join 回收（宿主 Pump 的快速判据）。</summary>
        public bool StaleJoinRollbackPending => _staleJoinRollbackPending != 0;

        /// <summary>清除迟到 Join 待办（回收尝试结束后，无论成败——失败留待下次命令再唤醒）。</summary>
        public void ClearStaleJoinRollback() => _staleJoinRollbackPending = 0;

        /// <summary>
        /// 排空截止时刻（单调毫秒；-1 = 未在排空）。设置只取**更早**的截止时刻
        /// （重复 BeginDrain 不得把已开始的排空往后推）。
        /// </summary>
        public void SetDrainDeadline(long deadlineMs)
        {
            if (_drainDeadlineMs < 0 || deadlineMs < _drainDeadlineMs) _drainDeadlineMs = deadlineMs;
        }

        /// <summary>排空截止时刻（-1 = 未在排空）。</summary>
        public long DrainDeadlineMs => _drainDeadlineMs;

        /// <summary>
        /// 尝试登记"排空超时 Shutdown 已投递"（0→1 返回 true）。到点未终态时宿主每 Pump 都会看到，
        /// 不设标记就会每帧重复发 Shutdown——既放大超时计数，也挤占控制 lane
        /// （重复关闭在 Runtime 幂等，但投递只需一次）。
        /// </summary>
        public bool TryIssueDrainTimeout() => Interlocked.CompareExchange(ref _drainTimeoutIssued, 1, 0) == 0;

        /// <summary>三条 lane 使用的统一容量（细分容量可从 <see cref="MailboxCounts"/> 观测）。</summary>
        public int MailboxCapacity => Mailbox.InputCapacity;

        /// <summary>Input lane 容量。</summary>
        public int MailboxInputCapacity => Mailbox.InputCapacity;

        /// <summary>Control lane 容量。</summary>
        public int MailboxControlCapacity => Mailbox.ControlCapacity;

        /// <summary>Outbound lane 容量。</summary>
        public int MailboxOutboundCapacity => Mailbox.OutboundCapacity;

        /// <summary>三条 lane 当前排队数量快照。</summary>
        public RoomMailboxCounts MailboxCounts => Mailbox.Counts;

        /// <summary>累计成功入队条数。</summary>
        public long MailboxAcceptedCount => Mailbox.AcceptedCount;

        /// <summary>累计成功出队条数。</summary>
        public long MailboxDequeuedCount => Mailbox.DequeuedCount;

        /// <summary>累计因 lane 满而拒绝的入队条数。</summary>
        public long MailboxRejectedFullCount => Mailbox.RejectedFullCount;

        /// <summary>是否处于排空（§12 第 2 步：停止接受新 Join，已在对局内的继续跑）。</summary>
        public bool Draining
        {
            get { return DrainDeadlineMs >= 0; }
        }

        /// <summary>
        /// 本房间的席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。
        /// **按房间传**——多房间下快照管线必须看自己房间的席位，不能共用宿主的全局判定。
        /// 读 Runtime 席位相位：宿主 owner 形态由宿主调用；Worker 执行形态由归属 Worker 调用（同一单线程）。
        /// </summary>
        public bool SeatBroadcastable(int playerId)
        {
            return Runtime.TryGetSeat(playerId, out PlayerSession seat) && seat.Phase == SeatPhase.Active;
        }

        public RoomInstance(RoomConfig config, CombatValues? combat = null, WeaponTable weapons = null, uint gameplayDigestBase = 0)
            : this(config, DefaultMailboxCapacity, false, combat, weapons, gameplayDigestBase)
        {
        }

        /// <summary>以统一容量创建房间 Mailbox；三条 lane 各自拥有该容量。</summary>
        public RoomInstance(RoomConfig config, int mailboxCapacity, CombatValues? combat = null, WeaponTable weapons = null,
            uint gameplayDigestBase = 0)
            : this(config, mailboxCapacity, false, combat, weapons, gameplayDigestBase)
        {
        }

        public RoomInstance(RoomConfig config, int mailboxCapacity, bool isDynamic, CombatValues? combat = null,
            WeaponTable weapons = null, uint gameplayDigestBase = 0)
            : this(config, mailboxCapacity, mailboxCapacity, mailboxCapacity, isDynamic, combat, weapons, gameplayDigestBase)
        {
        }

        /// <summary>
        /// 以独立 lane 容量创建房间 Mailbox。宿主配置当前只有一个统一容量，保留此
        /// 重载供后续按优先级配置输入/控制/出站配额。
        /// </summary>
        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            CombatValues? combat = null, WeaponTable weapons = null, uint gameplayDigestBase = 0)
            : this(config, inputCapacity, controlCapacity, outboundCapacity, false, combat, weapons, gameplayDigestBase)
        {
        }

        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            bool isDynamic, CombatValues? combat = null, WeaponTable weapons = null, uint gameplayDigestBase = 0)
            : this(config, inputCapacity, controlCapacity, outboundCapacity, isDynamic, false, combat, weapons, gameplayDigestBase)
        {
        }

        /// <summary>Worker 执行形态：管线吃房间本地的广播席位槽（连接事实来自 Runtime 席位）。</summary>
        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            bool isDynamic, bool workerExecution, CombatValues? combat = null, WeaponTable weapons = null,
            uint gameplayDigestBase = 0)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Config = config;
            RoomId = config.RoomId;
            IsDynamic = isDynamic;
            WorkerExecution = workerExecution;
            // 非确定性身份由宿主装配；Runtime 只消费已给定身份，固定 Seed 的新局也不得复用结算幂等键。
            Runtime = new RoomRuntime(config, combat, weapons, config.RoomId + ":" + Guid.NewGuid().ToString("N"),
                testRules: null, gameplayDigestBase: gameplayDigestBase);
            Seats = new Session[Runtime.ExpectedPlayers];
            if (workerExecution)
            {
                WorkerSeats = new RoomBroadcastSeat[Runtime.ExpectedPlayers];
                for (int i = 0; i < WorkerSeats.Length; i++) WorkerSeats[i] = new RoomBroadcastSeat();
                Pipeline = new SnapshotPipeline(WorkerSeats);
            }
            else
            {
                Pipeline = new SnapshotPipeline(Seats);
            }
            Mailbox = new RoomMailbox<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope>(
                inputCapacity, controlCapacity, outboundCapacity);
        }
    }
}
