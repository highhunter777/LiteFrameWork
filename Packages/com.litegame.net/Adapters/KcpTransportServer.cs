using System;
using System.Collections.Generic;
using System.Net;
using kcp2k;

namespace LiteNet.Transport
{
    /// <summary>
    /// 服务器传输封装（唯一允许碰 kcp2k 的地方——§4.6 分层的 Transport 侧）。
    /// </summary>
    public sealed class KcpTransportServer : IRoomTransport
    {
        private KcpServer _server;
        private readonly KcpConfig _config;

        public event Action<int, ArraySegment<byte>, bool> OnData;
        public event Action<int> OnConnected;
        public event Action<int> OnDisconnected;

        public KcpTransportServer(KcpConfig config = null)
        {
            _config = config ?? new KcpConfig();
        }

        public bool IsActive => _server != null && _server.IsActive();

        public void Start(int port)
        {
            _server = new KcpServer(
                id => OnConnected?.Invoke(id),
                (id, data, channel) => OnData?.Invoke(id, data, channel == KcpChannel.Reliable),
                id => OnDisconnected?.Invoke(id),
                (id, error, reason) => Log.Warn($"[KcpServer] conn {id} error {error}: {reason}"),
                _config);
            _server.Start((ushort)port);
        }

        /// <summary>
        /// 实际绑定端口（<see cref="Start"/> 后有效）。<c>Start(0)</c> 时是系统分配的真实端口——
        /// kcp2k 的 <see cref="KcpServer.LocalEndPoint"/> 直接给出，无需自行记录。
        /// </summary>
        public int BoundPort => (_server?.LocalEndPoint as IPEndPoint)?.Port ?? -1;

        public void TickIncoming() => _server?.TickIncoming();
        public void TickOutgoing() => _server?.TickOutgoing();

        public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            _server?.Send(connectionId, data, reliable ? KcpChannel.Reliable : KcpChannel.Unreliable);
        }

        public void Broadcast(ArraySegment<byte> data, bool reliable)
        {
            if (_server == null) return;
            KcpChannel channel = reliable ? KcpChannel.Reliable : KcpChannel.Unreliable;
            // kcp2k 无内建广播——遍历连接逐发（§4.6 实现点；E1 背压在此基础上按连接限队列）
            foreach (KeyValuePair<int, KcpServerConnection> kv in _server.connections)
            {
                kv.Value.SendData(data, channel);
            }
        }

        public void Disconnect(int connectionId) => _server?.Disconnect(connectionId);

        /// <summary>
        /// 规范化远端地址（无端口）。
        /// </summary>
        public string GetRemoteAddress(int connectionId)
        {
            return RemoteAddress.Normalize(_server?.GetClientEndPoint(connectionId));
        }

        public void Dispose()
        {
            if (_server != null)
            {
                _server.Stop();
                _server = null;
            }
        }

        private static class Log
        {
            // Server .NET 控制台共用本程序集——不引框架日志，落 Console
            public static void Warn(string message) => Console.WriteLine(message);
        }
    }
}
