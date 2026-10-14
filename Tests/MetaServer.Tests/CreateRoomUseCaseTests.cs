using System;
using System.Collections.Generic;
using LiteTesting;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Lobby;
using RoomServer.Application;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 创建房间用例（房主开局批）：**出号唯一性**（对照注册表投影 + 保留集）、签发复用
    /// （票据经房间端**真实**验签器——签发端形状漂移本组必红）、失败语义（无容量/版本不符）。
    /// 出号经 <see cref="CreateRoomUseCase"/> 注入缝注序列——确定性，不依赖真随机。
    /// 断言消息以注释携带（xUnit v2 的 Equal/StartsWith 无 message 重载）。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class CreateRoomUseCaseTests
    {
        private sealed class VirtualClock
        {
            public long Ms;

            public long Now()
            {
                return Ms;
            }
        }

        private static readonly byte[] Secret = Filled(0x5A);
        private const string Kid = "l1";
        private const string Audience = "lobby";
        private const string BuildHash = "hash-x";

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = value;
            return bytes;
        }

        private sealed class Fixture
        {
            public readonly VirtualClock Clock = new VirtualClock();
            public readonly InstanceRegistry Registry;
            public readonly LobbyTicketSigner Signer;
            public readonly JoinTicketIssueUseCase Ticket;
            public readonly CreateRoomUseCase UseCase;
            public readonly HmacJoinTicketValidator Validator;

            /// <summary>出号序列（注入缝——逐个弹出；耗尽即抛，测试责任）。</summary>
            public readonly Queue<string> Segments = new Queue<string>();

            public Fixture(long ttlMs = 60_000)
            {
                Registry = new InstanceRegistry(8, 30_000, Clock.Now);
                Signer = new LobbyTicketSigner(Kid, Secret, Audience);
                Ticket = new JoinTicketIssueUseCase(Registry, Signer, "ffa-1", ttlMs, Clock.Now);
                UseCase = new CreateRoomUseCase(Ticket, Registry, Clock.Now,
                    idSegment: () => Segments.Count > 0 ? Segments.Dequeue() : throw new InvalidOperationException("出号序列耗尽"));
                Validator = new HmacJoinTicketValidator(new[] { new JoinTicketKey(Kid, Secret) });
            }

            public void RegisterInstance(string instanceId = "r1", string buildHash = BuildHash,
                string address = "10.0.0.5:7777", int maxPlayers = 64, int playerCount = 0, bool draining = false)
            {
                Registry.Register(new InstanceRegisterCommand
                {
                    InstanceId = instanceId,
                    BuildHash = buildHash,
                    Address = address,
                    MaxRooms = 8,
                    RoomCount = 0,
                    MaxPlayers = maxPlayers,
                    PlayerCount = playerCount,
                    Draining = draining,
                }, out _);
            }

            /// <summary>注册一个**已在跑对局房间**的实例（投影含明细——出号避让面）。</summary>
            public void RegisterInstanceWithRooms(string roomId, string instanceId = "r1")
            {
                Registry.Register(new InstanceRegisterCommand
                {
                    InstanceId = instanceId,
                    BuildHash = BuildHash,
                    Address = "10.0.0.5:7777",
                    MaxRooms = 8,
                    RoomCount = 1,
                    MaxPlayers = 64,
                    PlayerCount = 2,
                    Rooms = new List<RoomReport>
                    {
                        // 相位用字面量（Created = 0）：MatchPhase 双程序集源链接共编同名，测试代码不得命名该类型（CS0433）
                        new RoomReport { RoomId = roomId, Phase = 0, Players = 2, ExpectedPlayers = 4 },
                    },
                }, out _);
            }

            public JoinTicketIssueUseCase.Result Create(string requestId = "req-1", string buildHash = BuildHash)
                => UseCase.Create("acc-1", new CreateRoomCommand { RequestId = requestId, BuildHash = buildHash });
        }

        [Fact]
        public void 创建_出唯一新号_按前缀可辨_票据经房间端验签通过()
        {
            var f = new Fixture();
            f.Segments.Enqueue("AAAAAA");
            f.RegisterInstance();
            f.Clock.Ms = 1_000;

            var accepted = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.Create());
            // 生成号带前缀（与手工房号可辨）
            Assert.StartsWith(CreateRoomUseCase.IdPrefix, accepted.RoomId);
            Assert.Equal("R-AAAAAA", accepted.RoomId);

            // 票据对生成号签发且可验：房间端真实验签器（跨端同源闭环——签发端形状漂移必红）
            JoinPrincipal principal = f.Validator.Validate(accepted.Ticket,
                new JoinContext(accepted.RoomId, BuildHash, Audience, f.Clock.Ms + 5));
            Assert.True(principal.IsValid, $"生成号票据必须通过房间端验签（{principal.Rejection}）");
            Assert.Equal(accepted.RoomId, principal.RoomId);   // 票据绑定生成号（进房时同值透传）
        }

        [Fact]
        public void 出号避让_注册表既有房间号不重出()
        {
            var f = new Fixture();
            f.RegisterInstanceWithRooms("R-EXIST");        // 投影含在册房间（该实例自带容量——勿再注册同 id，心跳覆盖会冲掉明细）
            f.Segments.Enqueue("EXIST");                   // 第一次出号撞在册号
            f.Segments.Enqueue("FRESH1");                  // 重试出 fresh

            var accepted = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.Create());
            Assert.Equal("R-FRESH1", accepted.RoomId);     // 撞号后重试出 fresh（避让投影既有号）
        }

        [Fact]
        public void 保留集_签发未物化窗口内不重出_过期后让位()
        {
            var f = new Fixture();
            f.RegisterInstance();
            f.Clock.Ms = 1_000;

            f.Segments.Enqueue("AAAAAA");
            var first = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.Create());
            Assert.Equal("R-AAAAAA", first.RoomId);        // 首次出号基线（保留集记录该号）

            f.Clock.Ms = 1_000 + CreateRoomUseCase.ReservedIdTtlMs - 1;
            f.RegisterInstance();                          // 心跳续活（保留窗 120s 远长于实例存活 30s——窗内须保实例可分配）
            f.Segments.Enqueue("AAAAAA");                  // 窗内同段 → 撞保留集，必须重试
            f.Segments.Enqueue("BBBBBB");                  // 重试出 fresh（兑现"避让"须先撞一次）
            var second = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.Create());
            Assert.Equal("R-BBBBBB", second.RoomId);       // 保留窗内同段出号必须避让（签发→物化窗口防撞）

            f.Clock.Ms = 1_000 + CreateRoomUseCase.ReservedIdTtlMs + 1;
            f.RegisterInstance();
            f.Segments.Enqueue("AAAAAA");
            var third = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.Create());
            Assert.Equal("R-AAAAAA", third.RoomId);        // 保留窗过期让位（创建后不入房＝号作废，无幽灵房）
        }

        [Fact]
        public void 版本不符_复用进房拒绝语义()
        {
            var f = new Fixture();
            f.RegisterInstance(buildHash: "hash-other");   // 注册表里只有其它构建的实例
            f.Segments.Enqueue("AAAAAA");

            var rejected = Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.Create(buildHash: BuildHash));
            Assert.Equal(LobbyErrorCodes.VersionMismatch, rejected.ErrorCode);   // 版本准入复用进房语义（409 映射归端点）
        }

        [Fact]
        public void 无容量_出号后签发按无位拒绝()
        {
            var f = new Fixture();
            f.RegisterInstance(playerCount: 64, maxPlayers: 64);   // 实例满员
            f.Segments.Enqueue("AAAAAA");

            Assert.IsType<JoinTicketIssueUseCase.Result.NoCapacity>(f.Create());
        }

        [Fact]
        public void 非法请求_不出号不签发()
        {
            var f = new Fixture();
            f.RegisterInstance();

            var rejected = Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(
                f.UseCase.Create("acc-1", new CreateRoomCommand { RequestId = "" }));

            Assert.Equal(LobbyErrorCodes.InvalidRequest, rejected.ErrorCode);
            Assert.Equal(0, RoomCountOrZero(f));           // 非法请求不出号（注册表房间明细零侧效）
        }

        private static int RoomCountOrZero(Fixture f)
        {
            int count = 0;
            foreach (var view in f.Registry.ProjectRooms())
                count += view.Rooms?.Count ?? 0;
            return count;
        }
    }
}
