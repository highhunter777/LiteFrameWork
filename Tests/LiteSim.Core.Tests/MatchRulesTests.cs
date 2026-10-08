using System;
using Xunit;

namespace LiteSim.Tests
{
    public sealed class MatchRulesTests
    {
        private static readonly SimInputFrame[] Empty = Array.Empty<SimInputFrame>();

        private static SimWorldState World(MatchRules? rules = null)
        {
            var s = new SimWorldState { RngState = 42UL };
            MatchSystem.Start(s, rules ?? new MatchRules(0, 0, 3, 2));
            for (int i = 0; i < 2; i++)
                s.Spawn(new EntitySlot { Hp = 100, Flags = EntityFlags.Player, SpawnPointIndex = i }, out _);
            return s;
        }

        private static void Step(SimWorldState s) => SimStep.Step(s, SimMapData.StandardBattleMap(), Empty,
            CombatValues.Default, WeaponTable.Default);

        private static void Kill(SimWorldState s, int target = 1, int source = 0)
        {
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[target].Id, source >= 0 ? s.Entities[source].Id : 0L, 1000);
            SimStep.FlushCommands(s);
        }

        [Fact]
        public void DeathPreservesPlayerIdAndRegistersOneKill()
        {
            var s = World(new MatchRules(0, 0, CombatConfig.CorpseFrames + 5, 2));
            long id = s.Entities[1].Id;
            Kill(s);
            for (int i = 0; i <= CombatConfig.CorpseFrames; i++) Step(s);
            Assert.True(s.TryResolve(id, out int slot));
            Assert.Equal(1, slot);
            Assert.Equal(0, s.Entities[slot].Hp);
            Assert.Equal(0, s.Entities[slot].CorpseFrames);
            Assert.Equal(1, s.Entities[slot].Deaths);
            Assert.Equal(1, s.Entities[0].Kills);
        }

        [Fact]
        public void RespawnOccursAtExactFrameAndKeepsScoreAndBag()
        {
            var s = World();
            s.MatchBag[SimConfig.MatchBagSlotsPerEntity] = new MatchBagSlot { ItemDefId = 701, Count = 2 };
            long id = s.Entities[1].Id;
            s.Entities[1].Kills = 4;
            Kill(s);
            int respawn = s.Entities[1].RespawnFrame;
            while (s.Frame < respawn) Step(s);
            Assert.Equal(0, s.Entities[1].Hp);
            Step(s);
            ref EntitySlot e = ref s.Entities[1];
            Assert.Equal(id, e.Id);
            Assert.Equal(CombatValues.Default.EntityHp, e.Hp);
            Assert.Equal(4, e.Kills);
            Assert.Equal(1, e.Deaths);
            Assert.Equal(0, e.RespawnFrame);
            Assert.Equal(respawn, e.LifeStartFrame);
            Assert.Equal(respawn + 2, e.InvulnerableUntilFrame);
            Assert.Equal(SimMapData.StandardBattleMap().SpawnPoints[1], e.Pos);
            Assert.Equal(2, s.MatchBag[SimConfig.MatchBagSlotsPerEntity].Count);
            Assert.Equal(WeaponSlotState.Ready, s.Weapons[SimConfig.WeaponSlotsPerEntity].State);
            Assert.Contains(s.Events.Items, ev => ev.Kind == FrameEventKind.Respawn && ev.EntityId == id);
        }

        [Fact]
        public void DeathCancelsCombatStateAndRespawnUsesConfiguredHp()
        {
            var s = World();
            s.Actions[SimConfig.ActionSlotsPerEntity].ActionId = 300;
            s.Status[SimConfig.StatusSlotsPerEntity].EffectId = 5;
            s.Resources[1] = 80;
            s.Entities[1].Shield = 30;
            s.Entities[1].FireStanceFrames = 10;
            s.Entities[1].Vel = new SimVector3(5, 0, 5);
            Kill(s);
            Assert.Equal(0, s.Actions[SimConfig.ActionSlotsPerEntity].ActionId);
            Assert.Equal(0, s.Status[SimConfig.StatusSlotsPerEntity].EffectId);
            Assert.Equal(0, s.Resources[1]);
            Assert.Equal(0, s.Entities[1].Shield);
            Assert.Equal(0, s.Entities[1].FireStanceFrames);
            Assert.Equal(0f, s.Entities[1].Vel.X);
            var values = CombatValues.Default;
            values.EntityHp = 175;
            s.Frame = s.Entities[1].RespawnFrame;
            RespawnSystem.Run(s, SimMapData.StandardBattleMap(), values);
            Assert.Equal(175, s.Entities[1].Hp);
            Assert.Equal(WeaponSlotState.Unequipped, s.Weapons[SimConfig.WeaponSlotsPerEntity].State);
        }

        [Fact]
        public void RepeatedDamageAndKillCommandsDoNotDuplicateScore()
        {
            var s = World();
            for (int i = 0; i < 3; i++)
                s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, s.Entities[0].Id, 1000);
            SimStep.FlushCommands(s);
            for (int i = 0; i < 3; i++)
                s.Cmds.Write(SimCommandKind.Kill, s.Entities[1].Id, s.Entities[0].Id, 0);
            SimStep.FlushCommands(s);
            Assert.Equal(1, s.Entities[0].Kills);
            Assert.Equal(1, s.Entities[1].Deaths);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        public void EnvironmentAndSuicideOnlyCountDeath(int source)
        {
            var s = World();
            Kill(s, source: source);
            Assert.Equal(0, s.Entities[0].Kills);
            Assert.Equal(0, s.Entities[1].Kills);
            Assert.Equal(1, s.Entities[1].Deaths);
        }

        [Fact]
        public void InvalidSourceAndNonPlayerVictimDoNotAwardKills()
        {
            var s = World();
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, long.MaxValue, 1000);
            SimStep.FlushCommands(s);
            Assert.Equal(1, s.Entities[1].Deaths);
            long dummy = s.Spawn(new EntitySlot { Hp = 1 }, out _);
            s.Cmds.Write(SimCommandKind.Damage, dummy, s.Entities[0].Id, 5);
            SimStep.FlushCommands(s);
            Assert.Equal(0, s.Entities[0].Kills);
        }

        [Fact]
        public void FullCommandBufferStillRecordsDeath()
        {
            var s = World();
            for (int i = 0; i < CommandBuffer.Capacity; i++)
                s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, s.Entities[0].Id, 1000);
            SimStep.FlushCommands(s);
            Assert.Equal(1, s.Entities[0].Kills);
            Assert.Equal(1, s.Entities[1].Deaths);
            Assert.True(s.Cmds.OverflowCount > 0);
        }

        [Fact]
        public void SpawnProtectionExpiresAtExclusiveBoundary()
        {
            var s = World();
            s.Entities[1].InvulnerableUntilFrame = 5;
            s.Frame = 4;
            Kill(s);
            Assert.Equal(100, s.Entities[1].Hp);
            Assert.Equal(0, s.Entities[1].Deaths);
            s.Frame = 5;
            Kill(s);
            Assert.Equal(1, s.Entities[1].Deaths);
        }

        [Fact]
        public void AcceptedShotCancelsProtectionButBlockedWeaponDoesNot()
        {
            var s = World();
            WeaponTable weapons = TestWeapons.NoSpread();
            WeaponSystem.Run(s, Empty, weapons);
            s.Entities[0].InvulnerableUntilFrame = 10;
            var input = new[] { new SimInputFrame { EntityId = s.Entities[0].Id, Buttons = SimInputFrame.ButtonFire } };
            ShootingSystem.Run(s, SimMapData.StandardBattleMap(), input, CombatValues.Default, weapons);
            Assert.Equal(0, s.Entities[0].InvulnerableUntilFrame);
            s.Entities[0].InvulnerableUntilFrame = 10;
            ShootingSystem.Run(s, SimMapData.StandardBattleMap(), input, CombatValues.Default, weapons);
            Assert.Equal(10, s.Entities[0].InvulnerableUntilFrame);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void NonPositiveDamageIsIgnored(int damage)
        {
            var s = World();
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, s.Entities[0].Id, damage);
            SimStep.FlushCommands(s);
            Assert.Equal(100, s.Entities[1].Hp);
        }

        [Fact]
        public void MaximumDamageDoesNotOverflowHp()
        {
            var s = World();
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, s.Entities[0].Id, int.MaxValue);
            SimStep.FlushCommands(s);
            Assert.Equal(0, s.Entities[1].Hp);
            Assert.Equal(1, s.Entities[0].Kills);
        }

        [Fact]
        public void KillLimitFinishesMatchAndFreezesAllGameplay()
        {
            var s = World(new MatchRules(100, 1, 3, 2));
            Kill(s);
            Step(s);
            Assert.Equal(SimMatchPhase.Finished, s.Match.Phase);
            Assert.Equal(MatchEndReason.KillLimit, s.Match.EndReason);
            Assert.Equal(s.Entities[0].Id, s.Match.Winner);
            uint checksum = SimChecksum.ComputeChecksum(s);
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[0].Id, s.Entities[1].Id, 1000);
            Step(s);
            SimStep.FlushCommands(s);
            Assert.Equal(checksum, SimChecksum.ComputeChecksum(s));
        }

        [Fact]
        public void SameFrameMutualKillsProduceDraw()
        {
            var s = World(new MatchRules(100, 1, 3, 2));
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[0].Id, s.Entities[1].Id, 1000);
            s.Cmds.Write(SimCommandKind.Damage, s.Entities[1].Id, s.Entities[0].Id, 1000);
            Step(s);
            Assert.Equal(1, s.Entities[0].Kills);
            Assert.Equal(1, s.Entities[1].Kills);
            Assert.Equal(1, s.Entities[0].Deaths);
            Assert.Equal(1, s.Entities[1].Deaths);
            Assert.Equal(SimMatchPhase.Finished, s.Match.Phase);
            Assert.Equal(0L, s.Match.Winner);
        }

        [Theory]
        [InlineData(0, 0, -1)]
        [InlineData(4, 3, 0)]
        [InlineData(3, 4, 1)]
        public void TimeLimitUsesHighestKillsOrDraw(int first, int second, int winner)
        {
            var s = World(new MatchRules(2, 0, 3, 2));
            s.Entities[0].Kills = first;
            s.Entities[1].Kills = second;
            Step(s);
            Assert.Equal(1, s.Match.Timer);
            Assert.Equal(SimMatchPhase.Running, s.Match.Phase);
            Step(s);
            Assert.Equal(2, s.Frame);
            Assert.Equal(MatchEndReason.TimeLimit, s.Match.EndReason);
            Assert.Equal(winner < 0 ? 0L : s.Entities[winner].Id, s.Match.Winner);
        }

        [Fact]
        public void ZeroLimitsAllowOngoingMatchAndFinishIsIdempotent()
        {
            var s = World();
            for (int i = 0; i < 5; i++) Step(s);
            Assert.Equal(SimMatchPhase.Running, s.Match.Phase);
            MatchSystem.Finish(s, MatchEndReason.External);
            uint checksum = SimChecksum.ComputeChecksum(s);
            MatchSystem.Finish(s, MatchEndReason.KillLimit);
            Assert.Equal(checksum, SimChecksum.ComputeChecksum(s));
        }

        [Theory]
        [InlineData(-1, 0, 3, 2)]
        [InlineData(0, -1, 3, 2)]
        [InlineData(0, 0, 0, 2)]
        [InlineData(0, 0, 3, -1)]
        public void InvalidRulesAreRejected(int duration, int kills, int respawn, int protection)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules(duration, kills, respawn, protection));

        [Fact]
        public void DeathRespawnSequenceReplaysFromSnapshotExactly()
        {
            var s = World();
            var ring = new SnapshotRing(8);
            Kill(s);
            Step(s);
            ring.Capture(s.Frame, s);
            var replay = new SimWorldState();
            Assert.True(ring.TryRestore(s.Frame, replay));
            for (int i = 0; i < 12; i++)
            {
                if (i == 7) { Kill(s); Kill(replay); }
                Step(s);
                Step(replay);
                Assert.Equal(SimChecksum.ComputeChecksum(s), SimChecksum.ComputeChecksum(replay));
                Assert.Equal(SimChecksum.ComputePublicChecksum(s), SimChecksum.ComputePublicChecksum(replay));
            }
        }
    }
}
