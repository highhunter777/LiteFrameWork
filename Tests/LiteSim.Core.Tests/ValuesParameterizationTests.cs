using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 技术债 #1 根治证明（《待办总览》§7.2 #1）：玩法数值按**实例**参数化——
    /// 自定义值经参数直达机制（SimStep/系统），全程不触碰任何全局读口；
    /// 无串行集、无 finally 还原（对比旧"静态装载面"用例形态：`CombatNumbersTests` 原串行集已拆除）。
    /// </summary>
    public sealed class ValuesParameterizationTests
    {
        private static SimWorldState WorldWithSpawn(long hp, out long id)
        {
            var world = new SimWorldState();
            id = world.Spawn(new EntitySlot { Hp = (int)hp, Pos = new SimVector3(0f, 0f, 0f) }, out int _);
            return world;
        }

        [Fact]
        public void 实例移速_直达SimStep()
        {
            var world = WorldWithSpawn(100, out long id);
            var values = new CombatValues(7.5f, -20f, 100f, 25, 1, 100);
            var inputs = new[] { new SimInputFrame { EntityId = id, MoveX = 1f } };

            Assert.True(world.TryResolve(id, out int slot));
            SimStep.Step(world, SimChecksumBaselineSpec.BuildMap(), inputs, values, WeaponTable.Default);

            Assert.Equal(7.5f, world.Entities[slot].Vel.X, 4);           // 实例值写进权威态（非默认 5）
        }

        [Fact]
        public void 实例瞄准限速_直达SimStep()
        {
            var world = WorldWithSpawn(100, out long id);
            var values = new CombatValues(7.5f, -20f, 100f, 25, 1, 100);
            var inputs = new[] { new SimInputFrame { EntityId = id, MoveX = 1f, Buttons = SimInputFrame.ButtonAim } };

            Assert.True(world.TryResolve(id, out int slot));
            SimStep.Step(world, SimChecksumBaselineSpec.BuildMap(), inputs, values, WeaponTable.Default);

            Assert.Equal(3.75f, world.Entities[slot].Vel.X, 4);          // 7.5 × AimMoveSpeedFactor(0.5)，位级精确
        }

        [Fact]
        public void 实例重力_直达MovementSystem()
        {
            var world = new SimWorldState();
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 10f, 0f) }, out int _);
            var values = new CombatValues(5f, -9.8f, 100f, 25, 1, 100);

            Assert.True(world.TryResolve(id, out int slot));
            MovementSystem.Run(world.Entities, world.AliveBitmap, SimChecksumBaselineSpec.BuildMap(), values);

            Assert.Equal(-9.8f * SimConfig.Dt, world.Entities[slot].Vel.Y, 4);   // 实例重力积分（非默认 -20）
        }

        [Fact]
        public void 摘要按实例_同值同摘要异值异摘要()
        {
            var a = new CombatValues(7.5f, -9.8f, 50f, 40, 0, 130);
            var b = new CombatValues(7.5f, -9.8f, 50f, 40, 0, 130);
            var c = new CombatValues(7.5f, -9.8f, 50f, 40, 0, 131);

            Assert.Equal(CombatConfigDigest.Compute(a), CombatConfigDigest.Compute(b));
            Assert.NotEqual(CombatConfigDigest.Compute(a), CombatConfigDigest.Compute(c));
            Assert.NotEqual(CombatConfigDigest.Compute(CombatValues.Default), CombatConfigDigest.Compute(a));
        }

        [Fact]
        public void 实例武器表_直达SimStep()
        {
            // 同一射手/目标几何，两套自定义武器表 → 命中与否沿表值分岔（无任何全局装载/还原）
            var values = new CombatValues(5f, -20f, 100f, 25, 0, 100);   // spread=0：伤害确定 = 武器表值
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };

            // 表A：射程 5 < 目标距离 20 → 打不中（Hp 不变）
            var shortRange = new WeaponTable();
            shortRange.SetRow(WeaponConfig.DefaultRifleId, damage: 40, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                reloadFrames: 132, range: 5f, spread: 1.2f, pellets: 1, switchFrames: 30, automatic: true);
            var worldA = new SimWorldState { RngState = 1UL };
            long shooterA = worldA.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long targetA = worldA.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);
            SimStep.Step(worldA, map, new[] { new SimInputFrame { EntityId = shooterA, AimPointX = 20f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } }, values, shortRange);
            Assert.True(worldA.TryResolve(targetA, out int slotA));
            Assert.Equal(100, worldA.Entities[slotA].Hp);                // 射程外：未命中

            // 表B：射程 100 → 命中且伤害 = 表值 40（HP 100→60）
            var longRange = new WeaponTable();
            longRange.SetRow(WeaponConfig.DefaultRifleId, damage: 40, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                reloadFrames: 132, range: 100f, spread: 1.2f, pellets: 1, switchFrames: 30, automatic: true);
            var worldB = new SimWorldState { RngState = 1UL };
            long shooterB = worldB.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long targetB = worldB.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);
            SimStep.Step(worldB, map, new[] { new SimInputFrame { EntityId = shooterB, AimPointX = 20f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } }, values, longRange);
            Assert.True(worldB.TryResolve(targetB, out int slotB));
            Assert.Equal(60, worldB.Entities[slotB].Hp);                 // 40 伤害 = 表值（散 0，无浮动）
        }
    }
}
