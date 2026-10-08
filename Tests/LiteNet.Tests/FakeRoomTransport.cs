using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;

namespace LiteNet.Tests
{
    /// <summary>
    /// 假传输：只实现窄端口 <see cref="IRoomTransport"/>（不碰 kcp2k）——同时验证"端口够不够用"
    /// （若 ServerHost 用到端口外的东西，本类就编译不过，解耦度自证）。
    ///
    /// **namespace 级单一来源**：多个测试类共用它；若各持有私有副本，两份假件会各自漂移，
    /// "端口够不够用"的自证便退化（谁编译不过都不再说明端口不够）。
    /// </summary>
    internal sealed class FakeRoomTransport : IRoomTransport
    {
        private readonly List<(int conn, ArraySegment<byte> data, bool reliable)> _sent
            = new List<(int, ArraySegment<byte>, bool)>();

        public int StartedPort { get; private set; } = -1;
        public bool Disposed { get; private set; }

        /// <summary>被宿主断开过的连接号（per-IP 限流拒绝等断言用）。</summary>
        public readonly List<int> Disconnects = new List<int>();

        private readonly Dictionary<int, string> _remoteAddresses = new Dictionary<int, string>();

        /// <summary>设定某连接的规范化远端地址（per-IP 限流用例）；默认 null = 未知（宿主跳过 IP 维度限流）。</summary>
        public void SetRemoteAddress(int conn, string address) => _remoteAddresses[conn] = address;

        public event Action<int, ArraySegment<byte>, bool> OnData;
        public event Action<int> OnConnected;
        public event Action<int> OnDisconnected;

        public void Start(int port) => StartedPort = port;

        /// <summary>假件无真实 socket：<c>Start(0)</c> 时回落一个确定值（测试要看的是"回读口存在且被调用"，
        /// 不需要真实端口语义——真端口回读由 KcpTransportServer 承担）。</summary>
        public int BoundPort => StartedPort > 0 ? StartedPort : (StartedPort == 0 ? 1 : -1);

        public void TickIncoming() { }
        public void TickOutgoing() { }
        public void Disconnect(int connectionId) => Disconnects.Add(connectionId);
        public void Broadcast(ArraySegment<byte> data, bool reliable) { }

        /// <summary>首个 Dispose 调用回调（装配收敛用例的释放序观测；默认 null = 无行为）。</summary>
        public Action OnDisposed;

        public void Dispose()
        {
            if (Disposed) return;                 // 宿主接管 + 装配层收尾的双释放是既有形态——只记首个
            Disposed = true;
            OnDisposed?.Invoke();
        }

        public string GetRemoteAddress(int connectionId)
            => _remoteAddresses.TryGetValue(connectionId, out string address) ? address : null;

        /// <summary>真实传输同步拷贝；假件同样拷贝（避免上层复用缓冲的假设在假件上假绿）。</summary>
        public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
            _sent.Add((connectionId, new ArraySegment<byte>(copy), reliable));
        }

        public void RaiseConnected(int conn) => OnConnected?.Invoke(conn);
        public void RaiseDisconnected(int conn) => OnDisconnected?.Invoke(conn);
        public void RaiseData(int conn, byte[] packet, bool reliable = true)
            => OnData?.Invoke(conn, new ArraySegment<byte>(packet), reliable);

        public JoinAck LastJoinAck(int conn) => (JoinAck)LastOf(conn, PacketType.JoinAck);
        public StartGame LastStartGame(int conn) => (StartGame)LastOf(conn, PacketType.StartGame);
        public object Last(int conn, PacketType type) => LastOf(conn, type);

        /// <summary>该连接收到的指定类型包数（抑制/恢复断言用）。</summary>
        public int CountOf(int conn, PacketType type)
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; i++)
            {
                if (_sent[i].conn != conn) continue;
                if (!PacketCodec.TryDecode(_sent[i].data, out PacketType t, out _)) continue;
                if (t == type) count++;
            }
            return count;
        }

        private object LastOf(int conn, PacketType type)
        {
            for (int i = _sent.Count - 1; i >= 0; i--)
            {
                if (_sent[i].conn != conn) continue;
                if (!PacketCodec.TryDecode(_sent[i].data, out PacketType t, out var msg)) continue;
                if (t == type) return msg;
            }
            return null;
        }
    }
}
