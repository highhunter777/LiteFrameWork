using System;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 静态障碍去穿插：
    /// 判定半 = SimObstacle（圆/盒 + y 区间闸）；身体圆柱复用命中身位（HitscanRadius/HitscanHeight）。
    /// 解析语义 = 去穿插（推出而非阻挡）——速度不衰减，贴墙滑行靠"积分+推出"自然涌现。
    /// 确定性：实体升序 × 障碍升序、SimMath 软件算术——用例含双跑逐位一致锚。
    /// </summary>
    public class MovementCollisionTests
    {
        /// <summary>建一人的世界（位置由调用方给），返回 (世界, 槽位)。</summary>
        private static (SimWorldState s, int slot) SpawnAt(float x, float z)
        {
            var s = new SimWorldState();
            long id = s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(x, 0f, z) }, out int slot);
            Assert.True(id > 0L);
            return (s, slot);
        }

        private static SimMapData EmptyMap(float half = 100f)
            => new SimMapData { GroundY = 0f, HalfWidth = half, HalfDepth = half };

        [Fact]
        public void 障碍_圆_从外压入被推回圆面()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Circle, Center = new SimVector3(0f, 0f, 0f), Radius = 2f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(2.2f, 0f);          // 已切入圆障碍（表面 2 + 身位 0.5）
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            float expectedX = 2.5f;                     // 推到 圆心 0 + 半径 2 + 身位 0.5
            Assert.Equal(expectedX, s.Entities[slot].Pos.X, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.Z, 5);
        }

        [Fact]
        public void 障碍_圆_圆心重合走确定性兜底沿X推出()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Circle, Center = new SimVector3(5f, 0f, 5f), Radius = 2f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(5f, 5f);             // 与障碍圆心重合
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            Assert.Equal(7.5f, s.Entities[slot].Pos.X, 5);   // 圆心 X + 半径 + 身位
            Assert.Equal(5f, s.Entities[slot].Pos.Z, 5);
        }

        [Fact]
        public void 障碍_盒_面外推到面外身位半径()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 3f, HalfZ = 1f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(3.2f, 0f);          // 切入 +X 面（面 3 + 身位 0.5 → 应停在 3.5）
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            Assert.Equal(3.5f, s.Entities[slot].Pos.X, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.Z, 5);
        }

        [Fact]
        public void 障碍_盒_中心在盒内走最浅轴X优先()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 3f, HalfZ = 3f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(1f, 0f);             // 盒内：X 穿透 2 < Z 穿透 3.5 → 沿 +X 推出
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            Assert.Equal(3.5f, s.Entities[slot].Pos.X, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.Z, 5);
        }

        [Fact]
        public void 障碍_盒_最浅轴在Z时沿Z推出()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 3f, HalfZ = 1f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(0f, 0.2f);          // 盒内：Z 穿透 0.8 < X 穿透 3.3 → 沿 +Z 推出
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            Assert.Equal(1.5f, s.Entities[slot].Pos.Z, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.X, 5);
        }

        [Fact]
        public void 障碍_y区间闸_悬空障碍不推地面实体()
        {
            var map = EmptyMap();
            // 悬空障碍：y 区间 [3, 8]，地面实体身体 [0, 2] —— 不相交 → 站在正下方也不推
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 3f, 0f), HalfX = 3f, HalfZ = 3f, Height = 5f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(0f, 0f);
            MovementSystem.Run(s.Entities, s.AliveBitmap, map);

            Assert.Equal(0f, s.Entities[slot].Pos.X, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.Z, 5);
            Assert.Equal(0f, s.Entities[slot].Pos.Y, 5);   // 落地，未从悬空障碍下方被推走
        }

        [Fact]
        public void 障碍_推离不衰减速度_下帧继续推入与切向积分并存()
        {
            var map = EmptyMap();
            map.Obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 3f, HalfZ = 3f, Height = 10f };
            map.ObstacleCount = 1;

            var (s, slot) = SpawnAt(4f, 0f);
            s.Entities[slot].Vel = new SimVector3(-CombatConfig.MoveSpeed, 0f, CombatConfig.MoveSpeed);   // 朝墙 + 沿墙（45° 走位）

            // 30 帧（2.5m 行程 > 0.5m 入墙行程）：X 被墙截在 3.5，Z 照常积分（贴墙滑行）
            for (int i = 0; i < 30; i++) MovementSystem.Run(s.Entities, s.AliveBitmap, map);
            Assert.Equal(3.5f, s.Entities[slot].Pos.X, 5);
            Assert.Equal(30f * CombatConfig.MoveSpeed * SimConfig.Dt, s.Entities[slot].Pos.Z, 5);
            Assert.Equal(-CombatConfig.MoveSpeed, s.Entities[slot].Vel.X);   // 速度不衰减

            // 再 30 帧（总 60，z≈5.0）：Z 越过盒沿（3）后实体**绕过墙角**脱离 +X 面约束、继续 −X 行进
            // ——拐角滑行是去穿插的涌现语义；Z 全程未被截（面法线推离只动 X）。
            for (int i = 0; i < 30; i++) MovementSystem.Run(s.Entities, s.AliveBitmap, map);
            Assert.True(s.Entities[slot].Pos.X < 3f, "绕过墙角后应脱离 +X 面约束（x=" + s.Entities[slot].Pos.X + "）");
            Assert.InRange(s.Entities[slot].Pos.Z, 4.8f, 5.2f);
        }

        [Fact]
        public void 标准地图_围墙_向四壁推进均停在墙内沿()
        {
            var map = SimMapData.StandardBattleMap();
            Assert.Equal(5, map.ObstacleCount);   // 4 段围墙 + 1 个测试方块（训练场 /Cube 同源）

            // 北墙（−Z 面 −68）：从 −55 向 −Z 持续走 600 帧（50m > 12.5m 行程）应停在 −68 + 身位 = −67.5
            var (s, slot) = SpawnAt(0f, -55f);
            for (int i = 0; i < 600; i++)
            {
                s.Entities[slot].Vel = new SimVector3(0f, 0f, -CombatConfig.MoveSpeed);
                MovementSystem.Run(s.Entities, s.AliveBitmap, map);
            }
            Assert.Equal(-67.5f, s.Entities[slot].Pos.Z, 5);

            // 东墙（+X 面 68）：从 55 向 +X 持续走应停在 67.5
            s.Entities[slot].Pos = new SimVector3(55f, 0f, 0f);
            for (int i = 0; i < 600; i++)
            {
                s.Entities[slot].Vel = new SimVector3(CombatConfig.MoveSpeed, 0f, 0f);
                MovementSystem.Run(s.Entities, s.AliveBitmap, map);
            }
            Assert.Equal(67.5f, s.Entities[slot].Pos.X, 5);
        }

        [Fact]
        public void 标准地图_出生网格不在任何障碍内()
        {
            var map = SimMapData.StandardBattleMap();
            float r = CombatConfig.HitscanRadius;
            for (int i = 0; i < map.SpawnPointCount; i++)
            {
                for (int o = 0; o < map.ObstacleCount; o++)
                {
                    SimObstacle ob = map.Obstacles[o];
                    Assert.False(
                        Math.Abs(map.SpawnPoints[i].X - ob.Center.X) < ob.HalfX + r
                        && Math.Abs(map.SpawnPoints[i].Z - ob.Center.Z) < ob.HalfZ + r,
                        $"出生点 {i} 落在障碍 {o} 内——出生必被推出，破坏落位直觉");
                }
            }
        }

        [Fact]
        public void 障碍_确定性_同输入双跑逐位一致()
        {
            var map = SimMapData.StandardBattleMap();

            uint a = RunTenFrames(map, seed: 123UL);
            uint b = RunTenFrames(map, seed: 123UL);
            Assert.Equal(a, b);

            static uint RunTenFrames(SimMapData map, ulong seed)
            {
                var s = new SimWorldState { RngState = seed };
                long id = s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(67f, 0f, -67f) }, out int slot);
                var inputs = new SimInputFrame[1];
                for (int f = 0; f < 10; f++)
                {
                    // 贴东北角：+X 压东墙、−Z 压北墙（两障碍依次解析 + 积分持续压入）
                    inputs[0] = new SimInputFrame { EntityId = id, MoveX = 1f, MoveZ = -1f, AimX = 1f, AimZ = 0f };
                    SimStep.Step(s, map, inputs);
                }
                // 帧内既推离墙又保持被推住——终态不得穿墙
                Assert.True(s.Entities[slot].Pos.X <= 67.5f);
                Assert.True(s.Entities[slot].Pos.Z >= -67.5f);
                return SimChecksum.ComputeChecksum(s);
            }
        }
    }
}
