using System;
using LiteNet;
using LiteNet.Transport;
using LiteFramework;

using LiteSync.Client;
namespace LiteGame
{
    /// <summary>
    /// 对局客户端会话：**持有传输与 RoomClient**，负责装配与生命周期。
    /// </summary>
    public sealed class BattleClient : IBattleClient
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

        /// <summary>网络质量观测（转发 <see cref="RoomClient.Quality"/>——HUD 展示面，不参与判定）。</summary>
        public NetworkQuality Quality => _client.Quality;

        /// <summary>最近一次传输错误文本（诊断用；null = 无。仅 KCP 实现提供 OnError）。</summary>
        public string LastError => _lastError;

        /// <param name="transport">注入传输（测试假件/本地服；null = 新建 KCP——生产形态，所有权归本类）。</param>
        /// <param name="secureEnvelopeOptions">安全信封密钥（PSK）。真实 KCP（自建或注入）必须装配——
        /// 缺失即抛（fail-closed，与 RoomServer 装配口径对称）；LocalServerTransport 等进程内传输不受影响。</param>
        /// <param name="nowMsProvider">单调毫秒源（测试注入虚拟时钟；null = Stopwatch 兜底——Unity 可编译）。</param>
        public BattleClient(string host, int port, string roomId, string token, string buildHash,
            IClientTransport transport = null, Func<long> nowMsProvider = null,
            SecureEnvelopeOptions secureEnvelopeOptions = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _roomId = roomId ?? throw new ArgumentNullException(nameof(roomId));
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _buildHash = buildHash ?? throw new ArgumentNullException(nameof(buildHash));
            _port = port;

            if (transport == null)
            {
                if (secureEnvelopeOptions == null)
                    throw new InvalidOperationException("真实 KCP 必须装配安全信封密钥。");
                _transport = new SecureEnvelopeClientTransport(new KcpTransportClient(), secureEnvelopeOptions);
            }
            else if (transport is KcpTransportClient && secureEnvelopeOptions != null)
            {
                _transport = new SecureEnvelopeClientTransport(transport, secureEnvelopeOptions);
            }
            else
            {
                _transport = transport;
            }
            _client = new RoomClient(_transport, nowMsProvider);
            _transport.OnConnected += OnTransportConnected;
            if (_transport is KcpTransportClient kcp)
                kcp.OnError += error => _lastError = error;   // 诊断面：会话语义由相位承担，错误只留文本
            if (_transport is SecureEnvelopeClientTransport secure)
                secure.OnError += error => _lastError = error;
            if (_transport is IClientTransportErrorSource errorSource)
                errorSource.OnError += error => _lastError = error;   // 可选诊断端口（含开发期弱网装饰器）
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

        /// <summary>本机是否房主（开房裁决 + 迁移续批——RoomClient 协议派生态透传；房间会话 UI 面消费）。</summary>
        public bool IsOwner => _client.IsOwner;

        /// <summary>房主提前开局请求（开房裁决：转发 RoomClient——相位守卫/被拒反馈/成功广播归它）。</summary>
        public void SendStartMatchRequest() => _client.SendStartMatchRequest();

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
