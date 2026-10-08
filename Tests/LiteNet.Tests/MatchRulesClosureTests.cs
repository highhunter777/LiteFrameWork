using System;
using System.Collections.Generic;
using System.IO;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class MatchRulesClosureTests
    {
        private static RoomRuntime Room(MatchRules? rules = null)
        {
            var room = new RoomRuntime(new RoomConfig { RoomId = "Rules", Seed = 42, ExpectedPlayers = 2,
                Rules = rules ?? new MatchRules(0, 1, 3, 2) });
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            return room;
        }

        private static void Damage(RoomRuntime room, int target = 1, int source = 0)
            => room.AuthSim.Cmds.Write(SimCommandKind.Damage, room.EntityIdOf(target), room.EntityIdOf(source), 1000);

        [Fact]
        public void NewRoomInstancesHaveIndependentSettlementIdsEvenWithSameRoomAndSeed()
        {
            var config = new RoomConfig { RoomId = "Rules", Seed = 42 };
            var first = new RoomInstance(config);
            var second = new RoomInstance(config);
            Assert.NotEqual(first.Runtime.MatchId, second.Runtime.MatchId);
            Assert.Equal(first.Runtime.RoomId, second.Runtime.RoomId);
            var direct = new RoomRuntime(config, matchId: "deterministic-test-match");
            Assert.Equal("deterministic-test-match", direct.MatchId);
        }

        [Fact]
        public void ScoreLimitClosesRoomAndFreezesSingleSettlement()
        {
            var room = Room();
            var outputs = new List<RoomOutput>();
            Damage(room);
            room.Execute(RoomCommand.Tick(10), outputs);
            Assert.Equal(MatchPhase.Closed, room.Phase);
            var ready = Assert.IsType<SettlementReadyOutput>(Assert.Single(outputs, o => o is SettlementReadyOutput));
            Assert.Equal(ShutdownReason.KillLimit, ready.Summary.EndReason);
            Assert.Equal(MatchEndReason.KillLimit, ready.Summary.GameplayEndReason);
            Assert.Equal(room.EntityIdOf(0), ready.Summary.WinnerEntityId);
            Assert.Equal(1, ready.Summary.Players[0].Kills);
            Assert.Equal(1, ready.Summary.Players[1].Deaths);
            int finalFrame = room.AuthSim.Frame;
            room.AuthSim.Entities[0].Kills = 500; // 结果与可变 Sim 完全隔离
            Assert.Equal(1, ready.Summary.Players[0].Kills);
            outputs.Clear();
            room.Execute(RoomCommand.Tick(999999), outputs);
            room.Execute(RoomCommand.Shutdown(ShutdownReason.Operator), outputs);
            Assert.Equal(finalFrame, room.AuthSim.Frame);
            Assert.DoesNotContain(outputs, o => o is SettlementReadyOutput);
        }

        [Fact]
        public void FrameTimeLimitIsIndependentOfWallTimeAndUsesFinalScores()
        {
            var room = Room(new MatchRules(3, 0, 3, 2));
            var outputs = new List<RoomOutput>();
            room.AuthSim.Entities[1].Kills = 2;
            room.Execute(RoomCommand.Tick(100000), outputs);
            room.Execute(RoomCommand.Tick(200000), outputs);
            Assert.True(room.Started);
            room.Execute(RoomCommand.Tick(300000), outputs);
            var ready = Assert.IsType<SettlementReadyOutput>(Assert.Single(outputs, o => o is SettlementReadyOutput));
            Assert.Equal(3, ready.Summary.FinalFrame);
            Assert.Equal(room.EntityIdOf(1), ready.Summary.WinnerEntityId);
            Assert.Equal(ShutdownReason.TimeLimit, ready.Summary.EndReason);
        }

        [Fact]
        public void RespawnAndReconnectKeepSeatIdentityAndPrivateState()
        {
            var room = Room(new MatchRules(0, 0, 3, 2));
            var outputs = new List<RoomOutput>();
            long id = room.EntityIdOf(1);
            Damage(room);
            room.Execute(RoomCommand.Tick(1), outputs);
            Assert.NotNull(SnapshotCodec.PackPrivate(room.AuthSim, id));
            room.Execute(RoomCommand.Disconnect(2), outputs);
            for (int i = 2; i <= 4; i++) room.Execute(RoomCommand.Tick(i), outputs);
            room.Execute(RoomCommand.Rebind(1, 3), outputs);
            room.Execute(RoomCommand.RestoreAck(1), outputs);
            Assert.Equal(id, room.EntityIdOf(1));
            Assert.Equal(id, room.SeatOf(1).EntityId);
            Assert.Equal(SeatPhase.Active, room.SeatOf(1).Phase);
            Assert.Equal(CombatValues.Default.EntityHp, room.AuthSim.Entities[1].Hp);
            Assert.Equal(1, room.AuthSim.Entities[1].Deaths);
        }

        [Fact]
        public void RoomRulesAreCapturedBeforeConfigMutation()
        {
            var config = new RoomConfig { ExpectedPlayers = 1, Rules = new MatchRules(2, 4, 3, 5) };
            var room = new RoomRuntime(config);
            config.Rules = new MatchRules(0, 0, 30, 50);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            Assert.Equal(2, room.AuthSim.Match.Timer);
            Assert.Equal(4, room.AuthSim.Match.KillLimit);
            Assert.Equal(3, room.AuthSim.Match.RespawnDelayFrames);
            Assert.Equal(5, room.AuthSim.Match.SpawnProtectionFrames);
            var started = Assert.IsType<MatchStarted>(Assert.IsType<SignalOutput>(
                Assert.Single(outputs, o => o is SignalOutput signal && signal.Signal is MatchStarted)).Signal);
            Assert.Equal(room.AuthSim.Match, started.MatchState);
        }

        [Fact]
        public void FrozenResultOwnsInputAndOutputArrays()
        {
            int[] seats = { 0, 1 };
            var players = new[] { new PlayerMatchResult(0, 65536L, 3, 2) };
            var result = new MatchResultSummary("Rules", 42, 100, ShutdownReason.KillLimit, seats,
                65536L, MatchEndReason.KillLimit, players);
            seats[0] = 100;
            players[0] = default;
            result.SeatPlayerIds[0] = 200;
            result.Players[0] = default;
            Assert.Equal(0, result.SeatPlayerIds[0]);
            Assert.Equal(3, result.Players[0].Kills);
        }

        [Fact]
        public void DeadAndRespawnedPublicStatesRoundTripExactly()
        {
            var room = Room(new MatchRules(0, 0, 3, 2));
            var outputs = new List<RoomOutput>();
            Damage(room);
            var mirror = new SimWorldState();
            for (int i = 1; i <= 5; i++)
            {
                room.Execute(RoomCommand.Tick(i), outputs);
                Proto.StateSnapshot msg = SnapshotCodec.PackFull(room.AuthSim.Frame, room.AuthSim, i);
                byte[] bytes = PacketCodec.Encode(PacketType.StateSnapshot, msg);
                Assert.True(PacketCodec.TryDecode(new ArraySegment<byte>(bytes), out _, out var decoded));
                SnapshotReassembler.Apply((Proto.StateSnapshot)decoded, mirror, out _);
                Assert.Equal(msg.Checksum, SimChecksum.ComputePublicChecksum(mirror));
                Assert.Equal(room.AuthSim.Entities[1].RespawnFrame, mirror.Entities[1].RespawnFrame);
                Assert.Equal(room.AuthSim.Entities[1].InvulnerableUntilFrame, mirror.Entities[1].InvulnerableUntilFrame);
                Assert.Equal(room.AuthSim.Entities[1].LifeStartFrame, mirror.Entities[1].LifeStartFrame);
                Assert.Equal(room.AuthSim.Match, mirror.Match);
            }
        }

        [Fact]
        public void NewRespawnFieldsAreCapturedByDiffBaseline()
        {
            var room = Room(new MatchRules(0, 0, 3, 2));
            var baseline = new SimWorldStateSnapshot();
            baseline.CaptureFull(room.AuthSim);
            room.AuthSim.Entities[1].RespawnFrame = 10;
            Assert.False(baseline.Matches(1, room.AuthSim));
            baseline.CaptureFull(room.AuthSim);
            room.AuthSim.Entities[1].LifeStartFrame = 4;
            Assert.False(baseline.Matches(1, room.AuthSim));
            baseline.CaptureFull(room.AuthSim);
            room.AuthSim.Entities[1].InvulnerableUntilFrame = 12;
            Assert.False(baseline.Matches(1, room.AuthSim));
        }

        [Theory]
        [InlineData("match_duration_frames", "-1")]
        [InlineData("kill_limit", "-1")]
        [InlineData("respawn_delay_frames", "0")]
        [InlineData("spawn_protection_frames", "-1")]
        [InlineData("kill_limit", "2147483648")]
        [InlineData("kill_limit", "1.5")]
        public void InvalidTemplateRulesFailBeforeRoomCreation(string field, string value)
        {
            string json = "{\"port\":17777,\"max_rooms\":4,\"default_template\":\"standard\",\"rooms\":{\"standard\":{\"expected_players\":2,\""
                + field + "\":" + value + "}}}";
            Assert.Throws<InvalidDataException>(() => RoomServerConfig.Parse(json));
        }

        [Fact]
        public void TemplateRulesReachRoomAndClientStartMessage()
        {
            var config = RoomServerConfig.Parse("{\"port\":17777,\"max_rooms\":4,\"default_template\":\"standard\",\"rooms\":{\"standard\":{"
                + "\"expected_players\":1,\"match_duration_frames\":4,\"kill_limit\":7,"
                + "\"respawn_delay_frames\":11,\"spawn_protection_frames\":13}}}");
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport, config.BuildRoomConfig("standard", "Rules"));
            Join(transport, 1);
            Proto.StartGame start = transport.LastStartGame(1);
            Assert.NotNull(start.Match);
            Assert.Equal(4, start.Match.Timer);
            Assert.Equal(7, start.Match.KillLimit);
            Assert.Equal(11, start.Match.RespawnDelayFrames);
            Assert.Equal(13, start.Match.SpawnProtectionFrames);
        }

        private static void Join(FakeRoomTransport transport, int id)
        {
            transport.RaiseConnected(id);
            transport.RaiseData(id, PacketCodec.Encode(PacketType.Join,
                new Proto.JoinRequest { RoomId = "Rules", Token = "token", BuildHash = ServerHost.ServerBuildHash }));
        }

        [Fact]
        public void HostSendsFrozenResultsToBothPlayersOnce()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { RoomId = "Rules", Seed = 42, Rules = new MatchRules(0, 1, 3, 2) });
            Join(transport, 1);
            Join(transport, 2);
            Damage(host.Room);
            host.Pump();
            foreach (int conn in new[] { 1, 2 })
            {
                var result = Assert.IsType<Proto.MatchEnded>(transport.Last(conn, PacketType.MatchEnded));
                Assert.Equal(1, result.Players[0].Kills);
                Assert.Equal(1, result.Players[1].Deaths);
                Assert.Equal((int)MatchEndReason.KillLimit, result.GameplayEndReason);
                Assert.Equal(1, transport.CountOf(conn, PacketType.MatchEnded));
            }
            host.Pump();
            Assert.Equal(1, transport.CountOf(1, PacketType.MatchEnded));
        }

        [Fact]
        public void CompensatedShotOnlyDamagesOnceAndCannotBypassCooldown()
        {
            var weapons = new WeaponTable();
            WeaponDef d = WeaponConfig.DefaultRifle;
            weapons.SetRow(d.Id, 25, 600, 30, 90, 60, 100f, 0f, 1, 2, true);
            weapons.SetSlotDefault(0, d.Id);
            var room = new RoomRuntime(new RoomConfig { RoomId = "Rules", ExpectedPlayers = 2,
                Rules = new MatchRules(0, 0, 3, 0) }, weapons: weapons);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.AuthSim.Entities[0].Pos = new SimVector3(0, 0, 0);
            room.AuthSim.Entities[1].Pos = new SimVector3(20, 0, 0);
            for (int i = 1; i <= 6; i++) room.Execute(RoomCommand.Tick(i), outputs);
            void Fire()
            {
                var batch = new ClientInputBatch { Frame = room.AuthSim.Frame + 1, Count = 1,
                    ViewFrame = room.AuthSim.Frame + SimConfig.InterpFrames, AckSnapshot = room.AuthSim.Frame,
                    Frames = new[] { new SimInputFrame { Buttons = SimInputFrame.ButtonFire,
                        AimPointX = 20, AimPointY = 1f } } };
                room.Execute(RoomCommand.ClientInput(0, batch), outputs);
                room.Execute(RoomCommand.Tick(room.AuthSim.Frame + 1), outputs);
            }
            Fire();
            Assert.Equal(1L, room.FireInputsProcessed);
            Assert.True(room.LagComp.LastHit, "lag=" + room.LagComp.LastOutcome + " target=" + room.LagComp.LastTargetFrame
                + " ammo=" + room.AuthSim.Weapons[0].MagAmmo + " frame=" + room.AuthSim.Frame);
            int hp = room.AuthSim.Entities[1].Hp;
            Assert.InRange(hp, 74, 76);
            Assert.Equal(29, room.AuthSim.Weapons[0].MagAmmo);
            Fire();
            Assert.Equal(hp, room.AuthSim.Entities[1].Hp);
            Assert.Equal(29, room.AuthSim.Weapons[0].MagAmmo);
            Assert.Equal(1L, room.FireInputsProcessed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void HistoricalHitsRespectCurrentLifeAndProtectionAndPreserveCurrentRng(int scenario)
        {
            var world = new SimWorldState { Frame = 2, RngState = 42UL };
            var map = new SimMapData { GroundY = 0, HalfWidth = 100, HalfDepth = 100 };
            var weapons = new WeaponTable();
            WeaponDef def = WeaponConfig.DefaultRifle;
            weapons.SetRow(def.Id, 25, 600, 30, 90, 60, 100f, 0f, 1, 2, true);
            weapons.SetSlotDefault(0, def.Id);
            world.Spawn(new EntitySlot { Hp = 100 }, out _);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20, 0, 0) }, out _);
            WeaponSystem.Run(world, Array.Empty<SimInputFrame>(), weapons);
            var ring = new SnapshotRing(SimConfig.LagCompHistory);
            ring.Capture(2, world);
            var lag = new LagCompensator(world, map, 2, ring, CombatValues.Default, weapons);
            world.Frame = 6;
            world.RngState = 9876UL;
            if (scenario == 1) world.Entities[1].LifeStartFrame = 4;
            if (scenario == 2) world.Entities[1].InvulnerableUntilFrame = 8;
            if (scenario == 3) world.Entities[0].LifeStartFrame = 4;
            lag.RecordInputs(6, new[] { new SimInputFrame { EntityId = world.Entities[0].Id,
                Buttons = SimInputFrame.ButtonFire, AimPointX = 20, AimPointY = 1 }, default });
            lag.CompensateFire(0, world.Entities[0].Id, 2 + SimConfig.InterpFrames, 2);
            Assert.Equal(9876UL, world.RngState);
            SimStep.FlushCommands(world);
            Assert.Equal(scenario == 0, lag.LastHit);
            if (scenario != 0) Assert.Equal(100, world.Entities[1].Hp);
            else Assert.InRange(world.Entities[1].Hp, 74, 76);
        }

        [Fact]
        public void LastFrameCompensatedKillsSettleBeforeTimeLimitAndMutualKillsDraw()
        {
            var weapons = new WeaponTable();
            WeaponDef def = WeaponConfig.DefaultRifle;
            weapons.SetRow(def.Id, 1000, 600, 30, 90, 60, 100f, 0f, 1, 2, true);
            weapons.SetSlotDefault(0, def.Id);
            var room = new RoomRuntime(new RoomConfig { Rules = new MatchRules(7, 1, 3, 0) }, weapons: weapons);
            var outputs = new List<RoomOutput>();
            room.Execute(RoomCommand.Join(1), outputs);
            room.Execute(RoomCommand.Join(2), outputs);
            room.AuthSim.Entities[0].Pos = new SimVector3(-10, 0, -10);
            room.AuthSim.Entities[1].Pos = new SimVector3(10, 0, -10);
            for (int i = 1; i <= 6; i++) room.Execute(RoomCommand.Tick(i), outputs);
            for (int i = 0; i < 2; i++)
                room.Execute(RoomCommand.ClientInput(i, new ClientInputBatch { Frame = 7, Count = 1,
                    AckSnapshot = 6, ViewFrame = 6 + SimConfig.InterpFrames,
                    Frames = new[] { new SimInputFrame { Buttons = SimInputFrame.ButtonFire,
                        AimPointX = room.AuthSim.Entities[1 - i].Pos.X, AimPointY = 1, AimPointZ = -10 } } }), outputs);
            room.Execute(RoomCommand.Tick(7), outputs);
            var result = Assert.IsType<SettlementReadyOutput>(Assert.Single(outputs, o => o is SettlementReadyOutput)).Summary;
            Assert.Equal(7, result.FinalFrame);
            Assert.Equal(MatchEndReason.KillLimit, result.GameplayEndReason);
            Assert.Equal(0L, result.WinnerEntityId);
            Assert.All(result.Players, p => { Assert.Equal(1, p.Kills); Assert.Equal(1, p.Deaths); });
        }
    }
}
