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

        /// 命中事件的伤害值（取第一个指定 Kind 的事件；未找到 = -1）。
        /// <paramref name="kind"/> 默认取<see cref="FrameEventKind.Hit"/>——**爆头事件是
        /// <see cref="FrameEventKind.Crit"/>**（三维化裁决，《俯视角三维命中与爆头判定专项设计》§4.2），
        /// 故爆头用例须显式传 Crit 取值，否则读不到（-1）。
        private static int EventValue(SimWorldState world, FrameEventKind kind = FrameEventKind.Hit)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind)
                    return world.Events.Items[i].Value;
            return -1;
        }

        /// <summary>命中事件的伤害值（默认口径：任一命中类事件——Hit 或 Crit，兼容旧断言）。</summary>
        private static int HitValue(SimWorldState world)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                FrameEventKind k = world.Events.Items[i].Kind;
                if (k == FrameEventKind.Hit || k == FrameEventKind.Crit)
                    return world.Events.Items[i].Value;
            }
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
            // （边界口径与 SimRaycast 的 Y 带闸一致：闭区间含端点——眼高恰等于身位顶仍算命中）
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 1f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));

            int hit = HitValue(world);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(hit, lower, upper);
            Assert.Equal(0, hit % (1 << CombatConfig.HeadshotDamageShift));   // 移位后的位级痕迹
            Assert.Equal(hit, DamageAmount(world));
            // **爆头事件必须是 Crit**（三维化裁决：爆头发 Crit、普通发 Hit——表现层靠它分档飘红字）
            Assert.True(HasEvent(world, FrameEventKind.Crit), "爆头应产出 Crit 事件（普通命中才是 Hit）");
            Assert.False(HasEvent(world, FrameEventKind.Hit), "爆头不应同时产出普通 Hit 事件");
        }

        /// <summary>是否产出指定 Kind 的事件。</summary>
        private static bool HasEvent(SimWorldState world, FrameEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == kind) return true;
            return false;
        }

        /// <summary>
        /// **平地爆头**（本批核心目标，《俯视角三维命中与爆头判定专项设计》§4.2）：
        /// 三维化前平地打不出爆头（射线恒水平、眼高 1.0m &lt; 爆头线 1.7m）；三维化后
        /// 玩家抬高准心 ⇒ 射线带上仰角 ⇒ 命中点抬进头部带。
        /// </summary>
        [Fact]
        public void 平地远距_射线带上仰角_命中头部带爆头()
        {
            // 射手平地（Y=0，眼高 1.0）；目标在 X=10。给 dy>0 的瞄准向量⇒命中点随距离抬高。
            // 取 tan(dy/dx) 使 X=10 处命中高度 ≈ 1.8m（落在头部带 [1.7, 2.0)）。
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 0f) }, out _);

            // 单位化方向：水平 +X，dy/dx = 0.08 ⇒ X=10 处 y = 1.0 + 0.08*10 = 1.8
            float invLen = 1f / (float)System.Math.Sqrt(1.0 + 0.08 * 0.08);
            var frame = new SimInputFrame
            {
                EntityId = shooter,
                AimX = invLen,
                AimZ = 0f,
                AimY = (float)(0.08 * invLen),
                Buttons = SimInputFrame.ButtonFire,
            };

            ShootingSystem.Run(world, NoObstacles, new[] { frame });

            Assert.True(HasEvent(world, FrameEventKind.Crit),
                $"平地远距 + 仰角应爆头（事件={DescribeKinds(world)}）");
            int crit = EventValue(world, FrameEventKind.Crit);
            int lower = (CombatConfig.BaseDamage - CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            int upper = (CombatConfig.BaseDamage + CombatConfig.DamageSpread) << CombatConfig.HeadshotDamageShift;
            Assert.InRange(crit, lower, upper);
        }

        /// <summary>
        /// 平地**不抬枪**（dy=0）⇒ 命中点恒为眼高 1.0m，不进头部带⇒ **不爆头**
        /// （对照组：证明爆头来自仰角而非"三维化让所有命中都变爆头"）。
        /// </summary>
        [Fact]
        public void 平地近距_无仰角_不进头部带_普通命中()
        {
            var (world, shooter, target, _, _) = SpawnPair(shooterY: 0f, targetY: 0f);

            ShootingSystem.Run(world, NoObstacles, FireAt(shooter));   // FireAt 的 AimY 恒0（水平）

            Assert.False(HasEvent(world, FrameEventKind.Crit),
                $"平地水平射线不应爆头（事件={DescribeKinds(world)}）");
            int hit = EventValue(world, FrameEventKind.Hit);
            Assert.InRange(hit, CombatConfig.BaseDamage - CombatConfig.DamageSpread,
                CombatConfig.BaseDamage + CombatConfig.DamageSpread);
        }

        /// <summary>事件 Kind 列表（失败消息辅助——让"为什么没爆头"一眼可读）。</summary>
        private static string DescribeKinds(SimWorldState world)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(world.Events.Items[i].Kind);
            }
            return sb.ToString();
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
