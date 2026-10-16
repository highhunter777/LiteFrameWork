using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 地图夹具自检（<see cref="TestMaps"/>）：首图 Arena01 与训练场快照的合法性（数量/边界/出生点不落障碍内）
    /// ＋摘要口径锚（== 对应 `Luban/Data/Map/*.json` 实烘产物 content_digest——双路对账：夹具按布局表独立
    /// 手推的值与烘焙器采样场景的值必须同 digest；改场景重烘后须联动本锚）。
    /// </summary>
    public sealed class TestMapsTests
    {
        [Fact]
        public void 首图快照_数量与边界()
        {
            SimMapData map = TestMaps.Arena01();
            Assert.Equal(52, map.ObstacleCount);
            Assert.Equal(16, map.SpawnPointCount);
            Assert.Equal(0f, map.GroundY);
            Assert.Equal(100f, map.HalfWidth);
            Assert.Equal(100f, map.HalfDepth);
        }

        [Fact]
        public void 首图快照_出生点不落障碍内()
        {
            SimMapData map = TestMaps.Arena01();
            for (int s = 0; s < map.SpawnPointCount; s++)
            {
                SimVector3 p = map.SpawnPoints[s];
                for (int o = 0; o < map.ObstacleCount; o++)
                {
                    ref SimObstacle ob = ref map.Obstacles[o];
                    float dx = System.MathF.Abs(p.X - ob.Center.X);
                    float dz = System.MathF.Abs(p.Z - ob.Center.Z);
                    bool inside = ob.Kind == SimObstacleKind.Circle   // lint-allow R3（整型等值，非浮点精度比较）
                        ? dx * dx + dz * dz < ob.Radius * ob.Radius
                        : dx < ob.HalfX && dz < ob.HalfZ;
                    Assert.False(inside, $"spawn[{s}] 落在障碍[{o}]（{ob.Kind} @ {ob.Center.X},{ob.Center.Z}）内");
                }
            }
        }

        [Fact]
        public void 首图快照_摘要口径锚()
        {
            // == Luban/Data/map.json 行内 content_digest（Arena01 实烘产物）——批3 独立双路对账锚；
            // 改场景重烘 + gen.bat 后同步本常量与 TestMaps.Arena01。
            Assert.Equal("9c3d504907d8210e", SimMapDigest.Compute(TestMaps.Arena01()));
        }

        [Fact]
        public void 训练场快照_数量与摘要口径锚()
        {
            SimMapData map = TestMaps.TrainingGround();
            Assert.Equal(8, map.ObstacleCount);
            Assert.Equal(16, map.SpawnPointCount);
            Assert.Equal(50f, map.HalfWidth);
            Assert.Equal(50f, map.HalfDepth);
            // == Luban/Data/Map/map_training_ground.json 行内 content_digest（多图批实烘产物）——
            // 改训练场布点（改场景/重跑布点工具）重烘 + gen.bat 后同步本常量与 TestMaps.TrainingGround。
            Assert.Equal("c78441a74f6bc5f9", SimMapDigest.Compute(map));
        }

        [Fact]
        public void 训练场快照_出生点不落障碍内且不越界()
        {
            SimMapData map = TestMaps.TrainingGround();
            for (int s = 0; s < map.SpawnPointCount; s++)
            {
                SimVector3 p = map.SpawnPoints[s];
                Assert.True(System.MathF.Abs(p.X) <= map.HalfWidth && System.MathF.Abs(p.Z) <= map.HalfDepth,
                    $"spawn[{s}] 越界（{p.X},{p.Z}）");
                for (int o = 0; o < map.ObstacleCount; o++)
                {
                    ref SimObstacle ob = ref map.Obstacles[o];
                    float dx = System.MathF.Abs(p.X - ob.Center.X);
                    float dz = System.MathF.Abs(p.Z - ob.Center.Z);
                    bool inside = ob.Kind == SimObstacleKind.Circle   // lint-allow R3（整型等值，非浮点精度比较）
                        ? dx * dx + dz * dz < ob.Radius * ob.Radius
                        : dx < ob.HalfX && dz < ob.HalfZ;
                    Assert.False(inside, $"spawn[{s}] 落在障碍[{o}]（{ob.Kind} @ {ob.Center.X},{ob.Center.Z}）内");
                }
            }
        }
    }
}
