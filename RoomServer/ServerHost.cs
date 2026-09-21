using System;
using System.Collections.Generic;
using System.Diagnostics;
using Google.Protobuf;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using LiteSim;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 服务器宿主（《状态同步实施方案》§4.5 RoomServer 结构图顶层组装）：
    /// Transport（IRoomTransport）+ Sessions + Rooms + **单循环**（MVP 不拆 I/O 线程，§10.2——
    /// tick 顺序：TickIncoming → 信令/输入路由 → 房间权威步 → TickOutgoing）。
    ///
    /// MVP 装配参数由 <see cref="RoomConfig"/> 提供（默认：端口 17777 / 房间 Room-A / 2 人房——M10 审计建议 2/3 收口）、
    /// **buildHash = <see cref="BuildHash.Value"/>**（源码内容哈希，两端不一 = 逻辑/协议版本不同 → 拒绝进房）。
    /// E3：连接 cookie 由 kcp2k V1.41 内建（白得）；per-IP 限速与重连票据见 <see cref="SessionManager"/>/<see cref="ReconnectService"/>。
    ///
    /// **传输解耦（2026-09-19 收口）**：构造参数是窄端口 <see cref="IRoomTransport"/>（原先吃具体类
    /// `KcpTransportServer`，导致该接口只存在于注释里——"换传输 = 只写适配器"名不副实）。
    /// 本类只用到窄端口的成员：3 个事件 + `Start/TickIncoming/TickOutgoing/SendTo/Dispose`
    /// → 换传输框架（LiteNetLib/ENet/Ruffles…）只需新写一个适配器 + 装配一行。
    /// **所有权**：宿主**接管**传入的传输（构造即 `Start`、`Dispose` 即释放）——调用方不必再管它。
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        /// <summary>服务器版本锚点 = 源码内容哈希（批③ 从字面量串切换到生成器：Sim 或协议一改，握手即拒）。</summary>
        public const string ServerBuildHash = BuildHash.Value;
        public const long OpsIntervalMs = 5000;

        private readonly IRoomTransport _transport;   // 窄端口（可替换性第二刀，2026-09-19 收口）
        private readonly SessionManager _sessions = new SessionManager();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly ReconnectService _reconnects = new ReconnectService();
        private readonly Ops _ops = new Ops();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private long _nowMs;
        private bool _disposed;

        public SessionManager Sessions => _sessions;
        public RoomConfig Config { get; }
        public Room Room { get; }
        public Ops Ops => _ops;

        public ServerHost(IRoomTransport transport, RoomConfig config = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Config = config ?? RoomConfig.Default();
            Room = new Room(Config);
            _rooms[Config.RoomId] = Room;                     // MVP：预置单房间注册（Pump 遍历 _rooms 驱动权威步）
            Room.SendTo = SendToSession;
            Room.Broadcaster.SendTo = SendToSession;   // 广播器与 Room 共用同一发送出口（2026-09-19 拆分接线）
            Room.OnInputAccepted = (session, message) => { _ops.InputPackets++; _ops.AckObserved++; };
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
            _sessions.Add(new Session(connectionId, _nowMs));
        }

        private void OnTransportDisconnected(int connectionId)
        {
            _nowMs = NowMs();
            if (_sessions.TryGet(connectionId, out Session session))
            {
                session.Disconnected = true;   // 掉线不停帧（§4.5-2）：标记 + 会话保留（重连票据在 ReconnectService 里）
            }
        }

        private void OnTransportData(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            _nowMs = NowMs();
            Session session = _sessions.GetOrAddOnFirstPacket(connectionId, _nowMs);
            if (session == null) return;
            session.Touch(_nowMs);
            if (session.Disconnected) return;

            if (!PacketCodec.TryDecode(data, out PacketType type, out IMessage message)) return;

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
                case PacketType.Leave:
                    HandleLeave(session);
                    break;
            }
        }

        /// <summary>
        /// Join 信令：token 非空 + **房间号一致** + buildHash 必须等于服务器版本（版本红线）
        /// → 分配玩家号 → JoinAck + 满员即 StartGame。
        ///
        /// 房间号校验（2026-09-19 审查收紧）：原先**完全忽略** `JoinRequest.RoomId`——MVP 单房间下无害，
        /// 但既然 `--room` 已是可配项，"请求 A 房却静默进 B 房"就成了不可解释的客户端体验 ✗。
        /// 现在房间号不符（或缺失）直接拒绝：错误必须显式，不能靠"恰好只有一个房间"。
        /// </summary>
        private void HandleJoin(Session session, JoinRequest join)
        {
            if (session.PlayerId >= 0) return;                              // 重复 Join 忽略
            if (string.IsNullOrEmpty(join.Token))                            // token 红线：空即拒绝
            {
                Reject(session, "token 缺失");
                return;
            }
            if (join.RoomId != Room.RoomId)                                  // 房间号红线：不符/缺失即拒绝
            {
                Reject(session, string.IsNullOrEmpty(join.RoomId)
                    ? $"房间号缺失（本服房间：{Room.RoomId}）"
                    : $"房间号不符：{join.RoomId} != {Room.RoomId}");
                return;
            }
            if (join.BuildHash != ServerBuildHash)                           // 版本红线：Sim/协议版本比对不符拒绝进房
            {
                Reject(session, $"buildHash 不符：{join.BuildHash} != {ServerBuildHash}");
                return;
            }
            session.BuildHash = join.BuildHash;

            int playerId = Room.AssignPlayerId(session);
            if (playerId < 0)
            {
                Reject(session, "房间已满");
                return;
            }

            // E3 重连票据（一次性）：进房成功才发——重连时凭它换权威快照（§5.6 首选路径的服务器侧能力）
            string ticket = _reconnects.Issue(playerId, Room.RoomId);
            SendToSession(session, PacketType.JoinAck, new Proto.JoinAck
            {
                PlayerId = playerId,
                Members = { Room.MemberIds() },
                ReconnectToken = ticket,
                ReconnectWindowSeconds = (int)(ReconnectService.TicketTtlMs / 1000),
                SnapshotHz = SimConfig.SnapshotHz,
                TickRate = SimConfig.TickRate,
            }, reliable: true);

            if (!Room.Started && Room.NextPlayerId >= Room.ExpectedPlayers)  // 满员自动 StartGame（无 MatchMaker）
            {
                Room.Start(Config.Seed);   // 0 = 按 RoomConfig 策略（时钟）/ 非 0 = 固定 seed
                foreach (var kv in Room.AllMembers())
                {
                    SendToSession(kv.Value, PacketType.StartGame, new Proto.StartGame
                    {
                        Seed = Room.Seed,
                        ConfigHash = unchecked((uint)ServerBuildHash.GetHashCode()),
                        Frame = Room.AuthSim.Frame,
                    }, reliable: true);
                }
            }
        }

        private void HandleInput(Session session, InputMessage msg)
        {
            if (session.Room == null || session.PlayerId < 0) return;
            Room room = session.Room;
            session.LastAckSnapshot = msg.AckSnapshot;
            room.OnClientAck(session, msg.AckSnapshot);
            room.OnInput(session, msg);   // 计数经 Room.OnInputAccepted 回挂到 Ops（只统计被闸门接受的包）
        }

        /// <summary>和解上报：只汇总计数（Ops 输出和解触发率；含 M8 已知的跨运行时 1-ULP 底噪预期基线说明）。</summary>
        private void HandleMismatch(Session session, Proto.MismatchReport report)
        {
            if (session.Room == null) return;
            _ops.MismatchReports++;
            if (report.Frame > _ops.LastMismatchFrame) _ops.LastMismatchFrame = report.Frame;
        }

        /// <summary>重连（§5.6 首选路径的服务器侧）：一次性票据 → 校验 → 权威快照 + 后续输入历史。</summary>
        private void HandleReconnect(Session session, Proto.ReconnectRequest request)
        {
            if (!_reconnects.TryConsume(request.OneTimeToken, out int playerId, out string roomId))
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "票据无效或已过期" }, reliable: true);
                return;
            }

            Room room = TryGetOrCreateRoom(roomId);
            if (!room.Started || !room.TryGetMember(playerId, out _))
            {
                SendToSession(session, PacketType.ReconnectResponse, new Proto.ReconnectResponse { Ok = false, Reason = "房间或席位不存在" }, reliable: true);
                return;
            }

            // 会话重挂：旧连接（若还在）标记断开，新连接接管席位
            if (room.TryGetMember(playerId, out Session old) && old != null && old != session) old.Disconnected = true;
            room.AssignPlayerId(session, playerId);
            room.RequestFullSnapshot();                                  // 下一广播整帧全量（该客户端要从零重建）

            var response = new Proto.ReconnectResponse { Ok = true };
            long viewerEntityId = room.EntityIdOf(playerId);
            // 重连响应里的快照是**独立探测**（不推进广播基线）：显式构造一份全量。
            // 分层顺序（§5.6）：公共全量（PackFull 槽位）→ 比赛状态（PackFull 内附）→ 本人私有状态 → 输入历史。
            // 私有面只发本人——不泄漏他人库存，也不依赖 CustomData 全量路径（P0 阻塞项 ①）。
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

        private void HandleLeave(Session session)
        {
            session.Disconnected = true;
        }

        private void SendToSession(Session session, PacketType type, IMessage message, bool reliable)
        {
            _transport.SendTo(session.ConnectionId, new ArraySegment<byte>(PacketCodec.Encode(type, message)), reliable);
        }

        private void Reject(Session session, string reason)
        {
            _ops.Rejects++;
            Console.WriteLine($"[Reject] conn {session.ConnectionId}: {reason}");
        }

        /// <summary>
        /// 单循环一帧（MVP 形态，§10.2）：tick 顺序 Incoming → 房间权威步 → Outgoing。
        /// 帧节拍由外部循环控制（<see cref="ServerLoop"/> 绝对锚定 60Hz）。
        /// </summary>
        public void Pump()
        {
            if (_disposed) return;
            _transport.TickIncoming();
            foreach (var room in _rooms.Values) room.StepFrame();
            _transport.TickOutgoing();
            MaybePrintOps();
        }

        /// <summary>Ops 周期打印（默认 5s；帧号/房间数/快照尺寸/和解率汇总——《M10实施指导》§2.5 登记的输出形式）。</summary>
        private void MaybePrintOps()
        {
            if (!_ops.PrintEnabled) return;
            if (_nowMs - _ops.LastPrintMs < OpsIntervalMs) return;
            _ops.LastPrintMs = _nowMs;
            Console.WriteLine(_ops.Format(Room, _sessions, LoopStats));
        }

        /// <summary>节拍统计来源（宿主装配 ServerLoop 后注入；null = 不打印节拍段——用例/嵌入式用法）。</summary>
        public ServerLoop.LoopStats LoopStats { get; set; }

        public Room TryGetOrCreateRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out Room room))
            {
                room = Room;                                                 // MVP：唯一预置房间 Room-A
                _rooms[roomId] = room;
            }
            return room;
        }
    }
}
