using System;
using LiteNet;
using LiteNet.Transport;

namespace LiteClient
{
    /// <summary>
    /// 对局客户端会话：**持有传输与 RoomClient**，负责装配与生命周期；会话状态机**不在此重复**——
    /// 相位机（Idle/Connected/SuspectedLost/Reconnecting/Restoring/Failed）全部在 <see cref="RoomClient"/>，
    /// 本类只做：
    /// - **连接与进房装配**：传输 OnConnected 且相位 Idle 时发 Join（初次进房；重连路径由 RoomClient
    ///   状态机自动发 ReconnectRequest，两条路径互斥不重叠）；
    /// - **所有权**：本类创建并拥有 <see cref="KcpTransportClient"/>（Account Scope 登记 BattleClient，
    ///   Dispose 逆序先 RoomClient 后 transport——RoomClient 不代管传输）；
    /// - **诊断**：传输错误留 <see cref="LastError"/>（kcp2k OnError 不进 RoomClient——会话语义由
    ///   OnDisconnected/相位承担，错误文本只作诊断）。
    ///
    /// 事件面（JoinAck/StartGame/Snapshot/相位迁移/断线）经 <see cref="Client"/> 直通——BattleContext
    /// 直挂 RoomClient 事件（编排层不转发第二遍事件，避免"转发即订阅泄漏"的双倍面）。
    /// </summary>
    public sealed class BattleClient : IDisposable
    {
        private readonly IClientTransport _transport;        // 本类创建并拥有（测试注入假件）
        private readonly RoomClient _client;
        private readonly string _host;
        private readonly int _port;
        private readonly string _roomId;
        private readonly string _token;
        private readonly string _buildHash;
        private string _lastError;

        /// <summary>RoomClient 会话面（事件订阅/相位观测的直通道）。</summary>
        public RoomClient Client => _client;

        /// <summary>会话相位（转发 <see cref="RoomClient.Phase"/>）。</summary>
        public ClientSessionPhase Phase => _client.Phase;

        /// <summary>传输连接是否存活。</summary>
        public bool Connected => _client.Connected;

        /// <summary>最近一次传输错误文本（诊断用；null = 无。仅 KCP 实现提供 OnError）。</summary>
        public string LastError => _lastError;

        /// <param name="transport">注入传输（测试假件；null = 新建 KCP——生产形态，所有权归本类）。</param>
        /// <param name="nowMsProvider">单调毫秒源（测试注入虚拟时钟；null = Stopwatch 兜底——Unity 可编译）。</param>
        public BattleClient(string host, int port, string roomId, string token, string buildHash,
            IClientTransport transport = null, Func<long> nowMsProvider = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _roomId = roomId ?? throw new ArgumentNullException(nameof(roomId));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _buildHash = buildHash ?? throw new ArgumentNullException(nameof(buildHash));
            _port = port;

            _transport = transport ?? new KcpTransportClient();
            _client = new RoomClient(_transport, nowMsProvider);
            _transport.OnConnected += OnTransportConnected;
            if (_transport is KcpTransportClient kcp)
                kcp.OnError += error => _lastError = error;   // 诊断面：会话语义由相位承担，错误只留文本
            _client.Connect(_host, _port);                    // 初次连接（重拨由 BeginReconnect 走 RoomClient）
        }

        /// <summary>初次进房 Join：连接建立且相位 Idle（未进房）时发——重连时相位非 Idle，自动跳过。</summary>
        private void OnTransportConnected()
        {
            if (_client.Phase == ClientSessionPhase.Idle)
                _client.SendJoin(_roomId, _token, _buildHash);
        }

        /// <summary>发起重连（SuspectedLost → Reconnecting；无票据/相位不符返回 false——Failed 终态唯一出路是重建会话）。</summary>
        public bool BeginReconnect() => _client.BeginReconnect();

        /// <summary>主动断开（离场路径；相位迁移由 RoomClient 状态机裁决）。</summary>
        public void Disconnect() => _client.Disconnect();

        /// <summary>泵驱动（含重连超时巡检——BattleContext.Tick 调用）。</summary>
        public void TickIncoming() => _client.TickIncoming();
        public void TickOutgoing() => _client.TickOutgoing();

        public void Dispose()
        {
            _transport.OnConnected -= OnTransportConnected;
            _client.Dispose();
            _transport.Dispose();                             // 所有权闭环：会话死了传输跟着死
        }
    }
}
