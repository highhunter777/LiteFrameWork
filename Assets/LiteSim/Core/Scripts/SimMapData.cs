using System;

namespace LiteSim
{
    /// <summary>静态障碍类型（§3.5：XZ 平面圆形/AABB，不做斜面地形）。</summary>
    public enum SimObstacleKind : byte
    {
        Circle = 0,
        Box = 1,
    }

    /// <summary>
    /// 静态障碍（判定半，§18/决策 #17）：Circle = Center + Radius（y 区间 [Center.Y, Center.Y + Height]）；
    /// Box = Center + HalfX/HalfZ（同 y 区间）。M8 只冻结数据结构，碰撞分桶系统留后续里程碑空位（#12）。
    /// </summary>
    public struct SimObstacle
    {
        public SimObstacleKind Kind;

        /// <summary>中心（圆心/盒中心；y 为底部高度）。</summary>
        public SimVector3 Center;

        /// <summary>圆半径（Kind == Circle）。</summary>
        public float Radius;

        /// <summary>盒半宽（Kind == Box，X 轴）。</summary>
        public float HalfX;

        /// <summary>盒半深（Kind == Box，Z 轴）。</summary>
        public float HalfZ;

        /// <summary>高度（y 区间上界 = Center.Y + Height）。</summary>
        public float Height;
    }

    /// <summary>
    /// 判定用地图数据（M8 决策 #17：M8 定下并冻结；系统实现可留空）。
    /// 定长可拷（同 #5）；对局中只读；是常量配置——不进快照、不进 checksum（§3.6 只哈希逻辑状态）。
    /// 视觉地图与它无关（§18：视觉半属 View，M11 装配）。
    /// </summary>
    public sealed class SimMapData
    {
        public const int MaxObstacles = 64;
        public const int MaxSpawnPoints = 16;

        /// <summary>有效障碍数（Obstacles 前 N 项有效）。</summary>
        public int ObstacleCount;

        public readonly SimObstacle[] Obstacles = new SimObstacle[MaxObstacles];

        /// <summary>有效出生点数。</summary>
        public int SpawnPointCount;

        public readonly SimVector3[] SpawnPoints = new SimVector3[MaxSpawnPoints];

        /// <summary>地面高度（§3.5 地面钳制：y ≤ GroundY 时贴地、速度清零）。</summary>
        public float GroundY;

        /// <summary>世界边界 X 半宽（移动/出生钳制用）。</summary>
        public float HalfWidth;

        /// <summary>世界边界 Z 半深。</summary>
        public float HalfDepth;

        /// <summary>定长深拷（保持可拷约束；M9 之后若地图需入网/落盘，此方法即拷贝原语）。</summary>
        public void CopyTo(SimMapData dst)
        {
            dst.ObstacleCount = ObstacleCount;
            Array.Copy(Obstacles, dst.Obstacles, Obstacles.Length);
            dst.SpawnPointCount = SpawnPointCount;
            Array.Copy(SpawnPoints, dst.SpawnPoints, SpawnPoints.Length);
            dst.GroundY = GroundY;
            dst.HalfWidth = HalfWidth;
            dst.HalfDepth = HalfDepth;
        }

        /// <summary>
        /// 标准灰盒对战地图（C2 单源）：±50 边界 + 16 网格出生点。
        /// 服务端（RoomRuntime 开局生成）与客户端（BattleContext 预测世界重建）**必须**共用同一构造——
        /// 两端地图不一致 = 出生点错位 = 预测永不分叉收敛。原 RoomServer.BuildStandardMap 平移至此。
        /// </summary>
        public static SimMapData StandardBattleMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            for (int i = 0; i < MaxSpawnPoints; i++)
            {
                map.SpawnPoints[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);
            }
            map.SpawnPointCount = MaxSpawnPoints;
            return map;
        }
    }
}
