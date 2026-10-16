using System;
using System.IO;
using LiteSim;

namespace LiteClient
{
    /// <summary>
    /// 地图表行 → <see cref="SimMapData"/> 的**共享投影**（服务端源链接共编同一物理文件——
    /// 同 <see cref="SimConfigMapper"/>/<see cref="TableLayout"/> 先例）：两端从同一份 `tbmap.bytes`
    /// 投影出同一张判定图（移动碰撞 + 射击遮挡）。
    ///
    /// **校验（fail-fast，投影出错两端必分叉——宁可拒载）**：数量上限、kind 合法、全 finite、
    /// surface ∈ 0..10（<see cref="SimMapBuilder"/> 定长填充闸）＋ **行内 `content_digest` 复算比对**
    /// （不等 = "map.json→bytes→投影"链路走样：手改源/生成物过期/行键漂移）。
    /// </summary>
    public static class MapTableProjection
    {
        /// <summary>首图行键（D3/D10；多图批次见 <see cref="TrainingMapId"/>）。</summary>
        public const int DefaultMapId = 1;

        /// <summary>训练场行键（第二张地图：`Luban/Data/Map/map_training_ground.json` → `tbmap` 行 2；
        /// 烘焙目标 `BakeTargets.TrainingMap` 与运行期选行（`BattleScenes.MapIdFor`）都引本常量）。</summary>
        public const int TrainingMapId = 2;

        /// <summary>表数据文件（域相对路径，不带扩展名）——须在 <see cref="TableLayout.Files"/> 内
        /// （一致性由 L1 守卫测试钉住；服务端取盘与域归位都按它拼路径）。</summary>
        public const string TableFile = "Map/tbmap";

        /// <summary>从全表装载面取行投影（客户端 `ConfigService` 的 Luban 面；按 <paramref name="mapId"/> 选行）。</summary>
        public static SimMapData Project(cfg.Tables tables, int mapId = DefaultMapId)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (tables.Tbmap == null)
                throw new InvalidDataException("tbmap 未装载——表链缺 Map 域（TableLayout.Files 应含 " + TableFile + "）");
            cfg.map row = tables.Tbmap.GetOrDefault(mapId);
            if (row == null)
                throw new InvalidDataException($"tbmap 缺 map_id={mapId} 行——地图数据未生成/行键漂移（跑 Luban/gen.bat）");
            return Project(row);
        }

        /// <summary>行 → 判定图（核心投影；坏值 fail-fast）。</summary>
        public static SimMapData Project(cfg.map row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (row.Obstacles == null) throw new InvalidDataException("tbmap 行缺 obstacles 列表");
            if (row.Spawns == null) throw new InvalidDataException("tbmap 行缺 spawns 列表");
            if (row.Obstacles.Count > SimMapData.MaxObstacles)
                throw new InvalidDataException(
                    $"tbmap obstacles 超上限：{row.Obstacles.Count} > {SimMapData.MaxObstacles}——重烘（烘焙器超限截断）");
            if (row.Spawns.Count == 0 || row.Spawns.Count > SimMapData.MaxSpawnPoints)
                throw new InvalidDataException(
                    $"tbmap spawns 数量非法：{row.Spawns.Count}（须 1..{SimMapData.MaxSpawnPoints}）——重烘");

            var obstacles = new SimObstacle[row.Obstacles.Count];
            for (int i = 0; i < obstacles.Length; i++)
            {
                cfg.mapobstacle o = row.Obstacles[i];
                SimObstacleKind kind;
                if (o.Kind == 0) kind = SimObstacleKind.Circle;
                else if (o.Kind == 1) kind = SimObstacleKind.Box;
                else throw new InvalidDataException($"tbmap obstacles[{i}].kind 未定义：{o.Kind}（0=圆/1=盒）");

                obstacles[i] = new SimObstacle
                {
                    Kind = kind,
                    Center = new SimVector3(o.X, o.Y, o.Z),
                    Radius = o.Radius,
                    HalfX = o.HalfX,
                    HalfZ = o.HalfZ,
                    Height = o.Height,
                    Surface = (SimObstacleSurface)o.Surface,   // 越界（>Wood）由 SimMapBuilder 拒
                };
            }
            var spawns = new SimVector3[row.Spawns.Count];
            for (int i = 0; i < spawns.Length; i++)
            {
                cfg.mapspawn s = row.Spawns[i];
                spawns[i] = new SimVector3(s.X, s.Y, s.Z);
            }

            SimMapData map = SimMapBuilder.Build(row.GroundY, row.HalfWidth, row.HalfDepth,
                obstacles, obstacles.Length, spawns, spawns.Length);

            string digest = SimMapDigest.Compute(map);
            if (!string.Equals(digest, row.ContentDigest, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"tbmap content_digest 不符：行内={row.ContentDigest ?? "(null)"} 复算={digest}"
                    + "——烘焙源/生成物/手改走样（map.json 勿手改；改布局＝改场景重烘 → gen.bat）");
            return map;
        }
    }
}
