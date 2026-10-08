using System;
using LiteNet.Diagnostics;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;

namespace LiteNet
{
    /// <summary>
    /// 客户端会话相位（《商业级通用服务端框架总设计》§9.3 重连闭环）：
    /// <c>Connected → SuspectedLost → Reconnecting → Restoring → Connected</c>；<c>Reconnecting</c> 超时/被拒 → <see cref="Failed"/>。
    /// </summary>
    public enum ClientSessionPhase
    {
        /// <summary>未进房（构造后、JoinAck 前）。</summary>
        Idle = 0,
        /// <summary>在房（JoinAck 已达；输入/快照/预测正常）。</summary>
        Connected,
        /// <summary>连接已断（传输 OnDisconnected）——席位仍在服务器重连窗口内，可凭票据恢复。</summary>
        SuspectedLost,
        /// <summary>重连中：已重拨传输并发 ReconnectRequest，等待响应（<see cref="RoomClient.ReconnectTimeoutMs"/> 内无果 → Failed）。</summary>
        Reconnecting,
        /// <summary>恢复中：验票通过且版本确认一致——快照/历史已交应用层（OnReconnectResponse），等 <see cref="RoomClient.CompleteRestore"/> 宣告完成。</summary>
        Restoring,
        /// <summary>终态：票据无效/已耗尽、版本不符或超时——需应用层决策（重 Join 或退出版本提示）。</summary>
        Failed,
    }

    /// <summary>
    /// 房间客户端（§4.5 协议矩阵的 C→S 侧封装）：
    /// Join/输入（**最近 ≤4 帧冗余窗口**）/MismatchReport 上报 + JoinAck/StartGame/StateSnapshot 事件化。
    /// 传输走窄端口 <see cref="IClientTransport"/>（双通道：信令 Reliable / 输入与快照 Unreliable）。
    /// 纯会话与打包——Sim 预测/和解不在此（客户端 Sim 侧由 RollbackSim 承担，M10 全量预测形态）。
    ///
    /// **重连状态机（§9.3）**：断线 → <see cref="ClientSessionPhase.SuspectedLost"/>；
    /// <see cref="BeginReconnect"/> 凭 JoinAck 下发的一次性票据重拨并自动发 ReconnectRequest；
    /// 响应 Ok 且版本确认（seed/配置摘要/构建哈希与 StartGame/Join 一致）→ <see cref="ClientSessionPhase.Restoring"/>
    /// （应用层经 OnReconnectResponse 应用权威快照与输入历史）→ <see cref="CompleteRestore"/> 发
    /// RestoreComplete（Reliable）→ 回 Connected。拒绝/版本不符/超时 → Failed（不自动重试——应用层决策）。
    ///
    /// **生命周期**：传输是**注入依赖、所有权归创建方**——`Dispose` 只退订与
    /// 释放自身缓冲，**不 Dispose 传输**（`KcpNetworkService` 会持有并管理它）。
    /// </summary>
    public sealed class RoomClient : IDisposable
    {
        /// <summary>重连等待窗口（毫秒）：重拨/等待 ReconnectResponse 的总预算——超时进 Failed（§9.3"超时/拒绝 → Failed"）。</summary>
        public const long ReconnectTimeoutMs = 10_000;

        private readonly IClientTransport _transport;
        private readonly Func<long> _nowMs;                                          // 单调毫秒（可注入：L1 用虚拟时钟）
        private readonly PacketWriter _writer = new PacketWriter();                 // 复用编码（高频输入零分配）
        private readonly SimInputFrame[] _ring = new SimInputFrame[InputPacker.MaxRedundancy];   // 环形：frame % Max
        private readonly SimInputFrame[] _window = new SimInputFrame[InputPacker.MaxRedundancy]; // 打包用连续窗口
        private int _recentFrame = -1;      // 最近记录的逻辑帧（-1 = 尚未记录）
        private int _recentCount;           // 从 _recentFrame 往回**连续**可用的帧数（跳帧即重置为 1）
        private int _lastInputAck;          // 最近收到的"输入确认"（服务器 AckInput 字段——诊断用，不上报）
        private bool _disposed;

        // ---- 重连状态机（§9.3）----
        private ClientSessionPhase _phase = ClientSessionPhase.Idle;
        private string _reconnectToken;      // JoinAck 下发的一次性票据（断线重连凭据）
        private string _joinedBuildHash;     // 本端 Join 时发送的构建哈希（版本确认比对基准）
        private string _joinAttemptRoomId;   // 最近一次进房尝试的目标房间（诊断记录用）
        private bool _joinAttemptPending;    // 已发 Join 未收 JoinAck（诊断：断线记录据此判定"未获应答"）
        private string _lastHost;            // 最近一次连接端点（重拨用）
        private int _lastPort;
        private bool _hasStartGame;         // StartGame 已达（版本确认的 seed/配置基准）
        private Proto.StartGame _startGame; // **最近一次 StartGame 载荷**（晚订阅者补发用——见 OnStartGame 的契约）
        private long _matchSeed;
        private uint _matchConfigHash;
        private long _reconnectDeadlineMs;   // Reconnecting 相位的超时线（注入时钟口径）

        public bool Connected => _transport.Connected;
        public int PlayerId { get; private set; } = -1;

        /// <summary>当前会话相位（§9.3 状态机的观测面）。</summary>
        public ClientSessionPhase Phase => _phase;

        /// <summary>
        /// 内容/事务诊断上下文（**组合根注入**；null/空 = 未注入）：发布身份/内容代次/激活事务 ID 的自由文本，
        /// 进房诊断记录会带上它——本类不认识内容系统，但"这次进房用的是哪份内容"必须能与内容/事务面关联
        /// （《框架先行》§8「可诊断」）。示例：`release=rel-7;gen=3;txn=txn-abc`。
        /// </summary>
        public string ContentContext { get; set; }

        /// <summary>会话相位迁移通知（从/到；Failed 原因见 OnReconnectResponse 或超时事实本身）。</summary>
        public event Action<ClientSessionPhase, ClientSessionPhase> OnPhaseChanged;
        /// <summary>
        /// 最近一次收到的**输入确认**（= 服务器已接受本客户端输入到的帧号，快照的 `AckInput` 字段）。
        /// ⚠️ 它**不是**"最新收到的快照帧号"——别拿它算视点帧（§3.4.1 的"视角帧"要的是快照帧，
        /// 见 <see cref="LastSnapshotFrame"/>；此处命名沿协议字段避免误用）。
        /// </summary>
        public int LastAckSnapshot => _lastInputAck;

        /// <summary>最近一次收到的快照帧号（**视点帧推导的正确来源**：+ `SimConfig.InterpFrames` = 玩家所见帧，§3.4.1）。</summary>
        public int LastSnapshotFrame { get; private set; } = -1;

        /// <summary>StartGame 是否已到达（版本确认基准已就位）。</summary>
        public bool HasStartGame => _hasStartGame;

        /// <summary>
        /// 最近一次 StartGame（未到达 = null）。**晚订阅者必须先读这里**：
        /// <see cref="OnStartGame"/> 是**一次性边缘事件**（消息到达那一刻发一次，无订阅者即丢失），
        /// 而 StartGame 在时序上可能**早于**应用层建好订阅——服务器在席位满员时立即开局广播，
        /// 而应用层往往要等 JoinAck 回来才开始建对局对象。
        /// 消费范式：先查本属性，为 null 再订阅事件。
        /// </summary>
        public Proto.StartGame StartGame => _startGame;

        /// <summary>当前冗余窗口可带的帧数（诊断/测试用：= 从最新帧往回连续可用的输入帧数，≤ 4）。</summary>
        public int RedundancyWindowSize => _recentCount;

        public event Action<Proto.JoinAck> OnJoinAck;
        public event Action<Proto.StartGame> OnStartGame;
        public event Action<Proto.StateSnapshot> OnSnapshot;
        public event Action<Proto.MatchEnded> OnMatchEnded;
        private Proto.MatchEnded _matchResult;
        private string _activeMatchId;
        public Proto.MatchEnded MatchResult => _matchResult?.Clone();
        /// <summary>重连响应（§5.6/§9.3：版本确认 + 权威快照[公共全量+比赛状态+本人私有] + 输入历史）。
        /// 应用原语见 <see cref="Protocol.SnapshotReassembler"/>（镜像 Apply + 输入历史喂 RollbackSim）。
        /// 契约：只有 <see cref="Phase"/> == <see cref="ClientSessionPhase.Restoring"/> 时本响应可应用——
        /// 应用完毕调用 <see cref="CompleteRestore"/>（否则服务器持续抑制本席位增量广播）。</summary>
        public event Action<Proto.ReconnectResponse> OnReconnectResponse;
        public event Action OnDisconnected;

        public RoomClient(IClientTransport transport, Func<long> nowMsProvider = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _nowMs = nowMsProvider ?? DefaultNowMs;
            _transport.OnData += OnWireData;
            _transport.OnConnected += HandleTransportConnected;
            _transport.OnDisconnected += HandleTransportDisconnected;
        }

        /// <summary>默认单调毫秒（Stopwatch 时间戳换算——Unity netstandard 与 .NET 8 双端可用；
        /// TickCount64 是 .NET 6+ 专有，Unity 编译不过。L1 注入虚拟时钟替换）。</summary>
        private static long DefaultNowMs()
            => System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;

        public void Connect(string host, int port)
        {
            _lastHost = host;                              // 记住端点——BeginReconnect 重拨用
            _lastPort = port;
            _transport.Connect(host, port);
        }

        public void SendJoin(string roomId, string token, string buildHash)
        {
            _matchResult = null;
            _hasStartGame = false;
            _activeMatchId = null;
            _startGame = null;
            _recentCount = 0;
            _recentFrame = -1;
            LastSnapshotFrame = -1;
            _joinedBuildHash = buildHash;                 // 版本确认基准（§9.3 步骤 2：重连响应须同值）
            _joinAttemptRoomId = roomId;
            _joinAttemptPending = true;
            // 结构化诊断（§8「可诊断」）：与服务器 RejectJoin 的拒绝记录**同键**（DiagTrace.JoinKey 单源）——
            // detail 带内容/事务上下文，使一次失败可把 版本/内容/事务/会话/房间 的记录串起来。
            DiagTrace.Emit(DiagStage.Session, DiagCode.JoinAttempt,
                DiagTrace.JoinKey(roomId, PlayerId, buildHash),
                "content=" + (string.IsNullOrEmpty(ContentContext) ? "-" : ContentContext));
            Send(PacketType.Join, new Proto.JoinRequest { RoomId = roomId, Token = token, BuildHash = buildHash }, reliable: true);
        }

        /// <summary>
        /// 发送逐帧输入（**最近 ≤4 帧冗余**）；viewFrame = 开火时刻所见帧（延迟补偿回溯点，§3.4.1）。
        ///
        /// 冗余语义：包里带的是**本帧 + 往回连续的历史帧**（每帧各自的内容与开火位），
        /// 服务器按 `InputPacker.TryGetFrame(msg, frame)` 逐帧取用 → 丢一包仍能从后续包补帧。
        /// 打包统一走 <see cref="InputPacker.Pack"/>（协议单源）。
        ///
        /// **ack 语义**：ackSnapshot = **已收最新快照帧号**（<see cref="LastSnapshotFrame"/>，协议注释口径）——
        /// 服务器按发送 ledger 验证 ACK，未发送过的帧号会被整体忽略；未收到快照时填 0。
        /// </summary>
        public void SendInput(int frame, in SimInputFrame input, int viewFrame)
        {
            if (_matchResult != null) return;
            RecordRecent(frame, input);

            for (int i = 0; i < _recentCount; i++)
                _window[i] = _ring[RingIndex(frame - i)];

            int ackSnapshot = LastSnapshotFrame >= 0 ? LastSnapshotFrame : 0;
            var msg = InputPacker.Pack(frame, new ReadOnlySpan<SimInputFrame>(_window, 0, _recentCount),
                ackSnapshot, viewFrame);
            Send(PacketType.Input, msg, reliable: false);
        }

        /// <summary>和解上报（Ops 汇总和解率——§4.5 关键机制 4）。</summary>
        public void SendMismatch(int frame)
        {
            Send(PacketType.MismatchReport, new Proto.MismatchReport { Frame = frame }, reliable: false);
        }

        // ---- 重连状态机（§9.3）----

        /// <summary>
        /// 发起重连（SuspectedLost → Reconnecting）：凭 JoinAck 票据重拨传输——连接建立后自动发
        /// ReconnectRequest（Reliable）。返回 false = 未发起（相位不符，或无票据 → 已转 Failed——
        /// 票据一次性，耗尽后唯一出路是重新 Join，由应用层决策）。超时窗口 <see cref="ReconnectTimeoutMs"/>。
        /// </summary>
        public bool BeginReconnect()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomClient));
            if (_matchResult != null) return false;
            if (_phase != ClientSessionPhase.SuspectedLost) return false;

            if (string.IsNullOrEmpty(_reconnectToken))
            {
                Transition(ClientSessionPhase.Failed);   // 无票据：重连窗口已耗尽/从未进房
                return false;
            }

            Transition(ClientSessionPhase.Reconnecting);
            _reconnectDeadlineMs = _nowMs() + ReconnectTimeoutMs;
            _transport.Connect(_lastHost, _lastPort);    // KcpTransportClient.Connect 支持重拨（内部重建 KcpClient）
            return true;
        }

        /// <summary>
        /// 宣告恢复完成（Restoring → Connected）：应用层已应用权威快照与输入历史（OnReconnectResponse），
        /// 发送 RestoreComplete（Reliable——丢失即重传；服务器收到前抑制本席位增量广播，§9.3 步骤 6）。
        /// 返回 false = 相位不符（未在恢复中——重复宣告/未验票成功均无害忽略）。
        /// </summary>
        public bool CompleteRestore()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomClient));
            if (_phase != ClientSessionPhase.Restoring) return false;

            Send(PacketType.RestoreComplete, new Proto.RestoreComplete(), reliable: true);
            Transition(ClientSessionPhase.Connected);
            return true;
        }

        /// <summary>相位迁移唯一入口（非法路径不静默改态——见各调用点的前置校验）。</summary>
        private void Transition(ClientSessionPhase to)
        {
            if (_phase == to) return;
            ClientSessionPhase from = _phase;
            _phase = to;
            OnPhaseChanged?.Invoke(from, to);
        }

        /// <summary>重拨连接建立（Reconnecting 中）：自动发 ReconnectRequest；连接等待窗重置（握手耗时不再挤占响应预算）。</summary>
        private void HandleTransportConnected()
        {
            if (_phase != ClientSessionPhase.Reconnecting) return;

            _reconnectDeadlineMs = _nowMs() + ReconnectTimeoutMs;
            Send(PacketType.ReconnectRequest, new Proto.ReconnectRequest { OneTimeToken = _reconnectToken }, reliable: true);
        }

        /// <summary>
        /// 重连响应（§9.3 步骤 2~4）：Ok 且版本确认一致 → Restoring（应用层应用快照/历史后
        /// <see cref="CompleteRestore"/>）；Ok 但版本不符 → Failed（绝不发恢复 ACK——不是同一局）；
        /// 拒绝 → Failed。OnReconnectResponse 照常派发（应用层据 Phase 判定是否可应用）。
        /// </summary>
        private void HandleReconnectResponse(Proto.ReconnectResponse response)
        {
            if (_phase == ClientSessionPhase.Reconnecting)
            {
                if (response.Ok && VersionMatches(response))
                    Transition(ClientSessionPhase.Restoring);
                else
                    Transition(ClientSessionPhase.Failed);
            }
            OnReconnectResponse?.Invoke(response);
        }

        /// <summary>版本确认（§9.3 步骤 2）：seed/配置摘要/构建哈希须与 StartGame/Join 事实一致——跨版本混房即失败。</summary>
        private bool VersionMatches(Proto.ReconnectResponse response)
        {
            if (_hasStartGame && (response.Seed != _matchSeed || response.ConfigHash != _matchConfigHash)) return false;
            if (_joinedBuildHash != null && response.BuildHash != _joinedBuildHash) return false;
            return true;
        }

        /// <summary>Reconnecting 超时巡检（§9.3"超时 → Failed"；Tick 驱动——无独立线程/定时器）。</summary>
        private void CheckReconnectDeadline()
        {
            if (_phase != ClientSessionPhase.Reconnecting) return;
            if (_nowMs() < _reconnectDeadlineMs) return;
            Transition(ClientSessionPhase.Failed);
        }

        public void TickIncoming() { CheckReconnectDeadline(); _transport.TickIncoming(); }
        public void TickOutgoing() { CheckReconnectDeadline(); _transport.TickOutgoing(); }

        public void Disconnect() => _transport.Disconnect();

        /// <summary>
        /// 退订 + 释放自身缓冲；**不 Dispose 传输**（注入依赖，所有权归创建方）。
        /// 退订是必需的：传输通常比 RoomClient 活得久（重连/换房/服务重建），漏退订会重复回调与内存滞留。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.OnData -= OnWireData;
            _transport.OnConnected -= HandleTransportConnected;
            _transport.OnDisconnected -= HandleTransportDisconnected;
            _writer.Dispose();
        }

        /// <summary>
        /// 记录本帧输入并维护冗余窗口（**只向前**、**只取连续段**）：
        /// - 重发同帧（frame == _recentFrame）：只覆盖内容，不推进窗口；
        /// - 更早的帧：忽略（窗口语义是"最近若干帧"，回退无意义）；
        /// - 跳帧（frame &gt; _recentFrame + 1）：连续性断裂 → 窗口重置为 1（**绝不用陈旧帧冒充缺失帧**，
        ///   否则服务器会把上一帧的移动当成这一帧的输入）。
        /// </summary>
        private void RecordRecent(int frame, in SimInputFrame input)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame), frame, "逻辑帧号不能为负");

            if (_recentFrame >= 0 && frame <= _recentFrame)
            {
                if (frame == _recentFrame) _ring[RingIndex(frame)] = input;   // 重发：覆盖即可
                return;
            }

            if (_recentFrame < 0) _recentCount = 1;
            else if (frame == _recentFrame + 1) _recentCount = Math.Min(InputPacker.MaxRedundancy, _recentCount + 1);
            else _recentCount = 1;                                            // 跳帧 → 断连续

            _ring[RingIndex(frame)] = input;
            _recentFrame = frame;
        }

        private static int RingIndex(int frame) => ((frame % InputPacker.MaxRedundancy) + InputPacker.MaxRedundancy)
                                                   % InputPacker.MaxRedundancy;

        private void Send(PacketType type, Google.Protobuf.IMessage message, bool reliable)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomClient));
            // 复用缓冲：传输同步拷贝（kcp2k BlockCopy → 内部缓冲），返回后即可被下次 Write 覆盖
            _transport.Send(_writer.Write(type, message), reliable);
        }

        /// <summary>传输断开：在房/恢复中 → SuspectedLost（席位仍在重连窗口内）；Reconnecting 保持（超时线裁决）。</summary>
        private void HandleTransportDisconnected()
        {
            // 诊断（§8「可诊断」）：发了 Join 又没等到 JoinAck 就断开——服务器侧拒绝（未回原因，见
            // ServerHost.RejectJoin）的**客户端侧事实**；与服务器记录同键，两端据此对齐。
            if (_joinAttemptPending && _phase == ClientSessionPhase.Idle)
            {
                _joinAttemptPending = false;
                DiagTrace.Emit(DiagStage.Session, DiagCode.JoinNoAckDisconnected,
                    DiagTrace.JoinKey(_joinAttemptRoomId, PlayerId, _joinedBuildHash),
                    "content=" + (string.IsNullOrEmpty(ContentContext) ? "-" : ContentContext));
            }
            if (_matchResult == null && (_phase == ClientSessionPhase.Connected || _phase == ClientSessionPhase.Restoring))
                Transition(ClientSessionPhase.SuspectedLost);
            OnDisconnected?.Invoke();
        }

        private void OnWireData(ArraySegment<byte> data, bool reliable)
        {
            if (!PacketCodec.TryDecode(data, out var type, out var msg)) return;
            switch (type)
            {
                case PacketType.JoinAck:
                    var ack = (Proto.JoinAck)msg;
                    PlayerId = ack.PlayerId;
                    _joinAttemptPending = false;                // 进房已有应答（诊断判定用）
                    _reconnectToken = string.IsNullOrEmpty(ack.ReconnectToken) ? null : ack.ReconnectToken;
                    Transition(ClientSessionPhase.Connected);   // 进房/重新 Join 均以 JoinAck 为准
                    OnJoinAck?.Invoke(ack);
                    break;
                case PacketType.StartGame:
                    var start = (Proto.StartGame)msg;
                    _matchSeed = start.Seed;                     // §9.3 步骤 2：重连版本确认基准
                    _activeMatchId = string.IsNullOrEmpty(start.MatchId) ? _joinAttemptRoomId : start.MatchId;
                    _matchConfigHash = start.ConfigHash;
                    _startGame = start;                          // 缓存载荷：晚订阅者靠它补发（见 StartGame 属性）
                    _hasStartGame = true;
                    OnStartGame?.Invoke(start);
                    break;
                case PacketType.MatchEnded:
                    if (_matchResult != null || !reliable) break;
                    var ended = (Proto.MatchEnded)msg;
                    if (ended.MatchId != _activeMatchId || !_hasStartGame) break;
                    _matchResult = ended;
                    _reconnectToken = null;
                    OnMatchEnded?.Invoke(ended.Clone());
                    break;
                case PacketType.StateSnapshot:
                    if (_matchResult != null) break;
                    var snapshot = (Proto.StateSnapshot)msg;
                    _lastInputAck = snapshot.AckInput;          // 诊断：服务器输入确认（不上报——上报的是快照帧）
                    LastSnapshotFrame = snapshot.Frame;
                    OnSnapshot?.Invoke(snapshot);
                    break;
                case PacketType.ReconnectResponse:
                    HandleReconnectResponse((Proto.ReconnectResponse)msg);
                    break;
            }
        }
    }
}
