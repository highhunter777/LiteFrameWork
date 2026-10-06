using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 爆头判定与伤害倍率：命中高度带（水平射线 → 命中高度 = 射手眼高，
    /// 相对目标头部带 <see cref="CombatConfig.HeadHitLine"/>）× `2^HeadshotDamageShift` 位级倍率——
    /// 倍率在命中判定处应用，Damage 命令/Hit 事件携带即最终值，结算侧零改动。
    /// 死亡守卫半边（同批）：死亡射手不开火 / 死亡目标不可命中 / 死亡实体输入作废（尸体不受操控）。
    /// </summary>
    public sealed class HeadshotTests
    {
        private static (SimWorldState world, long shooter, long target, int shooterSlot, int targetSlot)
            SpawnPair(float shooterY, float targetY, int targetHp = 100)
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, shooterY, 0f) }, out int ss);
            long target = world.Spawn(new EntitySlot { Hp = targetHp, Pos = new SimVector3(10f, targetY, 0f) }, out int ts);
            return (world, shooter, target, ss, ts);
        }

        private static SimInputFrame[] FireAt(long id, float ax = 1f, float az = 0f)
            => new[] { new SimInputFrame { EntityId = id, AimX = ax, AimZ = az, Buttons = SimInputFrame.ButtonFire } };

        /// <summary>空障碍图（本组只验命中高度/倍率/死亡守卫——不参与障碍遮挡判定）。</summary>
        private static readonly SimMapData NoObstacles = new SimMapData();

        private static int HitValue(SimWorldState world)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == FrameEventKind.Hit)
                    return world.Events.Items[i].Value;
            return -1;
        }

        private static int DamageAmount(SimWorldState world)
        {
            for (int i = 0; i < world.Cmds.Count; i++)
                if (world.Cmds.Items[i].Kind == SimCommandKind.Damage)
                    return world.Cmds.Items[i].Amount;
            return -1;
        }

        [Fact]
        public void 同地平面对枪_命中高度不达爆头带_基础伤害()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));

            int hit = HitValue(world);
            Assert.InRange(hit, CombatConfig.BaseDamage - CombatConfig.DamageSpread,
                CombatConfig.BaseDamage + CombatConfig.DamageSpread);
            Assert.Equal(hit, DamageAmount(world));
            Assert.Equal(2, world.Events.Count);           // Fire + Hit（无 Death——目标未死）
        }

        [Fact]
        public void 高差位命中头部带_倍率移位生效_伤害为偶数区间()
        {
            // 射手高台（Y=1）→ 眼高 2.0 ≥ 目标头部带线 1.7 → 爆头
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 1f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));

            int hit = HitValue(world);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(hit, lower, upper);
            Assert.Equal(0, hit % (1 << CombatConfig.HeadshotDamageShift));   // 移位后的位级痕迹
            Assert.Equal(hit, DamageAmount(world));
        }

        [Fact]
        public void 死亡射手不开火_零事件零命令()
        {
            var (world, shooter, target, shooterSlot, _) = SpawnPair(shooterY: 0f, targetY: 0f);
            world.Entities[shooterSlot].Hp = 0;            // 死亡事实（Hp≤0）

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));

            Assert.Equal(0, world.Events.Count);
            Assert.Equal(0, world.Cmds.Count);
        }

        [Fact]
        public void 死亡目标不可命中_无命中无伤害()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f, targetHp: 0);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));

            Assert.Equal(1, world.Events.Count);           // 只有 Fire（无 Hit）
            Assert.Equal(FrameEventKind.Fire, world.Events.Items[0].Kind);
            Assert.Equal(-1, DamageAmount(world));
        }

        [Fact]
        public void 死亡实体输入作废_移动朝向开火窗标志位全不写()
        {
            var world = new SimWorldState { RngState = 1UL };
            long id = world.Spawn(new EntitySlot { Hp = 0, Pos = new SimVector3(0f, 0f, 0f) }, out int slot);

            InputSystem.Run(world, new[] { new SimInputFrame
            {
                EntityId = id, MoveX = 1f, AimX = 1f, AimZ = 0f,
                Buttons = SimInputFrame.ButtonAim | SimInputFrame.ButtonFire,
            } });

            Assert.Equal(0f, world.Entities[slot].Vel.X, 4);
            Assert.Equal(0, world.Entities[slot].FireStanceFrames);
            Assert.Equal(0u, world.Entities[slot].Flags & EntityFlags.Aiming);
        }
    }
}
