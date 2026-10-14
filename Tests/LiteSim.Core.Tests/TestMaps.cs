using LiteSim;

namespace LiteSim.Tests
{
    /// <summary>
    /// 测试地图夹具（测试侧单源）：灰盒值域与既有 `StandardBattleMap` **逐值一致**
    /// （±100 边界 / 围墙 ±69 厚 2 高 20 / 测试方块 (5,0,0) / 16 网格出生点）——
    /// 供 L1 用例在该方法退役后继续以同一地图驱动断言；构建走 <see cref="SimMapBuilder"/>（越界/NaN fail-fast）。
    /// LiteNet.Tests 经 csproj 源链接共编本文件（同一份夹具）。
    /// </summary>
    internal static class TestMaps
    {
        public static SimMapData Greybox()
        {
            var obstacles = new SimObstacle[5];
            obstacles[0] = Box(0f, -69f, 70f, 1f, 20f);
            obstacles[1] = Box(0f, 69f, 70f, 1f, 20f);
            obstacles[2] = Box(-69f, 0f, 1f, 70f, 20f);
            obstacles[3] = Box(69f, 0f, 1f, 70f, 20f);
            obstacles[4] = Box(5f, 0f, 0.5f, 0.5f, 1f);

            var spawns = new SimVector3[SimMapData.MaxSpawnPoints];
            for (int i = 0; i < spawns.Length; i++)
                spawns[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);

            return SimMapBuilder.Build(0f, 100f, 100f, obstacles, obstacles.Length, spawns, spawns.Length);
        }

        private static SimObstacle Box(float x, float z, float halfX, float halfZ, float height)
            => new SimObstacle
            {
                Kind = SimObstacleKind.Box,
                Center = new SimVector3(x, 0f, z),
                HalfX = halfX,
                HalfZ = halfZ,
                Height = height,
            };
    }
}
