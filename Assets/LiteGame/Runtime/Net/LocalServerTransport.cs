using System;
using System.Collections.Generic;
using LiteNet;
using LiteNet.Proto;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;

namespace LiteGame
{
    /// <summary>
    /// **进程内本地服务器**（离线隔离开发，2026-09-27）：实现 <see cref="IClientTransport"/>，
    /// 在同一进程里跑**真的**房间内核 <see cref="RoomRuntime"/> 与**真的** <see cref="SnapshotPipeline"/>
    /// （两者与服务端宿主编译同一份源码：`Assets/RoomServer/{Runtime,Application}`），中间不走网络。
    ///
    /// **它解决什么**：此前客户端要动战斗代码必须先起 `RoomServer` 进程（端点硬编码 127.0.0.1:17777），
    /// 否则 `ProcedureMatch` 十秒超时、根本进不了对局。开关打开即自带权威端跑完整闭环。
    ///
    /// **真实性边界（务必分清，别把"本地绿"当"联机绿"）**：
    /// - **真**：Sim 判定、`InputGate` 校验、快照差分/AOI、协议编解码（`PacketCodec`）、会话相位机；
    /// - **假**：无 Socket（无丢包/乱序/延迟/MTU）、无票据验签（token 只做非空）、单房间、
    ///   无多房间/排空/Ops/持久化、时间轴自走（不依赖墙钟）、**自动补位**（真客户端进房后，
    ///   剩余席位由假连接补满并按"缺席空输入"站桩——真实多人交互不可由此模拟）。
    ///   弱网、断线重连真实性、多房间隔离仍须在**真服务器**验（L3 与两房用例覆盖）。
    ///
    /// **与真服务端应用层的关系**：本类只补最小的那层映射（Join → JoinAck、MatchStarted → StartGame、
    /// Tick → 快照广播），对应 `ServerHost.ApplySignal`/`ApplyOutput` 里那几行。**不复制**票据/多房间/
    /// 排空/Ops——那些属于 Application 的带 IO 半边，本形态不需要。
    /// </summary>
    public sealed class LocalServerTransport : IClientTransport
    {
        /// <summary>本连接号（本地形态单连接；服务端 Application 的 Session 以它标识连接）。</summary>
        private const int Conn = 0;

        private readonly RoomRuntime _runtime;
        private readonly SnapshotPipeline _pipeline;
        private readonly Session[] _seats;
        private readonly List<RoomOutput> _outputs = new List<RoomOutput>();
        private readonly List<(PacketType type, Google.Protobuf.IMessage msg)> _outbox
            = new List<(PacketType, Google.Protobuf.IMessage)>();
        private readonly SimInputFrame[] _batchFrames = new SimInputFrame[ClientInputBatch.MaxFrames];   // 复用（热路径零分配，与 ServerHost 同纪律）
        private ClientInputBatch _inputBatch;

        private double _tickAccumMs;                 // 虚拟时钟（自走，不读系统钟——与 R11 同纪律）
        private long _nowMs;
        private int _playerId = -1;
        private bool _joined;
        private bool _connected;
        private bool _connectPending;                // Connect 已请求、等下一次泵建立（时序对齐真 KCP，见 Connect）
        private bool _disposed;

        public bool Connected => _connected;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>, bool> OnData;
        public event Action OnDisconnected;

        /// <summary>诊断：已接受的输入包 / 已广播的快照数。</summary>
        public long InputsAccepted;
        public long SnapshotsSent;

        /// <param name="roomConfig">房间配置（人数/种子/时限——与真服务端同类型）。</param>
        public LocalServerTransport(RoomConfig roomConfig)
        {
            if (roomConfig == null) throw new ArgumentNullException(nameof(roomConfig));
            _runtime = new RoomRuntime(roomConfig);
            _seats = new Session[_runtime.ExpectedPlayers];
            _pipeline = new SnapshotPipeline(_seats)
            {
                SendTo = (session, type, msg, reliable) => _outbox.Add((type, msg)),
            };
        }

        public void Connect(string address, int port)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalServerTransport));
            // 本地形态没有拨号/握手过程（真 KCP 的 cookie 握手在此不存在——这正是它的边界），但**时序
            // 必须对齐真传输**：`KcpTransportClient` 的连接完成是 **Tick 驱动的异步过程**（Connect 后
            // 持续泵至 OnConnected），不在 Connect() 内同步发——若本地形态同步发，Join→JoinAck 整条链
            // 会在 BattleClient 构造内跑完，客户端的 JoinAck 边缘事件在订阅者（WaitJoined）挂好之前
            // 就发掉了，表现为"本地模式必超时"。故挂起到下一次 TickIncoming 再触发。
            _tickAccumMs = 0;
            _nowMs = 0;
            _connectPending = true;
        }

        public void Disconnect()
        {
            _connectPending = false;
            if (!_connected) return;
            _connected = false;
            OnDisconnected?.Invoke();
        }

        /// <summary>客户端上行（`RoomClient.Send` 落到这里）。</summary>
        public void Send(ArraySegment<byte> data, bool reliable)
        {
            if (_disposed || !_connected) return;
            if (!PacketCodec.TryDecode(data, out PacketType type, out Google.Protobuf.IMessage msg)) return;

            switch (type)
            {
                case PacketType.Join:
                {
                    var join = (JoinRequest)msg;
                    HandleJoin(join);
                    break;
                }
                case PacketType.Input:
                {
                    if (!_joined) break;
                    HandleInput((InputMessage)msg);
                    break;
                }
            }
        }

        /// <summary>Join 信令：本形态保留 buildHash 红线（与真服务端同判据），其余校验从简。</summary>
        private void HandleJoin(JoinRequest join)
        {
            if (_joined) return;
            if (join.BuildHash != LiteNet.BuildHash.Value)
            {
                // 与真服务端同一条红线：不一致直接拒绝（否则本地绿会掩盖"两端 hash 不同"这类真问题）。
                // 处置也与 ServerHost.Reject 同款：**不回 JoinAck**（假 Ack 会带 -1 号位进 Battle、
                // 静默挂在"等 StartGame"），只打日志——客户端按 Join 超时进确定错误态，红线照常暴露。
                UnityEngine.Debug.LogWarning(
                    $"[LocalServer] Join 被拒：buildHash 不符（{join.BuildHash} != {LiteNet.BuildHash.Value}）");
                return;
            }

            _runtime.Execute(RoomCommand.Join(Conn), _outputs);
            DrainOutputs();                       // 本地客户端的 PlayerAdmitted → JoinAck
            FillRoomWithBots();                   // 离线形态：补满剩余席位（站桩对手）→ 满员即 MatchStarted
            FlushOutbox();
        }

        /// <summary>输入包：proto → 纯数据（与 ServerHost.HandleInput 同口径——Runtime 不见 proto）。</summary>
        private void HandleInput(InputMessage msg)
        {
            int raw = msg.Frames.Count;
            int copy = raw <= ClientInputBatch.MaxFrames ? raw : ClientInputBatch.MaxFrames;
            for (int i = 0; i < copy; i++)
            {
                InputFrame f = msg.Frames[i];
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

            // 语义 ACK（与真服务端同一道闸——差分基线靠它前推；不接则 NeedsFull 恒真、每帧全量）
            _seats[_playerId]?.TryAcceptAck(msg.AckSnapshot);

            long acceptedBefore = _runtime.Gate.AcceptedCount;
            _runtime.Execute(RoomCommand.ClientInput(_playerId, _inputBatch), _outputs);
            if (_runtime.Gate.AcceptedCount > acceptedBefore) InputsAccepted++;   // 只计闸门真接受的（ServerHost 同口径）
            DrainOutputs();
            FlushOutbox();
        }

        /// <summary>
        /// 自动补位（离线形态专属裁决）：真客户端进房后，剩余席位用**假连接**补满——房间立即满员、
        /// `MatchStarted` 即刻触发。补位者永不发包 = 权威循环每帧给它**空输入**（`RoomRuntime`
        /// "缺席沿用空输入"语义）＝站桩对手。
        ///
        /// **为什么必须**：客户端预测世界按 `BattleContext.ExpectedPlayers`（=2，首版房间规模）
        /// 定容重建，两端世界构造必须逐位一致（`OnStartGame` 契约）——1 人房会让服务端少生成一个
        /// 实体，首帧权威快照起每帧假和解。补位保住"同一份世界构造"，离线才跑得通完整闭环。
        /// 连接号从 Conn+1 起编（不与本地客户端冲突）。
        /// </summary>
        private void FillRoomWithBots()
        {
            int conn = Conn + 1;
            while (!_runtime.Closed && _runtime.NextPlayerId < _runtime.ExpectedPlayers)
            {
                _runtime.Execute(RoomCommand.Join(conn++), _outputs);
                DrainOutputs(admitIsLocal: false);   // 补位者的 PlayerAdmitted 不出包（不给客户端伪造进房事件），只透传 MatchStarted
            }
        }

        /// <summary>权威步进（`RoomClient.TickOutgoing` → 这里 → 内核与快照）。</summary>
        public void TickOutgoing()
        {
            if (_disposed || !_connected || !_joined) return;

            // 虚拟时钟推进：按真实帧间隔累计，满一逻辑帧就走一步（与服务端 ServerLoop 同节拍口径）
            _tickAccumMs += UnityEngine.Time.unscaledDeltaTime * 1000.0;
            double stepMs = 1000.0 / SimConfig.TickRate;

            int guard = 0;
            while (_tickAccumMs >= stepMs && guard++ < 8)      // 上限防追帧雪崩（与 FrameDriver 同思路）
            {
                _tickAccumMs -= stepMs;
                _nowMs += (long)stepMs;

                _runtime.Execute(RoomCommand.Tick(_nowMs), _outputs);
                DrainOutputs();

                _pipeline.BroadcastIfDue(_runtime.AuthSim.Frame, _runtime.AuthSim, _runtime.Gate,
                    entityIdOf: p => _runtime.EntityIdOf(p),
                    seatBroadcastable: p => _seats[p] != null && !_seats[p].Disconnected);

                FlushOutbox();
            }
        }

        public void TickIncoming()
        {
            // 收包在 Send/TickOutgoing 内同步产生并直接回调；连接建立事件在此补发（见 Connect 注释）
            if (!_connectPending) return;
            _connectPending = false;
            _connected = true;
            OnConnected?.Invoke();                   // Join 随后经 Send 同步处理，收发在同一泵内闭环
        }

        /// <summary>内核输出 → 协议包入 outbox（对应真服务端 ServerHost.ApplySignal 的那几行）。
        /// <paramref name="admitIsLocal"/> = 该批输出里的 PlayerAdmitted 是否属于本地连接：
        /// 补位（<see cref="FillRoomWithBots"/>）传 false——只收 MatchStarted，不占号位不回包。</summary>
        private void DrainOutputs(bool admitIsLocal = true)
        {
            for (int i = 0; i < _outputs.Count; i++)
            {
                if (!(_outputs[i] is SignalOutput so)) continue;

                switch (so.Signal)
                {
                    case PlayerAdmitted pa:
                    {
                        if (!admitIsLocal) break;   // 补位席位：静默（假连接不对应任何客户端）
                        _playerId = pa.PlayerId;
                        _joined = true;
                        _seats[pa.PlayerId] = new Session(Conn, _nowMs) { PlayerId = pa.PlayerId };
                        _outbox.Add((PacketType.JoinAck, new JoinAck
                        {
                            PlayerId = pa.PlayerId,
                            Members = { pa.Members },
                            ReconnectToken = "local-dev-" + pa.PlayerId,   // 本地形态：非安全凭据，仅满足非空
                            ReconnectWindowSeconds = 0,
                            SnapshotHz = SimConfig.SnapshotHz,
                            TickRate = SimConfig.TickRate,
                        }));
                        break;
                    }
                    case MatchStarted ms:
                        _outbox.Add((PacketType.StartGame, new StartGame
                        {
                            Seed = ms.Seed,
                            ConfigHash = ms.ConfigHash,
                            Frame = ms.Frame,
                        }));
                        break;
                }
            }
            _outputs.Clear();
        }

        /// <summary>outbox → <see cref="OnData"/>（与真传输同一个客户端收包入口）。</summary>
        private void FlushOutbox()
        {
            for (int i = 0; i < _outbox.Count; i++)
            {
                byte[] bytes = PacketCodec.Encode(_outbox[i].type, _outbox[i].msg);
                if (_outbox[i].type == PacketType.StateSnapshot) SnapshotsSent++;
                OnData?.Invoke(new ArraySegment<byte>(bytes), _outbox[i].type != PacketType.StateSnapshot);
            }
            _outbox.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connected = false;
            _connectPending = false;
        }
    }
}
