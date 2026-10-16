using System;
using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;
using LiteSim.Tests;

namespace LiteNet.Tests
{
    /// <summary>
    /// **开房参数批：房间人数选择**——建房路径的期望人数来源、校验与权威回执：
    ///
    /// - 人数来源优先级：**票据盖章值 ＞ 房间模板 `expected_players`**（0 = 未指定＝按模板）；
    /// - 请求人数越界（超出内核席位容量 <see cref="PendingInputRing.MaxPlayerCapacity"/>）→
    ///   **拒绝建房**（不静默钳制——钳出来的房不是玩家要的房）；
    /// - 既有房间以**房事实**为准：请求人数不参与判定（回执把房事实交给客户端定容）；
    /// - **JoinAck.ExpectedPlayers = 房事实**（客户端据此定容预测世界与输入模板——权威回执面）。
    ///
    /// 形态：<see cref="FakeRoomTransport"/> 逐包级（同 <see cref="RoomMapSelectionTests"/> 形态，无真实 socket）。
    /// </summary>
    public sealed class RoomExpectedPlayersTests
    {
        private const string ConfigTwo = @"{
            ""port"": 0, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""two"",
            ""rooms"": { ""two"": { ""expected_players"": 2 } }
        }";

        private const string ConfigFour = @"{
            ""port"": 0, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""four"",
            ""rooms"": { ""four"": { ""expected_players"": 4 } }
        }";

        private static (ServerHost host, FakeRoomTransport t, TestTicketIssuer issuer) NewHost(string config)
        {
            var t = new FakeRoomTransport();
            TestTicketIssuer issuer = TestTicketIssuer.Random("k1");
            var host = new ServerHost(t, null,
                new HmacJoinTicketValidator(new[] { issuer.AsValidatorKey() }),
                "", RoomServerConfig.Parse(config),
                map: TestMaps.Greybox());
            return (host, t, issuer);
        }

        private static byte[] Join(string roomId, TestTicketIssuer issuer, int expectedPlayers)
        {
            // 票据盖章：roomId 与 Join 请求同值（验签绑定项之一）；expectedPlayers = 房间创建意图里的期望人数
            string ticket = issuer.Issue("p1", roomId, ServerHost.ServerBuildHash, nowMs: 1_000,
                audience: "", expectedPlayers: expectedPlayers);
            return PacketCodec.Encode(PacketType.Join, new JoinRequest
            {
                RoomId = roomId, Token = ticket, BuildHash = ServerHost.ServerBuildHash,
            });
        }

        [Fact]
        public void 票据盖章人数_新建房按它建房_JoinAck回执房事实()
        {
            var h = NewHost(ConfigTwo);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-4", h.issuer, expectedPlayers: 4));

            JoinAck ack = h.t.LastJoinAck(1);
            Assert.NotNull(ack);
            Assert.Equal(4, ack.ExpectedPlayers);                  // 权威回执 = 房事实（客户端据此定容）
            Assert.True(host.TryGetRoom("Room-4", out RoomRuntime room));
            Assert.Equal(4, room.ExpectedPlayers);                 // 盖章值 > 模板（2）——设置生效
        }

        [Fact]
        public void 票据未指定_按模板人数建房()
        {
            var h = NewHost(ConfigFour);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-T", h.issuer, expectedPlayers: 0));   // 0 = 未指定

            Assert.Equal(4, h.t.LastJoinAck(1).ExpectedPlayers);              // 模板人数生效
            Assert.True(host.TryGetRoom("Room-T", out RoomRuntime room));
            Assert.Equal(4, room.ExpectedPlayers);
        }

        [Fact]
        public void 请求人数越界_拒绝建房_不静默钳制()
        {
            var h = NewHost(ConfigTwo);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-Big", h.issuer, expectedPlayers: PendingInputRing.MaxPlayerCapacity + 1));

            Assert.Null(h.t.LastJoinAck(1));                       // 拒绝进房（不建房）
            Assert.False(host.TryGetRoom("Room-Big", out _), "越界请求不得静默建成别的规模的房");
        }

        [Fact]
        public void 既有房间_请求人数不参与判定_回执房事实()
        {
            // 计数是**创建**参数：既有房间以房事实为准（请求值只作用于新建房）——回执把房事实交给客户端，
            // 不一致不构成错误（快速进入/列表进房恒不带人数，同一条口径）。
            var h = NewHost(ConfigTwo);
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-K", h.issuer, expectedPlayers: 0));      // 按模板 2 建
            Assert.Equal(2, h.t.LastJoinAck(1).ExpectedPlayers);

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-K", h.issuer, expectedPlayers: 6));      // 同一房再请求 6

            Assert.Equal(2, h.t.LastJoinAck(2).ExpectedPlayers);                 // 仍以房事实（2）为准
            Assert.True(host.TryGetRoom("Room-K", out RoomRuntime room));
            Assert.Equal(2, room.ExpectedPlayers);
        }
    }
}
