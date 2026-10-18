using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 客户端预测隔离（P0）：`resolveCombat:false` 时保留开火资源/开火窗/Fire 事件，
    /// 但**不产权威伤害与命中/死亡事实**——命中、伤害、死亡只由服务端快照/权威反馈面驱动。
    /// 这是"一边打死、另一边没死"的结构性根因用例。
    /// </summary>
    public sealed class PredictionCombatTests
    {
        private static SimMapData Map() => new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };

        private static SimWorldState SpawnPair(out long shooter, out long target, out int shooterSlot)
        {
            var world = new SimWorldState { RngState = 7UL };
            shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out shooterSlot);
            target = world.Spawn(new EntitySlot { Hp = 1, Pos = new SimVector3(3f, 0f, 0f) }, out _);
            return world;
        }

        private static SimInputFrame[] FireAt(long id)
            => new[] { new SimInputFrame { EntityId = id, AimPointX = 3f, AimPointY = 1f, AimPointZ = 0f, Buttons = SimInputFrame.ButtonFire } };

        private static bool HasEvent(SimWorldState world, FrameEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind) return true;
            return false;
        }

        [Fact]
        public void 预测模式_开火资源照常_伤害与死亡不落本地()
        {
            SimWorldState world = SpawnPair(out long shooter, out long target, out int shooterSlot);
            int beforeHp = world.Entities[shooterSlot].Hp;

            SimStep.Step(world, Map(), FireAt(shooter), CombatValues.Default, TestWeapons.NoSpread(),
                resolveCombat: false);

            Assert.True(HasEvent(world, FrameEventKind.Fire), "预测仍产 Fire（枪口火光/准心反馈）");
            Assert.False(HasEvent(world, FrameEventKind.Hit), "预测模式不得产 Hit");
            Assert.False(HasEvent(world, FrameEventKind.Crit), "预测模式不得产 Crit");
            Assert.False(HasEvent(world, FrameEventKind.Death), "预测模式不得产 Death");

            Assert.True(world.TryResolve(target, out int targetSlot));
            Assert.Equal(1, world.Entities[targetSlot].Hp);       // 伤害不落本地
            Assert.Equal(beforeHp, world.Entities[shooterSlot].Hp);

            // 开火资源/开火窗仍预测（本地手感与武器门一致）
            ref WeaponRuntime weapon = ref world.Weapons[shooterSlot * SimConfig.WeaponSlotsPerEntity];
            Assert.True(weapon.MagAmmo < WeaponConfig.DefaultRifle.MagazineSize, "预测应扣弹");
            Assert.True(world.Entities[shooterSlot].FireStanceFrames > 0, "预测应置开火窗");
        }

        [Fact]
        public void 权威模式_同一发开火照常造成伤害与死亡()
        {
            SimWorldState world = SpawnPair(out long shooter, out long target, out _);

            SimStep.Step(world, Map(), FireAt(shooter), CombatValues.Default, TestWeapons.NoSpread());

            Assert.True(world.TryResolve(target, out int targetSlot));
            Assert.True(world.Entities[targetSlot].Hp <= 0, "权威模式应结算伤害");
            Assert.True(HasEvent(world, FrameEventKind.Hit) || HasEvent(world, FrameEventKind.Crit),
                "权威模式应产命中反馈事件");
            Assert.True(HasEvent(world, FrameEventKind.Death), "跨死线应产 Death");
        }
    }
}
