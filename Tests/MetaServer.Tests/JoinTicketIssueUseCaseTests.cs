using System;
using LiteTesting;
using MetaServer.Contracts.Lobby;
using MetaServer.Modules.Lobby;
using RoomServer.Application;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// Join Ticket 签发用例 + **跨端同源闭环**（《Meta 服务专项设计》§6.2/§7：
    /// Meta 签发 → RoomServer 同 key 验签）。
    ///
    /// L1：纯逻辑 + 虚拟时钟；验签用房间端**真实** <see cref="HmacJoinTicketValidator"/>
    /// （不是测试替身）——签发端形状漂移时本组必红。
    /// </summary>
    [Trait(TestTrait.Category, TestCategory.Contract)]
    public sealed class JoinTicketIssueUseCaseTests
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
        private const string Room = "ffa-1";

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
            public readonly JoinTicketIssueUseCase UseCase;
            public readonly HmacJoinTicketValidator Validator;

            public Fixture(string defaultRoomId = Room, long ttlMs = 60_000)
            {
                Registry = new InstanceRegistry(8, 30_000, Clock.Now);
                Signer = new LobbyTicketSigner(Kid, Secret, Audience);
                UseCase = new JoinTicketIssueUseCase(Registry, Signer, defaultRoomId, ttlMs, Clock.Now);
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
        }

        private static JoinTicketCommand Command(string requestId = "req-1", string roomId = Room, string buildHash = null)
        {
            return new JoinTicketCommand { RequestId = requestId, RoomId = roomId, BuildHash = buildHash };
        }

        [Fact]
        public void 签发_成功_房间端验证器验签通过_二次验签为重放()
        {
            var f = new Fixture();
            f.RegisterInstance();
            f.Clock.Ms = 1_000;

            var accepted = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc-1", Command()));

            Assert.Equal("r1", accepted.InstanceId);
            Assert.Equal("10.0.0.5:7777", accepted.Address);
            Assert.Equal(Room, accepted.RoomId);
            Assert.Equal(1_000 + 60_000, accepted.ExpiresAtMs);

            // 形状：13 段 + kid 位（不命名跨程序集重复的格式类型——见 csproj 注释）
            string[] parts = accepted.Ticket.Split('.');
            Assert.Equal(13, parts.Length);
            Assert.Equal("v1", parts[0]);
            Assert.Equal(Kid, parts[1]);

            JoinPrincipal principal = f.Validator.Validate(accepted.Ticket,
                new JoinContext(Room, BuildHash, Audience, f.Clock.Ms + 5));
            Assert.True(principal.IsValid);
            Assert.Equal("acc-1", principal.AccountId);
            Assert.Equal("acc-1", principal.PlayerId);          // 游客模型：playerId = accountId
            Assert.Equal(Room, principal.RoomId);
            Assert.Equal(accepted.ExpiresAtMs, principal.ExpiresAtMs);

            // nonce 一次性：同一票据只应成功一次
            Assert.Equal(JoinTicketRejection.Replayed,
                f.Validator.Validate(accepted.Ticket, new JoinContext(Room, BuildHash, Audience, f.Clock.Ms + 6)).Rejection);
        }

        [Fact]
        public void 签发_绑定项不符_房间端按分类拒绝_且不吞nonce()
        {
            var f = new Fixture();
            f.RegisterInstance();
            f.Clock.Ms = 1_000;

            string ticket = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc-1", Command())).Ticket;

            Assert.Equal(JoinTicketRejection.RoomMismatch,
                f.Validator.Validate(ticket, new JoinContext("other-room", BuildHash, Audience, f.Clock.Ms)).Rejection);
            Assert.Equal(JoinTicketRejection.BuildHashMismatch,
                f.Validator.Validate(ticket, new JoinContext(Room, "hash-other", Audience, f.Clock.Ms)).Rejection);

            // 前面全是绑定项失败（nonce 未消费）——正确的上下文仍可验通过
            Assert.True(f.Validator.Validate(ticket, new JoinContext(Room, BuildHash, Audience, f.Clock.Ms)).IsValid);

            // 过期判定用服务端时钟：到期即拒
            string fresh = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc-2", Command("req-2"))).Ticket;
            Assert.Equal(JoinTicketRejection.Expired,
                f.Validator.Validate(fresh, new JoinContext(Room, BuildHash, Audience, f.Clock.Ms + 60_000)).Rejection);
        }

        [Fact]
        public void 签发_受众不符_房间端拒绝()
        {
            var f = new Fixture();
            f.RegisterInstance();
            string ticket = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc-1", Command())).Ticket;

            Assert.Equal(JoinTicketRejection.AudienceMismatch,
                f.Validator.Validate(ticket, new JoinContext(Room, BuildHash, "other-audience", f.Clock.Ms)).Rejection);
        }

        [Fact]
        public void 签发_字段边界_非法请求拒绝()
        {
            var f = new Fixture();
            f.RegisterInstance();

            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.UseCase.Issue("acc", Command(requestId: null))).ErrorCode);
            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.UseCase.Issue("acc", Command(requestId: new string('r', 129)))).ErrorCode);
            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.UseCase.Issue("acc", Command(roomId: new string('c', 65)))).ErrorCode);
            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.UseCase.Issue("acc", Command(buildHash: new string('b', 65)))).ErrorCode);
            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(f.UseCase.Issue(null, Command())).ErrorCode);

            // 未产生任何签发副作用（注册表未被触碰，句柄仍可正常签发）
            f.Clock.Ms = 10;
            Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(f.UseCase.Issue("acc", Command()));
        }

        [Fact]
        public void 签发_请求与默认房间号都为空_拒绝()
        {
            var f = new Fixture(defaultRoomId: "");
            f.RegisterInstance();

            Assert.Equal(LobbyErrorCodes.InvalidRequest,
                Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(
                    f.UseCase.Issue("acc", Command(roomId: null))).ErrorCode);
        }

        [Fact]
        public void 签发_默认房间号生效()
        {
            var f = new Fixture(defaultRoomId: "default-room");
            f.RegisterInstance();

            var accepted = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc", Command(roomId: null)));
            Assert.Equal("default-room", accepted.RoomId);
        }

        [Fact]
        public void 签发_版本不符与无容量_分开返回()
        {
            var f = new Fixture();
            f.RegisterInstance(buildHash: "hash-old");

            var mismatch = Assert.IsType<JoinTicketIssueUseCase.Result.Rejected>(
                f.UseCase.Issue("acc", Command(buildHash: "hash-new")));
            Assert.Equal(LobbyErrorCodes.VersionMismatch, mismatch.ErrorCode);

            // 无容量：可分配集合为空（满员 + 排空 + 无实例三态都归 NoCapacity，不进版本冲突）
            var full = new Fixture();
            full.RegisterInstance("full", maxPlayers: 2, playerCount: 2);
            full.RegisterInstance("drain", draining: true);
            Assert.IsType<JoinTicketIssueUseCase.Result.NoCapacity>(
                full.UseCase.Issue("acc", Command(buildHash: "hash-new")));

            var empty = new Fixture();
            Assert.IsType<JoinTicketIssueUseCase.Result.NoCapacity>(
                empty.UseCase.Issue("acc", Command()));
        }

        [Fact]
        public void 签发_未声明构建_按实例盖章()
        {
            var f = new Fixture();
            f.RegisterInstance(buildHash: "hash-live");

            var accepted = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc", Command(buildHash: null)));      // 未声明 → 不做准入，按实例盖章

            // 盖章值为实例构建：与本服哈希一致才验通过（JoinPrincipal 不回填 buildHash，绑定以正/反两侧证明）
            Assert.True(f.Validator.Validate(accepted.Ticket,
                new JoinContext(Room, "hash-live", Audience, f.Clock.Ms)).IsValid);

            string other = Assert.IsType<JoinTicketIssueUseCase.Result.Accepted>(
                f.UseCase.Issue("acc", Command("req-2", buildHash: null))).Ticket;
            Assert.Equal(JoinTicketRejection.BuildHashMismatch,
                f.Validator.Validate(other, new JoinContext(Room, "hash-other", Audience, f.Clock.Ms)).Rejection);
        }
    }
}
