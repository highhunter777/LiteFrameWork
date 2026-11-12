using System;
using System.Collections.Generic;
using LiteFramework;
using LiteNet;
using LiteNet.Proto;
using LiteNet.Protocol;
using LiteSync.Input;
using LiteNet.Transport;
using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Map;

using LiteSync.Client;
// 开发面剥离：与 DevHud/DevStartupConfig 同款三宏 #if（release Player 中本件整体不编译；
// asmdef 层不设约束）。
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame.Adapters.Dev.LocalServer
{
    /// <summary>
    /// **进程内本地服务器**（离线隔离开发）：实现 IClientTransport， 在同一进程里跑**真的**房间内核 RoomRuntime 与**真的** SnapshotPipeline （两者与服务端宿主编译同一。）。
    /// </summary>
    public sealed class LocalServerTransport : IClientTransport
    {
        /// <summary>本连接号（本地形态单连接；服务端 Application 的 Session 以它标识连接）。</summary>
        private const int Conn = 0;

        private readonly RoomRuntime _runtime;
        private readonly SnapshotPipeline _pipeline;
        private readonly CombatEventPipeline _events;
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

        /// <summary>诊断只读：进程内真房间内核（测试读 bot 生命/权威状态）。</summary>
        public RoomServer.Runtime.RoomRuntime Runtime => _runtime;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>, bool> OnData;
        public event Action OnDisconnected;

        /// <summary>诊断：已接受的输入包 / 已广播的快照数（只读观测面——写入面仅本类）。</summary>
        public long InputsAccepted { get; private set; }
        public long SnapshotsSent { get; private set; }

        /// <summary>测试房规则持有件（null = 常规对局）——盖初值进数值快照 + 注入权威步进。</summary>
        private readonly MatchTestRules _testRules;

        /// <summary>本地测试房宿主钩子执行件（bot 冻结/传送/无限生命/重置）。</summary>
        private readonly LocalTestOverrides _testOverrides;

        /// <param name="roomConfig">房间配置（人数/种子/时限——与真服务端同类型）。</param>
        /// <param name="testRules">测试房规则持有件（null = 常规对局）——盖初值进数值快照 + 注入权威步进。</param>
        /// <param name="gameplayDigestBase">判定面表字节基摘要（批 E）= 客户端 `ConfigService.GameplayDigestBase`
        /// ——本地房间身份必须与客户端同源（客户端 `BattleContext` 按同一提供者复算 `StartGame.ConfigHash`）。</param>
        /// <param name="roomValues">本局判定数值（装配根在进房时点解析：表值或表值+覆写合并）——本地服建房
        /// 与客户端预测必须逐字段同值；null = 直构形态（测试）回落 <see cref="CombatValues.Default"/>＝表值
        /// （L1 守卫钉住两者一致）。</param>
        public LocalServerTransport(RoomConfig roomConfig, MatchTestRules testRules = null,
            uint gameplayDigestBase = 0, ILocalTestHooks hooks = null, CombatValues? roomValues = null)
        {
            if (roomConfig == null) throw new ArgumentNullException(nameof(roomConfig));
            _testRules = testRules;
            CombatValues? combat = null;
            if (testRules != null)
            {
                // 初值盖章进房间数值快照：测试房与常规房联机身份可区分（摘要随 Capture 同刻绑定）。
                // 源值 = 装配根解析的本局判定数值（与客户端发布面同一结果——覆写因此两端同值）。
                CombatValues stamped = roomValues ?? CombatValues.Default;
                stamped.NoDeath = testRules.NoDeath;
                stamped.InfiniteAmmo = testRules.InfiniteAmmo;
                combat = stamped;
            }
            else if (roomValues.HasValue)
            {
                combat = roomValues.Value;   // 常规房：按传入值建房（无测试规则可盖）
            }
            _runtime = new RoomRuntime(roomConfig, combat, testRules: testRules, gameplayDigestBase: gameplayDigestBase);
            int botRefillHp = (combat ?? CombatValues.Default).EntityHp;   // 与 RoomRuntime 取值同口径（combat ?? Default）
            _testOverrides = new LocalTestOverrides(_runtime, botRefillHp, hooks);
            _seats = new Session[_runtime.ExpectedPlayers];
            _pipeline = new SnapshotPipeline(_seats, aoi: _runtime.Config.Aoi, snapshotHz: _runtime.Config.SnapshotHz)
            {
                SendTo = (session, type, msg, reliable) => _outbox.Add((type, msg)),
            };
            _events = new CombatEventPipeline(_runtime.Events, _seats)   // 与状态面同一份席位表（生产装配同形）
            {
                SendTo = (session, type, msg, reliable) => _outbox.Add((type, msg)),
            };
        }

        /// <summary>
        /// 按房间号构造（**调用方的唯一入口**）。
        /// </summary>
        /// <param name="expectedPlayers">总席位（0 = 房间默认 2）；测试模式按 bot 数量配置。</param>
        /// <param name="testRules">测试房规则持有件（null = 常规对局）；初值同时盖进房间数值快照
        /// <param name="gameplayDigestBase">判定面表字节基摘要（批 E——装配根传 `ConfigService.GameplayDigestBase`
        /// <param name="map">判定图（**必填**——装配根经 `IMapProvider` 传入，与客户端逐位同图：
        /// <param name="mapId">地图行键（多图批；随 JoinAck 回执——客户端按它选表行/场景，与真服务器同一条
        /// <param name="roomValues">本局判定数值（装配根在进房时点解析；null = 直构形态回落默认表值）。</param>
        public static LocalServerTransport ForRoom(string roomId, int expectedPlayers = 0, MatchTestRules testRules = null,
            uint gameplayDigestBase = 0, SimMapData map = null, int mapId = 0, ILocalTestHooks hooks = null,
            CombatValues? roomValues = null, MatchRules? rules = null)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("房间号不得为空", nameof(roomId));
            if (map == null)
                throw new ArgumentException("地图必填——装配根经 IMapProvider 注入（D22：无回退地图）", nameof(map));
            var config = new RoomConfig
            {
                RoomId = roomId, Map = map, MapId = mapId,
                Rules = rules ?? MatchRules.Default,   // 策略表默认（装配根传入）；null = 缺省
            };
            if (expectedPlayers > 0) config.ExpectedPlayers = expectedPlayers;
            return new LocalServerTransport(config, testRules, gameplayDigestBase, hooks, roomValues);
        }

        public void Connect(string address, int port)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LocalServerTransport));
            // 本地形态没有拨号/握手过程（真 KCP 的 cookie 握手在此不存在——这正是它的边界），但**时序 必须对齐真传输**：`KcpTransportClient` 的连接完成是 **Tick 驱动的异步过程**（Connect 后 持续泵至 OnConnected）。
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
                case PacketType.Heartbeat:
                {
                    // RTT/丢包探针回声：与真服务端同口径——原样回显 client_time（Unreliable）。
                    _outbox.Add((PacketType.Heartbeat, new Heartbeat { ClientTime = ((Heartbeat)msg).ClientTime }));
                    FlushOutbox();
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
                UnityEngine.Debug.LogWarning(
                    $"[LocalServer] Join 被拒：buildHash 不符（{join.BuildHash} != {LiteNet.BuildHash.Value}）");
                return;
            }

            _runtime.Execute(RoomCommand.Join(Conn), _outputs);
            DrainOutputs();                       // 本地客户端的 PlayerAdmitted → JoinAck
            FillRoomWithBots();                   // 离线形态：补满剩余席位（站桩对手）
            StartAsOwner();                       // 开局须房主发起（满员不再自动开局）——本形态单客户端恒为房主
            FlushOutbox();
        }

        /// <summary>输入包：proto → 纯数据。**逐字段映射与真服同一份单源**
        /// （<see cref="InputPacker.UnpackFrames"/>——P0-3：此处曾与 ServerHost 各手抄一份，
        /// AimPoint 六字段漂移过一次，且本地服"照常工作"掩盖了真服断点）。</summary>
        private void HandleInput(InputMessage msg)
        {
            int raw = InputPacker.UnpackFrames(msg, _batchFrames);
            _inputBatch.Frame = msg.Frame;
            _inputBatch.AckSnapshot = msg.AckSnapshot;
            _inputBatch.ViewFrame = msg.ViewFrame;
            _inputBatch.Count = raw;
            _inputBatch.Frames = _batchFrames;

            // 语义 ACK（与真服务端同一道闸——差分基线靠它前推；不接则 NeedsFull 恒真、每帧全量）。
            _seats[_playerId]?.TryAcceptAck(msg.AckSnapshot);

            long acceptedBefore = _runtime.Gate.AcceptedCount;
            _runtime.Execute(RoomCommand.ClientInput(_playerId, _inputBatch), _outputs);
            if (_runtime.Gate.AcceptedCount > acceptedBefore) InputsAccepted++;   // 只计闸门真接受的（ServerHost 同口径）
            DrainOutputs();
            FlushOutbox();
        }

        /// <summary>
        /// 代房主开局（本地形态专属）：开局唯一入口是房主发起（裁决——满员不再自动开局）， 而本形态没有房间页/开局钮（TestMode 由开发控制台/GM 直开对局）——单客户端恒为首入座者（房主）。
        /// </summary>
        private void StartAsOwner()
        {
            if (_playerId < 0 || _runtime.Started) return;   // 未入座/已开局（重复 Join 等）：不重复发起
            _runtime.Execute(RoomCommand.StartEarly(_playerId), _outputs);
            DrainOutputs(admitIsLocal: false);               // MatchStarted → StartGame（无新 PlayerAdmitted 出包）
        }

        /// <summary>
        /// 自动补位（离线形态专属）：真客户端进房后，剩余席位用**假连接**补满——席位即刻占满 （开局由 StartAsOwner 紧接着代房主发起）。
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

            // 虚拟时钟推进：按真实帧间隔累计，满一逻辑帧就走一步（与服务端 ServerLoop 同节拍口径）。
            _tickAccumMs += UnityEngine.Time.unscaledDeltaTime * 1000.0;
            double stepMs = 1000.0 / SimConfig.TickRate;

            int guard = 0;
            while (_tickAccumMs >= stepMs && guard++ < 8)      // 上限防追帧雪崩（与 FrameDriver 同思路）
            {
                _tickAccumMs -= stepMs;
                _nowMs += (long)stepMs;

                _testOverrides.Apply(_joined, _playerId);
                _runtime.Execute(RoomCommand.Tick(_nowMs), _outputs);
                DrainOutputs();

                _pipeline.BroadcastIfDue(_runtime.AuthSim.Frame, _runtime.AuthSim, _runtime.Gate,
                    entityIdOf: p => _runtime.EntityIdOf(p),
                    seatBroadcastable: p => _seats[p] != null && !_seats[p].Disconnected);

                // 事件面与状态面同节拍（本地服与真服同一条纪律——否则"本地能复现、联机复现不了"）。
                if (_pipeline.BroadcastedThisFrame)
                    _events.Publish(_runtime.AuthSim.Frame,
                        p => _seats[p] != null && !_seats[p].Disconnected);

                FlushOutbox();
            }
        }

        public void TickIncoming()
        {
            // 收包在 Send/TickOutgoing 内同步产生并直接回调；连接建立事件在此补发（见 Connect 注释）。
            if (!_connectPending) return;
            _connectPending = false;
            _connected = true;
            OnConnected?.Invoke();                   // Join 随后经 Send 同步处理，收发在同一泵内闭环
        }

        /// <summary>内核输出 → 协议包入 outbox（对应真服务端 ServerHost.ApplySignal/ApplyOutput 的那几行）。
        /// <paramref name="admitIsLocal"/> = 该批输出里的 PlayerAdmitted 是否属于本地连接：
        /// 补位（<see cref="FillRoomWithBots"/>）传 false——只收 MatchStarted，不占号位不回包。</summary>
        private void DrainOutputs(bool admitIsLocal = true)
        {
            for (int i = 0; i < _outputs.Count; i++)
            {
                RoomOutput output = _outputs[i];

                if (output is SignalOutput so)
                {
                    switch (so.Signal)
                    {
                        case PlayerAdmitted pa:
                        {
                            if (!admitIsLocal) break;   // 补位席位：静默（假连接不对应任何客户端）
                            _playerId = pa.PlayerId;
                            _joined = true;
                            _seats[pa.PlayerId] = new Session(Conn, _nowMs) { PlayerId = pa.PlayerId };
                            // 回执 proto 单源（HandshakePacker，与真服共一份——字段面结构性一致；
                            // 凭据差异经参数注入：本地形态无重连语义，占位串 + 0 秒窗口）
                            _outbox.Add((PacketType.JoinAck, HandshakePacker.BuildJoinAck(pa,
                                "local-dev-" + pa.PlayerId, 0, _runtime.MapId, _runtime.ExpectedPlayers,
                                _runtime.Config.SnapshotHz)));
                            break;
                        }
                        case MatchStarted ms:
                            _outbox.Add((PacketType.StartGame, HandshakePacker.BuildStartGame(ms)));
                            break;
                    }
                    continue;
                }

                // 终局信令（与真服同一条链）：对局中收尾 SettlementReady → MatchEnded（带结果）。
                switch (output)
                {
                    case SettlementReadyOutput sr:
                        _outbox.Add((PacketType.MatchEnded, HandshakePacker.BuildMatchEnded(sr.Summary)));
                        break;
                    case RoomAbortedOutput ra:
                        _outbox.Add((PacketType.MatchEnded, HandshakePacker.BuildAbortedMatchEnded(ra)));
                        break;
                }
            }
            _outputs.Clear();
        }

        /// <summary>
        /// 宿主级关闭（运维口径镜像——与真服投递 RoomCommand.Shutdown(ShutdownReason) 同入口）： 对局中 → 结算收尾（SettlementReady → MatchEnded 带结果）。
        /// </summary>
        public void ShutdownHost(ShutdownReason reason)
        {
            if (_disposed) return;
            _runtime.Execute(RoomCommand.Shutdown(reason), _outputs);
            DrainOutputs();
            FlushOutbox();
        }

        /// <summary>outbox → <see cref="OnData"/>（与真传输同一个客户端收包入口）。</summary>
        private void FlushOutbox()
        {
            for (int i = 0; i < _outbox.Count; i++)
            {
                PacketType type = _outbox[i].type;
                byte[] bytes = PacketCodec.Encode(type, _outbox[i].msg);
                if (type == PacketType.StateSnapshot) SnapshotsSent++;
                // 不可靠面与真服同口径：状态快照与公共战斗事件批都走 Unreliable
                // （客户端收包入口按这个布尔分流，漂移会让本地服与真服行为不一致）。
                bool reliable = type != PacketType.StateSnapshot && type != PacketType.CombatEvents;
                OnData?.Invoke(new ArraySegment<byte>(bytes), reliable);
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

#endif
