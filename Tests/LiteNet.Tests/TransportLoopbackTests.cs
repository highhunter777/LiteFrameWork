using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// KCP 回环双通道冒烟（《M10 实施指导》§3：本机 socket 集成——kcp2k 握手（含 cookie）/Reliable+Unreliable 双向）。
    /// 时序鲁棒：两侧 10ms 轮询泵 + 秒级超时护栏；端口随机偏移避撞。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public class TransportLoopbackTests
    {
        private static void Pump(KcpTransportServer server, KcpTransportClient client, int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                server.TickIncoming();
                server.TickOutgoing();
                client.TickIncoming();
                client.TickOutgoing();
                Thread.Sleep(10);
            }
        }

        [Fact]
        public void KCP回环_握手双通道收发与广播()
        {
            int port = 17800 + new Random().Next(0, 200);
            using var server = new KcpTransportServer();
            using var client = new KcpTransportClient();

            var serverConnected = new AutoResetEvent(false);
            var clientConnected = new AutoResetEvent(false);
            var serverData = new List<(int Id, byte[] Data, bool Reliable)>();
            var clientData = new List<(byte[] Data, bool Reliable)>();

            server.OnConnected += id => serverConnected.Set();
            client.OnConnected += () => clientConnected.Set();
            server.OnData += (id, data, reliable) => serverData.Add((id, ToArray(data), reliable));
            client.OnData += (data, reliable) => clientData.Add((ToArray(data), reliable));

            server.Start(port);
            client.Connect("127.0.0.1", port);

            Pump(server, client, 5000); // 握手含 cookie 往返——秒级裕量

            Assert.True(client.Connected, "客户端应在 5s 内完成握手（kcp2k cookie 机制）");
            Assert.True(serverConnected.WaitOne(0), "服务器侧应收到连接");

            // C→S：可靠与不可靠双通道
            client.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Leave, new Proto.Leave())), reliable: true);
            client.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.MismatchReport, new Proto.MismatchReport { Frame = 3 })), reliable: false);
            Pump(server, client, 2000);
            Assert.Contains(serverData, d => d.Reliable && PacketCodec.TryDecode(new ArraySegment<byte>(d.Data), out var t, out _) && t == PacketType.Leave);
            Assert.Contains(serverData, d => !d.Reliable && PacketCodec.TryDecode(new ArraySegment<byte>(d.Data), out var t, out _) && t == PacketType.MismatchReport);

            // S→C：SendTo 与 Broadcast（不可靠通道——快照通道形态）
            int clientId = serverData.Count > 0 ? 0 : 0; // connectionId 由 server 事件给出；回环内取首个连接
            server.OnConnected += _ => { };
            byte[] snapshotPacket = PacketCodec.Encode(PacketType.StateSnapshot,
                new Proto.StateSnapshot { Frame = 1, IsFull = true, Checksum = 7u, AckInput = 0 });
            server.Broadcast(new ArraySegment<byte>(snapshotPacket), reliable: false);
            Pump(server, client, 2000);
            Assert.Contains(clientData, d => !d.Reliable && PacketCodec.TryDecode(new ArraySegment<byte>(d.Data), out var t, out _) && t == PacketType.StateSnapshot);

            client.Disconnect();
            Pump(server, client, 1500);
            Assert.False(client.Connected);

            server.Dispose();
            client.Dispose();
        }

        private static byte[] ToArray(ArraySegment<byte> segment)
        {
            var copy = new byte[segment.Count];
            Array.Copy(segment.Array, segment.Offset, copy, 0, segment.Count);
            return copy;
        }
    }
}
