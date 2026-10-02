using System;
using kcp2k;

namespace LiteNet.Transport
{
    /// <summary>
    /// 客户端传输封装（唯一允许碰 kcp2k 的地方）。
    /// 连接完成是 Tick 驱动的异步过程（握手经 kcp2k 内建 hello/cookie）：Connect 后调用方需持续 Tick 直至 OnConnected。
    /// LiteGame 侧的 KcpNetworkService : INetworkService 桥接以本类为底（LiteNet 禁 UniTask——异步语义由桥接层翻译）。
    /// </summary>
    public sealed class KcpTransportClient : IClientTransport
    {
        private KcpClient _client;
        private readonly KcpConfig _config;

        public bool Connected => _client != null && _client.connected;

        public event Action OnConnected;
        public event Action<ArraySegment<byte>, bool> OnData;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public KcpTransportClient(KcpConfig config = null)
        {
            _config = config ?? new KcpConfig();
        }

        public void Connect(string address, int port)
        {
            _client = new KcpClient(
                () => OnConnected?.Invoke(),
                (data, channel) => OnData?.Invoke(data, channel == KcpChannel.Reliable),
                () => OnDisconnected?.Invoke(),
                (error, reason) => OnError?.Invoke($"{error}: {reason}"),
                _config);
            _client.Connect(address, (ushort)port);
        }

        public void TickIncoming() => _client?.TickIncoming();
        public void TickOutgoing() => _client?.TickOutgoing();

        public void Send(ArraySegment<byte> data, bool reliable)
        {
            _client?.Send(data, reliable ? KcpChannel.Reliable : KcpChannel.Unreliable);
        }

        public void Disconnect()
        {
            _client?.Disconnect();
        }

        public void Dispose()
        {
            Disconnect();
            _client = null;
        }
    }
}
