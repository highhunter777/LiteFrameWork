using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// tick 顺序 TickIncoming → 命令直投 Runtime（同步，无队列；R2 换有界 Mailbox）→ 快照广播 → TickOutgoing。
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

        /// <summary>会话周期清理节拍（600 tick ≈ 10s @60Hz）。</summary>
        public const int SessionCleanupIntervalTicks = 600;
        private int _ticksSinceCleanup;

        // ---- R1 Runtime 装配 ----
        private readonly RoomRuntime _runtime;
        private readonly SnapshotPipeline _pipeline;
        /// <summary>席位映射：playerId → 连接会话（Join/重绑时写入；广播与信号按此定位连接）。</summary>
        private readonly Session[] _seatSessions;
        /// <summary>命令输出复用缓冲与输入批量复用缓冲（热路径零分配）。</summary>
        private readonly List<RoomOutput> _outputs = new List<RoomOutput>();
        private readonly SimInputFrame[] _batchFrames = new SimInputFrame[ClientInputBatch.MaxFrames];
        private ClientInputBatch _inputBatch;

        private long _nowMs;
        private bool _disposed;

        public SessionManager Sessions => _sessions;
        public RoomConfig Config { get; }
        public RoomRuntime Room => _runtime;
        public SnapshotPipeline Pipeline => _pipeline;
        public Ops Ops => _ops;

        public ServerHost(IRoomTransport transport, RoomConfig config = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Config = config ?? RoomConfig.Default();
            _runtime = new RoomRuntime(Config);
            _sessions = new SessionManager(4 * _runtime.ExpectedPlayers);   // R1 容量上限（§9.2）
            _seatSessions = new Session[_runtime.ExpectedPlayers];
            _pipeline = new SnapshotPipeline(_seatSessions);
            _pipeline.SendTo = SendToSession;
            _transport.OnConnected += OnTransportConnected;
            _transport.OnData += OnTransportData;
            _transport.OnDisconnected += OnTransportDisconnected;
            _transport.Start(Config.Port);   // Start 必须显式调用——此前遗漏导致服务器不监听（握手全失败）
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.Dispose();
        }

        private long NowMs() => _clock.ElapsedMilliseconds;

        private void OnTransportConnected(int connectionId)
        {
            _nowMs = NowMs();
            if (!_sessions.TryAdd(new Session(connectionId, _nowMs)))
            {
                // R1 会话容量上限（§9.2）：超限不登记并断开——未认证连接不能把记账表撑成无界内存
                _ops.SessionsRejected++;
                _transport.Disconnect(connectionId);
            }
        }

        private void OnTransportDisconnected(int connectionId)
        {
            _nowMs = NowMs();
            if (_sessions.TryGet(connectionId, out Session session))
                session.Disconnected = true;   // 掉线不停帧（§4.5-2）：标记 + 席位保留（重连窗口内可重绑）
            SubmitCommand(RoomCommand.Disconnect(connectionId));   // 席位侧通报（幂等：无映射即忽略）
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
            session.Touch(_nowMs);
            if (session.Disconnected) return;

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
        /// **安全能力现状（R0 声明，《服务端总设计》§5 P0-6/R2 补齐）**：本校验是**原型级**——
        /// token 只判非空与长度，重连票据是可预测串；kcp2k V1.41 cookie 只解决 UDP 探测/放大防护，
        /// **不提供业务身份、机密性或完整性**。公开部署前必须完成 R2（签名 Join Ticket、CSPRNG 重连票据、
        /// 远端地址限流与安全信封）——在此之前本服务不得直接暴露公网。
        /// </summary>
        private void HandleJoin(Session session, JoinRequest join)
        {
            if (session.PlayerId >= 0) return;                              // 重复 Join 忽略

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
            if (join.RoomId != _runtime.RoomId)                              // 房间号红线：不符/缺失即拒绝
            {
                Reject(session, string.IsNullOrEmpty(join.RoomId)
                    ? $"房间号缺失（本服房间：{_runtime.RoomId}）"
                    : $"房间号不符：{join.RoomId} != {_runtime.RoomId}");
                return;
            }
            if (join.BuildHash != ServerBuildHash)                           // 版本红线：Sim/协议版本比对不符拒绝进房
            {
                Reject(session, $"buildHash 不符：{join.BuildHash} != {ServerBuildHash}");
                return;
            }
            session.BuildHash = join.BuildHash;

            SubmitCommand(RoomCommand.Join(session.ConnectionId), joinContext: session);
        }

        private void HandleInput(Session session, InputMessage msg)
        {
            if (session.PlayerId < 0) return;

            // 语义 ACK 验证化（P0-4）：LastAckSnapshot 只在此路径前推；违纪分类计数归 Ops
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

            long acceptedBefore = _runtime.Gate.AcceptedCount;
            SubmitCommand(RoomCommand.ClientInput(session.PlayerId, _inputBatch));
            if (_runtime.Gate.AcceptedCount > acceptedBefore)   // 只统计被闸门接受的包（取代旧 OnInputAccepted 回挂）
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
            if (!_reconnects.TryConsume(request.OneTimeToken, out int playerId, out string roomId))
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }, reliable: true);
                return;
            }
            if (roomId != _runtime.RoomId)                     // 票据绑定房间号（R1：原实现忽略该绑定）
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "票据与房间不符" }, reliable: true);
                return;
            }

            RoomRuntime room = TryGetOrCreateRoom(roomId);
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
            SubmitCommand(RoomCommand.Rebind(playerId, session.ConnectionId));   // 席位 → Restoring（增量广播抑制中）
            session.PlayerId = playerId;
            _seatSessions[playerId] = session;

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

        /// <summary>恢复完成 ACK（§9.3 步骤 6）：席位 Restoring → Active——增量广播自此恢复该席位。</summary>
        private void HandleRestoreComplete(Session session)
        {
            if (session.PlayerId < 0) return;
            SubmitCommand(RoomCommand.RestoreAck(session.PlayerId));   // 幂等：非 Restoring 时 Runtime 静默忽略
        }

        private void HandleLeave(Session session)
        {
            session.Disconnected = true;
            SubmitCommand(RoomCommand.Disconnect(session.ConnectionId));
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
        /// 命令直投 + 输出应用（单房间同步形态；R2 换有界 Mailbox 时本方法只改投递方式，Runtime 零改动）。
        /// <paramref name="joinContext"/> = 触发本次命令的进房连接（PlayerAdmitted 落位用）。
        /// </summary>
        private void SubmitCommand(in RoomCommand cmd, Session joinContext = null)
        {
            _outputs.Clear();
            _runtime.Execute(cmd, _outputs);
            for (int i = 0; i < _outputs.Count; i++) ApplyOutput(_outputs[i], joinContext);
            _outputs.Clear();
        }

        private void ApplyOutput(RoomOutput output, Session joinContext)
        {
            switch (output)
            {
                case SignalOutput so:
                    ApplySignal(so, joinContext);
                    break;
                case JoinRejectedOutput jr:
                    _ops.Rejects++;
                    Console.WriteLine($"[Reject] conn {jr.ConnectionId}: {jr.Reason}");   // 只打原因（P0-3）
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
                    break;
            }
        }

        private void ApplySignal(SignalOutput so, Session joinContext)
        {
            switch (so.Signal)
            {
                case PlayerAdmitted pa:
                {
                    Session session = joinContext ?? (pa.PlayerId < _seatSessions.Length ? _seatSessions[pa.PlayerId] : null);
                    if (session == null) break;
                    session.PlayerId = pa.PlayerId;
                    _seatSessions[pa.PlayerId] = session;

                    // E3 重连票据（一次性）：进房成功才发——重连时凭它换权威快照（§5.6 首选路径的服务器侧能力）
                    string ticket = _reconnects.Issue(pa.PlayerId, _runtime.RoomId);
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
                    for (int p = 0; p < _seatSessions.Length; p++)
                    {
                        Session member = _seatSessions[p];
                        if (member == null || member.Disconnected) continue;
                        SendToSession(member, PacketType.StartGame, new Proto.StartGame
                        {
                            Seed = ms.Seed,
                            ConfigHash = ms.ConfigHash,   // 房间创建时绑定的规范化摘要（§4/P0-5）
                            Frame = ms.Frame,
                        }, reliable: true);
                    }
                    break;
                case SeatRestored sr:
                    _ops.RestoresCompleted++;              // §9.3 步骤 6：席位回 Active——增量广播恢复
                    _pipeline.RequestFullSnapshot();       // 重锚广播链：抑制期间基线已越过重连快照帧，整帧全量补齐 (N_r, B] 缝
                    break;
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
            SubmitCommand(RoomCommand.Tick(_nowMs));
            if (_runtime.Started)
                _pipeline.BroadcastIfDue(_runtime.AuthSim.Frame, _runtime.AuthSim, _runtime.Gate, _runtime.EntityIdOf,
                    SeatBroadcastable);
            if (++_ticksSinceCleanup >= SessionCleanupIntervalTicks)
            {
                _ticksSinceCleanup = 0;
                _ops.SessionsCleaned += _sessions.Cleanup(_nowMs);   // §9.2 周期清理（容量+时效）
                _reconnects.PurgeExpired();
            }
            _transport.TickOutgoing();
            MaybePrintOps();
        }

        /// <summary>席位可播判定（§9.3 步骤 6）：只有 Active 席位接收增量广播；Restoring（重连恢复中）抑制。</summary>
        private bool SeatBroadcastable(int playerId)
        {
            return _runtime.TryGetSeat(playerId, out PlayerSession seat) && seat.Phase == SeatPhase.Active;
        }

        /// <summary>Ops 周期打印（默认 5s；帧号/房间数/快照尺寸/和解率汇总）。</summary>
        private void MaybePrintOps()
        {
            if (!_ops.PrintEnabled) return;
            if (_nowMs - _ops.LastPrintMs < OpsIntervalMs) return;
            _ops.LastPrintMs = _nowMs;
            Console.WriteLine(_ops.Format(_runtime, _pipeline, _sessions, LoopStats));
        }

        /// <summary>节拍统计来源（宿主装配 ServerLoop 后注入；null = 不打印节拍段——用例/嵌入式用法）。</summary>
        public ServerLoop.LoopStats LoopStats { get; set; }

        /// <summary>按房间号取房间（MVP：唯一预置房间；R2 RoomRegistry 接管路由）。</summary>
        public RoomRuntime TryGetOrCreateRoom(string roomId) => _runtime;
    }
}
