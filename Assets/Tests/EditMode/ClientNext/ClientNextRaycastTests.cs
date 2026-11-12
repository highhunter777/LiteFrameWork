using LiteSim;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>命中几何单测：水平射线从 +Z 打向 -Z 的实体，验证 SimRaycast 本身。</summary>
    public sealed class ClientNextRaycastTests
    {
        [Test]
        public void SimStep_开火伤到前方实体()
        {
            var world = new SimWorldState();
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 3f), Flags = EntityFlags.Player },
                out int shooterSlot);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Flags = EntityFlags.Player },
                out int targetSlot);
            var inputs = new[]
            {
                new SimInputFrame
                {
                    EntityId = world.Entities[shooterSlot].Id,
                    Buttons = SimInputFrame.ButtonFire,
                    AimPointX = 0f, AimPointY = 1.2f, AimPointZ = 0f,
                },
            };
            var map = new SimMapData { GroundY = 0f, HalfWidth = 10f, HalfDepth = 10f };
            SimStep.Step(world, map, inputs, CombatValues.Default, WeaponTable.Default,
                advanceMatch: false, rules: null, resolveCombat: true);

            Assert.Less(world.Entities[targetSlot].Hp, 100, "开火未造成伤害");
        }

        [Test]
        public void 水平射线_命中前方实体()
        {
            var world = new SimWorldState();
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Flags = EntityFlags.Player },
                out int slot);
            bool hit = SimRaycast.RaycastEntities(world, skipSlot: -1,
                ox: 0f, oy: 1.2f, oz: 3f, dx: 0f, dz: -1f, dy: 0f, maxT: 10f,
                out int hitSlot, out float distance, CombatGeometry.Default);

            Assert.IsTrue(hit, "射线应命中实体");
            Assert.AreEqual(0, hitSlot);
            Assert.Greater(distance, 0f);
            Assert.LessOrEqual(distance, 3f);
        }
    }
}
