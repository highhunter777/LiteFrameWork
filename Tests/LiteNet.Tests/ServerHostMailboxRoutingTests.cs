using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Transport 入站先进入每房间 Mailbox，由 Host owner 单线程消费。
    /// WorkerPool 不执行 Runtime；这些用例只钉路由、优先级与满载语义。
    /// </summary>
    public sealed class ServerHostMailboxRoutingTests
    {
        [Fact]
        public void Mailbox路由_兼容立即排空_Join仍在回调内产生Ack并驱动输入()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { Port = 40201, RoomId = "MailboxRoom", ExpectedPlayers = 2 },
                mailboxRouting: true, drainMailboxesImmediately: true);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("MailboxRoom"));
            transport.RaiseData(2, JoinPacket("MailboxRoom"));

            Assert.NotNull(transport.LastJoinAck(1));
            Assert.NotNull(transport.LastJoinAck(2));
            Assert.True(host.Room.Started);

            long before = host.Room.Gate.AcceptedCount;
            transport.RaiseData(1, InputPacket(frame: 1, moveX: 0.5f));

            Assert.Equal(before + 1, host.Room.Gate.AcceptedCount);
            Assert.Equal(1, host.Ops.InputPackets);
            Assert.True(host.MailboxRoutingEnabled);
        }

        [Fact]
        public void Mailbox路由_延迟排空时Join等待Pump_且Pump消费Control优先()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { Port = 40202, RoomId = "DeferredRoom", ExpectedPlayers = 2 },
                mailboxRouting: true, drainMailboxesImmediately: false);

            transport.RaiseConnected(1);
            transport.RaiseData(1, JoinPacket("DeferredRoom"));
            Assert.Null(transport.LastJoinAck(1));

            host.Pump();

            Assert.NotNull(transport.LastJoinAck(1));
            Assert.Equal(1, host.Room.SeatOf(0).ConnectionId);
        }

        [Fact]
        public void Control优先于Input_断线控制先消费_排队输入不再进入闸门()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { Port = 40203, RoomId = "PriorityRoom", ExpectedPlayers = 2 },
                mailboxRouting: true, drainMailboxesImmediately: false);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("PriorityRoom"));
            transport.RaiseData(2, JoinPacket("PriorityRoom"));
            host.Pump();
            Assert.True(host.Room.Started);

            long acceptedBefore = host.Room.Gate.AcceptedCount;
            transport.RaiseData(1, InputPacket(frame: 1, moveX: 0.5f));
            transport.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Proto.Leave()));

            host.Pump();

            Assert.Equal(acceptedBefore, host.Room.Gate.AcceptedCount);
            Assert.Equal(SeatPhase.Disconnected, host.Room.SeatOf(0).Phase);
        }

        [Fact]
        public void InputMailbox满载_明确拒绝且不提前推进输入计数()
        {
            var transport = new FakeRoomTransport();
            var config = ConfigWithMailboxCapacity(1);
            using var host = new ServerHost(transport, null, null, null, config, null, null,
                mailboxRouting: true, drainMailboxesImmediately: false);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("FullRoom"));
            host.Pump();
            transport.RaiseData(2, JoinPacket("FullRoom"));
            host.Pump();
            Assert.True(host.Room.Started);

            long before = host.Room.Gate.AcceptedCount;
            // 已到达输入先于 owner Tick 消费，故首个待消费输入落在当前帧 + 1。
            int nextFrame = host.Room.AuthSim.Frame + 1;
            transport.RaiseData(1, InputPacket(frame: nextFrame, moveX: 0.25f));
            transport.RaiseData(1, InputPacket(frame: nextFrame + 1, moveX: 0.5f));
            Assert.Equal(before, host.Room.Gate.AcceptedCount);
            Assert.True(host.MailboxRejectedFull >= 1);
            host.Pump();
            Assert.Equal(before + 1, host.Room.Gate.AcceptedCount);
        }

        [Fact]
        public void Mailbox重连_重绑前旧连接被标记断开且票据只消费一次()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { Port = 40206, RoomId = "ReconnectMailboxRoom", ExpectedPlayers = 2 },
                mailboxRouting: true, drainMailboxesImmediately: true);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("ReconnectMailboxRoom"));
            transport.RaiseData(2, JoinPacket("ReconnectMailboxRoom"));
            string token = transport.LastJoinAck(1).ReconnectToken;

            // 旧连接仍在表中且尚未收到 OnDisconnected；重绑完成后它也必须失效。
            transport.RaiseConnected(3);
            transport.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));

            ReconnectResponse response = (ReconnectResponse)transport.Last(3, PacketType.ReconnectResponse);
            Assert.True(response.Ok);
            Assert.Equal(3, host.Room.SeatOf(0).ConnectionId);
            Assert.True(host.Sessions.TryGet(1, out Session old));
            Assert.True(old.Disconnected);

            transport.RaiseConnected(4);
            transport.RaiseData(4, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));
            Assert.False(((ReconnectResponse)transport.Last(4, PacketType.ReconnectResponse)).Ok);
        }

        [Fact]
        public void Mailbox重连_控制队列满时不消费票据_排空后可重试()
        {
            var transport = new FakeRoomTransport();
            var config = ConfigWithMailboxCapacity(1);
            using var host = new ServerHost(transport, null, null, null, config, null, null,
                mailboxRouting: true, drainMailboxesImmediately: false);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("FullRoom"));
            host.Pump();
            transport.RaiseData(2, JoinPacket("FullRoom"));
            host.Pump();
            string token = transport.LastJoinAck(1).ReconnectToken;

            // RestoreComplete 占住唯一的 Control 槽位；重连 admission 必须拒绝，
            // 但仍保留一次性票据，待 owner 排空后可重试。
            transport.RaiseData(1, PacketCodec.Encode(PacketType.RestoreComplete,
                new Proto.RestoreComplete()));
            transport.RaiseConnected(3);
            transport.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));
            Assert.False(((ReconnectResponse)transport.Last(3, PacketType.ReconnectResponse)).Ok);

            host.Pump();

            transport.RaiseData(3, PacketCodec.Encode(PacketType.ReconnectRequest,
                new ReconnectRequest { OneTimeToken = token }));
            Assert.True(((ReconnectResponse)transport.Last(3, PacketType.ReconnectResponse)).Ok);
        }

        [Fact]
        public void Control满载时断线_先排空已有控制再按FIFO处理断线()
        {
            var transport = new FakeRoomTransport();
            var config = ConfigWithMailboxCapacity(1);
            using var host = new ServerHost(transport, null, null, null, config, null, null,
                mailboxRouting: true, drainMailboxesImmediately: false);

            transport.RaiseConnected(1);
            transport.RaiseConnected(2);
            transport.RaiseData(1, JoinPacket("FullRoom"));
            host.Pump();
            transport.RaiseData(2, JoinPacket("FullRoom"));
            host.Pump();
            Assert.True(host.Room.Started);

            // 先占满 Control lane，再报告 conn1 断线。断线不得旁路越过这条控制消息。
            transport.RaiseData(2, PacketCodec.Encode(PacketType.RestoreComplete,
                new Proto.RestoreComplete()));
            transport.RaiseDisconnected(1);
            Assert.True(host.Room.SeatOf(0).Phase != SeatPhase.Disconnected);

            host.Pump();

            Assert.Equal(SeatPhase.Disconnected, host.Room.SeatOf(0).Phase);
        }

        private static byte[] JoinPacket(string room)
            => PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = room,
                Token = "mailbox-token",
                BuildHash = ServerHost.ServerBuildHash,
            });

        private static byte[] InputPacket(int frame, float moveX)
            => PacketCodec.Encode(PacketType.Input, new InputMessage
            {
                Frame = frame,
                AckSnapshot = -1,
                ViewFrame = 0,
                Frames = { new InputFrame
                {
                    EntityId = 0,
                    MoveX = moveX,
                    MoveZ = 0f,
                    AimPointX = 10f,
                    AimPointY = 1f,
                    AimPointZ = 0f,
                } },
            });

        private static RoomServerConfig ConfigWithMailboxCapacity(int capacity)
            => RoomServerConfig.Parse($@"{{
                ""port"": 40205, ""max_rooms"": 1, ""worker_count"": 1,
                ""mailbox_capacity"": {capacity}, ""audience"": """", ""default_template"": ""two"",
                ""rooms"": {{ ""two"": {{ ""expected_players"": 2 }} }}
            }}");
    }
}
