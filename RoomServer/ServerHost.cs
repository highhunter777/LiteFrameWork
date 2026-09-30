using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Google.Protobuf;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 服务器宿主（《状态同步实施方案》§4.5 RoomServer 结构图顶层组装 + R1 《商业级通用服务端框架总设计》§7/§8）。
    ///
    /// R1 分层（现为 Application+Host 合体，R2 再拆 Generic Host/Options）：
    /// - **协议边界**：解码 <c>PacketCodec</c> → 校验 → 构造 <see cref="RoomCommand"/>；反向把
    ///   <see cref="RoomOutput"/> 映射为 proto 发送。RoomRuntime 完全不见 proto/Transport。
    /// - **会话**：<see cref="Session"/>（连接级记账/ACK 水位/背压）+ 席位（Runtime <see cref="PlayerSession"/>）
    ///   映射表 <c>_seatSessions</c>（playerId → 连接）。
    /// - **快照**：<see cref="SnapshotPipeline"/> 在每次权威步进后拉取 AuthSim 构建下发；
    ///   重连席位在恢复完成 ACK 前停在 Restoring（管线抑制其增量广播），恢复完成时整帧全量重锚广播链（§9.3 步骤 6）。
    ///
    /// 单循环（MVP 不拆 I/O 线程，§10.2——IO/Room Worker 解耦归 R2）：
    /// tick 顺序 TickIncoming → Host owner 排空每房间 Mailbox（Control → Input → Outbound）
    /// → 权威 Tick → 快照广播 → TickOutgoing；房间执行迁移到 Worker 仍归后续批次。
    ///
    /// 装配参数由 <see cref="RoomConfig"/> 提供；**buildHash = <see cref="BuildHash.Value"/>**
    /// （源码内容哈希，两端不一 = 逻辑/协议版本不同 → 拒绝进房）。
    ///
    /// **传输解耦**：构造参数是窄端口 <see cref="IRoomTransport"/>——换传输框架只需新写适配器 + 装配一行。
    /// **所有权**：宿主**接管**传入的传输（构造即 `Start`、`Dispose` 即释放）。
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        /// <summary>服务器版本锚点 = 源码内容哈希（批③ 从字面量串切换到生成器：Sim 或协议一改，握手即拒）。</summary>
        public const string ServerBuildHash = BuildHash.Value;
        public const long OpsIntervalMs = 5000;

        /// <summary>入包长度上限（字节）：《服务端总设计》§5 P0-3——protobuf 解析**之前**的硬边界。
        /// 入站合法包 = 冗余输入（4 帧 × ~50B）或 Join/重连信令，远小于此；超限按恶意包丢弃计数。</summary>
        public const int MaxInboundPacketBytes = 4096;

        /// <summary>Join 字段 UTF-8 字节上限（P0-3：所有字符串限制 UTF-8 字节数——防超长串打爆日志/内存）。</summary>
        public const int MaxRoomIdBytes = 64;
        public const int MaxTokenBytes = 256;
        public const int MaxBuildHashBytes = 128;

        private readonly IRoomTransport _transport;   // 窄端口（可替换性第二刀，2026-09-19 收口）
        private readonly SessionManager _sessions;
        private readonly ReconnectService _reconnects = new ReconnectService();
        private readonly Ops _ops = new Ops();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>
        /// Join 票据验证器（《服务端总设计》§P0-6；《框架先行》§5-4 必建接缝）。**null = 未装配**：
        /// 此时 token 只作原型级非空校验（R0 声明行为，历史用例不受影响）；
        /// 装配后**逐一验签**，失败即拒绝——<see cref="JoinTicketRejection"/> 分类进 Ops。
        /// </summary>
        private readonly IJoinTicketValidator _tickets;

        /// <summary>本服受众标识（票据 audience 比对；空 = 不做受众校验）。由装配方传入——本层不依赖 Meta 配置。</summary>
        private readonly string _audience;

        /// <summary>票据拒绝分类计数（OPS 输出；索引 = <see cref="JoinTicketRejection"/> 值）。</summary>
        private readonly int[] _ticketRejections = new int[16];

        /// <summary>会话周期清理节拍（600 tick ≈ 10s @60Hz）。</summary>
        public const int SessionCleanupIntervalTicks = 600;
        private int _ticksSinceCleanup;

        // ---- 房间表（§6"一个 roomId 只能映射一个独立 RoomActor"）----
        //
        // **为什么是表而不是字段**：2026-09-26 之前 Runtime / 快照管线 / 席位表是宿主上的三个并列
        // 字段，"每房间一份"在类型上无处表达；§116 记的"当前任意 roomId 指向同一 Room"即此。
        // 收成 RoomInstance 之后，多房间 = 这张表，"每房间一份"由类型保证。
        //
        // **容量**：§429 要求房间容量**可配 + 范围校验**，§520/§600 明确"不把估算值写死为事实"
        // "不在设计阶段虚构固定房间数"——故上限来自配置，达上限**拒绝新建**而非静默拒绝进房。
        private readonly Dictionary<string, RoomInstance> _roomTable = new Dictionary<string, RoomInstance>(StringComparer.Ordinal);

        /// <summary>当前房间列表（顺序 = 插入序，非稳定契约；用于 Pump/Ops 遍历）。</summary>
        private readonly List<RoomInstance> _rooms = new List<RoomInstance>();

        /// <summary>建房时把选定的 roomId 交给 ApplySignal 用（提交到输出应用之间无重入）。</summary>
        private RoomInstance _currentRoom;

        /// <summary>是否已进入排空（§12 第 1–2 步：不再接受新 Join／新房间）。</summary>
        private bool _draining;

        /// <summary>排空截止时刻（单调毫秒；-1 = 未排空）。</summary>
        private long _drainDeadlineMs = -1;

        /// <summary>
        /// 结算 Outbox（§11.3"本地持久 Outbox"；M0-c 后续批接入）。**null = 未装配**：
        /// SettlementReady 只计数＋日志（现状形态，历史用例不变）；装配后入盒（幂等/有界/失败
        /// 全部显式计数，不抛进权威循环），排空第 4 步经 <see cref="FlushSettlementOutbox"/> 收口。
        /// 宿主**接管**其生命周期（Dispose 释放）。
        /// </summary>
        private readonly ISettlementOutbox _settlementOutbox;

        /// <summary>
        /// R2 Worker Pool 生命周期接缝。固定池已装配并启动；当前入站 Control/Input 先进入
        /// 每房间 Mailbox，再由 Host owner 单线程消费。房间执行迁移到 Worker 仍归后续批次。
        /// 配置形态（<see cref="RoomServerConfig"/>）自动创建，测试/嵌入式形态可显式注入。
        /// </summary>
        private readonly RoomWorkerPool _workerPool;
        private long _workerFailures;

        /// <summary>
        /// 是否把入站命令先放入每房间 Mailbox。当前批次仍由 Host owner 单线程消费；
        /// 默认关闭以保留嵌入式/历史用例的直投语义，生产入口显式开启。
        /// </summary>
        private readonly bool _mailboxRouting;
        private readonly bool _drainMailboxesImmediately;
        private long _mailboxRejectedFull;
        private long _mailboxClosed;
        private long _mailboxStale;

        /// <summary>
        /// Control lane 满载时暂存断线事实，待 owner 消费已有控制命令后再按 FIFO 入盒。
        /// 断线不能直接旁路执行，否则会越过同房间其他连接已经排队的 Join/Rebind。
        /// </summary>
        private readonly object _pendingDisconnectGate = new object();
        private readonly List<PendingDisconnect> _pendingDisconnects = new List<PendingDisconnect>();

        private struct PendingDisconnect
        {
            public RoomInstance Room;
            public Session Session;
            public int ConnectionId;
            public long SessionEpoch;
        }

        private sealed class ReconnectTicketRaceException : Exception
        {
        }

        private readonly RoomServerConfig _serverConfig;

        /// <summary>房间容量上限（配置项；§429 范围校验在装载期完成）。</summary>
        public int MaxRooms
        {
            get { return _serverConfig?.MaxRooms ?? 1; }
        }
        /// <summary>命令输出复用缓冲与输入批量复用缓冲（热路径零分配）。</summary>
        private readonly List<RoomOutput> _outputs = new List<RoomOutput>();
        private readonly SimInputFrame[] _batchFrames = new SimInputFrame[ClientInputBatch.MaxFrames];
        private ClientInputBatch _inputBatch;

        private long _nowMs;
        private bool _disposed;

        public SessionManager Sessions => _sessions;

        /// <summary>首房间配置（兼容只读投影；多房间下"哪个房间"见 <see cref="Rooms"/> / <see cref="TryGetOrCreateRoom"/>）。</summary>
        public RoomConfig Config { get; }

        /// <summary>首房间运行时（兼容投影；等价 <c>TryGetOrCreateRoom(Config.RoomId)</c> 的结果）。</summary>
        public RoomRuntime Room => _rooms.Count > 0 ? _rooms[0].Runtime : null;

        /// <summary>首房间快照管线（兼容投影）。</summary>
        public SnapshotPipeline Pipeline => _rooms.Count > 0 ? _rooms[0].Pipeline : null;

        /// <summary>当前装配的 Worker Pool；未使用配置或显式注入时为 null。</summary>
        public RoomWorkerPool WorkerPool => _workerPool;

        /// <summary>Worker Pool 是否已启动。</summary>
        public bool WorkerPoolStarted => _workerPool != null && _workerPool.IsStarted;

        /// <summary>Worker Pool 是否已停止接收并完成停止。</summary>
        public bool WorkerPoolStopped => _workerPool != null && _workerPool.IsStopped;

        /// <summary>Worker Pool 当前待执行工作项数。</summary>
        public int WorkerPoolPending => _workerPool?.PendingCount ?? 0;

        /// <summary>Worker 工作项异常数；异常由池内回调吸收，不杀死 Worker 线程。</summary>
        public long WorkerFailures => Interlocked.Read(ref _workerFailures);

        /// <summary>是否启用了每房间 Mailbox 入站路由。</summary>
        public bool MailboxRoutingEnabled => _mailboxRouting;

        /// <summary>Mailbox 满载拒绝总数（各房间三 lane 合计）。</summary>
        public long MailboxRejectedFull => Interlocked.Read(ref _mailboxRejectedFull);

        /// <summary>Mailbox 已关闭后的入队拒绝总数。</summary>
        public long MailboxClosed => Interlocked.Read(ref _mailboxClosed);

        /// <summary>因连接会话已替换/失效而丢弃的迟到消息数。</summary>
        public long MailboxStale => Interlocked.Read(ref _mailboxStale);

        /// <summary>当前在册房间数（Ops/容量观测）。</summary>
        public int RoomCount
        {
            get { return _rooms.Count; }
        }

        /// <summary>当前房间号快照（诊断用；顺序 = 插入序）。</summary>
        public string[] RoomIds
        {
            get
            {
                var ids = new string[_rooms.Count];
                for (int i = 0; i < _rooms.Count; i++) ids[i] = _rooms[i].RoomId;
                return ids;
            }
        }

        public Ops Ops => _ops;

        /// <summary>
        /// 建宿主。<paramref name="roomServerConfig"/> 给出房间容量上限、动态建房模板与
        /// Worker Pool 默认装配参数；传入 <paramref name="workerPool"/> 可在纯 .NET 用例中显式注入。
        ///
        /// **不传 <paramref name="roomServerConfig"/> 时**退回"单房间预置"形态：只按
        /// <paramref name="config"/> 建一个房间、容量 1、拒绝任何别的 roomId——这是历史用例与
        /// 嵌入式用法的兼容通道，**不是**生产形态（生产走配置文件 + 动态建房）。
        /// 传入配置但未传 Worker Pool 时，宿主按配置创建并启动固定池；未传配置且未显式注入
        /// 时保持历史单循环形态（<see cref="WorkerPool"/> 为 null）。
        /// </summary>
        public ServerHost(IRoomTransport transport, RoomConfig config = null,
            IJoinTicketValidator ticketValidator = null, string audience = null,
            RoomServerConfig roomServerConfig = null, ISettlementOutbox settlementOutbox = null,
            RoomWorkerPool workerPool = null, bool mailboxRouting = false,
            bool drainMailboxesImmediately = true)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serverConfig = roomServerConfig;
            Config = config ?? RoomConfig.Default();
            _tickets = ticketValidator;
            _audience = audience ?? _serverConfig?.Audience ?? string.Empty;
            _settlementOutbox = settlementOutbox;
            _workerPool = workerPool ?? (_serverConfig != null
                ? new RoomWorkerPool(_serverConfig.WorkerCount, _serverConfig.MailboxCapacity, OnWorkerFailure)
                : null);
            _mailboxRouting = mailboxRouting;
            _drainMailboxesImmediately = mailboxRouting && drainMailboxesImmediately;

            // 预置房间：
            // - 显式给了 config 且未给 roomServerConfig → 单房间兼容形态，预置该房间；
            // - 两者都没给 → 完全默认的单房间；
            // - 给了 roomServerConfig 且 config 为 null → **不预置**：所有房间首次进房时按模板创建。
            //   （动态建房下预置一个房间会白占一格容量，且"哪个 roomId 该被预置"本身没有依据。）
            RoomInstance first = null;
            if (config != null) first = AddRoom(config);
            else if (_serverConfig == null) first = AddRoom(RoomConfig.Default());

            // 会话表容量按**全服潜在连接数**算（§9.2 会话容量上限）：
            // 配了配置 → 房间上限 × 最大房间人数 ×4（按单房间算会让第二个房间的连接被会话上限拒掉，
            // 多房间实测踩过）；未配 → 沿用 R1 口径 首房间 ×4。
            int sessionCapacity = _serverConfig != null
                ? 4 * _serverConfig.MaxRooms * _serverConfig.MaxExpectedPlayers
                : 4 * (first != null ? first.Runtime.ExpectedPlayers : 2);
            _sessions = new SessionManager(sessionCapacity);

            _transport.OnConnected += OnTransportConnected;
            _transport.OnData += OnTransportData;
            _transport.OnDisconnected += OnTransportDisconnected;
            try
            {
                _workerPool?.Start();
                _transport.Start(_serverConfig != null ? _serverConfig.Port : Config.Port);   // Start 必须显式调用——此前遗漏导致服务器不监听（握手全失败）
            }
            catch
            {
                // Transport 启动失败时回收已启动的池，避免 composition root 在构造异常后遗留后台线程。
                // 保留原始启动异常；清理本身失败不得遮蔽它。
                try { _workerPool?.Stop(drain: true); } catch { }
                try { _transport.Dispose(); } catch { }
                try { (_settlementOutbox as IDisposable)?.Dispose(); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Mailbox 路由阶段先停止接收并由 Host owner 排空，再按 Worker → Transport → Outbox
            // 逆序释放；这样 drain=false 的测试/嵌入式形态也不会在 Transport 已释放后残留出站。
            if (_mailboxRouting)
            {
                for (int i = 0; i < _rooms.Count; i++) _rooms[i].Mailbox.Complete();
                for (int i = 0; i < _rooms.Count; i++) DrainRoomMailbox(_rooms[i]);
            }
            try
            {
                _workerPool?.Stop(drain: true);
            }
            finally
            {
                try
                {
                    _transport.Dispose();
                }
                finally
                {
                    (_settlementOutbox as IDisposable)?.Dispose();   // 宿主接管（同 transport 所有权约定）
                }
            }
        }

        private void OnWorkerFailure(Exception ex)
        {
            Interlocked.Increment(ref _workerFailures);
            Console.WriteLine($"[Worker] 工作项异常已隔离：{ex.GetType().Name}");
        }

        private long NowMs() => _clock.ElapsedMilliseconds;

        private void OnTransportConnected(int connectionId)
        {
            _nowMs = NowMs();
            if (!_sessions.TryAddNew(connectionId, _nowMs, out _))
            {
                // R1 会话容量上限（§9.2）：超限不登记并断开——未认证连接不能把记账表撑成无界内存
                _ops.SessionsRejected++;
                _transport.Disconnect(connectionId);
            }
        }

        private void OnTransportDisconnected(int connectionId)
        {
            _nowMs = NowMs();
            string roomId = null;
            Session disconnectedSession = null;
            if (_sessions.TryGet(connectionId, out Session session))
            {
                session.Disconnected = true;   // 掉线不停帧（§4.5-2）：标记 + 席位保留（重连窗口内可重绑）
                roomId = session.RoomId;
                disconnectedSession = session;
            }
            // 按会话所在房间通报（§6 路由）；未进房的连接没有房间，通报无对象——幂等忽略。
            RoomInstance room = GetRoomInstance(roomId);
            if (room == null) return;
            RoomCommand command = RoomCommand.Disconnect(connectionId);
            if (!TrySubmitCommandTo(room, command, disconnectedSession) && _mailboxRouting)
            {
                // 断线是控制面事实：若同一连接已有排队 Join/Rebind，先清理其迟到命令，
                // 再重试入盒，避免直接旁路打破 Control FIFO。
                room.Mailbox.DiscardControls(e => e.ConnectionId == connectionId);
                if (!TrySubmitCommandTo(room, command, disconnectedSession))
                    QueuePendingDisconnect(room, disconnectedSession);
            }
        }

        private void QueuePendingDisconnect(RoomInstance room, Session session)
        {
            if (room == null || session == null) return;
            lock (_pendingDisconnectGate)
            {
                for (int i = 0; i < _pendingDisconnects.Count; i++)
                {
                    PendingDisconnect pending = _pendingDisconnects[i];
                    if (ReferenceEquals(pending.Room, room)
                        && pending.ConnectionId == session.ConnectionId
                        && pending.SessionEpoch == session.Epoch)
                        return;
                }
                _pendingDisconnects.Add(new PendingDisconnect
                {
                    Room = room,
                    Session = session,
                    ConnectionId = session.ConnectionId,
                    SessionEpoch = session.Epoch,
                });
            }
        }

        /// <summary>
        /// 重试控制队列满载时暂存的断线事实。只在入盒成功后移除；不做 owner 旁路，
        /// 因而仍保持同一 Control lane 的 FIFO。Mailbox 关闭时丢弃未处理的排队事实，
        /// 因为宿主已经进入释放阶段。
        /// </summary>
        private void FlushPendingDisconnects(RoomInstance onlyRoom = null)
        {
            PendingDisconnect[] pending;
            lock (_pendingDisconnectGate)
                pending = _pendingDisconnects.ToArray();

            for (int i = 0; i < pending.Length; i++)
            {
                PendingDisconnect item = pending[i];
                if (onlyRoom != null && !ReferenceEquals(onlyRoom, item.Room)) continue;

                if (item.Room == null || item.Room.Mailbox.IsClosed)
                {
                    RemovePendingDisconnect(item);
                    continue;
                }

                if (TrySubmitCommandTo(item.Room,
                    RoomCommand.Disconnect(item.ConnectionId), item.Session))
                    RemovePendingDisconnect(item);
            }
        }

        private void RemovePendingDisconnect(PendingDisconnect item)
        {
            lock (_pendingDisconnectGate)
            {
                for (int i = 0; i < _pendingDisconnects.Count; i++)
                {
                    PendingDisconnect current = _pendingDisconnects[i];
                    if (!ReferenceEquals(current.Room, item.Room)
                        || current.ConnectionId != item.ConnectionId
                        || current.SessionEpoch != item.SessionEpoch)
                        continue;
                    _pendingDisconnects.RemoveAt(i);
                    return;
                }
            }
        }

        private void OnTransportData(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            _nowMs = NowMs();

            // P0-3 包体硬边界：长度限制在 protobuf 解析**之前**（超长包不进解析器 = 不给"解析耗尽内存"留门）
            if (data.Count > MaxInboundPacketBytes)
            {
                _ops.PacketOversized++;
                return;
            }

            Session session = _sessions.GetOrAddOnFirstPacket(connectionId, _nowMs);
            if (session == null) return;
            if (session.Disconnected) return;
            session.Touch(_nowMs);

            if (!PacketCodec.TryDecode(data, out PacketType type, out IMessage message))
            {
                _ops.PacketRejects++;                                     // 未知类型/截断/坏 proto：计数（P0-3 统一拒绝并计数）
                return;
            }

            switch (type)
            {
                case PacketType.Join:
                    HandleJoin(session, (Proto.JoinRequest)message);
                    break;
                case PacketType.Input:
                    HandleInput(session, (InputMessage)message);
                    break;
                case PacketType.MismatchReport:
                    HandleMismatch(session, (Proto.MismatchReport)message);
                    break;
                case PacketType.ReconnectRequest:
                    HandleReconnect(session, (Proto.ReconnectRequest)message);
                    break;
                case PacketType.RestoreComplete:
                    HandleRestoreComplete(session);
                    break;
                case PacketType.Leave:
                    HandleLeave(session);
                    break;
            }
        }

        /// <summary>
        /// Join 信令：token 非空 + **房间号一致** + buildHash 必须等于服务器版本（版本红线）
        /// → 投递 <see cref="RoomCommand.Join"/> → JoinAck + 满员即 MatchStarted 广播。
        ///
        /// **安全能力现状（R0 声明 + 2026-09-26 票据接缝接入，《服务端总设计》§5 P0-6/R2 补齐）**：
        /// 装配了 <see cref="IJoinTicketValidator"/> 后，token 走**验签 + 六项绑定 + 重放窗口**
        /// （过期/篡改/重放/受众/房间/构建哈希，见 <see cref="JoinTicketValidatorTests"/>）；
        /// **未装配**时退回原型级非空校验（<see cref="Session.Principal"/> 为 null）。
        /// 两处**仍未**达标（公开部署前必须完成 R2）：kcp2k V1.41 cookie 只解决 UDP 探测/放大防护，
        /// **不提供业务身份、机密性或完整性**；重连票据仍由本服务自签且是**可预测串**（非 CSPRNG，
        /// 见 <see cref="ReconnectService"/>）。**在两者完成前本服务不得直接暴露公网**。
        /// 远端地址限流与安全信封同属 R2。
        /// </summary>
        private void HandleJoin(Session session, JoinRequest join)
        {
            if (session.PlayerId >= 0 || session.JoinPending) return;         // 重复/排队中的 Join 忽略

            // P0-3 字符串边界：UTF-8 字节数上限（先于一切语义；拒绝日志不回显字段内容）
            if (OverByteLimit(join.RoomId, MaxRoomIdBytes)
                || OverByteLimit(join.Token, MaxTokenBytes)
                || OverByteLimit(join.BuildHash, MaxBuildHashBytes))
            {
                Reject(session, "Join 字段超长");
                return;
            }

            if (string.IsNullOrEmpty(join.Token))                            // token 红线：空即拒绝
            {
                Reject(session, "token 缺失");
                return;
            }

            // 排空红线（§12 第 2 步"停止接受新 Join"）：置位后**任何**新进房都拒，含已在册房间。
            // 既有对局的输入/重连不受影响（各自走 HandleInput / HandleReconnect）。
            // 放在字段边界之后：超长包已按 PacketRejects 归类，不该在排空计数里再记一次。
            if (_draining)
            {
                _ops.RejectsWhileDraining++;      // Rejects 由 Reject() 记，此处只记排空专属分类
                Reject(session, "服务排空中：不接受新进房");
                return;
            }

            if (join.BuildHash != ServerBuildHash)                           // 版本红线：Sim/协议版本比对不符拒绝进房
            {
                Reject(session, $"buildHash 不符：{join.BuildHash} != {ServerBuildHash}");
                return;
            }

            // 已存在房间在票据验签前先做控制 lane admission 检查，避免队列满时消费
            // 一次性 Join nonce。新房间尚不存在，创建后 mailbox 为空，不会落入满拒分支。
            RoomInstance existingRoom = GetRoomInstance(join.RoomId);
            if (_mailboxRouting && existingRoom != null
                && (existingRoom.Mailbox.IsClosed
                    || existingRoom.Mailbox.ControlCount >= existingRoom.Mailbox.ControlCapacity))
            {
                if (existingRoom.Mailbox.IsClosed) Interlocked.Increment(ref _mailboxClosed);
                else Interlocked.Increment(ref _mailboxRejectedFull);
                Reject(session, "房间控制队列已满");
                return;
            }

            // 票据验证（§P0-6）：装配了验证器就**逐一验签**——非空不再构成准入理由。
            // 未装配（null）时保留 R0 声明的原型行为；生产装配必须传入验证器。
            //
            // **顺序：票据先于建房**。动态建房下若先建房再验票，任何人拿垃圾 token 打不同 roomId
            // 就能把房间表撑到容量上限（拒绝服务）。故票据的 roomId 绑定在**建房之前**比对。
            JoinPrincipal previousPrincipal = session.Principal;
            string previousBuildHash = session.BuildHash;
            JoinPrincipal validatedPrincipal = null;
            if (_tickets != null)
            {
                JoinPrincipal principal = _tickets.Validate(join.Token, new JoinContext(
                    join.RoomId, ServerBuildHash, _audience, _nowMs));
                if (principal == null || !principal.IsValid)
                {
                    JoinTicketRejection reason = principal == null
                        ? JoinTicketRejection.Malformed
                        : principal.Rejection;
                    CountTicketRejection(reason);
                    // 拒绝原因只打分类，**不打票据原文与字段值**（Meta 专项 §13.1 禁写 token/票据）
                    Reject(session, $"票据拒绝：{reason}");
                    return;
                }
                validatedPrincipal = principal;
            }

            // 房间解析/创建（§6"一个 roomId 只能映射一个独立 RoomActor"；重复 roomId 复用既有房间）
            RoomInstance room = existingRoom;
            bool createdRoom = false;
            if (room == null)
            {
                RoomRuntime created = OpenRoom(join.RoomId);
                if (created == null)
                {
                    // 空房号 / 达容量上限 / 模板缺失——一律拒绝进房（不静默排队、不静默建成别的配置）
                    Reject(session, string.IsNullOrEmpty(join.RoomId)
                        ? "房间号缺失"
                        : $"房间不可用：{join.RoomId}（在册 {_rooms.Count}/{MaxRooms}）");
                    return;
                }
                room = GetRoomInstance(join.RoomId);
                createdRoom = room != null;
            }

            RoomCommand command = RoomCommand.Join(session.ConnectionId);
            if (!_mailboxRouting)
            {
                session.Principal = validatedPrincipal;
                session.BuildHash = join.BuildHash;
                SubmitCommandTo(room, command, joinContext: session);
                return;
            }

            session.JoinPending = true;
            // 先完成队列准入，再写入可变 Session 身份字段；控制队列满不会吞票据/占房。
            if (!TrySubmitCommandTo(room, command, joinContext: session))
            {
                session.JoinPending = false;
                session.Principal = previousPrincipal;
                session.BuildHash = previousBuildHash;
                if (createdRoom) RemoveEmptyRoom(room);
                Reject(session, "房间控制队列不可用");
            }
            else
            {
                session.Principal = validatedPrincipal;
                session.BuildHash = join.BuildHash;
            }
        }

        /// <summary>票据拒绝分类计数（同时进 Ops 周期行——越界分类并入 Miscellaneous）。</summary>
        private void CountTicketRejection(JoinTicketRejection reason)
        {
            int i = (int)reason;
            _ticketRejections[i >= 0 && i < _ticketRejections.Length ? i : 0]++;

            _ops.TicketRejected++;
            switch (reason)
            {
                case JoinTicketRejection.BadSignature: _ops.TicketRejectedBadSignature++; break;
                case JoinTicketRejection.Expired: _ops.TicketRejectedExpired++; break;
                case JoinTicketRejection.Replayed: _ops.TicketRejectedReplayed++; break;
                case JoinTicketRejection.UnknownKey: _ops.TicketRejectedUnknownKey++; break;
            }
        }

        /// <summary>某拒绝分类的累计次数（Ops 观测）。</summary>
        public int TicketRejections(JoinTicketRejection reason)
        {
            int i = (int)reason;
            return i >= 0 && i < _ticketRejections.Length ? _ticketRejections[i] : 0;
        }

        private void HandleInput(Session session, InputMessage msg)
        {
            if (session.PlayerId < 0) return;

            // 直投兼容路径沿用历史时序；Mailbox 路径把 ACK 验证延后到 Host owner
            // 实际消费输入之后，避免队列满拒仍前推会话 ledger。
            if (!_mailboxRouting)
                _ops.CountAck(session.TryAcceptAck(msg.AckSnapshot));

            // proto → 纯数据（App 职责；Runtime 不见 proto）。Count 传原始条数——
            // 超冗余窗的整条拒绝由闸门判定（形状纪律不在 App 预筛，避免两处口径漂移）。
            int raw = msg.Frames.Count;
            int copy = raw <= ClientInputBatch.MaxFrames ? raw : ClientInputBatch.MaxFrames;
            for (int i = 0; i < copy; i++)
            {
                Proto.InputFrame f = msg.Frames[i];
                _batchFrames[i] = new SimInputFrame
                {
                    EntityId = f.EntityId,
                    MoveX = f.MoveX, MoveZ = f.MoveZ,
                    AimX = f.AimX, AimZ = f.AimZ,
                    Buttons = f.Buttons,
                    SelectedWeaponSlot = f.SelectedWeaponSlot,
                    TargetEntityId = f.TargetEntityId,
                    ActionSeq = f.ActionSeq,
                };
            }
            _inputBatch.Frame = msg.Frame;
            _inputBatch.AckSnapshot = msg.AckSnapshot;
            _inputBatch.ViewFrame = msg.ViewFrame;
            _inputBatch.Count = raw;
            _inputBatch.Frames = _batchFrames;

            // 输入按会话所在房间路由（§6：一个连接只属于一个房间）
            RoomInstance inputRoom = GetRoomInstance(session.RoomId);
            if (inputRoom == null) return;

            if (_mailboxRouting)
            {
                TrySubmitCommandTo(inputRoom, RoomCommand.ClientInput(session.PlayerId, _inputBatch), session);
                return;
            }

            long acceptedBefore = inputRoom.Runtime.Gate.AcceptedCount;
            SubmitCommandTo(inputRoom, RoomCommand.ClientInput(session.PlayerId, _inputBatch));
            if (inputRoom.Runtime.Gate.AcceptedCount > acceptedBefore)   // 只统计被闸门接受的包（取代旧 OnInputAccepted 回挂）
            {
                _ops.InputPackets++;
                _ops.AckObserved++;
            }
        }

        /// <summary>和解上报：只汇总计数（Ops 输出和解触发率）。</summary>
        private void HandleMismatch(Session session, Proto.MismatchReport report)
        {
            if (session.PlayerId < 0) return;
            _ops.MismatchReports++;
            if (report.Frame > _ops.LastMismatchFrame) _ops.LastMismatchFrame = report.Frame;
        }

        /// <summary>
        /// 重连（§5.6 首选路径 + §9.3 恢复顺序的服务器侧）：
        /// 一次性票据 → 校验（绑定房间号）→ 重绑席位（Restoring）→ 版本确认 + 权威快照 + 输入历史。
        /// 客户端恢复完成 ACK（RestoreComplete）到达前，席位停在 Restoring——管线抑制其增量广播。
        /// </summary>
        private void HandleReconnect(Session session, Proto.ReconnectRequest request)
        {
            if (_mailboxRouting)
            {
                HandleReconnectMailbox(session, request);
                return;
            }
            if (!_reconnects.TryConsume(request.OneTimeToken, out int playerId, out string roomId))
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }, reliable: true);
                return;
            }
            RoomInstance instance = GetRoomInstance(roomId);
            if (instance == null)                              // 票据绑定房间号（R1：原实现忽略该绑定）
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "票据与房间不符" }, reliable: true);
                return;
            }

            RoomRuntime room = instance.Runtime;
            if (!room.TryGetSeat(playerId, out PlayerSession seat))
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "席位不存在" }, reliable: true);
                return;
            }
            if (!room.Started)   // R1：重连只在 Running 有效（终态房间无增量可恢复）
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "房间不在进行中" }, reliable: true);
                return;
            }

            // 会话重挂：旧连接（若还在）标记断开，新连接接管席位（§9.2 重连原子绑定新 Connection）
            if (seat.ConnectionId >= 0 && seat.ConnectionId != session.ConnectionId
                && _sessions.TryGet(seat.ConnectionId, out Session old))
            {
                old.Disconnected = true;
            }
            SubmitCommandTo(instance, RoomCommand.Rebind(playerId, session.ConnectionId));   // 席位 → Restoring（增量广播抑制中）
            session.PlayerId = playerId;
            session.RoomId = roomId;
            instance.Seats[playerId] = session;

            var response = new Proto.ReconnectResponse { Ok = true };
            long viewerEntityId = room.EntityIdOf(playerId);
            // 重连响应里的快照是**独立探测**（不推进广播基线）：显式构造一份全量。
            // 分层顺序（§5.6/§9.3）：版本确认 → 公共全量（PackFull 槽位）→ 比赛状态（PackFull 内附）→ 本人私有状态 → 输入历史；
            // 客户端应用完毕发 RestoreComplete（§9.3 步骤 6），席位回 Active 后广播管线恢复投递。
            response.Seed = room.Seed;                         // §9.3 步骤 2：协议/Sim/配置版本确认（与 StartGame 同源）
            response.ConfigHash = room.FixedConfig.Digest;
            response.BuildHash = ServerBuildHash;
            response.Snapshot = SnapshotCodec.PackFull(room.AuthSim.Frame, room.AuthSim, room.Gate.LastAcceptedFrame(playerId));
            response.Snapshot.PrivateState = SnapshotCodec.PackPrivate(room.AuthSim, viewerEntityId);
            for (int f = room.AuthSim.Frame - SimConfig.MaxInputHistory + 1; f <= room.AuthSim.Frame; f++)
            {
                if (f <= 0) continue;
                if (room.HistoryFor(f, out SimInputFrame[] inputs))
                    // 重连补发是"每帧一条"的完整历史（不是丢包冗余窗），viewFrame 无意义填 0；
                    // 帧号字段随 Pack 写入，客户端按 frame 逐帧取用即可
                    response.History.Add(InputPacker.Pack(f, inputs, room.AuthSim.Frame, 0));
            }
            SendToSession(session, PacketType.ReconnectResponse, response, reliable: true);
            _ops.ReconnectsServed++;
        }

        /// <summary>
        /// Mailbox 路由下的重连 admission。先 peek 目标房间并检查控制 lane 容量，
        /// 再消费一次性票据；Rebind 经过 Control lane 后才提交 Session/Seats 与响应。
        /// 当前 Host owner 阶段即使关闭“回调末尾立即排空”，重连也会在本次 owner 调用
        /// 内排空该房间的控制消息，避免响应先于 Runtime 重绑。
        /// </summary>
        private void HandleReconnectMailbox(Session session, Proto.ReconnectRequest request)
        {
            if (!_reconnects.TryPeek(request.OneTimeToken, out int peekPlayerId, out string peekRoomId))
            {
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }, reliable: true);
                return;
            }

            RoomInstance instance = GetRoomInstance(peekRoomId);
            if (instance == null)
            {
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "票据与房间不符" }, reliable: true);
                return;
            }
            if (instance.Mailbox.IsClosed)
            {
                Interlocked.Increment(ref _mailboxClosed);
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "房间控制队列已关闭" }, reliable: true);
                return;
            }
            if (instance.Mailbox.ControlCount >= instance.Mailbox.ControlCapacity)
            {
                Interlocked.Increment(ref _mailboxRejectedFull);
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "房间控制队列已满" }, reliable: true);
                return;
            }

            RoomRuntime room = instance.Runtime;
            if (!room.TryGetSeat(peekPlayerId, out PlayerSession seat))
            {
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "席位不存在" }, reliable: true);
                return;
            }
            if (!room.Started)
            {
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "房间不在进行中" }, reliable: true);
                return;
            }
            int oldConnectionId = seat.ConnectionId;
            int playerId = peekPlayerId;
            string roomId = peekRoomId;

            // 在 mailbox 的容量锁内消费票据：满载/关闭会在 factory 之前返回，
            // 因而不会吞掉仍可重试的一次性票据。当前 Host owner 串行处理入站，
            // TryPeek 与此处 TryConsume 之间不存在另一个 owner admission。
            RoomCommand command = RoomCommand.Rebind(playerId, session.ConnectionId);
            RoomMailboxEnqueueResult admission;
            try
            {
                admission = instance.Mailbox.TryEnqueueControl(() =>
                {
                    if (!_reconnects.TryConsume(request.OneTimeToken,
                        out int consumedPlayer, out string consumedRoom)
                        || consumedPlayer != playerId
                        || !string.Equals(consumedRoom, roomId, StringComparison.Ordinal))
                        throw new ReconnectTicketRaceException();

                    return RoomControlEnvelope.FromCommand(
                        instance.RoomId, session.ConnectionId, playerId, session.Epoch,
                        _nowMs, command);
                });
            }
            catch (ReconnectTicketRaceException)
            {
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }, reliable: true);
                return;
            }

            if (admission != RoomMailboxEnqueueResult.Accepted)
            {
                if (admission == RoomMailboxEnqueueResult.RejectedFull)
                    Interlocked.Increment(ref _mailboxRejectedFull);
                else
                    Interlocked.Increment(ref _mailboxClosed);
                SendToSession(session, PacketType.ReconnectResponse,
                    new Proto.ReconnectResponse
                    {
                        Ok = false,
                        Reason = admission == RoomMailboxEnqueueResult.RejectedFull
                            ? "房间控制队列已满" : "房间控制队列已关闭"
                    }, reliable: true);
                return;
            }

            // 当前批次由 Host owner 消费；无论立即还是延迟装配，重连响应都必须
            // 在 Runtime 完成 Rebind 后发送，避免客户端先收到快照再发生重绑。
            DrainRoomMailbox(instance);

            // Runtime Rebind 已把 seat.ConnectionId 改成新连接；必须使用重绑前
            // 捕获的旧连接号，否则旧 Session 会继续被当作活跃连接。
            if (oldConnectionId >= 0 && oldConnectionId != session.ConnectionId
                && _sessions.TryGet(oldConnectionId, out Session old))
                old.Disconnected = true;
            session.PlayerId = playerId;
            session.RoomId = roomId;
            instance.Seats[playerId] = session;

            var response = new Proto.ReconnectResponse { Ok = true };
            long viewerEntityId = room.EntityIdOf(playerId);
            response.Seed = room.Seed;
            response.ConfigHash = room.FixedConfig.Digest;
            response.BuildHash = ServerBuildHash;
            response.Snapshot = SnapshotCodec.PackFull(room.AuthSim.Frame, room.AuthSim, room.Gate.LastAcceptedFrame(playerId));
            response.Snapshot.PrivateState = SnapshotCodec.PackPrivate(room.AuthSim, viewerEntityId);
            for (int f = room.AuthSim.Frame - SimConfig.MaxInputHistory + 1; f <= room.AuthSim.Frame; f++)
            {
                if (f <= 0) continue;
                if (room.HistoryFor(f, out SimInputFrame[] inputs))
                    response.History.Add(InputPacker.Pack(f, inputs, room.AuthSim.Frame, 0));
            }
            SendToSession(session, PacketType.ReconnectResponse, response, reliable: true);
            _ops.ReconnectsServed++;
        }

        /// <summary>恢复完成 ACK（§9.3 步骤 6）：席位 Restoring → Active——增量广播自此恢复该席位。</summary>
        private void HandleRestoreComplete(Session session)
        {
            if (session.PlayerId < 0) return;
            // 幂等：非 Restoring 时 Runtime 静默忽略。路由到会话所在房间。
            TrySubmitCommandTo(GetRoomInstance(session.RoomId), RoomCommand.RestoreAck(session.PlayerId), session);
        }

        private void HandleLeave(Session session)
        {
            RoomInstance room = GetRoomInstance(session.RoomId);
            RoomCommand command = RoomCommand.Disconnect(session.ConnectionId);
            bool accepted = TrySubmitCommandTo(room, command, session);
            if (accepted)
            {
                session.Disconnected = true;
            }
            else if (_mailboxRouting)
            {
                // Leave 与传输断线具有相同的控制面语义；满载时保留事实，
                // 等 owner 按 Control FIFO 重试，不静默丢失。
                session.Disconnected = true;
                QueuePendingDisconnect(room, session);
            }
        }

        private void SendToSession(Session session, PacketType type, IMessage message, bool reliable)
        {
            _transport.SendTo(session.ConnectionId, new ArraySegment<byte>(PacketCodec.Encode(type, message)), reliable);
        }

        private void Reject(Session session, string reason)
        {
            _ops.Rejects++;
            Console.WriteLine($"[Reject] conn {session.ConnectionId}: {reason}");   // 只打原因，不回显 token/payload（P0-3）
        }

        /// <summary>
        /// 当前 Host owner 对已消费命令执行 Runtime 并应用输出；入站命令的 Mailbox admission
        /// 与消费由上层路由负责，后续批次再把 Runtime 执行迁移到 Worker。
        /// <paramref name="joinContext"/> = 触发本次命令的进房连接（PlayerAdmitted 落位用）。
        /// </summary>
        private void SubmitCommand(in RoomCommand cmd, Session joinContext = null)
        {
            RoomInstance room = _currentRoom ?? (_rooms.Count > 0 ? _rooms[0] : null);
            if (room == null) return;
            _outputs.Clear();
            room.Runtime.Execute(cmd, _outputs);
            for (int i = 0; i < _outputs.Count; i++) ApplyOutput(_outputs[i], joinContext);
            _outputs.Clear();
        }

        /// <summary>
        /// 把命令提交到指定房间。Mailbox 路由阶段仍由当前 Host owner 消费；
        /// <paramref name="joinContext"/> 不跨队列保存，Join 输出应用时按连接代次重新解析。
        /// </summary>
        private bool TrySubmitCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (room == null) return false;

            if (_mailboxRouting)
            {
                int connectionId = cmd.ConnectionId;
                int playerId = cmd.PlayerId;
                long epoch = 0;
                if (joinContext != null)
                {
                    if (cmd.Kind == RoomCommandKind.Rebind)
                        connectionId = cmd.NewConnectionId;
                    else
                        connectionId = joinContext.ConnectionId;
                    if (cmd.Kind != RoomCommandKind.Rebind && cmd.Kind != RoomCommandKind.RestoreAck)
                        playerId = joinContext.PlayerId;
                    epoch = joinContext.Epoch;
                }
                else if (connectionId >= 0 && _sessions.TryGet(connectionId, out Session current))
                {
                    epoch = current.Epoch;
                    if (playerId < 0) playerId = current.PlayerId;
                }

                RoomMailboxEnqueueResult result;
                if (cmd.Kind == RoomCommandKind.ClientInput)
                {
                    RoomInputEnvelope envelope = RoomInputEnvelope.FromCommand(
                        room.RoomId, connectionId, playerId, epoch, _nowMs, cmd);
                    result = room.Mailbox.EnqueueInput(envelope);
                }
                else
                {
                    RoomControlEnvelope envelope = RoomControlEnvelope.FromCommand(
                        room.RoomId, connectionId, playerId, epoch, _nowMs, cmd);
                    result = room.Mailbox.EnqueueControl(envelope);
                }

                if (result != RoomMailboxEnqueueResult.Accepted)
                {
                    if (result == RoomMailboxEnqueueResult.RejectedFull)
                        Interlocked.Increment(ref _mailboxRejectedFull);
                    else
                        Interlocked.Increment(ref _mailboxClosed);
                    return false;
                }

                if (_drainMailboxesImmediately)
                    DrainRoomMailbox(room);
                return true;
            }

            ExecuteCommandTo(room, cmd, joinContext);
            return true;
        }

        /// <summary>兼容旧调用点的提交入口；路由失败由对应 admission 处理。</summary>
        private void SubmitCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (!TrySubmitCommandTo(room, cmd, joinContext)
                && _mailboxRouting && cmd.Kind != RoomCommandKind.ClientInput)
            {
                // 控制命令不能静默丢失；当前仍由同一 Host owner 执行，故在
                // Mailbox 关闭/满载时保留一个显式旁路兜底。入站 Join/Reconnect
                // 使用各自 admission 分支，不经过这里。
                ExecuteCommandTo(room, cmd, joinContext);
            }
        }

        private void ExecuteCommandTo(RoomInstance room, in RoomCommand cmd, Session joinContext = null)
        {
            if (room == null) return;
            RoomInstance saved = _currentRoom;
            _currentRoom = room;
            try
            {
                SubmitCommand(cmd, joinContext);
            }
            finally
            {
                _currentRoom = saved;
            }
        }

        /// <summary>
        /// Host owner 对单房间 Mailbox 的消费顺序：Control → Input → Outbound。
        /// 当前 Outbound 仅支持未来 Worker 输出回传，Runtime 仍在本 Host 执行。
        /// </summary>
        private void DrainRoomMailbox(RoomInstance room)
        {
            if (room == null) return;
            RoomMailboxMessage<RoomInputEnvelope, RoomControlEnvelope, RoomOutboundEnvelope> message;
            while (room.Mailbox.TryDequeue(out message))
            {
                switch (message.Lane)
                {
                    case RoomMailboxLane.Control:
                        DrainControlEnvelope(room, message.Control);
                        break;
                    case RoomMailboxLane.Input:
                        DrainInputEnvelope(room, message.Input);
                        break;
                    case RoomMailboxLane.Outbound:
                    {
                        RoomOutboundEnvelope envelope = message.Outbound;
                        RoomInstance saved = _currentRoom;
                        _currentRoom = room;
                        try { ApplyOutput(envelope.Output, null); }
                        finally { _currentRoom = saved; }
                        break;
                    }
                }
            }
        }

        /// <summary>只排空 Control lane，用于在重试暂存的断线事实前保留跨 lane 优先级。</summary>
        private void DrainRoomControlMailbox(RoomInstance room)
        {
            if (room == null) return;
            while (room.Mailbox.TryDequeueControl(out RoomControlEnvelope envelope))
                DrainControlEnvelope(room, envelope);
        }

        private void DrainControlEnvelope(RoomInstance room, RoomControlEnvelope envelope)
        {
            if (!IsCurrentEnvelope(envelope.RoomId, envelope.ConnectionId,
                envelope.PlayerId, envelope.SessionEpoch, envelope.Command.Kind))
            {
                if (envelope.Command.Kind == RoomCommandKind.AuthenticatedJoin
                    && _sessions.TryGet(envelope.ConnectionId, out Session staleJoin)
                    && staleJoin.Epoch == envelope.SessionEpoch)
                {
                    staleJoin.JoinPending = false;
                    staleJoin.Principal = null;
                    staleJoin.BuildHash = null;
                }
                RemoveEmptyRoom(room);
                Interlocked.Increment(ref _mailboxStale);
                return;
            }

            Session context = ResolveEnvelopeSession(envelope.ConnectionId, envelope.SessionEpoch);
            ExecuteCommandTo(room, envelope.Command, context);
        }

        private void DrainInputEnvelope(RoomInstance room, RoomInputEnvelope envelope)
        {
            if (!IsCurrentEnvelope(envelope.RoomId, envelope.ConnectionId,
                envelope.PlayerId, envelope.SessionEpoch, envelope.Command.Kind))
            {
                Interlocked.Increment(ref _mailboxStale);
                return;
            }
            if (!_sessions.TryGet(envelope.ConnectionId, out Session session)
                || session.PlayerId != envelope.PlayerId
                || session.Disconnected)
            {
                Interlocked.Increment(ref _mailboxStale);
                return;
            }

            long acceptedBefore = room.Runtime.Gate.AcceptedCount;
            ExecuteCommandTo(room, envelope.Command);
            _ops.CountAck(session.TryAcceptAck(envelope.Command.Input.AckSnapshot));
            if (room.Runtime.Gate.AcceptedCount > acceptedBefore)
            {
                _ops.InputPackets++;
                _ops.AckObserved++;
            }
        }

        private bool IsCurrentEnvelope(string roomId, int connectionId, int playerId,
            long epoch, RoomCommandKind kind)
        {
            if (connectionId < 0 || epoch == 0) return true;
            if (!_sessions.TryGet(connectionId, out Session current) || current.Epoch != epoch)
                return false;
            switch (kind)
            {
                case RoomCommandKind.AuthenticatedJoin:
                    return current.RoomId == null && current.JoinPending && !current.Disconnected;
                case RoomCommandKind.Disconnect:
                    // Disconnect is deliberately marked on Session before it enters the
                    // mailbox; the disconnected bit is the fact being delivered here.
                    return string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
                case RoomCommandKind.Rebind:
                    return !current.Disconnected
                        && (current.RoomId == null
                            || string.Equals(roomId, current.RoomId, StringComparison.Ordinal))
                        && (current.PlayerId < 0 || current.PlayerId == playerId);
                case RoomCommandKind.RestoreAck:
                    return !current.Disconnected
                        && current.PlayerId == playerId
                        && string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
                default:
                    return !current.Disconnected
                        && string.Equals(roomId, current.RoomId, StringComparison.Ordinal);
            }
        }

        private Session ResolveEnvelopeSession(int connectionId, long epoch)
        {
            if (connectionId < 0 || !_sessions.TryGet(connectionId, out Session session)) return null;
            return epoch == 0 || session.Epoch == epoch ? session : null;
        }

        private void ApplyOutput(RoomOutput output, Session joinContext)
        {
            switch (output)
            {
                case SignalOutput so:
                    ApplySignal(so, joinContext);
                    break;
                case JoinRejectedOutput jr:
                    RoomInstance rejectedRoom = _currentRoom;
                    if (_sessions.TryGet(jr.ConnectionId, out Session rejectedSession))
                    {
                        rejectedSession.JoinPending = false;
                        if (rejectedSession.PlayerId < 0)
                        {
                            rejectedSession.Principal = null;
                            rejectedSession.BuildHash = null;
                        }
                    }
                    _ops.Rejects++;
                    Console.WriteLine($"[Reject] conn {jr.ConnectionId}: {jr.Reason}");   // 只打原因（P0-3）
                    // 动态房间是在 Join admission 时预留的；若 Runtime 在消费
                    // 命令时拒绝（例如房间已在排空/终态），释放仍为空的预留，
                    // 避免无效 roomId 占满 MaxRooms。已有状态或非动态房间不会被回收。
                    RemoveEmptyRoom(rejectedRoom);
                    break;
                case CloseConnectionOutput cc:
                    _transport.Disconnect(cc.ConnectionId);
                    break;
                case MatchStateChangedOutput mcs:
                    // §9.1/§13.1 结构化事件（R4 换 OpenTelemetry）：低频迁移只在日志记录，不进指标高基数标签
                    _ops.MatchStateChanges++;
                    Console.WriteLine($"[Match] room={mcs.MatchId} {mcs.From}->{mcs.To} reason={mcs.Reason} frame={mcs.Frame} nowMs={mcs.NowMs}");
                    break;
                case SettlementReadyOutput sr:
                    _ops.SettlementsReady++;
                    Console.WriteLine($"[Settle] room={sr.Summary.MatchId} seed={sr.Summary.Seed} finalFrame={sr.Summary.FinalFrame} end={sr.Summary.EndReason} seats={sr.Summary.SeatPlayerIds.Length}");
                    // §11.3"本地持久 Outbox"：冻结事实入盒（幂等/有界/失败显式计数——不抛进权威循环，
                    // §6"持久化写入不得阻塞/炸掉 Room Worker"）。null = 未装配（现状形态）。
                    if (_settlementOutbox != null)
                    {
                        switch (_settlementOutbox.Enqueue(sr.Summary))
                        {
                            case SettlementOutboxResult.Appended:
                                _ops.SettlementsJournaled++;
                                break;
                            case SettlementOutboxResult.Duplicate:
                                _ops.SettlementsOutboxDuplicates++;
                                break;
                            case SettlementOutboxResult.RejectedFull:
                                _ops.SettlementsOutboxRejected++;
                                Console.WriteLine($"[Settle] !! Outbox 容量满拒（计数不丢）：{sr.Summary.MatchId}");
                                break;
                            default:
                                _ops.SettlementsOutboxFailed++;
                                Console.WriteLine($"[Settle] !! Outbox 写入失败（介质故障，计数不丢）：{sr.Summary.MatchId}");
                                break;
                        }
                    }
                    break;
            }
        }

        private void ApplySignal(SignalOutput so, Session joinContext)
        {
            switch (so.Signal)
            {
                case PlayerAdmitted pa:
                {
                    RoomInstance room = _currentRoom ?? (_rooms.Count > 0 ? _rooms[0] : null);
                    if (room == null) break;
                    Session session = joinContext ?? (pa.PlayerId < room.Seats.Length ? room.Seats[pa.PlayerId] : null);
                    if (session == null) break;
                    session.JoinPending = false;
                    session.PlayerId = pa.PlayerId;
                    session.RoomId = room.RoomId;          // 多房间路由键（输入/重连/断线按它找房间）
                    room.Seats[pa.PlayerId] = session;

                    // E3 重连票据（一次性）：进房成功才发——重连时凭它换权威快照（§5.6 首选路径的服务器侧能力）
                    string ticket = _reconnects.Issue(pa.PlayerId, room.RoomId);
                    SendToSession(session, PacketType.JoinAck, new Proto.JoinAck
                    {
                        PlayerId = pa.PlayerId,
                        Members = { pa.Members },
                        ReconnectToken = ticket,
                        ReconnectWindowSeconds = (int)(ReconnectService.TicketTtlMs / 1000),
                        SnapshotHz = SimConfig.SnapshotHz,
                        TickRate = SimConfig.TickRate,
                    }, reliable: true);
                    break;
                }
                case MatchStarted ms:
                {
                    RoomInstance room = _currentRoom ?? (_rooms.Count > 0 ? _rooms[0] : null);
                    if (room == null) break;
                    for (int p = 0; p < room.Seats.Length; p++)
                    {
                        Session member = room.Seats[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.StartGame, new Proto.StartGame
                        {
                            Seed = ms.Seed,
                            ConfigHash = ms.ConfigHash,   // 房间创建时绑定的规范化摘要（§4/P0-5）
                            Frame = ms.Frame,
                        }, reliable: true);
                    }
                    break;
                }
                case SeatRestored:
                {
                    _ops.RestoresCompleted++;              // §9.3 步骤 6：席位回 Active——增量广播恢复
                    RoomInstance room = _currentRoom ?? (_rooms.Count > 0 ? _rooms[0] : null);
                    room?.Pipeline.RequestFullSnapshot();  // 重锚广播链：抑制期间基线已越过重连快照帧，整帧全量补齐 (N_r, B] 缝
                    break;
                }
            }
        }

        /// <summary>字符串 UTF-8 字节数超限判定（P0-3：空串由各语义检查自理，此处只挡超长）。</summary>
        private static bool OverByteLimit(string value, int maxBytes)
        {
            return value != null && System.Text.Encoding.UTF8.GetByteCount(value) > maxBytes;
        }

        /// <summary>
        /// 单循环一帧（MVP 形态，§10.2）：tick 顺序 Incoming → 权威步（命令）→ 快照广播 → Outgoing。
        /// 帧节拍由外部循环控制（<see cref="ServerLoop"/> 绝对锚定 60Hz）。
        /// </summary>
        public void Pump()
        {
            if (_disposed) return;
            _nowMs = NowMs();
            _transport.TickIncoming();
            if (_mailboxRouting)
                FlushPendingDisconnects();

            // 逐房间推进（§8.2"一个 Worker 顺序驱动多个 RoomActor"——当前宿主主线程即唯一 Worker）。
            // 快照广播的播放条件由**每房间自己的席位**判定：多房间下必须按房间传各自的可播视图。
            for (int i = 0; i < _rooms.Count; i++)
            {
                RoomInstance room = _rooms[i];

                // 排空超时兜底（§12 第 3 步"超时则归档 Aborted 原因并安全关闭"）：
                // 排空期间若某房间未在时限内自然收敛，此处强制关闭，避免永久停在排空态。
                if (room.DrainDeadlineMs >= 0 && _nowMs >= room.DrainDeadlineMs && !room.Runtime.Closed)
                {
                    _ops.RoomsDrainTimedOut++;
                    SubmitCommandTo(room, RoomCommand.Shutdown(ShutdownReason.DrainTimeout));
                }

                // Deferred Mailbox mode must consume the transport batch before the
                // owner-generated Tick. Otherwise Tick advances the authoritative frame
                // first and an input that was valid for frame+1 is classified stale when
                // the Input lane is drained afterwards. Control still wins over Input
                // within this drain; Tick is submitted only after the already-arrived
                // batch has been applied.
                if (_mailboxRouting && !_drainMailboxesImmediately)
                {
                    // 先清空已经在 Control lane 中的消息，再重试暂存的断线事实，
                    // 最后才消费 Input/Outbound；这样重试命令不会越过同批输入。
                    DrainRoomControlMailbox(room);
                    FlushPendingDisconnects(room);
                    DrainRoomMailbox(room);
                }

                // A stale dynamic Join can release this room reservation while the
                // mailbox is being drained. Adjust the index so the next room is
                // still visited in this Pump instead of being skipped.
                if (!_roomTable.TryGetValue(room.RoomId, out RoomInstance registered)
                    || !ReferenceEquals(registered, room))
                {
                    i--;
                    continue;
                }

                SubmitCommandTo(room, RoomCommand.Tick(_nowMs));
                if (_mailboxRouting && !_drainMailboxesImmediately)
                    DrainRoomMailbox(room);
                if (room.Runtime.Started)
                    room.Pipeline.BroadcastIfDue(room.Runtime.AuthSim.Frame, room.Runtime.AuthSim,
                        room.Runtime.Gate, room.Runtime.EntityIdOf, room.SeatBroadcastable);
            }
            if (++_ticksSinceCleanup >= SessionCleanupIntervalTicks)
            {
                _ticksSinceCleanup = 0;
                _ops.SessionsCleaned += _sessions.Cleanup(_nowMs);   // §9.2 周期清理（容量+时效）
                _reconnects.PurgeExpired();
            }
            _transport.TickOutgoing();
            MaybePrintOps();
        }

        /// <summary>
        /// 席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。
        /// 快照管线按房间构造，故这里只需按 <paramref name="playerId"/> 查该房间自己的席位表。
        /// </summary>
        private bool SeatBroadcastable(int playerId)
        {
            RoomInstance room = _currentRoom ?? (_rooms.Count > 0 ? _rooms[0] : null);
            return room != null && room.Runtime.TryGetSeat(playerId, out PlayerSession seat)
                && seat.Phase == SeatPhase.Active;
        }

        /// <summary>
        /// 开始排空／优雅关闭（《商业级通用服务端框架总设计》§12"优雅关闭"第 2–5 步的宿主侧）。
        ///
        /// **设计五步与本实现的对应**：
        /// 1. "Readiness 置 false，Lobby 不再分配新 Match"——本服务不接 Lobby（实例注册/容量上报归 R2），
        ///    故无 readiness 面；等价语义由 <see cref="Draining"/> 对**本进程**生效：不做新房间、不收新进房。
        /// 2. "停止接受新 Join，现有房间进入 drain"——**已实现**：<see cref="Draining"/> 置位后
        ///    <see cref="HandleJoin"/> 一律拒绝（含已在册房间），既有对局继续跑。
        /// 3. "在配置时限内完成对局；超时则归档 Aborted 原因并安全关闭"——**已实现**：
        ///    到 <paramref name="deadlineMs"/> 仍未终态的房间在 <see cref="Pump"/> 中发
        ///    <see cref="ShutdownReason.DrainTimeout"/> 强制关闭。
        /// 4. "刷新 Outbox/Archive 到持久介质"——**已实现**（M0-c 后续批）：装配了
        ///    <see cref="ISettlementOutbox"/> 时结算在 SettlementReady 即逐条 write-through 落盘，
        ///    排空收口经 <see cref="FlushSettlementOutbox"/>（未装配时本步为无操作）。
        ///    Archive（完整 Match 归档）归 R3。
        /// 5. "停止 Worker、Transport 和 Host"——**生命周期已接线**：<see cref="Dispose"/> 按
        ///    Worker Stop(drain:true) → Transport → Outbox 顺序收尾；本方法不主动执行第 5 步。
        ///    真正 Mailbox 排空与 Worker/房间 Owner 切换的前置门禁归后续路由批次。
        ///
        /// **幂等**：重复调用只在前移截止时刻时生效（取更早者，不延后已定的排空期限）。
        /// </summary>
        /// <param name="deadlineMs">排空截止（单调毫秒，与 <c>Pump</c> 同一时钟源）。</param>
        public void BeginDrain(long deadlineMs)
        {
            if (_draining && deadlineMs >= _drainDeadlineMs) return;   // 已排空且新期限不更早 → 无操作
            _draining = true;
            _drainDeadlineMs = deadlineMs;

            for (int i = 0; i < _rooms.Count; i++)
            {
                RoomInstance room = _rooms[i];
                if (room.Runtime.Closed) continue;                     // 终态房间不必排空
                // 取更早的截止时刻：排空期限只可前移，不可被后一次调用延后
                if (room.DrainDeadlineMs < 0 || deadlineMs < room.DrainDeadlineMs)
                    room.DrainDeadlineMs = deadlineMs;
            }
            Console.WriteLine($"[RoomServer] 开始排空：在册 {_rooms.Count} 房，截止 +{deadlineMs}ms（单调时钟）");
        }

        /// <summary>是否已进入排空（§12 第 1–2 步：不再接受新 Join／新房间）。</summary>
        public bool Draining
        {
            get { return _draining; }
        }

        /// <summary>当前在盒待提交的结算条数（未装配 Outbox 时为 0；退出日志/排空收口用）。</summary>
        public int SettlementOutboxPending
        {
            get { return _settlementOutbox?.Count ?? 0; }
        }

        /// <summary>
        /// §12 第 4 步"刷新 Outbox 到持久介质"——排空完成（<see cref="DrainComplete"/>）后的收口动作：
        /// 逐条 write-through 形态下条目在 Enqueue 时已落盘，此处为幂等收口确认（未装配为无操作）。
        /// 调用序：BeginDrain → 等 DrainComplete → 本方法 → Dispose（第 5 步）。
        /// </summary>
        public void FlushSettlementOutbox()
        {
            _settlementOutbox?.Flush();
        }

        /// <summary>
        /// 宿主单调时钟当前毫秒——与 <see cref="Pump"/> 的 <c>_nowMs</c>、<see cref="BeginDrain"/>
        /// 的截止时刻**同一时钟源**。调用方据此刻画排空期限（<c>CurrentMs + 配置时限</c>）。
        /// </summary>
        public long CurrentMs
        {
            get { return NowMs(); }
        }

        /// <summary>排空截止时刻（-1 = 未排空）。</summary>
        public long DrainDeadlineMs
        {
            get { return _drainDeadlineMs; }
        }

        /// <summary>
        /// 排空是否已完成：已进入排空，且**所有在册房间都到终态**（§12 第 3 步收敛判据）。
        /// 调用方据此决定何时执行第 5 步（停 Transport/Host）。
        /// </summary>
        public bool DrainComplete
        {
            get
            {
                if (!_draining) return false;
                for (int i = 0; i < _rooms.Count; i++)
                    if (!_rooms[i].Runtime.Closed) return false;
                return true;
            }
        }

        /// <summary>按房间号取房间（不存在返回 false；**不创建**——建房要容量校验与配置模板，见 <see cref="OpenRoom"/>）。</summary>
        public bool TryGetRoom(string roomId, out RoomRuntime room)
        {
            if (roomId != null && _roomTable.TryGetValue(roomId, out RoomInstance inst))
            {
                room = inst.Runtime;
                return true;
            }
            room = null;
            return false;
        }

        /// <summary>
        /// 打开房间（幂等：已存在则返回既有实例，**不重建**）。走配置模板造房间配置，受
        /// <see cref="MaxRooms"/> 约束——达上限返回 null（调用方**拒绝进房**，不是静默排队）。
        ///
        /// **失败语义**（都返回 null，由调用方转成"拒绝进房"）：
        /// - 未装配 <see cref="RoomServerConfig"/> 且 roomId 与预置房间不符（单房间形态）；
        /// - 达容量上限；
        /// - 模板缺失或模板配置非法（<see cref="RoomServerConfig.BuildRoomConfig"/> 抛，此处捕获转为 null）。
        ///
        /// **空 roomId**：建房必须有路由键，直接 null。
        /// </summary>
        public RoomRuntime OpenRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return null;
            if (_roomTable.TryGetValue(roomId, out RoomInstance existing)) return existing.Runtime;

            // 单房间兼容形态：只认预置房间，不动态建房（容量 1 由构造路径保证）
            if (_serverConfig == null) return null;
            if (_rooms.Count >= _serverConfig.MaxRooms)
            {
                _ops.RoomsRejectedAtCapacity++;
                return null;
            }

            RoomConfig cfg;
            try
            {
                cfg = _serverConfig.BuildRoomConfig(null, roomId);   // 无模板参数 → 配置的 default_template
            }
            catch (Exception ex)
            {
                // 配置错误不炸宿主：拒绝该房号并计数（§429 范围校验在装载期已挡掉大部分，此处兜底）
                _ops.RoomsRejectedBadConfig++;
                Console.WriteLine($"[Room] 建房被拒 room={roomId}: {ex.Message}");
                return null;
            }

            return AddRoom(cfg, dynamic: true).Runtime;
        }

        /// <summary>登记一个房间（构造预置与 <see cref="OpenRoom"/> 共用）。</summary>
        private RoomInstance AddRoom(RoomConfig cfg, bool dynamic = false)
        {
            int mailboxCapacity = _serverConfig?.MailboxCapacity ?? RoomInstance.DefaultMailboxCapacity;
            var inst = new RoomInstance(cfg, mailboxCapacity, dynamic);
            inst.Pipeline.SendTo = SendToSession;
            _roomTable[inst.RoomId] = inst;
            _rooms.Add(inst);
            return inst;
        }

        /// <summary>
        /// 只移除刚刚预留且尚未形成席位的空房间。动态 Join 入盒失败或 Runtime
        /// 消费后拒绝时用来回滚房间容量 reservation；已有席位/已开局房间绝不回收。
        /// </summary>
        private bool RemoveEmptyRoom(RoomInstance room)
        {
            if (room == null || !room.IsDynamic || room.Runtime.Started
                || room.Mailbox.Count != 0)
                return false;
            for (int i = 0; i < room.Seats.Length; i++)
                if (room.Seats[i] != null) return false;
            if (!_roomTable.Remove(room.RoomId)) return false;
            _rooms.Remove(room);
            room.Mailbox.Complete();
            return true;
        }

        /// <summary>按房间号取房间（新建房时用；不存在返回 null）。</summary>
        private RoomInstance GetRoomInstance(string roomId)
        {
            return roomId != null && _roomTable.TryGetValue(roomId, out RoomInstance i) ? i : null;
        }

        /// <summary>
        /// Ops 周期打印（默认 5s）。**逐房间一行**——多房间下把整台服务器的帧号/快照合成一行会丢掉
        /// "哪个房间慢"这个唯一有用的信息（§514"多房间并发时一个慢客户端或过载房间不拖累其他房间"
        /// 的观测前提就是能按房间看）。节拍与宿主级计数只打在首行（属进程/Worker 级，不属房间）。
        /// </summary>
        private void MaybePrintOps()
        {
            if (!_ops.PrintEnabled) return;
            if (_nowMs - _ops.LastPrintMs < OpsIntervalMs) return;
            _ops.LastPrintMs = _nowMs;

            for (int i = 0; i < _rooms.Count; i++)
            {
                RoomInstance room = _rooms[i];
                Console.WriteLine(_ops.Format(room.RoomId, room.Runtime, room.Pipeline, _sessions,
                    _rooms.Count, i == 0 ? LoopStats : null));
            }
        }

        /// <summary>节拍统计来源（宿主装配 ServerLoop 后注入；null = 不打印节拍段——用例/嵌入式用法）。</summary>
        public ServerLoop.LoopStats LoopStats { get; set; }

        /// <summary>
        /// 按房间号取房间；**不存在则创建**（OpenRoom 语义）。历史名保留给既有调用方——
        /// 单房间形态下它等价于"取预置房间"（不存在时返回 null，因为该形态下容量为 1 且无模板）。
        /// </summary>
        public RoomRuntime TryGetOrCreateRoom(string roomId) => OpenRoom(roomId);
    }
}
