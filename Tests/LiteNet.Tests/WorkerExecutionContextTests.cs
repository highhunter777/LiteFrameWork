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
    /// Worker 执行形态的**真实传输**段（《商业级通用服务端框架总设计》§8.2）：
    /// 与 <see cref="ServerHostWorkerExecutionTests"/>（假传输、纯命令面）的分工是——
    /// 那组钉"执行 owner 换成 Worker + 回传边界"的语义；本组钉该形态在**真 KCP + 真客户端**
    /// 下同样成立（回传 → 宿主编码发送 → 客户端实收）。
    ///
    /// 装配即生产入口形态：<c>mailboxRouting + workerExecution</c>（<see cref="Program"/> 同款）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class WorkerExecutionContextTests : IDisposable
    {
        private const int Port = 28884;
        private const string Config = @"{
            ""port"": 28884, ""max_rooms"": 4, ""worker_count"": 2, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private readonly ServerHost _host;
        private readonly List<KcpTransportClient> _clients = new List<KcpTransportClient>();

        public WorkerExecutionContextTests()
        {
            var cfg = RoomServerConfig.Parse(Config);
            _host = new ServerHost(new KcpTransportServer(), null, null, null, cfg, null, null,
                mailboxRouting: true, drainMailboxesImmediately: false, workerExecution: true);
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

        private bool WaitFor(Func<bool> predicate, int timeoutMs = 8000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (predicate()) return true;
                PumpOne();
            }
            return predicate();
        }

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

        [Fact]
        public void 真实传输_Worker执行_开局快照经回传到达_全员离场结算并销毁()
        {
            var c1 = ConnectClient();
            var c2 = ConnectClient();
            var ack1 = new Inbox<Proto.JoinAck>(); ack1.Wire(c1, PacketType.JoinAck);
            var ack2 = new Inbox<Proto.JoinAck>(); ack2.Wire(c2, PacketType.JoinAck);
            var sg1 = new Inbox<Proto.StartGame>(); sg1.Wire(c1, PacketType.StartGame);
            var snap1 = new Inbox<Proto.StateSnapshot>(); snap1.Wire(c1, PacketType.StateSnapshot);

            SendJoin(c1, "Room-A");
            SendJoin(c2, "Room-A");

            Assert.True(WaitFor(() => ack1.Count > 0 && ack2.Count > 0),
                $"两端应收到 JoinAck（经回传）ack1={ack1.Count} ack2={ack2.Count} rejects={_host.Ops.Rejects} "
                + $"pktBad={_host.Ops.PacketRejects} rooms={_host.RoomCount} sessRej={_host.Ops.SessionsRejected} "
                + $"tickets={_host.Ops.TicketRejected} rlIp={_host.RateLimit.RejectedIpEntry} rlSess={_host.RateLimit.RejectedSessionPackets} "
                + $"cmds={_host.WorkerCommands} pumps={_host.WorkerPumps} pending={_host.WorkerPoolPending} fails={_host.WorkerFailures} "
                + $"stale={_host.MailboxStale} next={(_host.TryGetRoom("Room-A", out var dr) ? dr.NextPlayerId : -1)} "
                + $"mbClosed={_host.MailboxClosed} mbFull={_host.MailboxRejectedFull}");
            Assert.True(WaitFor(() => sg1.Count > 0), "满员应由 Worker 开局并广播 StartGame");
            Assert.True(WaitFor(() => snap1.Count > 0), "快照应由 Worker 构建、经回传发出并到达客户端");
            Assert.True(_host.TryGetRoom("Room-A", out RoomRuntime room) && room.Started);
            Assert.True(_host.Ops.SettlementsReady == 0, "对局进行中不应有结算");

            // 全员离场 → Worker 执行收尾 → 结算回传入盒（未装配 Outbox 时只计数）→ 静默后销毁。
            c1.Disconnect();
            c2.Disconnect();
            Assert.True(WaitFor(() => _host.Ops.SettlementsReady == 1), "全员离场应由 Worker 收尾并产出结算");
            Assert.True(WaitFor(() => _host.RoomCount == 0), "终态房间应在静默收口后销毁");
            Assert.Equal(1, _host.Ops.RoomsDestroyed);
        }

        [Fact]
        public void 真实传输_Worker执行_两Worker两房并行_隔离不串()
        {
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

            Assert.True(WaitFor(() => _host.TryGetRoom("Room-A", out var a) && a.Started
                && _host.TryGetRoom("Room-B", out var b) && b.Started), "两房应各自开局");
            Assert.True(WaitFor(() => snapA.Count > 0 && snapB.Count > 0), "两房快照都应到达各自客户端");

            _host.TryGetRoom("Room-A", out RoomRuntime roomA);
            _host.TryGetRoom("Room-B", out RoomRuntime roomB);
            Assert.NotSame(roomA.AuthSim, roomB.AuthSim);
            Assert.Equal(2, roomA.NextPlayerId);
            Assert.Equal(2, roomB.NextPlayerId);

            // 断线只影响本房（Worker 执行下同样成立）
            c1.Disconnect();
            Assert.True(WaitFor(() => roomA.SeatOf(0).Phase == SeatPhase.Disconnected), "A 房席位应转断开");
            Assert.Equal(SeatPhase.Active, roomB.SeatOf(0).Phase);
            Assert.True(_host.Ops.SettlementsReady == 0, "对局仍在进行（A 房未全离场）");
        }
    }
}