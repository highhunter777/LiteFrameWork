using LiteSim;

namespace LiteSim.Tests
{
    /// <summary>
    /// 测试地图夹具（测试侧单源）：灰盒值域与旧 `StandardBattleMap` **逐值一致**（该方法已随批4 退役；
    /// 本夹具承接其测试场景角色）
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

        /// <summary>
        /// 首图 Arena01 快照夹具（批3 冻结：与 `Luban/Data/map.json` 实烘产物逐值一致）：52 件代理
        /// （四点旋转对称布局，注释名＝场景对象名；条目序＝烘焙输出序）+ 16 出生点 + 边界 ±100。
        /// 批4 两端投影对账（tbmap.bytes 投影 == 本快照）用之；锚定摘要见 TestMapsTests。
        /// </summary>
        public static SimMapData Arena01()
        {
            var obstacles = new SimObstacle[52];
            obstacles[0] = Box(-1.5f, 41f, 1.0f, 1.0f, 1f);   // axis0_box_1
            obstacles[1] = Box(1.5f, 44f, 1.0f, 1.0f, 1f);   // axis0_box_2
            obstacles[2] = Box(41f, 1.5f, 1.0f, 1.0f, 1f);   // axis1_box_1
            obstacles[3] = Box(44f, -1.5f, 1.0f, 1.0f, 1f);   // axis1_box_2
            obstacles[4] = Box(1.5f, -41f, 1.0f, 1.0f, 1f);   // axis2_box_1
            obstacles[5] = Box(-1.5f, -44f, 1.0f, 1.0f, 1f);   // axis2_box_2
            obstacles[6] = Box(-41f, -1.5f, 1.0f, 1.0f, 1f);   // axis3_box_1
            obstacles[7] = Box(-44f, 1.5f, 1.0f, 1.0f, 1f);   // axis3_box_2
            obstacles[8] = Box(-5f, -5f, 1.0f, 1.0f, 1f);   // mid_box_1
            obstacles[9] = Box(5f, -5f, 1.0f, 1.0f, 1f);   // mid_box_2
            obstacles[10] = Box(-5f, 5f, 1.0f, 1.0f, 1f);   // mid_box_3
            obstacles[11] = Box(5f, 5f, 1.0f, 1.0f, 1f);   // mid_box_4
            obstacles[12] = Box(7f, -4.5f, 0.3f, 2.5f, 2.5f);   // mid_wall_e1
            obstacles[13] = Box(7f, 4.5f, 0.3f, 2.5f, 2.5f);   // mid_wall_e2
            obstacles[14] = Box(-4.5f, 7f, 2.5f, 0.3f, 2.5f);   // mid_wall_n1
            obstacles[15] = Box(4.5f, 7f, 2.5f, 0.3f, 2.5f);   // mid_wall_n2
            obstacles[16] = Box(-4.5f, -7f, 2.5f, 0.3f, 2.5f);   // mid_wall_s1
            obstacles[17] = Box(4.5f, -7f, 2.5f, 0.3f, 2.5f);   // mid_wall_s2
            obstacles[18] = Box(-7f, -4.5f, 0.3f, 2.5f, 2.5f);   // mid_wall_w1
            obstacles[19] = Box(-7f, 4.5f, 0.3f, 2.5f, 2.5f);   // mid_wall_w2
            obstacles[20] = Box(55f, 25f, 1.0f, 1.0f, 1f);   // outer0_box
            obstacles[21] = Box(25f, -55f, 1.0f, 1.0f, 1f);   // outer1_box
            obstacles[22] = Box(-55f, -25f, 1.0f, 1.0f, 1f);   // outer2_box
            obstacles[23] = Box(-25f, 55f, 1.0f, 1.0f, 1f);   // outer3_box
            obstacles[24] = Box(25f, 55f, 1.0f, 1.0f, 1f);   // outer4_box
            obstacles[25] = Box(55f, -25f, 1.0f, 1.0f, 1f);   // outer5_box
            obstacles[26] = Box(-25f, -55f, 1.0f, 1.0f, 1f);   // outer6_box
            obstacles[27] = Box(-55f, 25f, 1.0f, 1.0f, 1f);   // outer7_box
            obstacles[28] = Box(31f, 31f, 1.0f, 1.0f, 1f);   // q0_box_1
            obstacles[29] = Box(36f, 35f, 1.0f, 1.0f, 1f);   // q0_box_2
            obstacles[30] = Box(33f, 38.5f, 0.5f, 1.0f, 1f);   // q0_box_3
            obstacles[31] = Box(31f, 37f, 3.0f, 0.3f, 2.5f);   // q0_wall_1
            obstacles[32] = Box(37f, 33f, 0.3f, 2.0f, 2.5f);   // q0_wall_2
            obstacles[33] = Box(31f, -31f, 1.0f, 1.0f, 1f);   // q1_box_1
            obstacles[34] = Box(35f, -36f, 1.0f, 1.0f, 1f);   // q1_box_2
            obstacles[35] = Box(38.5f, -33f, 1.0f, 0.5f, 1f);   // q1_box_3
            obstacles[36] = Box(37f, -31f, 0.3f, 3.0f, 2.5f);   // q1_wall_1
            obstacles[37] = Box(33f, -37f, 2.0f, 0.3f, 2.5f);   // q1_wall_2
            obstacles[38] = Box(-31f, -31f, 1.0f, 1.0f, 1f);   // q2_box_1
            obstacles[39] = Box(-36f, -35f, 1.0f, 1.0f, 1f);   // q2_box_2
            obstacles[40] = Box(-33f, -38.5f, 0.5f, 1.0f, 1f);   // q2_box_3
            obstacles[41] = Box(-31f, -37f, 3.0f, 0.3f, 2.5f);   // q2_wall_1
            obstacles[42] = Box(-37f, -33f, 0.3f, 2.0f, 2.5f);   // q2_wall_2
            obstacles[43] = Box(-31f, 31f, 1.0f, 1.0f, 1f);   // q3_box_1
            obstacles[44] = Box(-35f, 36f, 1.0f, 1.0f, 1f);   // q3_box_2
            obstacles[45] = Box(-38.5f, 33f, 1.0f, 0.5f, 1f);   // q3_box_3
            obstacles[46] = Box(-37f, 31f, 0.3f, 3.0f, 2.5f);   // q3_wall_1
            obstacles[47] = Box(-33f, 37f, 2.0f, 0.3f, 2.5f);   // q3_wall_2
            obstacles[48] = Box(69f, 0f, 1.0f, 70.0f, 20f);   // wall_bound_e
            obstacles[49] = Box(0f, -69f, 70.0f, 1.0f, 20f);   // wall_bound_n
            obstacles[50] = Box(0f, 69f, 70.0f, 1.0f, 20f);   // wall_bound_s
            obstacles[51] = Box(-69f, 0f, 1.0f, 70.0f, 20f);   // wall_bound_w

            var spawns = new SimVector3[16]
            {
                new SimVector3(-60f, 0f, -60f), new SimVector3(60f, 0f, 60f), new SimVector3(60f, 0f, -60f), new SimVector3(-60f, 0f, 60f),   // spawn_00..03 = 四角
                new SimVector3(-60f, 0f, 0f), new SimVector3(60f, 0f, 0f), new SimVector3(0f, 0f, -60f), new SimVector3(0f, 0f, 60f),       // spawn_04..07 = 四边中
                new SimVector3(-51f, 0f, -51f), new SimVector3(51f, 0f, 51f), new SimVector3(51f, 0f, -51f), new SimVector3(-51f, 0f, 51f), // spawn_08..11 = 备份圈角
                new SimVector3(-51f, 0f, 0f), new SimVector3(51f, 0f, 0f), new SimVector3(0f, 0f, -51f), new SimVector3(0f, 0f, 51f),       // spawn_12..15 = 备份圈边中
            };

            return SimMapBuilder.Build(0f, 100f, 100f, obstacles, obstacles.Length, spawns, spawns.Length);
        }

        /// <summary>
        /// 训练场快照夹具（多图批冻结：与 `Luban/Data/Map/map_training_ground.json` 实烘产物逐值一致）：
        /// 8 件代理（种子布点；注释名＝场景对象名，条目序＝烘焙输出序=名 Ordinal）+ 16 出生点（半径 22 环）
        /// + 边界 ±50。多图批两端投影对账（tbmap 行 2 投影 == 本快照）用之；锚定摘要见 TestMapsTests。
        /// **改训练场布点（改场景重烘）必须联动更新本夹具**（同 Arena01 口径）。
        /// </summary>
        public static SimMapData TrainingGround()
        {
            var obstacles = new SimObstacle[8];
            obstacles[0] = Box(0f, 0f, 0.5f, 2.0f, 2f);      // block_00
            obstacles[1] = Box(-6f, -6f, 1.0f, 1.0f, 1f);    // cover_00
            obstacles[2] = Box(6f, -6f, 1.0f, 1.0f, 1f);     // cover_01
            obstacles[3] = Box(-6f, 6f, 1.0f, 1.0f, 1f);     // cover_02
            obstacles[4] = Box(6f, 6f, 1.0f, 1.0f, 1f);      // cover_03
            obstacles[5] = Box(5f, 0f, 0.5f, 0.5f, 1f);      // cover_04（场景原顶层 Cube）
            obstacles[6] = Box(-14f, 0f, 4.0f, 0.5f, 1f);    // wall_00
            obstacles[7] = Box(14f, 0f, 4.0f, 0.5f, 1f);     // wall_01

            var spawns = new SimVector3[16]
            {
                new SimVector3(22f, 0f, 0f), new SimVector3(20.325f, 0f, 8.419f),
                new SimVector3(15.556f, 0f, 15.556f), new SimVector3(8.419f, 0f, 20.325f),
                new SimVector3(0f, 0f, 22f), new SimVector3(-8.419f, 0f, 20.325f),
                new SimVector3(-15.556f, 0f, 15.556f), new SimVector3(-20.325f, 0f, 8.419f),
                new SimVector3(-22f, 0f, 0f), new SimVector3(-20.325f, 0f, -8.419f),
                new SimVector3(-15.556f, 0f, -15.556f), new SimVector3(-8.419f, 0f, -20.325f),
                new SimVector3(0f, 0f, -22f), new SimVector3(8.419f, 0f, -20.325f),
                new SimVector3(15.556f, 0f, -15.556f), new SimVector3(20.325f, 0f, -8.419f),
            };

            return SimMapBuilder.Build(0f, 50f, 50f, obstacles, obstacles.Length, spawns, spawns.Length);
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
