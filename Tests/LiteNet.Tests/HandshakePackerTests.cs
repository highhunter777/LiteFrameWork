using LiteSim;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 回执字段面契约（<see cref="HandshakePacker"/>——真服/本地服共用的 JoinAck/StartGame 单源）：
    /// 本地服回执曾缺 IsOwner/ExpectedPlayers/MatchId/Match 一族（两端手抄、只在一端补——输入面
    /// P0-3 已单源，回执面同型问题在此收口）。本用例在**单点**钉住"哪些字段必须进回执"；
    /// 两端集成面另各有钉子（真服：RoomExpectedPlayersTests/OwnerStartHostTests/MatchRulesClosureTests；
    /// 本地服：EditMode 侧 LocalServerHandshakeEditModeTests）——proto 加字段时三处同批改，漏改即红。
    /// </summary>
    public sealed class HandshakePackerTests
    {
        [Fact]
        public void JoinAck_字段面_身份与房事实齐备()
        {
            var admitted = new PlayerAdmitted(2, new[] { 0, 1, 2 }, isOwner: false);

            var ack = HandshakePacker.BuildJoinAck(admitted, "ticket-1", 300, mapId: 7, expectedPlayers: 4);

            Assert.Equal(2, ack.PlayerId);
            Assert.Equal(3, ack.Members.Count);
            Assert.Equal(0, ack.Members[0]);
            Assert.Equal(2, ack.Members[2]);
            Assert.False(ack.IsOwner);
            Assert.Equal(4, ack.ExpectedPlayers);
            Assert.Equal(7, ack.MapId);
            Assert.Equal("ticket-1", ack.ReconnectToken);
            Assert.Equal(300, ack.ReconnectWindowSeconds);
            Assert.Equal(SimConfig.SnapshotHz, ack.SnapshotHz);
            Assert.Equal(SimConfig.TickRate, ack.TickRate);
        }

        [Fact]
        public void StartGame_字段面_携带比赛身份与状态()
        {
            var started = new MatchStarted(123L, 9, 77u, new MatchStateData { Phase = 1, Timer = 100 }, "match-abc");

            var game = HandshakePacker.BuildStartGame(started);

            Assert.Equal(123L, game.Seed);
            Assert.Equal(9, game.Frame);
            Assert.Equal(77u, game.ConfigHash);
            Assert.Equal("match-abc", game.MatchId);
            Assert.NotNull(game.Match);
            Assert.Equal(1, game.Match.Phase);
            Assert.Equal(100, game.Match.Timer);
        }

        [Fact]
        public void StartGame_MatchId空值归一为空串()
        {
            var game = HandshakePacker.BuildStartGame(new MatchStarted(1L, 0, 0u));

            Assert.Equal("", game.MatchId);
        }
    }
}
