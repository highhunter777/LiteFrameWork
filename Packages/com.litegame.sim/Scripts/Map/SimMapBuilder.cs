using System;

namespace LiteSim
{
    /// <summary>
    /// 地图数据构建单源（烘焙链的注入面）：定长填充 ＋ 越界/NaN fail-fast——坏数据宁拒勿载。
    /// 两端（`MapTableProjection` 投影、测试夹具）共用同一构建入口；超出定容即抛
    /// （禁静默截断——截断是静默分叉的入口）。
    /// </summary>
    public static class SimMapBuilder
    {
        public static SimMapData Build(float groundY, float halfWidth, float halfDepth,
            SimObstacle[] obstacles, int obstacleCount, SimVector3[] spawns, int spawnCount)
        {
            if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
            if (spawns == null) throw new ArgumentNullException(nameof(spawns));
            if (obstacleCount < 0 || obstacleCount > SimMapData.MaxObstacles)
                throw new ArgumentOutOfRangeException(nameof(obstacleCount), obstacleCount,
                    $"障碍数越界（0..{SimMapData.MaxObstacles}）");
            if (obstacleCount > obstacles.Length)
                throw new ArgumentException("obstacleCount 超出数组长度", nameof(obstacleCount));
            if (spawnCount < 0 || spawnCount > SimMapData.MaxSpawnPoints)
                throw new ArgumentOutOfRangeException(nameof(spawnCount), spawnCount,
                    $"出生点数越界（0..{SimMapData.MaxSpawnPoints}）");
            if (spawnCount > spawns.Length)
                throw new ArgumentException("spawnCount 超出数组长度", nameof(spawnCount));

            RequireFinite(groundY, nameof(groundY));
            RequireFinite(halfWidth, nameof(halfWidth));
            RequireFinite(halfDepth, nameof(halfDepth));
            if (halfWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(halfWidth), halfWidth, "边界半宽须为正");
            if (halfDepth <= 0f) throw new ArgumentOutOfRangeException(nameof(halfDepth), halfDepth, "边界半深须为正");

            for (int i = 0; i < obstacleCount; i++)
            {
                ref SimObstacle o = ref obstacles[i];
                if (o.Kind != SimObstacleKind.Circle && o.Kind != SimObstacleKind.Box)   // lint-allow R3（整型等值，非浮点精度比较）
                    throw new ArgumentOutOfRangeException(nameof(obstacles), o.Kind, $"障碍[{i}] kind 未定义");
                RequireFinite(o.Center.X, $"{nameof(obstacles)}[{i}].X");
                RequireFinite(o.Center.Y, $"{nameof(obstacles)}[{i}].Y");
                RequireFinite(o.Center.Z, $"{nameof(obstacles)}[{i}].Z");
                RequireNonNegative(o.Radius, $"{nameof(obstacles)}[{i}].Radius");
                RequireNonNegative(o.HalfX, $"{nameof(obstacles)}[{i}].HalfX");
                RequireNonNegative(o.HalfZ, $"{nameof(obstacles)}[{i}].HalfZ");
                RequireFinite(o.Height, $"{nameof(obstacles)}[{i}].Height");
                if (o.Height <= 0f) throw new ArgumentOutOfRangeException(nameof(obstacles), o.Height, $"障碍[{i}] height 须为正");
                if (o.Kind == SimObstacleKind.Circle && o.Radius <= 0f)   // lint-allow R3（整型等值，非浮点精度比较）
                    throw new ArgumentOutOfRangeException(nameof(obstacles), o.Radius, $"圆障碍[{i}] radius 须为正");
                if (o.Kind == SimObstacleKind.Box && (o.HalfX <= 0f || o.HalfZ <= 0f))   // lint-allow R3（整型等值，非浮点精度比较）
                    throw new ArgumentOutOfRangeException(nameof(obstacles), i, $"盒障碍[{i}] half_x/half_z 须为正");
                if ((byte)o.Surface > (byte)SimObstacleSurface.Wood)
                    throw new ArgumentOutOfRangeException(nameof(obstacles), (byte)o.Surface, $"障碍[{i}] surface 未定义");
            }
            for (int i = 0; i < spawnCount; i++)
            {
                RequireFinite(spawns[i].X, $"{nameof(spawns)}[{i}].X");
                RequireFinite(spawns[i].Y, $"{nameof(spawns)}[{i}].Y");
                RequireFinite(spawns[i].Z, $"{nameof(spawns)}[{i}].Z");
            }

            var map = new SimMapData { GroundY = groundY, HalfWidth = halfWidth, HalfDepth = halfDepth };
            map.ObstacleCount = obstacleCount;
            Array.Copy(obstacles, map.Obstacles, obstacleCount);
            map.SpawnPointCount = spawnCount;
            Array.Copy(spawns, map.SpawnPoints, spawnCount);
            return map;
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, $"{name} 非有限值");
        }

        private static void RequireNonNegative(float value, string name)
        {
            RequireFinite(value, name);
            if (value < 0f) throw new ArgumentOutOfRangeException(name, value, $"{name} 不得为负");
        }
    }
}
