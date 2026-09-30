using System;
using LiteTesting;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 多房间隔离与动态建房（《框架先行》§6 样例④"**使用至少两个独立房间验证隔离**"、
    /// §8 准入项「联机宿主闭环」"…**两房间隔离**…通过"；《服务端总设计》§6"一个 roomId
    /// 只能映射一个独立 RoomActor"、§514"多房间并发时一个慢客户端或过载房间不拖累其他房间"）。
    ///
    /// **本组存在的理由**：在此之前"任意 roomId 指向同一 Room"（§116）——隔离无从谈起。
    /// 光有 <see cref="ServerHost.TryGetRoom"/> 不算数，必须证明**状态真的分家**：
    /// 席位互不可见、帧号各自推进、房间参数互不串用、关一个房间不影响另一个。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MultiRoomIsolationTests
    {
        private const string Config = @"{
            ""port"": 45001, ""max_rooms"": 2, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 }, ""four"": { ""expected_players"": 4 } }
        }";

        private static (ServerHost host, FakeRoomTransport t, RoomServerConfig cfg) NewHost(int maxRooms = 2)
        {
            var cfg = RoomServerConfig.Parse(Config.Replace(@"""max_rooms"": 2", $@"""max_rooms"": {maxRooms}"));
            var t = new FakeRoomTransport();
            // 配置驱动形态：**不预置房间**——所有房间首次进房时按模板创建，容量上限就是全部房间数。
            var host = new ServerHost(t, null, null, null, cfg);
            return (host, t, cfg);
        }

        private static byte[] JoinPacket(string roomId)
        {
            return PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = roomId,
                Token = "tok",
                BuildHash = ServerHost.ServerBuildHash,
            });
        }

        [Fact]
        public void 两个roomId_建成两个独立房间_席位互不可见()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinPacket("Room-A"));
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, JoinPacket("Room-B"));

            Assert.True(host.TryGetRoom("Room-A", out RoomRuntime roomA));
            Assert.True(host.TryGetRoom("Room-B", out RoomRuntime roomB));
            Assert.NotSame(roomA, roomB);                      // §116：一个 roomId 一个独立 RoomActor

            Assert.Equal(1, roomA.NextPlayerId);               // 各自只有一人
            Assert.Equal(1, roomB.NextPlayerId);
            Assert.Single(roomA.MemberIds());
            Assert.Single(roomB.MemberIds());

            // 席位是**各自从 0 起**的编号空间——不是全局序号
            Assert.Equal(0, roomA.MemberIds()[0]);
            Assert.Equal(0, roomB.MemberIds()[0]);
        }

        [Fact]
        public void 两个房间各自满员开局_互不等待()
        {
            var h = NewHost();
            using var host = h.host;

            // A 房满员（2 人）→ A 开局；B 房此时无人
            h.t.RaiseConnected(1); h.t.RaiseConnected(2);
            h.t.RaiseData(1, JoinPacket("Room-A"));
            h.t.RaiseData(2, JoinPacket("Room-A"));

            host.TryGetRoom("Room-A", out RoomRuntime roomA);
            Assert.True(roomA.Started, "A 房满员应自行开局");
            // B 房从未有人进过 → **不存在**（懒创建）。比"存在但未开局"更强的隔离断言：
            // A 房的活动不会凭空造出 B 房，也不会把 B 房带着推进。
            Assert.False(host.TryGetRoom("Room-B", out _), "A 房的活动不得造出 B 房");
        }

        [Fact]
        public void 同房号重复进房_复用既有房间不重建()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinPacket("Room-A"));
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, JoinPacket("Room-A"));      // 同一 roomId 的第二人

            host.TryGetRoom("Room-A", out RoomRuntime roomA);
            Assert.Equal(2, roomA.MemberIds().Length);    // 进的是同一个房间
            Assert.Equal(1, host.RoomCount);              // 只建了一个房间（不是每连接一个）
        }

        [Fact]
        public void 达房间容量上限_拒绝新建房且不影响既有房间()
        {
            var h = NewHost(maxRooms: 2);                 // 容量 2 格，无预置房
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinPacket("Room-A"));       // 第 1 格，应成功
            Assert.NotNull(h.t.LastJoinAck(1));

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, JoinPacket("Room-B"));       // 第 2 格，应成功
            Assert.NotNull(h.t.LastJoinAck(2));
            Assert.Equal(2, host.RoomCount);

            h.t.RaiseConnected(3);
            h.t.RaiseData(3, JoinPacket("Room-C"));       // 第 3 格 → 超容量
            Assert.Null(h.t.LastJoinAck(3));              // 拒绝进房（不静默排队）
            Assert.Equal(2, host.RoomCount);              // 房间表未增长
            Assert.Equal(1, host.Ops.RoomsRejectedAtCapacity);

            // 既有房间不受影响
            Assert.True(host.TryGetRoom("Room-A", out _));
            Assert.True(host.TryGetRoom("Room-B", out _));
        }

        [Fact]
        public void 房间参数按模板各自生效_不串用()
        {
            var cfg = RoomServerConfig.Parse(Config);
            Assert.Equal(2, cfg.BuildRoomConfig("two", "R").ExpectedPlayers);
            Assert.Equal(4, cfg.BuildRoomConfig("four", "R").ExpectedPlayers);

            // 默认模板建房 → 2 人；同房号不会因为重建而拿到别的模板
            var host2cfg = cfg.BuildRoomConfig(null, "Room-Z");
            Assert.Equal(2, host2cfg.ExpectedPlayers);
            Assert.Equal("Room-Z", host2cfg.RoomId);
        }

        [Fact]
        public void 单房间兼容形态_不动态建房()
        {
            // 未装配 RoomServerConfig：只认预置房间，别的 roomId 一律拒绝（历史用例与本机联调通道）
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, new RoomConfig { Port = 45002, RoomId = "Only", ExpectedPlayers = 2 });

            t.RaiseConnected(1);
            t.RaiseData(1, JoinPacket("Only"));
            Assert.NotNull(t.LastJoinAck(1));

            t.RaiseConnected(2);
            t.RaiseData(2, JoinPacket("Other"));
            Assert.Null(t.LastJoinAck(2));
            Assert.Equal(1, host.RoomCount);
        }

        [Fact]
        public void 断线只影响本房间_另一房间席位不动()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinPacket("Room-A"));
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, JoinPacket("Room-B"));

            h.t.RaiseDisconnected(1);                     // A 房玩家掉线

            host.TryGetRoom("Room-A", out RoomRuntime roomA);
            host.TryGetRoom("Room-B", out RoomRuntime roomB);
            Assert.Equal(SeatPhase.Disconnected, roomA.SeatOf(0).Phase);
            Assert.Equal(SeatPhase.Active, roomB.SeatOf(0).Phase);   // B 房不受牵连
        }

        [Fact]
        public void 票据绑定房间与所进房间不符_拒绝()
        {
            // 多房间下票据的 roomId 绑定必须落到**实际要进的房间**，否则等于拿到 A 房票据进 B 房。
            var issuer = TestTicketIssuer.Random("k1");
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, null,
                new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }), "", cfg);

            t.RaiseConnected(1);
            string ticketForB = issuer.Issue("p1", "Room-B", ServerHost.ServerBuildHash, 0,
                ttlMs: 600_000, audience: "");
            t.RaiseData(1, PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = "Room-A", Token = ticketForB, BuildHash = ServerHost.ServerBuildHash,
            }));

            Assert.Null(t.LastJoinAck(1));
            Assert.Equal(1, host.TicketRejections(JoinTicketRejection.RoomMismatch));
            Assert.False(host.TryGetRoom("Room-A", out _), "被拒的进房不得留下房间");
        }

        [Fact]
        public void 票据先于建房_垃圾票据打不同房号不会撑大房间表()
        {
            // 顺序纪律：若先建房再验票，任何人拿垃圾 token 遍历 roomId 就能把房间表撑到容量上限。
            var issuer = TestTicketIssuer.Random("k1");
            var cfg = RoomServerConfig.Parse(Config);
            var t = new FakeRoomTransport();
            using var host = new ServerHost(t, null,
                new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }), "", cfg);

            for (int i = 0; i < 20; i++)
            {
                t.RaiseConnected(100 + i);
                t.RaiseData(100 + i, JoinPacket("Junk-" + i));
            }

            Assert.Equal(0, host.RoomCount);                        // 一个房间都没建
            Assert.Equal(0, host.Ops.RoomsRejectedAtCapacity);      // 走的不是容量拒绝，是票据拒绝
            Assert.True(host.TicketRejections(JoinTicketRejection.Malformed) >= 20);
        }

        [Fact]
        public void 每房间各自推帧_互不驱动()
        {
            // §8.2"一个 Worker 顺序驱动多个 RoomActor"：Worker 逐房间 Tick，但每房间**各自**的
            // 开局状态决定它是否推进。B 房只有 1 人（未满员开局）→ 即使被逐房间 Tick 也不推帧。
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1); h.t.RaiseConnected(2); h.t.RaiseConnected(3);
            h.t.RaiseData(1, JoinPacket("Room-A"));
            h.t.RaiseData(2, JoinPacket("Room-A"));      // A 房满员开局
            h.t.RaiseData(3, JoinPacket("Room-B"));      // B 房只有 1 人（2 人房，未开局）

            host.TryGetRoom("Room-A", out RoomRuntime roomA);
            host.TryGetRoom("Room-B", out RoomRuntime roomB);
            Assert.True(roomA.Started);
            Assert.False(roomB.Started);

            for (int i = 0; i < 5; i++) host.Pump();

            Assert.True(roomA.AuthSim.Frame > 0, "A 房（已开局）应推进");
            Assert.Equal(0, roomB.AuthSim.Frame);        // B 房未开局 → 帧号不动（不被 A 房带着推）
        }

        [Fact]
        public void 未知房间号_动态建成且用默认模板()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, JoinPacket("Never-Seen-Before"));

            Assert.NotNull(h.t.LastJoinAck(1));
            Assert.True(host.TryGetRoom("Never-Seen-Before", out RoomRuntime room));
            Assert.Equal(2, room.ExpectedPlayers);       // 默认模板 "two"
        }
    }
}
