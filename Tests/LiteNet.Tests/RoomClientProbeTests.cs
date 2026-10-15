using System;
using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 应用层 RTT/丢包探针（ping/丢包批）：<see cref="RoomClient.Quality"/> 由 1Hz Unreliable 探针
    /// （client_time = 发送时刻，服务器原样回显）推断——回声配对算 RTT，滚动窗口未回声占比算丢包。
    /// 全假传输 + 虚拟时钟驱动（与房主位测试件同形态）。
    /// </summary>
    public sealed class RoomClientProbeTests
    {
        private sealed class FakeTransport : IClientTransport
        {
            public readonly List<byte[]> Sent = new List<byte[]>();

            public bool Connected => true;
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;
            public void Connect(string address, int port) => OnConnected?.Invoke();
            public void Disconnect() => OnDisconnected?.Invoke();
            public void TickIncoming() { }
            public void TickOutgoing() { }

            public void Send(ArraySegment<byte> data, bool reliable)
            {
                var copy = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, copy, 0, data.Count);
                Sent.Add(copy);
            }

            public void RaiseData(byte[] packet) => OnData?.Invoke(new ArraySegment<byte>(packet), true);

            /// <summary>最近一条探针的 client_time（-1 = 未发过）。</summary>
            public long LastProbeClientTime()
            {
                for (int i = Sent.Count - 1; i >= 0; i--)
                {
                    if (PacketCodec.TryDecode(new ArraySegment<byte>(Sent[i]), out PacketType type, out var msg)
                        && type == PacketType.Heartbeat)
                        return ((Proto.Heartbeat)msg).ClientTime;
                }
                return -1;
            }

            public void Echo(long clientTime)
                => RaiseData(PacketCodec.Encode(PacketType.Heartbeat, new Proto.Heartbeat { ClientTime = clientTime }));

            public void Dispose() { }
        }

        private sealed class Harness
        {
            public RoomClient Client;
            public FakeTransport Transport;
            public long Now;

            public void Tick() => Client.TickOutgoing();
        }

        private static Harness Connected(long startMs = 1000)
        {
            var transport = new FakeTransport();
            var h = new Harness { Transport = transport, Now = startMs };
            h.Client = new RoomClient(transport, () => h.Now);
            h.Client.Connect("127.0.0.1", 17777);
            h.Client.SendJoin("Room-A", "tok", "hash-1");
            transport.RaiseData(PacketCodec.Encode(PacketType.JoinAck, new Proto.JoinAck { PlayerId = 0 }));
            Assert.Equal(ClientSessionPhase.Connected, h.Client.Phase);
            return h;
        }

        [Fact]
        public void 探针_1Hz发送_回声配对算RTT()
        {
            Harness h = Connected();
            Assert.True(h.Client.Quality.RttMs == NetworkQuality.NoSample, "进房前无样本");

            h.Tick();                                                      // 相位转 Connected 后首个 Tick 即发探针
            Assert.True(h.Transport.LastProbeClientTime() == 1000, "探针 client_time = 发送时刻");

            h.Now = 1048;
            h.Transport.Echo(1000);                                        // 服务器原样回显
            Assert.True(h.Client.Quality.RttMs == 48, "RTT = 回声到达 − 发送时刻");
            Assert.True(h.Client.Quality.LossPermille == 0, "窗口内唯一探针已回声 → 无丢包");
            Assert.True(h.Client.Quality.HasSample);
            Assert.True(h.Client.Quality.Connected);

            h.Transport.Echo(999);                                         // 未命中的回声（非在途探针 id）忽略
            h.Now = 1099;
            Assert.True(h.Client.Quality.RttMs == 48, "未命中回声不改 RTT");
            h.Client.Dispose();
        }

        [Fact]
        public void 探针_滚动窗口未回声占比算丢包()
        {
            Harness h = Connected();

            h.Now = 1000; h.Tick();                                       // 探针 #1 @1000
            h.Now = 2000; h.Tick();                                       // 探针 #2 @2000
            h.Now = 3000; h.Tick();                                       // 探针 #3 @3000
            Assert.True(h.Transport.LastProbeClientTime() == 3000);

            h.Now = 1050; h.Transport.Echo(1000);                          // 只有 #1 回声
            h.Now = 3050; h.Transport.Echo(3000);                          // 与 #3 回声（#2 丢失）
            Assert.True(h.Client.Quality.LossPermille == 333, "3 条探针丢 1 条 → 333‰");
            Assert.True(h.Client.Quality.RttMs == 50, "最近一次回声（#3）推得 RTT");
            h.Client.Dispose();
        }

        [Fact]
        public void 探针_周期未到不重复发送_相位离开即重置窗口()
        {
            Harness h = Connected();

            h.Now = 1000; h.Tick();
            h.Now = 1500; h.Tick();                                       // 不足 1s：不发
            h.Now = 1999; h.Tick();
            Assert.True(h.Transport.LastProbeClientTime() == 1000, "1Hz 节流：周期未到不重复发送");

            h.Now = 2050; h.Transport.Echo(1000);
            Assert.True(h.Client.Quality.HasSample);

            h.Transport.Disconnect();                                     // Connected → SuspectedLost：重置窗口
            Assert.False(h.Client.Quality.Connected);
            Assert.True(h.Client.Quality.RttMs == NetworkQuality.NoSample, "离开在房相位即清零（不把上一段带进新会话）");
            Assert.True(h.Client.Quality.LossPermille == NetworkQuality.NoSample);
            h.Client.Dispose();
        }

        [Fact]
        public void 探针_离线相位不发()
        {
            var transport = new FakeTransport();
            var now = 1000L;
            var client = new RoomClient(transport, () => now);            // 未进房（Idle）
            client.Connect("127.0.0.1", 17777);
            client.TickOutgoing();
            Assert.True(transport.LastProbeClientTime() == -1, "仅 Connected/Restoring 相位发探针");
            client.Dispose();
        }
    }
}
