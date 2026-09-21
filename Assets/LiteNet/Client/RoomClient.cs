using System;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;

namespace LiteNet
{
    /// <summary>
    /// 房间客户端（§4.5 协议矩阵的 C→S 侧封装）：
    /// Join/输入（**最近 ≤4 帧冗余窗口**）/MismatchReport 上报 + JoinAck/StartGame/StateSnapshot 事件化。
    /// 传输走窄端口 <see cref="IClientTransport"/>（双通道：信令 Reliable / 输入与快照 Unreliable）。
    /// 纯会话与打包——Sim 预测/和解不在此（客户端 Sim 侧由 RollbackSim 承担，M10 全量预测形态）。
    ///
    /// **生命周期（2026-09-19 审查修正）**：传输是**注入依赖、所有权归创建方**——`Dispose` 只退订与
    /// 释放自身缓冲，**不 Dispose 传输**（M11 的 `KcpNetworkService` 会持有并管理它）。
    /// </summary>
    public sealed class RoomClient : IDisposable
    {
        private readonly IClientTransport _transport;
        private readonly PacketWriter _writer = new PacketWriter();                 // 复用编码（高频输入零分配）
        private readonly SimInputFrame[] _ring = new SimInputFrame[InputPacker.MaxRedundancy];   // 环形：frame % Max
        private readonly SimInputFrame[] _window = new SimInputFrame[InputPacker.MaxRedundancy]; // 打包用连续窗口
        private int _recentFrame = -1;      // 最近记录的逻辑帧（-1 = 尚未记录）
        private int _recentCount;           // 从 _recentFrame 往回**连续**可用的帧数（跳帧即重置为 1）
        private int _lastInputAck;          // 最近收到的"输入确认"（服务器 AckInput 字段——诊断用，不上报）
        private bool _disposed;

        public bool Connected => _transport.Connected;
        public int PlayerId { get; private set; } = -1;
        /// <summary>
        /// 最近一次收到的**输入确认**（= 服务器已接受本客户端输入到的帧号，快照的 `AckInput` 字段）。
        /// ⚠️ 它**不是**"最新收到的快照帧号"——别拿它算视点帧（§3.4.1 的"视角帧"要的是快照帧，
        /// 见 <see cref="LastSnapshotFrame"/>；此处命名沿协议字段，2026-09-19 审查加注避免误用）。
        /// </summary>
        public int LastAckSnapshot => _lastInputAck;

        /// <summary>最近一次收到的快照帧号（**视点帧推导的正确来源**：+ `SimConfig.InterpFrames` = 玩家所见帧，§3.4.1）。</summary>
        public int LastSnapshotFrame { get; private set; } = -1;

        /// <summary>当前冗余窗口可带的帧数（诊断/测试用：= 从最新帧往回连续可用的输入帧数，≤ 4）。</summary>
        public int RedundancyWindowSize => _recentCount;

        public event Action<Proto.JoinAck> OnJoinAck;
        public event Action<Proto.StartGame> OnStartGame;
        public event Action<Proto.StateSnapshot> OnSnapshot;
        /// <summary>重连响应（§5.6：权威快照[公共全量+比赛状态+本人私有] + 后续输入历史；P0 前该包被静默丢弃）。
        /// 应用原语见 <see cref="Protocol.SnapshotReassembler"/>（镜像 Apply + 输入历史喂 RollbackSim）。</summary>
        public event Action<Proto.ReconnectResponse> OnReconnectResponse;
        public event Action OnDisconnected;

        public RoomClient(IClientTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.OnData += OnWireData;
            _transport.OnDisconnected += HandleTransportDisconnected;
        }

        public void Connect(string host, int port) => _transport.Connect(host, port);

        public void SendJoin(string roomId, string token, string buildHash)
        {
            Send(PacketType.Join, new Proto.JoinRequest { RoomId = roomId, Token = token, BuildHash = buildHash }, reliable: true);
        }

        /// <summary>
        /// 发送逐帧输入（**最近 ≤4 帧冗余**）；viewFrame = 开火时刻所见帧（延迟补偿回溯点，§3.4.1）。
        ///
        /// 冗余语义（2026-09-19 审查修正）：包里带的是**本帧 + 往回连续的历史帧**（每帧各自的内容与开火位），
        /// 服务器按 `InputPacker.TryGetFrame(msg, frame)` 逐帧取用 → 丢一包仍能从后续包补帧。
        /// 修正前实现把同一份"最新输入"重复 4 次（冗余形同虚设，且开火位只留首个）——那是**协议语义破损**，
        /// 不只是低效。此处统一走 <see cref="InputPacker.Pack"/>（协议单源）。
        ///
        /// **ack 语义（R0-P0-4 修正）**：ackSnapshot = **已收最新快照帧号**（<see cref="LastSnapshotFrame"/>，
        /// 协议注释口径）——修正前错报服务器下发的 AckInput（输入确认帧号），服务器按发送 ledger 验证 ACK 后
        /// 这种"从未发送过的帧号"会被整体忽略 → 背压水位永不释放 + NeedsFull 常真。未收到快照时填 0。
        /// </summary>
        public void SendInput(int frame, in SimInputFrame input, int viewFrame)
        {
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

        public void TickIncoming() => _transport.TickIncoming();
        public void TickOutgoing() => _transport.TickOutgoing();

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

        private void HandleTransportDisconnected() => OnDisconnected?.Invoke();

        private void OnWireData(ArraySegment<byte> data, bool reliable)
        {
            if (!PacketCodec.TryDecode(data, out var type, out var msg)) return;
            switch (type)
            {
                case PacketType.JoinAck:
                    var ack = (Proto.JoinAck)msg;
                    PlayerId = ack.PlayerId;
                    OnJoinAck?.Invoke(ack);
                    break;
                case PacketType.StartGame:
                    OnStartGame?.Invoke((Proto.StartGame)msg);
                    break;
                case PacketType.StateSnapshot:
                    var snapshot = (Proto.StateSnapshot)msg;
                    _lastInputAck = snapshot.AckInput;          // 诊断：服务器输入确认（不上报——上报的是快照帧）
                    LastSnapshotFrame = snapshot.Frame;
                    OnSnapshot?.Invoke(snapshot);
                    break;
                case PacketType.ReconnectResponse:
                    OnReconnectResponse?.Invoke((Proto.ReconnectResponse)msg);
                    break;
            }
        }
    }
}
