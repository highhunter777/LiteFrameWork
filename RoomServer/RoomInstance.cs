using System;
using RoomServer.Application;
using RoomServer.Runtime;

namespace RoomServer
{
    /// <summary>
    /// 一个房间的**全部宿主侧状态**（《商业级通用服务端框架总设计》§6"一个 roomId 只能映射一个独立
    /// RoomActor"、§8.2"一个 Worker 顺序驱动多个 RoomActor"）。
    ///
    /// 为什么独立成类：在此之前 RoomRuntime / 快照管线 / 席位表是 <see cref="ServerHost"/> 上的三个
    /// **并列字段**，"每房间一份"这件事在类型上无处表达——加第二个房间就得让每个字段各复制一遍。
    /// 收成一个对象后，多房间 = 一张 roomId → RoomInstance 的表，"每房间一份"由类型保证。
    ///
    /// **单线程约束**：本类不自我同步。驱动它的 Worker 必须单线程（§8.2"每个 RoomActor 仍是单线程"）
    /// ——与 <see cref="RoomRuntime"/> 同一纪律。当前实现由宿主主线程驱动（单 Worker）。
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

        /// <summary>权威运行时（Sim + 闸门 + 席位）。</summary>
        public readonly RoomRuntime Runtime;

        /// <summary>快照管线（增量差分 + 广播节流 + 背压降档）。</summary>
        public readonly SnapshotPipeline Pipeline;

        /// <summary>席位 → 连接会话映射（广播与信号按此定位连接；与 Runtime 的席位一一对应）。</summary>
        public readonly Session[] Seats;

        /// <summary>本房间的固定配置（创建时定，此后不变——§P0-5/§4"房间创建时固定不可变玩法配置"）。</summary>
        public readonly RoomConfig Config;

        /// <summary>
        /// 房间宿主消息盒。入站 Control/Input 已由 ServerHost 按房间路由并由 owner 消费；
        /// Outbound lane 为后续 Worker 输出回传保留，保证路由与房间状态一起表达"每房间一份"。
        /// </summary>
        public readonly RoomMailbox<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope> Mailbox;

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
        /// 到点仍未终态则由 Pump 强制关闭（§12 第 3 步）。
        /// </summary>
        public long DrainDeadlineMs = -1;

        /// <summary>是否处于排空（§12 第 2 步：停止接受新 Join，已在对局内的继续跑）。</summary>
        public bool Draining
        {
            get { return DrainDeadlineMs >= 0; }
        }

        /// <summary>
        /// 本房间的席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。
        /// **按房间传**——多房间下快照管线必须看自己房间的席位，不能共用宿主的全局判定。
        /// </summary>
        public bool SeatBroadcastable(int playerId)
        {
            return Runtime.TryGetSeat(playerId, out PlayerSession seat) && seat.Phase == SeatPhase.Active;
        }

        public RoomInstance(RoomConfig config)
            : this(config, DefaultMailboxCapacity, false)
        {
        }

        /// <summary>以统一容量创建房间 Mailbox；三条 lane 各自拥有该容量。</summary>
        public RoomInstance(RoomConfig config, int mailboxCapacity)
            : this(config, mailboxCapacity, false)
        {
        }

        public RoomInstance(RoomConfig config, int mailboxCapacity, bool isDynamic)
            : this(config, mailboxCapacity, mailboxCapacity, mailboxCapacity, isDynamic)
        {
        }

        /// <summary>
        /// 以独立 lane 容量创建房间 Mailbox。宿主配置当前只有一个统一容量，保留此
        /// 重载供后续按优先级配置输入/控制/出站配额。
        /// </summary>
        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity)
            : this(config, inputCapacity, controlCapacity, outboundCapacity, false)
        {
        }

        public RoomInstance(RoomConfig config, int inputCapacity, int controlCapacity, int outboundCapacity,
            bool isDynamic)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Config = config;
            RoomId = config.RoomId;
            IsDynamic = isDynamic;
            Runtime = new RoomRuntime(config);
            Seats = new Session[Runtime.ExpectedPlayers];
            Pipeline = new SnapshotPipeline(Seats);
            Mailbox = new RoomMailbox<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope>(
                inputCapacity, controlCapacity, outboundCapacity);
        }
    }
}
