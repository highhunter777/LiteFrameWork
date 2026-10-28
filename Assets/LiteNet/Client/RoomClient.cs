using System;
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
    /// 房间客户端（§4.5 协议矩阵的 C→S 侧封装）：Join/输入（**未确认段冗余窗口**——锚"最近快照帧+1"，
    /// 容量 16 帧）/MismatchReport 上报 + JoinAck/StartGame/StateSnapshot 事件化。
    /// 传输走窄端口 <see cref="IClientTransport"/>（双通道：信令 Reliable / 输入与快照 Unreliable）。
    /// 纯会话与打包——Sim 预测/和解不在此（客户端 Sim 侧由 RollbackSim 承担，M10 全量预测形态）。
    ///
    /// **重连状态机（§9.3）**：断线 → <see cref="ClientSessionPhase.SuspectedLost"/>；
    /// <see cref="BeginReconnect"/> 凭 JoinAck 下发的一次性票据重拨并自动发 ReconnectRequest；
    /// 响应 Ok 且版本确认（seed/配置摘要/构建哈希与 StartGame/Join 一致）→ <see cref="ClientSessionPhase.Restoring"/>
    /// （应用层经 OnReconnectResponse 应用权威快照与输入历史）→ <see cref="CompleteRestore"/> 发
    /// RestoreComplete（Reliable）→ 回 Connected。拒绝/版本不符/超时 → Failed（不自动重试——应用层决策）。
    ///
    /// **方向拆分（路线图 §4.6，M2→M3-6）**：入站解码/分发/快照水位归 <see cref="RoomInbound"/>，
    /// C→S 打包发送归 <see cref="RoomOutbound"/>，诊断发射点归 <see cref="RoomClientDiagnostics"/>；
    /// 会话事实与重连凭据归 <see cref="RoomSession"/> / <see cref="ReconnectState"/>，
    /// 输入冗余窗口与探针各自的件（<see cref="InputWindow"/> / <see cref="NetworkQualityProbe"/>）。
    /// 本类只留公共 API、会话编排（相位门控/换房重置/重连流程）与事件面。
    ///
    /// **生命周期**：传输是**注入依赖、所有权归创建方**——`Dispose` 只退订与
    /// 释放自身缓冲，**不 Dispose 传输**（持有方〔如 BattleClient〕负责它的生命周期）。
    /// </summary>
    public sealed class RoomClient : IDisposable
    {
        /// <summary>重连等待窗口（毫秒）：重拨/等待 ReconnectResponse 的总预算——超时进 Failed（§9.3"超时/拒绝 → Failed"）。</summary>
        public const long ReconnectTimeoutMs = 10_000;

        /// <summary>RTT/丢包探针发送周期（毫秒）。</summary>
        public const long ProbeIntervalMs = NetworkQualityProbe.IntervalMs;

        /// <summary>探针滚动窗口（丢包统计分母——越大越平滑、越钝；须 ≥ 1）。</summary>
        public const int ProbeWindow = NetworkQualityProbe.Window;

        private readonly IClientTransport _transport;
        private readonly Func<long> _nowMs;                                          // 单调毫秒（可注入：L1 用虚拟时钟）
        private readonly NetworkQualityProbe _probe = new NetworkQualityProbe();    // RTT/丢包探针
        private readonly ReconnectState _reconnect;                                 // 相位/票据/版本基准
        private readonly RoomSession _session = new RoomSession();                  // 席位/房主/载荷/终局事实
        private readonly RoomInbound _inbound;                                      // 入站分发/快照水位/重连响应
        private readonly RoomOutbound _outbound;                                    // C→S 打包发送

        private bool _disposed;

        public bool Connected => _transport.Connected;
        public int PlayerId => _session.PlayerId;

        /// <summary>本机是否房主（开房裁决 owner 位 + 房主迁移续批——**协议派生态单源**）：
        /// JoinAck.IsOwner 定初值；OwnerChanged 广播维护（PlayerId == 本机席位）。
        /// UI 的"开始对局"按钮归属按此事实（迁移后旧主重连不夺回——服务器侧确定性裁决，本端只消费）。</summary>
        public bool IsOwner => _session.IsOwner;

        /// <summary>本房地图行键（多图批；**JoinAck 权威事实**——JoinAck 定值，重连重进同源刷新）。
        /// 0 = 服务器未标注（嵌入式/测试直构形态）：消费者按首图口径回落（裁决单源见客户端 `BattleScenes.ResolveMapId`）。
        /// 本类只搬运协议事实，不认识地图表/场景。</summary>
        public int MapId => _session.MapId;

        /// <summary>本房期望人数（开房参数批；**JoinAck 权威事实**——JoinAck 定值，重连重进同源刷新）。
        /// 0 = 服务器未标注（嵌入式/测试直构形态）：消费者按默认 2 座回落
        /// （裁决单源见 `BattleContext` 的定容口径）。本类只搬运协议事实。</summary>
        public int ExpectedPlayers => _session.ExpectedPlayers;

        /// <summary>当前会话相位（§9.3 状态机的观测面）。</summary>
        public ClientSessionPhase Phase => _reconnect.Phase;

        /// <summary>网络质量观测（1Hz 探针推断的 RTT/丢包；表现/诊断面——不参与任何游戏逻辑或准入）。</summary>
        public NetworkQuality Quality => _probe.Quality(Phase == ClientSessionPhase.Connected);

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
        public int LastAckSnapshot => _inbound.LastAckInput;

        /// <summary>最近一次收到的快照帧号（**单调**：旧包/重复包在入口丢弃）。视点帧由
        /// `SimView.RenderFrame`（jitter buffer 渲染帧）提供，见 `BattleContext.ViewRenderFrame`。</summary>
        public int LastSnapshotFrame => _inbound.LastSnapshotFrame;

        /// <summary>StartGame 是否已到达（版本确认基准已就位）。</summary>
        public bool HasStartGame => _reconnect.HasStartGame;

        /// <summary>
        /// 最近一次 StartGame（未到达 = null）。**晚订阅者必须先读这里**：
        /// <see cref="OnStartGame"/> 是**一次性边缘事件**（消息到达那一刻发一次，无订阅者即丢失），
        /// 而 StartGame 在时序上可能**早于**应用层建好订阅——开局广播（房主发起）与重连迟到帧
        /// 都可能落在应用层建对局对象之前（应用层往往要等 JoinAck 回来才开始建）。
        /// 消费范式：先查本属性，为 null 再订阅事件。
        /// </summary>
        public Proto.StartGame StartGame => _session.StartGame;

        /// <summary>最近一包携带的冗余帧数（诊断/测试用：= 未确认段长度（含本帧），≤ <see cref="InputPacker.MaxRedundancy"/>）。</summary>
        public int RedundancyWindowSize => _outbound.Input.LastWindowCount;

        public event Action<Proto.JoinAck> OnJoinAck;
        public event Action<Proto.StartGame> OnStartGame;
        public event Action<Proto.StateSnapshot> OnSnapshot;

        /// <summary>公共战斗事件批（独立包，与快照同节拍；《状态同步专项设计》§5.2 CombatEvent 面）。
        /// **独立于 <see cref="OnSnapshot"/>**：事件面有自己的包型与预算，状态流不再被事件量拖累
        /// **独立于状态快照**：事件量再大也不会把快照顶出不可靠上限（两者各有一份载荷预算）。</summary>
        public event Action<Proto.CombatEventBatch> OnCombatEvents;

        public event Action<Proto.MatchEnded> OnMatchEnded;

        /// <summary>开局请求被拒反馈（开房裁决：非房主/席位无效——UI 据此提示；
        /// 成功路径以 <see cref="OnStartGame"/> 广播为准，本事件只在被拒时到达）。</summary>
        public event Action<Proto.StartMatchResponse> OnStartMatchResponse;

        /// <summary>房主迁移广播（原房主离席 → 本消息带新房主席位——UI 换"开始对局"按钮归属；
        /// 旧房主重连不夺回——服务器侧确定性裁决，客户端只消费事实）。</summary>
        public event Action<Proto.OwnerChanged> OnOwnerChanged;

        /// <summary>席位实时面广播（全量快照——进房/断线/恢复完成时到达；UI 列表整表重绑。
        /// 载荷为接收实例，订阅方即取即用；缓存读侧 Clone 防御见 <see cref="Seats"/>）。</summary>
        public event Action<Proto.RoomSeats> OnRoomSeats;

        public Proto.MatchEnded MatchResult => _session.MatchResult;

        /// <summary>席位实时面最新快照（房间页玩家列表——进房/断线/恢复完成广播全量；未收到前为 null。
        /// 读侧 Clone 防御——同 <see cref="MatchResult"/> 先例：订阅方不得拿到可变的共享缓存实例）。</summary>
        public Proto.RoomSeats Seats => _session.Seats;

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
            _reconnect = new ReconnectState(_nowMs, ReconnectTimeoutMs);
            _reconnect.Changed += HandlePhaseChanged;
            _outbound = new RoomOutbound(_transport);
            _inbound = new RoomInbound(_session, _reconnect, _probe, _nowMs, _outbound.Send);

            // 入站事件照原样转发到公共订阅面（订阅方与拆分前逐项一致）
            _inbound.JoinAck += ack => OnJoinAck?.Invoke(ack);
            _inbound.StartGame += start => OnStartGame?.Invoke(start);
            _inbound.Snapshot += snapshot => OnSnapshot?.Invoke(snapshot);
            _inbound.CombatEvents += batch => OnCombatEvents?.Invoke(batch);
            _inbound.MatchEnded += ended => OnMatchEnded?.Invoke(ended);
            _inbound.StartMatchResponse += response => OnStartMatchResponse?.Invoke(response);
            _inbound.OwnerChanged += changed => OnOwnerChanged?.Invoke(changed);
            _inbound.RoomSeats += seats => OnRoomSeats?.Invoke(seats);
            _inbound.ReconnectResponse += response => OnReconnectResponse?.Invoke(response);
            _inbound.Disconnected += () => OnDisconnected?.Invoke();

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
            _reconnect.RememberEndpoint(host, port);       // 记住端点——BeginReconnect 重拨用
            _transport.Connect(host, port);
        }

        public void SendJoin(string roomId, string token, string buildHash)
        {
            _session.ResetOnJoin();
            _outbound.Input.Reset();
            _inbound.ResetSnapshotWatermark();
            _reconnect.ResetMatchIdentity();
            _reconnect.MarkJoinSent(roomId, buildHash);    // 版本确认基准（§9.3 步骤 2：重连响应须同值）
            RoomClientDiagnostics.JoinAttempt(roomId, PlayerId, buildHash, ContentContext);
            _outbound.Join(roomId, token, buildHash);
        }

        /// <summary>
        /// 发送房主开局请求（开房裁决：Reliable 空载荷——服务器按会话映射席位，
        /// owner 判定在 Runtime；**唯一开局入口**，满员/未满皆可发）。
        /// 未入房（Phase != Connected）调用为 no-op（UI 按相位禁用按钮，
        /// 这里再守一道——迟到点击不产生垃圾包）。被拒反馈见 <see cref="OnStartMatchResponse"/>；
        /// 成功即 <see cref="OnStartGame"/> 广播。
        /// </summary>
        public void SendStartMatchRequest()
        {
            if (Phase != ClientSessionPhase.Connected) return;
            _outbound.StartMatchRequest();
        }

        /// <summary>
        /// 发送逐帧输入（**未确认段冗余**；viewFrame = 开火时刻所见帧，延迟补偿回溯点，§3.4.1）。
        ///
        /// 冗余语义：包里带的是**本帧 + 往回连续的历史帧**（每帧各自的内容与开火位），窗口下界
        /// 锚在"<see cref="LastSnapshotFrame"/> + 1"——服务器已广播确认的帧之后都可能还没被消费。
        /// 为什么锚快照而不是固定"最近 N 帧"：预测领先（本地帧 − 权威帧）在本形态是常态，
        /// 固定窗一旦被领先甩出，服务器要的 `serverFrame + 1` 永远不在窗内 → 整包被判"越界"丢弃、
        /// 输入链锁死（现场实测：接受数冻结、越界按包增长、客户端不停和解回滚）。
        /// 上界双约束：环内连续可用帧数（跳帧后绝不用陈旧帧冒充缺失帧）与窗口容量。
        /// 服务器按 `InputPacker.TryGetFrame(msg, frame)` 逐帧取用 → 丢一包仍能从后续包补帧。
        ///
        /// **ack 语义**：ackSnapshot = **已收最新快照帧号**（<see cref="LastSnapshotFrame"/>，协议注释口径）——
        /// 服务器按发送 ledger 验证 ACK，未发送过的帧号会被整体忽略；未收到快照时填 0。
        /// </summary>
        public void SendInput(int frame, in SimInputFrame input, int viewFrame)
        {
            if (_session.HasMatchResult) return;
            _outbound.Input.Record(frame, input);

            int lastSnapshotFrame = _inbound.LastSnapshotFrame;
            ReadOnlySpan<SimInputFrame> window = _outbound.Input.Build(frame, lastSnapshotFrame);
            int ackSnapshot = lastSnapshotFrame >= 0 ? lastSnapshotFrame : 0;
            _outbound.SendInput(frame, window, ackSnapshot, viewFrame);
        }

        /// <summary>和解上报（Ops 汇总和解率——§4.5 关键机制 4）。</summary>
        public void SendMismatch(int frame) => _outbound.Mismatch(frame);

        // ---- 重连状态机（§9.3）----

        /// <summary>
        /// 发起重连（SuspectedLost → Reconnecting）：凭 JoinAck 票据重拨传输——连接建立后自动发
        /// ReconnectRequest（Reliable）。返回 false = 未发起（相位不符，或无票据 → 已转 Failed——
        /// 票据一次性，耗尽后唯一出路是重新 Join，由应用层决策）。
        /// </summary>
        public bool BeginReconnect()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RoomClient));
            if (_session.HasMatchResult) return false;
            if (!_reconnect.BeginReconnect(out string host, out int port)) return false;
            _transport.Connect(host, port);                // KcpTransportClient.Connect 支持重拨（内部重建 KcpClient）
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
            if (Phase != ClientSessionPhase.Restoring) return false;

            _outbound.RestoreComplete();
            _reconnect.Transition(ClientSessionPhase.Connected);
            return true;
        }

        /// <summary>相位迁移副作用：离开在房/恢复相位即重置探针窗口（新会话/重连不把上一段的丢包带进本轮统计）。</summary>
        private void HandlePhaseChanged(ClientSessionPhase from, ClientSessionPhase to)
        {
            if (to != ClientSessionPhase.Connected && to != ClientSessionPhase.Restoring) _probe.Reset();
            OnPhaseChanged?.Invoke(from, to);
        }

        /// <summary>
        /// RTT/丢包探针（1Hz，Unreliable）：仅 Connected/Restoring 相位发——离线期不产生新样本
        /// （<see cref="NetworkQualityProbe.Reset"/> 已把窗口清零，展示面据 <see cref="NetworkQuality.Connected"/> 决定是否显示）。
        /// </summary>
        private void PumpProbe()
        {
            if (_disposed) return;
            ClientSessionPhase phase = Phase;
            if (phase != ClientSessionPhase.Connected && phase != ClientSessionPhase.Restoring) return;
            _probe.Pump(_nowMs(), time => _outbound.Heartbeat(time));
        }

        /// <summary>重拨连接建立（Reconnecting 中）：自动发 ReconnectRequest；连接等待窗重置（握手耗时不再挤占响应预算）。</summary>
        private void HandleTransportConnected() => _inbound.OnTransportConnected();

        /// <summary>传输断开：在房/恢复中 → SuspectedLost（席位仍在重连窗口内）；Reconnecting 保持（超时线裁决）。</summary>
        private void HandleTransportDisconnected() => _inbound.OnTransportDisconnected(ContentContext);

        public void TickIncoming() { _reconnect.CheckDeadline(); _transport.TickIncoming(); }
        public void TickOutgoing() { _reconnect.CheckDeadline(); PumpProbe(); _transport.TickOutgoing(); }

        public void Disconnect() => _transport.Disconnect();

        /// <summary>优雅离场（可靠）：服务端按 Leave 立即释放席位（与传输断线同一控制面语义，但不等重连窗口）。
        /// 仅 Connected/Restoring 发送；未入座/已断开/已释放 → no-op（调用方不必判相位）。</summary>
        public void SendLeave()
        {
            if (_disposed) return;
            ClientSessionPhase phase = Phase;
            if (phase != ClientSessionPhase.Connected && phase != ClientSessionPhase.Restoring) return;
            _outbound.Leave();
        }

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
            _reconnect.Changed -= HandlePhaseChanged;
            _outbound.Dispose();
        }

        private void OnWireData(ArraySegment<byte> data, bool reliable) => _inbound.Handle(data, reliable);
    }
}
