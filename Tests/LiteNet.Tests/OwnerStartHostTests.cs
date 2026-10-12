using LiteNet.Protocol;
using LiteNet.Proto;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房主提前开局·宿主全链用例（开房裁决：**开房者 = 首入座者**）：
    /// StartMatchRequest 信令 → 会话/席位映射 → RoomCommand.StartEarly → Runtime owner 判定 →
    /// StartGame 广播；拒绝经 StartMatchResponse 回发起者。FakeRoomTransport 逐包级（同 DrainTests 形态）；
    /// Runtime 直测（未满开局/缺席实体/中途拒加入）见 <see cref="OwnerStartEarlyTests"/>。
    /// </summary>
    public sealed class OwnerStartHostTests
    {
        private const string Config = @"{
            ""port"": 46009, ""max_rooms"": 4, ""audience"": """",
            ""default_template"": ""four"",
            ""rooms"": { ""four"": { ""expected_players"": 4 } }
        }";

        private static (ServerHost host, FakeRoomTransport t) NewHost()
        {
            var t = new FakeRoomTransport();
            return (new ServerHost(t, null, null, null, RoomServerConfig.Parse(Config)), t);
        }

        private static byte[] Join(string roomId)
            => PacketCodec.Encode(PacketType.Join, new JoinRequest { RoomId = roomId, Token = "tok", BuildHash = ServerHost.ServerBuildHash });

        private static byte[] StartRequest()
            => PacketCodec.Encode(PacketType.StartMatchRequest, new StartMatchRequest());

        [Fact]
        public void 房主提前开局_未满即开_JoinAck带房主位_全体收StartGame()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            var ack1 = h.t.LastJoinAck(1);

            Assert.NotNull(ack1);
            Assert.True(ack1.IsOwner);                                     // 开房者 = 首入座者：房主位随 JoinAck 下发

            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));
            Assert.False(h.t.LastJoinAck(2).IsOwner);                       // 第二入座者非房主
            Assert.Null(h.t.LastStartGame(1));                              // 未满不开（4 座只 2 人）

            h.t.RaiseData(1, StartRequest());                               // 房主提前开局

            Assert.NotNull(h.t.LastStartGame(1));                           // 全体收 StartGame 广播
            Assert.NotNull(h.t.LastStartGame(2));
            Assert.Null(h.t.Last(1, PacketType.StartMatchResponse));        // 成功路径不回 Response（StartGame 即成功）
        }

        [Fact]
        public void 非房主开局_收StartMatchResponse拒绝_不开局()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));

            h.t.RaiseData(2, StartRequest());                               // 第二入座者（非房主）发起

            var response = (StartMatchResponse)h.t.Last(2, PacketType.StartMatchResponse);
            Assert.NotNull(response);
            Assert.False(response.Ok);
            Assert.Contains("房主", response.Reason);
            Assert.Null(h.t.LastStartGame(1));                              // 没开——全体无 StartGame
            Assert.Null(h.t.Last(1, PacketType.StartMatchResponse));        // 拒绝只回发起者
        }

        [Fact]
        public void 提前开局后_中途加入被拒()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseData(1, StartRequest());                                // 1 人开局 4 座房
            Assert.NotNull(h.t.LastStartGame(1));

            h.t.RaiseConnected(3);
            h.t.RaiseData(3, Join("Room-A"));                                // 中途加入 → 拒（无 JoinAck）

            Assert.Null(h.t.LastJoinAck(3));
            Assert.Equal(1, host.Ops.Rejects);                               // 房间侧拒绝计数进 Ops
        }

        [Fact]
        public void 房主离席_在座者收到OwnerChanged信令_新方案开局()
        {
            var h = NewHost();
            using var host = h.host;

            h.t.RaiseConnected(1);
            h.t.RaiseData(1, Join("Room-A"));
            h.t.RaiseConnected(2);
            h.t.RaiseData(2, Join("Room-A"));
            Assert.True(h.t.LastJoinAck(1).IsOwner);

            h.t.RaiseData(1, PacketCodec.Encode(PacketType.Leave, new Leave()));   // 房主退房

            var changed = (LiteNet.Proto.OwnerChanged)h.t.Last(2, PacketType.OwnerChanged);
            Assert.NotNull(changed);
            Assert.Equal(1, changed.PlayerId);                                // 转给最低 playerId 的其余席位
            // 注：离席者同批也可能收到广播（ApplyOutput 先于 session.Disconnected 落地）——无害，不断言

            h.t.RaiseData(2, StartRequest());                                  // 新房主开局成功（wire 全链）
            Assert.NotNull(h.t.LastStartGame(2));
        }
    }
}
