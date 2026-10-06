using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 多房间隔离的**真实传输**段（《框架先行》§6 样例④"两个**真实客户端** → 测试房间 → …
    /// 多房间隔离"、§8 准入项「联机宿主闭环」的证据类型"**纯运行时与真实传输/客户端联合记录**"）。
    ///
    /// 与 <see cref="MultiRoomIsolationTests"/> 的分工：
    /// - 那一组走**假传输**（纯命令面、零 Socket），钉的是路由与状态隔离的**逻辑**；
    /// - 本组走**真 KCP + 真客户端**（127.0.0.1 回环），钉的是隔离在**真实传输下**同样成立
    ///   ——假件代替不了这一层（UDP 回环、编解码、连接与房间的绑定都是假件抹掉的）。
    ///
    /// 形态沿用 <see cref="RoomServerTests"/>（ServerHost 同进程内嵌 + 真实 KCP 客户端）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MultiRoomTransportTests : IDisposable
    {
        private int Port => _host.BoundPort;   // 动态端口：宿主绑定后回读（Start(0) 由系统分配）
        private const string Config = @"{
            ""port"": 0, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private readonly ServerHost _host;
        private readonly List<KcpTransportClient> _clients = new List<KcpTransportClient>();

        public MultiRoomTransportTests()
        {
            var cfg = RoomServerConfig.Parse(Config);
            _host = new ServerHost(new KcpTransportServer(), null, null, null, cfg);
        }

        public void Dispose()
        {
            foreach (var c in _clients) c.Dispose();
            _host.Dispose();
        }

        private KcpTransportClient ConnectClient()
        {
            var client = new KcpTransportClient();
            _clients.Add(client);
            var connected = new ManualResetEventSlim(false);
            client.OnConnected += () => connected.Set();
            client.Connect("127.0.0.1", Port);
            var watch = Stopwatch.StartNew();
            while (!connected.Wait(10) && watch.ElapsedMilliseconds < 5000) PumpOne();
            Assert.True(client.Connected, "客户端 5s 内未完成握手");
            return client;
        }

        private void PumpOne()
        {
            _host.Pump();
            foreach (var c in _clients) { c.TickIncoming(); c.TickOutgoing(); }
            Thread.Sleep(10);
        }

        private void Pump(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms) PumpOne();
        }

        private bool WaitFor(Func<bool> predicate, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpOne();
            }
            return predicate();
        }

        /// <summary>收包捕获器：按 PacketType 收集最近一条 proto 消息与计数。</summary>
        private sealed class Inbox<T> where T : Google.Protobuf.IMessage<T>
        {
            public T Last;
            public int Count;
            public void Wire(KcpTransportClient client, PacketType type)
            {
                client.OnData += (data, reliable) =>
                {
                    if (PacketCodec.TryDecode(data, out var t, out var msg) && t == type)
                    {
                        Last = (T)msg;
                        Count++;
                    }
                };
            }
        }

        private static void SendJoin(KcpTransportClient client, string roomId)
        {
            client.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Join,
                new JoinRequest { RoomId = roomId, Token = "test", BuildHash = ServerHost.ServerBuildHash })), true);
        }

        // ---- 用例 ----

        [Fact]
        public void 真实传输_两客户端进两房间_各自满员开局且席位互不可见()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var ack1 = new Inbox<Proto.JoinAck>(); ack1.Wire(c1, PacketType.JoinAck);
            var ack2 = new Inbox<Proto.JoinAck>(); ack2.Wire(c2, PacketType.JoinAck);
            var sg1 = new Inbox<Proto.StartGame>(); sg1.Wire(c1, PacketType.StartGame);
            var sg2 = new Inbox<Proto.StartGame>(); sg2.Wire(c2, PacketType.StartGame);

            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-B");                        // 不同房间，各只有 1 人

            Assert.True(WaitFor(() => ack1.Count > 0 && ack2.Count > 0), "两端应各收到 JoinAck");
            Assert.Equal(0, ack1.Last.PlayerId);           // 各自从 0 起——不是全局序号
            Assert.Equal(0, ack2.Last.PlayerId);

            Assert.True(_host.TryGetRoom("Room-A", out RoomRuntime roomA));
            Assert.True(_host.TryGetRoom("Room-B", out RoomRuntime roomB));
            Assert.NotSame(roomA, roomB);
            Assert.Single(roomA.MemberIds());
            Assert.Single(roomB.MemberIds());
            Assert.False(roomA.Started, "两房各 1 人（2 人房）都不该开局");
            Assert.False(roomB.Started);

            Pump(300);
            Assert.Equal(0, sg1.Count);                    // 未满员不得开局广播
            Assert.Equal(0, sg2.Count);
        }

        [Fact]
        public void 真实传输_A房满员开局_不影响B房()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var c3 = ConnectClient();
            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-A");                        // A 房满员
            SendJoin(c3, "Room-B");                        // B 房 1 人

            Assert.True(WaitFor(() => _host.TryGetRoom("Room-A", out var a) && a.Started), "A 房应满员开局");
            Assert.True(_host.TryGetRoom("Room-B", out RoomRuntime roomB));
            Assert.False(roomB.Started, "B 房不应被 A 房带着开局");
            Assert.Equal(1, roomB.NextPlayerId);
        }

        [Fact]
        public void 真实传输_两房快照各自独立_不串房间()
        {
            // 隔离在真实传输下的关键判据：两个房间都在跑时，各自客户端只收到**本房间**的帧。
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var c3 = ConnectClient();
            var c4 = ConnectClient();
            var snapA = new Inbox<Proto.StateSnapshot>(); snapA.Wire(c1, PacketType.StateSnapshot);
            var snapB = new Inbox<Proto.StateSnapshot>(); snapB.Wire(c3, PacketType.StateSnapshot);

            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-A");
            SendJoin(c3, "Room-B");
            SendJoin(c4, "Room-B");

            Assert.True(WaitFor(() => _host.TryGetRoom("Room-A", out var a) && a.Started && a.AuthSim.Frame > 3),
                "A 房应开局并推帧");
            Pump(500);

            Assert.True(snapA.Count > 0, "A 房客户端应收到快照");
            Assert.True(snapB.Count > 0, "B 房客户端应收到快照");

            _host.TryGetRoom("Room-A", out RoomRuntime roomA);
            _host.TryGetRoom("Room-B", out RoomRuntime roomB);
            Assert.NotSame(roomA.AuthSim, roomB.AuthSim);   // 各自独立的 Sim 状态
        }

        [Fact]
        public void 真实传输_断线只影响本房间()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var c3 = ConnectClient();
            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-B");
            Assert.True(WaitFor(() => _host.TryGetRoom("Room-A", out _) && _host.TryGetRoom("Room-B", out _)));

            c1.Disconnect();
            Pump(400);

            Assert.True(_host.TryGetRoom("Room-A", out RoomRuntime roomA));
            Assert.True(_host.TryGetRoom("Room-B", out RoomRuntime roomB));
            Assert.Equal(SeatPhase.Disconnected, roomA.SeatOf(0).Phase);
            Assert.Equal(SeatPhase.Active, roomB.SeatOf(0).Phase);   // B 房不受牵连
        }

        [Fact]
        public void 真实传输_排空后新进房被拒_既有对局不受影响()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-A");                        // A 房满员开局
            Assert.True(WaitFor(() => _host.TryGetRoom("Room-A", out var a) && a.Started));

            _host.BeginDrain(_host.CurrentMs + 10_000);    // §12 第 2 步

            var c3 = ConnectClient();
            var ack3 = new Inbox<Proto.JoinAck>(); ack3.Wire(c3, PacketType.JoinAck);
            SendJoin(c3, "Room-B");
            Pump(300);

            Assert.Equal(0, ack3.Count);                   // 排空后不接新进房
            Assert.False(_host.TryGetRoom("Room-B", out _), "排空后不应建出新房间");
            Assert.True(_host.TryGetRoom("Room-A", out RoomRuntime roomA));
            Assert.False(roomA.Closed, "既有对局不受排空影响");
        }
    }
}
