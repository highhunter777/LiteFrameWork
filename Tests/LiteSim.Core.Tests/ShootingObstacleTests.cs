using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 权威射击 × 静态障碍遮挡（子弹不穿墙——判定单源 <see cref="SimRaycast"/>）：
    /// 墙后目标不可命中（无 Damage/无 Hit，只写 Fire）、矮盒恰在弹道高度带内挡弹、
    /// 目标近于障碍仍正常命中、被截停不消耗 RngState、开火驻留窗照常置满。
    /// </summary>
    public sealed class ShootingObstacleTests
    {
        private static SimMapData WallBetween(float wallX, float height = 20f, float halfZ = 20f)
        {
            // 射手 (0,0,0) → 目标 (10,0,0) 之间的横墙（覆盖 z 向通道）
            var map = new SimMapData();
            map.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(wallX, 0f, 0f),
                HalfX = 0.5f, HalfZ = halfZ, Height = height,
            };
            map.ObstacleCount = 1;
            return map;
        }

        private static (SimWorldState world, long shooter, long target) Pair()
        {
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out _);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 0f) }, out _);
            return (world, shooter, target);
        }

        /// <summary>以**瞄准点**构造开火输入（AimPoint 单口径）。
        /// 缺省点 (10,1,0) = 目标 (10,0,0) 正前方身位中部——弹道自烘焙枪口（高 ≈1.29）微降步进。</summary>
        private static SimInputFrame[] FireAt(long id, float px = 10f, float pz = 0f)
            => new[] { new SimInputFrame { EntityId = id, AimPointX = px, AimPointY = 1f, AimPointZ = pz, Buttons = SimInputFrame.ButtonFire } };

        private static bool HasDamage(SimWorldState world)
        {
            for (int i = 0; i < world.Cmds.Count; i++)
                if (world.Cmds.Items[i].Kind == SimCommandKind.Damage) return true;
            return false;
        }

        private static bool HasHitEvent(SimWorldState world)
        {
            for (int i = 0; i < world.Events.Count; i++)
                if (world.Events.Items[i].Kind == FrameEventKind.Hit) return true;
            return false;
        }

        [Fact]
        public void 墙后目标_子弹被截停_只写Fire不写Damage与Hit()
        {
            var (world, shooter, target) = Pair();
            var map = WallBetween(wallX: 5f);
            ulong rngBefore = world.RngState;

            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.Equal(1, world.Events.Count);                    // 只有 Fire
            Assert.Equal(FrameEventKind.Fire, world.Events.Items[0].Kind);
            Assert.False(HasDamage(world), "墙后目标不可命中——无伤害命令");
            Assert.False(HasHitEvent(world), "无命中事件");
            Assert.Equal(rngBefore, world.RngState);                // 截停不进伤害分支——不消耗随机数
            Assert.Equal(100, world.Entities[1].Hp);                // 目标不掉血
            _ = target;
        }

        [Fact]
        public void 墙前截停_开火驻留窗照常置满()
        {
            var (world, shooter, _) = Pair();
            var map = WallBetween(wallX: 5f);

            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.Equal((byte)CombatConfig.FireStanceFrames, world.Entities[0].FireStanceFrames);
        }

        [Fact]
        public void 目标近于墙_正常命中_伤害与事件齐全()
        {
            var (world, shooter, target) = Pair();
            var map = WallBetween(wallX: 12f);                       // 墙近面 11.5 在目标命中弧（9.5）之后——墙挡的是"更远处"
            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.True(HasDamage(world), "目标近于障碍——正常命中（障碍不改变墙前判定）");
            Assert.True(HasHitEvent(world));
            Assert.Equal(2, world.Events.Count);                    // Fire + Hit
            _ = target;
        }

        [Fact]
        public void 矮盒恰在弹道高度带内_挡弹()
        {
            // 训练场测试方块同源口径：盒底 0、盒顶 = 弹道原点高（含端点）⇒ 挡弹。
            // 盒高取烘焙枪口高 ⇒ 本用例随枪口常量自动跟随（"恰在带内"的端点语义锁定）。
            var (world, shooter, target) = Pair();
            var map = WallBetween(wallX: 5f, height: CombatConfig.MuzzleOffsetHeight, halfZ: 0.5f);

            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.False(HasDamage(world), "弹道原点高在盒带内（含端点）——矮盒也挡弹");
            _ = target;
        }

        [Fact]
        public void 眼高越过矮障碍_不挡弹()
        {
            // 眼高 1.0 > 盒顶 0.5 → 越过（y 区间闸含端点同口径）
            var (world, shooter, target) = Pair();
            var map = WallBetween(wallX: 5f, height: 0.5f, halfZ: 0.5f);

            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.True(HasDamage(world), "眼高在障碍带外——子弹越过");
            _ = target;
        }

        [Fact]
        public void 射线偏离障碍_侧向通道照常命中()
        {
            // 墙只盖 z ∈ [-2,2]，目标在 z=5 通道外——斜向射线不穿墙但也不该被挡。
            // 射线方向从**逻辑枪口**（本体 (0,0,0)、Yaw=0 ⇒ 枪口 (0.35,-0.2)）指向目标归一化——
            // 墙挪到 x=8（近面 7.5）：射线在 z 出带（t≈5.35）时 x≈4.4，未及墙带即已穿出侧通道。
            var (world, shooter, _) = Pair();
            var map = new SimMapData();
            map.Obstacles[0] = new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(8f, 0f, 0f),
                HalfX = 0.5f, HalfZ = 2f, Height = 20f,
            };
            map.ObstacleCount = 1;
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 5f) }, out _);

            // 瞄准点取目标 (10,5) 正前方眼高位——从逻辑枪口 (0.35,-0.2) 指向该点的方向与旧方向口径逐位一致
            ShootingSystem.Run(world, map, FireAt(shooter, 10f, 5f));

            Assert.True(HasDamage(world), "斜向射线走通道外——不受墙影响");
            _ = target;
        }

        [Fact]
        public void 逻辑枪口_右向偏移平移命中带_原点随朝向系()
        {
            // 射手 Yaw=0（朝 +X，右手侧 = −Z）：逻辑枪口右偏 r ⇒ 射线向 −Z 平移 r——
            // 目标位按"中心出射命中带 |z|<R"构造（余量 2cm）：
            //   oldBand z=+(R−0.01)（中心出射命中[余量 1cm]；右偏后垂距 R−0.01+r > R ⇒ 漏）
            //   newBand z=−(r+R−0.02)（中心出射垂距 r+R−0.02 > R 漏；右偏后垂距 R−0.02 ⇒ 擦中）
            float r = CombatConfig.MuzzleOffsetRight;
            float R = CombatConfig.HitscanRadius;
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out _);
            long oldBand = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, R - 0.01f) }, out _);
            long newBand = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, -(r + R - 0.02f)) }, out _);

            // 瞄准点取"枪口 Z 延长线"（z = −MuzzleOffsetRight）⇒ 弹道严格沿 +X 平移过枪口
            ShootingSystem.Run(world, new SimMapData(), FireAt(shooter, 10f, -CombatConfig.MuzzleOffsetRight));

            Assert.True(HasDamage(world), "命中带随枪口右偏平移——新带（z=−(r+R−0.02)）擦中");
            Assert.Equal(newBand, DamageTarget(world));
            Assert.NotEqual(oldBand, DamageTarget(world));        // 旧带（z=+(R−0.01)）被右偏推出命中带
        }

        /// <summary>命中命令的目标实体 Id（无 = -1）。</summary>
        private static long DamageTarget(SimWorldState world)
        {
            for (int i = 0; i < world.Cmds.Count; i++)
                if (world.Cmds.Items[i].Kind == SimCommandKind.Damage) return world.Cmds.Items[i].Target;
            return -1;
        }

        [Fact]
        public void 逻辑枪口_朝向翻转偏移镜像_偏移系随身体转()
        {
            // Yaw=π（朝 −X）：右向 = (sin π, −cos π) = +Z ⇒ 枪口 z = +r（镜像而非世界固定）。
            // 打 −X：z=−(R−0.01)（中心出射命中[余量 1cm]；镜像右偏后垂距 R−0.01+r > R ⇒ 漏）；
            // z=+(r+R−0.02)（中心出射漏；镜像右偏后垂距 R−0.02 ⇒ 擦中）。
            float r = CombatConfig.MuzzleOffsetRight;
            float R = CombatConfig.HitscanRadius;
            var world = new SimWorldState { RngState = 1UL };
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 3.14159274f }, out _);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-10f, 0f, -(R - 0.01f)) }, out _);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(-10f, 0f, r + R - 0.02f) }, out _);

            // 瞄准点取 −X 方向、z = +MuzzleOffsetRight（Yaw=π 时枪口 z）⇒ 弹道严格沿 −X 平移过镜像枪口
            ShootingSystem.Run(world, new SimMapData(), FireAt(shooter, -10f, CombatConfig.MuzzleOffsetRight));

            Assert.True(HasDamage(world), "朝向翻转后偏移镜像——z=+(r+R−0.02) 侧目标擦中（偏移随身体转）");
        }

        [Fact]
        public void 空图回归_无障碍时命中行为不变()
        {
            var (world, shooter, target) = Pair();
            var map = new SimMapData();                             // ObstacleCount = 0

            ShootingSystem.Run(world, map, FireAt(shooter));

            Assert.True(HasDamage(world), "空图——既有命中行为不变");
            Assert.Equal(2, world.Events.Count);
            _ = target;
        }
    }
}
