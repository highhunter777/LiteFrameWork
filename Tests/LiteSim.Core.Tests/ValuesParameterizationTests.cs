using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 技术债 #1 根治证明（《待办总览》§7.2 #1）：玩法数值按**实例**参数化——
    /// 自定义值经参数直达机制（SimStep/系统），全程不触碰任何全局读口；
    /// 无串行集、无 finally 还原（对比旧"静态装载面"用例形态：`ServerTableLoadTests` 原串行集已拆除）。
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
            var values = new CombatValues(7.5f, -20f, 100f, 25, 1, 100, 2.5f, 60);
            var inputs = new[] { new SimInputFrame { EntityId = id, MoveX = 1f } };

            Assert.True(world.TryResolve(id, out int slot));
            SimStep.Step(world, SimChecksumBaselineSpec.BuildMap(), inputs, values, WeaponTable.Default);

            Assert.Equal(7.5f, world.Entities[slot].Vel.X, 4);           // 实例值写进权威态（非默认 5）
        }

        [Fact]
        public void 实例瞄准限速_直达SimStep()
        {
            var world = WorldWithSpawn(100, out long id);
            // 限速档**独立于** MoveSpeed：moveSpeed 给 7.5（默认 5），aim_move_speed 给 2.5。
            // 旧式口径 MoveSpeed × 0.5（=3.75）已随表化退役——现直落最终速度，
            // 代价是调 move_speed 不再带动限速档——本用例即钉这条语义（见 CombatValues.AimMoveSpeed）。
            var values = new CombatValues(7.5f, -20f, 100f, 25, 1, 100, 2.5f, 60);
            var inputs = new[] { new SimInputFrame { EntityId = id, MoveX = 1f, Buttons = SimInputFrame.ButtonAim } };

            Assert.True(world.TryResolve(id, out int slot));
            SimStep.Step(world, SimChecksumBaselineSpec.BuildMap(), inputs, values, WeaponTable.Default);

            Assert.Equal(2.5f, world.Entities[slot].Vel.X, 4);      // = aim_move_speed 表值，**非** 7.5 × 0.5
        }

        [Fact]
        public void 实例重力_直达MovementSystem()
        {
            var world = new SimWorldState();
            long id = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 10f, 0f) }, out int _);
            var values = new CombatValues(5f, -9.8f, 100f, 25, 1, 100, 2.5f, 60);

            Assert.True(world.TryResolve(id, out int slot));
            MovementSystem.Run(world.Entities, world.AliveBitmap, SimChecksumBaselineSpec.BuildMap(), values);

            Assert.Equal(-9.8f * SimConfig.Dt, world.Entities[slot].Vel.Y, 4);   // 实例重力积分（非默认 -20）
        }

        [Fact]
        public void 摘要终值_随本局规则初值区分_与基摘要绑定()
        {
            // 批 E 起身份输入 = 判定面表字节（基摘要）+ 本局规则初值（终值）——不再按值投影字段；
            // 本用例钉"规则初值经实例进身份"（测试房与常规房可区分）：值侧入口只剩 NoDeath/InfiniteAmmo。
            var plain = CombatValues.Default;
            var noDeath = CombatValues.Default;
            noDeath.NoDeath = true;

            uint a = CombatConfigDigest.WithTestRules(0x1234u, plain.NoDeath, plain.InfiniteAmmo);
            uint b = CombatConfigDigest.WithTestRules(0x1234u, noDeath.NoDeath, noDeath.InfiniteAmmo);

            Assert.Equal(a, CombatConfigDigest.WithTestRules(0x1234u, false, false));   // 确定性
            Assert.NotEqual(a, b);                                                      // 规则初值改变房间身份
        }

        [Fact]
        public void 实例武器表_直达SimStep()
        {
            // 同一射手/目标几何，两套自定义武器表 → 命中与否沿表值分岔（无任何全局装载/还原）
            var values = new CombatValues(5f, -20f, 100f, 25, 0, 100, 2.5f, 60);   // spread=0：伤害确定 = 武器表值
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };

            // 表A：射程 5 < 目标距离 20 → 打不中（Hp 不变）
            var shortRange = new WeaponTable();
            shortRange.SetRow(WeaponConfig.DefaultRifleId, damage: 40, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                maxReserveAmmo: 90, reloadFrames: 132, range: 5f, spread: 0f, pellets: 1, switchFrames: 30, automatic: true);
            var worldA = new SimWorldState { RngState = 1UL };
            long shooterA = worldA.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long targetA = worldA.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);
            SimStep.Step(worldA, map, new[] { new SimInputFrame { EntityId = shooterA, AimPointX = 20f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } }, values, shortRange);
            Assert.True(worldA.TryResolve(targetA, out int slotA));
            Assert.Equal(100, worldA.Entities[slotA].Hp);                // 射程外：未命中

            // 表B：射程 100 → 命中且伤害 = 表值 40（HP 100→60）
            var longRange = new WeaponTable();
            longRange.SetRow(WeaponConfig.DefaultRifleId, damage: 40, rpm: 600, magazineSize: 30, reserveAmmo: 90,
                maxReserveAmmo: 90, reloadFrames: 132, range: 100f, spread: 0f, pellets: 1, switchFrames: 30, automatic: true);
            var worldB = new SimWorldState { RngState = 1UL };
            long shooterB = worldB.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long targetB = worldB.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, 0f) }, out _);
            SimStep.Step(worldB, map, new[] { new SimInputFrame { EntityId = shooterB, AimPointX = 20f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } }, values, longRange);
            Assert.True(worldB.TryResolve(targetB, out int slotB));
            Assert.Equal(60, worldB.Entities[slotB].Hp);                 // 40 伤害 = 表值（散 0，无浮动）
        }
    }
}
