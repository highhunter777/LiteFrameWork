using System;
using System.Collections.Generic;
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

        /// <summary>Worker 驱动去重标记（0 = 无待驱动，1 = 已登记/正在驱动；见 ServerHost.ScheduleRoomPump）。</summary>
        public int PumpScheduled;

        /// <summary>
        /// 房间 Worker 在途驱动数（0/1）。Worker 在驱动体入口递增、**在重新登记之后**递减——
        /// 宿主据此与 <see cref="PumpScheduled"/> 配合判定房间静默（销毁前提，见 ServerHost.TryDestroyTerminalRoom）。
        /// </summary>
        public int WorkerInFlight;

        /// <summary>在途 Tick 标记（合并节流：宿主每 Pump 至多入盒一条 Tick，Worker 执行后清零）。</summary>
        public int PendingTicks;

        /// <summary>Worker 驱动入口（缓存委托，避免每帧分配；仅 Worker 执行形态装配）。</summary>
        public Action DriveAction;

        /// <summary>Worker 侧命令输出复用缓冲（每房间一份：房间 Worker 单线程写，宿主不碰）。</summary>
        public readonly List<RoomOutput> WorkerOutputs = new List<RoomOutput>();

        /// <summary>
        /// 迟到 Join 的回滚待办标记（宿主写读）：回传应用发现"座位已占但发起连接已不在"时置位，
        /// 由宿主在后续 Pump 中尝试回收动态预留（见 ServerHost.TryRollbackStaleReservation）。
        /// </summary>
        public int StaleJoinRollbackPending;

        /// <summary>出站回传序号（Worker 写，宿主读；诊断用，不参与语义）。</summary>
        public long OutboundSequence;

        /// <summary>Outbound lane 满/关闭导致的回传拒绝数（Worker 递增；宿主观测）。</summary>
        public long OutboundRejected;

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

        /// <summary>
        /// 排空截止时刻（单调毫秒；-1 = 未在排空）。由 <see cref="ServerHost.BeginDrain"/> 置位，
        /// 到点仍未终态则由 Pump 发 Shutdown 命令（§12 第 3 步）。
        /// </summary>
        public long DrainDeadlineMs = -1;

        /// <summary>
        /// 排空超时 Shutdown 是否已投递（0/1，宿主写读）。Worker 执行形态下到达截止时刻后
        /// 宿主每 Pump 都会看到"未终态"，不设标记就会每帧重复发 Shutdown——既放大超时计数，
        /// 也让控制 lane 被重复命令挤占（重复关闭在 Runtime 幂等，但投递只需一次）。
        /// </summary>
        public int DrainTimeoutIssued;

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

        public RoomInstance(RoomConfig config, CombatValues? combat = null, WeaponTable weapons = null)
            : this(config, DefaultMailboxCapacity, false, combat, weapons)
        {
        }

        /// <summary>以统一容量创建房间 Mailbox；三条 lane 各自拥有该容量。</summary>
        public RoomInstance(RoomConfig config, int mailboxCapacity, CombatValues? combat = null, WeaponTable weapons = null)
            : this(config, mailboxCapacity, false, combat, weapons)
        {
        }

        public RoomInstance(RoomConfig config, int mailboxCapacity, bool isDynamic, CombatValues? combat = null,
            WeaponTable weapons = null)
            : this(config, mailboxCapacity, mailboxCapacity, mailboxCapacity, isDynamic, combat, weapons)
        {
        }

        /// <summary>
        /// 以独立 lane 容量创建房间 Mailbox。宿主配置当前只有一个统一容量，保留此
        /// 重载供后续按优先级配置输入/控制/出站配额。
        /// </summary>
        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            CombatValues? combat = null, WeaponTable weapons = null)
            : this(config, inputCapacity, controlCapacity, outboundCapacity, false, combat, weapons)
        {
        }

        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            bool isDynamic, CombatValues? combat = null, WeaponTable weapons = null)
            : this(config, inputCapacity, controlCapacity, outboundCapacity, isDynamic, false, combat, weapons)
        {
        }

        /// <summary>Worker 执行形态：管线吃房间本地的广播席位槽（连接事实来自 Runtime 席位）。</summary>
        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            bool isDynamic, bool workerExecution, CombatValues? combat = null, WeaponTable weapons = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Config = config;
            RoomId = config.RoomId;
            IsDynamic = isDynamic;
            WorkerExecution = workerExecution;
            // 非确定性身份由宿主装配；Runtime 只消费已给定身份，固定 Seed 的新局也不得复用结算幂等键。
            Runtime = new RoomRuntime(config, combat, weapons, config.RoomId + ":" + Guid.NewGuid().ToString("N"));
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
