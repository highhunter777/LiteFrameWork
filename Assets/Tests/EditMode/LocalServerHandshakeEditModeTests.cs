using System;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using RoomServer.Runtime;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 本地服回执字段面契约（离线隔离开发档）：真服/本地服的 JoinAck/StartGame 构造已收进
    /// <c>RoomServer.Application.HandshakePacker</c> 单源——本用例在**本地服 wire 面**钉住
    /// "字段真的发出来了"（曾缺 IsOwner/ExpectedPlayers/MatchId/Match 一族：两端手抄、只在一端补，
    /// 且本地服照常工作掩盖真服断点的镜像问题）。对端钉子：.NET 侧 HandshakePackerTests
    /// （packer 单元面）＋ RoomExpectedPlayersTests/OwnerStartHostTests/MatchRulesClosureTests（真服集成面）。
    /// 分类口径：EditMode 用 Category（Unity NUnit 3.5 无 TraitAttribute，既有约定）。
    /// </summary>
    public sealed class LocalServerHandshakeEditModeTests : UnityTestBase
    {
        /// <summary>与 BattleSessionEditModeTests.GreyboxMap 同值就地构建（Unity 侧测试无法源链接 Tests/ 夹具）。</summary>
        private static SimMapData GreyboxMap()
        {
            var obstacles = new SimObstacle[5];
            obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, -69f), HalfX = 70f, HalfZ = 1f, Height = 20f };
            obstacles[1] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 69f), HalfX = 70f, HalfZ = 1f, Height = 20f };
            obstacles[2] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(-69f, 0f, 0f), HalfX = 1f, HalfZ = 70f, Height = 20f };
            obstacles[3] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(69f, 0f, 0f), HalfX = 1f, HalfZ = 70f, Height = 20f };
            obstacles[4] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(5f, 0f, 0f), HalfX = 0.5f, HalfZ = 0.5f, Height = 1f };

            var spawns = new SimVector3[SimMapData.MaxSpawnPoints];
            for (int i = 0; i < spawns.Length; i++)
                spawns[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);

            return SimMapBuilder.Build(0f, 100f, 100f, obstacles, obstacles.Length, spawns, spawns.Length);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 本地服进房回执_字段面与真服一致()
        {
            var transport = LiteGame.LocalServerTransport.ForRoom("Contract", expectedPlayers: 3, map: GreyboxMap());
            using (transport)
            {
                JoinAck ack = null;
                StartGame start = null;
                transport.OnData += (data, reliable) =>
                {
                    if (!PacketCodec.TryDecode(data, out PacketType type, out var msg)) return;
                    if (type == PacketType.JoinAck) ack = (JoinAck)msg;
                    else if (type == PacketType.StartGame) start = (StartGame)msg;
                };

                transport.Connect("local", 0);
                transport.TickIncoming();     // 挂起的连接落定（与真 KCP 同为 Tick 驱动）
                transport.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Join,
                    new JoinRequest { RoomId = "Contract", Token = "tok", BuildHash = LiteNet.BuildHash.Value })), true);

                // Send 内联闭环：Join → 补位 → 代房主开局 → 回执经 OnData 同步到达
                Assert.NotNull(ack, "本地服应回 JoinAck");
                Assert.NotNull(start, "本地服应代房主开局并广播 StartGame");

                // JoinAck 字段面（HandshakePacker 单源——与真服逐字段同面）
                Assert.AreEqual(0, ack.PlayerId);
                Assert.AreEqual(1, ack.Members.Count);            // 就位时刻：补位尚未发生
                Assert.IsTrue(ack.IsOwner, "单客户端=首入座者=房主");
                Assert.AreEqual(3, ack.ExpectedPlayers, "房规模权威值（客户端定容/房间页分母）");
                Assert.AreEqual(0, ack.MapId);
                Assert.AreEqual(SimConfig.SnapshotHz, ack.SnapshotHz);
                Assert.AreEqual(SimConfig.TickRate, ack.TickRate);
                Assert.IsNotEmpty(ack.ReconnectToken, "本地形态为占位串但须非空");

                // StartGame 字段面
                Assert.AreNotEqual(0L, start.Seed, "开局种子（未 Tick 时走派生兜底常量，恒非零）");
                Assert.AreEqual(0, start.Frame, "开局时刻权威帧");
                Assert.AreNotEqual("", start.MatchId, "对局身份必须携带（结算对账键）");
                Assert.NotNull(start.Match, "比赛状态层必须携带（客户端镜像开局状态）");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 本地服对局终局_客户端收MatchEnded带结果()
        {
            var transport = LiteGame.LocalServerTransport.ForRoom("Contract-End", expectedPlayers: 3, map: GreyboxMap());
            using (transport)
            {
                StartGame start = null;
                MatchEnded ended = null;
                bool? endedReliable = null;
                transport.OnData += (data, reliable) =>
                {
                    if (!PacketCodec.TryDecode(data, out PacketType type, out var msg)) return;
                    if (type == PacketType.StartGame) start = (StartGame)msg;
                    else if (type == PacketType.MatchEnded) { ended = (MatchEnded)msg; endedReliable = reliable; }
                };

                transport.Connect("local", 0);
                transport.TickIncoming();
                transport.Send(new ArraySegment<byte>(PacketCodec.Encode(PacketType.Join,
                    new JoinRequest { RoomId = "Contract-End", Token = "tok", BuildHash = LiteNet.BuildHash.Value })), true);
                Assert.NotNull(start, "前置：代房主开局成立");

                // 宿主级关闭（运维口径）：对局中 → 结算收尾 → SettlementReady 转 MatchEnded
                transport.ShutdownHost(ShutdownReason.Operator);

                Assert.NotNull(ended, "终局信令必须发出——此前 SettlementReady 被静默丢弃（僵尸对局根因）");
                Assert.IsTrue(endedReliable == true, "客户端只收 reliable MatchEnded（不可靠面即弃）——本地服必走可靠通道");
                Assert.AreEqual(start.MatchId, ended.MatchId, "终局身份与开局身份同源（客户端对账键）");
                Assert.AreEqual((int)ShutdownReason.Operator, ended.EndReason);
                Assert.AreEqual((int)MatchEndReason.External, ended.GameplayEndReason, "Operator 关闭的玩法终因＝External（非时限）");
                Assert.AreEqual(start.Frame, ended.FinalFrame, "权威帧未推进（EditMode 无真实帧泵，帧面冻结于开局时刻）");
                Assert.AreEqual(3, ended.Players.Count, "成员对账行全员携带（结算页数据源——含站桩补位席位）");
                Assert.AreEqual(0, ended.Players[0].PlayerId, "本地客户端＝首入座 0 号");
                Assert.AreEqual(0, ended.Players[0].Kills, "未交战零击杀");
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 本地服未开局收尾_客户端收MatchEnded无结果散场()
        {
            var transport = LiteGame.LocalServerTransport.ForRoom("Contract-Abort", expectedPlayers: 2, map: GreyboxMap());
            using (transport)
            {
                MatchEnded ended = null;
                transport.OnData += (data, reliable) =>
                {
                    if (!PacketCodec.TryDecode(data, out PacketType type, out var msg)) return;
                    if (type == PacketType.MatchEnded) ended = (MatchEnded)msg;
                };

                // 未入座即收尾（等待期运维/超时口径）→ Abort 路径 → 无结果散场信令
                transport.ShutdownHost(ShutdownReason.WaitingTimeout);

                Assert.NotNull(ended, "等待期收尾必须发终局信令（真服同口径）——不得静默散场");
                Assert.AreEqual(0, ended.Players.Count, "无结果可结算——Players 恒空");
                Assert.AreEqual((int)ShutdownReason.WaitingTimeout, ended.EndReason);
                Assert.AreEqual((int)MatchEndReason.None, ended.GameplayEndReason);
                Assert.AreEqual(0L, ended.WinnerEntityId);
            }
        }
    }
}
