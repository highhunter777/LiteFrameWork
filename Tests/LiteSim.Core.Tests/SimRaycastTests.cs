using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 射线判定单源用例（<see cref="SimRaycast"/>——射击障碍遮挡与瞄准激光共用的几何件）：
    /// 盒/圆求交、y 区间闸（含端点）、起点在形状内、身后、平行轴、取最近、射程外出局。
    /// 纯几何、零引擎；结果 t 与原 ShootingSystem 内联式逐位同源（实体侧）。
    /// </summary>
    public sealed class SimRaycastTests
    {
        /// <summary>空世界（实体求交用例大多不需要实体——只验几何）。</summary>
        private static SimWorldState EmptyWorld() => new SimWorldState { RngState = 1UL };

        private static SimMapData MapOf(params SimObstacle[] obstacles)
        {
            var map = new SimMapData();
            for (int i = 0; i < obstacles.Length; i++) map.Obstacles[i] = obstacles[i];
            map.ObstacleCount = obstacles.Length;
            return map;
        }

        // ---- 盒（slab）----

        [Fact]
        public void 盒_正面命中_t为近面距离()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 0f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });

            bool hit = SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t);

            Assert.True(hit);
            Assert.Equal(9f, t, 5);                       // 近面 x=9
        }

        [Fact]
        public void 盒_平行轴且区间外_不相交()
        {
            // 射线沿 +X（dz=0），盒在 z ∈ [4,6]——原点 z=0 在区间外 → 永不进入
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 5f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });

            Assert.False(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out _));
        }

        [Fact]
        public void 盒_平行轴且区间内_仍可命中()
        {
            // 射线沿 +X（dz=0），盒横跨 z=0（半深 5）→ 命中近面
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 0f),
                HalfX = 1f, HalfZ = 5f, Height = 20f,
            });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(9f, t, 5);
        }

        [Fact]
        public void 盒_斜向命中与正交同距()
        {
            // 射线沿对角线（√2/2, √2/2）打 (10,10) 的单位盒——t 单位 = 方向向量长度 ⇒ 近面距离 = 9/单位分量
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 10f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });
            float d = 0.70710678118f;

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, d, d, 100f, out float t));
            Assert.Equal(9f / d, t, 3);                   // t 以方向向量长度计（≈13 米路程）
        }

        [Fact]
        public void 盒_起点在盒内_t为零()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(0f, t, 5);
        }

        [Fact]
        public void 盒_整体在身后_不相交()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(-10f, 0f, 0f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });

            Assert.False(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out _));
        }

        [Fact]
        public void 盒_射程外_出局()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(200f, 0f, 0f),
                HalfX = 1f, HalfZ = 1f, Height = 20f,
            });

            Assert.False(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(100f, t, 5);                     // 未命中时 t = maxT（调用方拿它当上界用）
        }

        [Fact]
        public void 盒_y带闸_眼高在带外不相交()
        {
            // 高 1 的矮盒：眼高 1.0 在带内（含端点）；眼高 1.5 在带外 → 不挡弹
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Box, Center = new SimVector3(10f, 0f, 0f),
                HalfX = 1f, HalfZ = 1f, Height = 1f,
            });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1.0f, 0f, 1f, 0f, 100f, out _));
            Assert.False(SimRaycast.RaycastObstacles(map, 0f, 1.5f, 0f, 1f, 0f, 100f, out _));
        }

        // ---- 圆（与实体圆柱同式）----

        [Fact]
        public void 圆_正对命中_t为近弧距离()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Circle, Center = new SimVector3(10f, 0f, 0f),
                Radius = 1f, Height = 20f,
            });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(9f, t, 5);
        }

        [Fact]
        public void 圆_垂距超半径_不相交()
        {
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Circle, Center = new SimVector3(10f, 0f, 3f),
                Radius = 1f, Height = 20f,
            });

            Assert.False(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out _));
        }

        [Fact]
        public void 圆_切线恰命中_与原内联式同界()
        {
            // 垂距恰等于半径：c2 == r2（`c2 > r2` 才出局——与原实体判定同界，切线算命中）
            var map = MapOf(new SimObstacle
            {
                Kind = SimObstacleKind.Circle, Center = new SimVector3(10f, 0f, 1f),
                Radius = 1f, Height = 20f,
            });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(10f, t, 5);                      // 切点即圆心正前方（b=10）
        }

        // ---- 取最近 ----

        [Fact]
        public void 多障碍_取最近()
        {
            var map = MapOf(
                new SimObstacle
                {
                    Kind = SimObstacleKind.Box, Center = new SimVector3(30f, 0f, 0f),
                    HalfX = 1f, HalfZ = 1f, Height = 20f,
                },
                new SimObstacle
                {
                    Kind = SimObstacleKind.Circle, Center = new SimVector3(12f, 0f, 0f),
                    Radius = 1f, Height = 20f,
                });

            Assert.True(SimRaycast.RaycastObstacles(map, 0f, 1f, 0f, 1f, 0f, 100f, out float t));
            Assert.Equal(11f, t, 5);                      // 圆（11）近于盒（29）
        }

        // ---- 实体（与原内联式逐位同源）----

        [Fact]
        public void 实体_圆柱命中_跳过自身与尸体()
        {
            var world = EmptyWorld();
            long shooter = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int ss);
            long target = world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 0f) }, out int ts);
            long dead = world.Spawn(new EntitySlot { Hp = 0, Pos = new SimVector3(5f, 0f, 0f) }, out int ds);
            _ = dead;

            bool hit = SimRaycast.RaycastEntities(world, ss, 0f, 1f, 0f, 1f, 0f, 100f, out int hitSlot, out float t);

            Assert.True(hit);
            Assert.Equal(ts, hitSlot);                    // 尸体不挡（Hp≤0 跳过），活体 10m 命中
            Assert.Equal(10f - CombatConfig.HitscanRadius, t, 4);   // 近弧 = 圆心距 − 半径
        }

        [Fact]
        public void 实体_并列距离_取低槽位()
        {
            // 两目标同距且都恰压圆柱边缘（垂距 = 半径，切线命中）——原内联式"遍历升序 + 严格小于" ⇒ 低槽位胜
            var world = EmptyWorld();
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int ss);
            float R = CombatConfig.HitscanRadius;
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, R) }, out int a);
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, -R) }, out int b);
            _ = b;

            bool hit = SimRaycast.RaycastEntities(world, ss, 0f, 1f, 0f, 1f, 0f, 100f, out int hitSlot, out float t);

            Assert.True(hit);
            Assert.Equal(a, hitSlot);
            Assert.Equal(10f, t, 4);                      // 边缘相切：t = b（前向投影），恰在圆柱面上
        }

        [Fact]
        public void 实体_未命中_t回传射程上界()
        {
            var world = EmptyWorld();
            world.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f) }, out int ss);

            Assert.False(SimRaycast.RaycastEntities(world, ss, 0f, 1f, 0f, 1f, 0f, 100f, out int hitSlot, out float t));
            Assert.Equal(-1, hitSlot);
            Assert.Equal(100f, t, 5);
        }
    }
}
