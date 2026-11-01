using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework;
using LiteSim;
using LiteGame.Modules.Config;

namespace LiteGame.Modules.Map
{

    /// <summary>
    /// 默认实现：读 <see cref="IConfigService"/> 已发布的 Luban 表快照，经**共享投影**
    /// <see cref="MapTableProjection"/> 产出（与服务端同代码同数据——同一份 `Map/tbmap.bytes`）。
    /// 取值点（`MatchSessionFactory` 进房装配 / `ProcedureMatch` 场景装载）恒在 Preload 装载之后
    /// ——未装载即访问由 <c>ConfigService.Tables</c> 抛（同 `GameplayDigestBase` 惰性读口径）。
    /// </summary>
    public sealed class MapProvider : IMapProvider
    {
        private readonly IConfigService _config;

        public MapProvider(IConfigService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public SimMapData GetMap(int mapId = MapTableProjection.DefaultMapId)
            => MapTableProjection.Project(_config.Tables, mapId);

        public string ScenePath(int mapId)
        {
            cfg.map row = RowOrThrow(mapId);
            if (string.IsNullOrEmpty(row.Scene))
                throw new InvalidDataException(
                    $"tbmap map_id={mapId} 缺 scene 列（地图场景路径——多图批新增；重烘+gen.bat 后表行才有值）");
            return row.Scene;
        }

        public IReadOnlyList<string> ScenePaths()
        {
            cfg.Tbmap table = _config.Tables?.Tbmap;
            if (table == null)
                throw new InvalidDataException("tbmap 未装载——表链缺 Map 域（TableLayout.Files 应含 "
                    + MapTableProjection.TableFile + "）");
            var paths = new List<string>(table.DataList.Count);
            for (int i = 0; i < table.DataList.Count; i++)
            {
                string scene = table.DataList[i].Scene;
                if (string.IsNullOrEmpty(scene))
                    throw new InvalidDataException(
                        $"tbmap map_id={table.DataList[i].MapId} 缺 scene 列（场景路径）——重烘+gen.bat 后表行才有值");
                if (!paths.Contains(scene)) paths.Add(scene);   // 多行共用场景=去重（幂等卸载面）
            }
            return paths;
        }

        private cfg.map RowOrThrow(int mapId)
        {
            cfg.Tbmap table = _config.Tables?.Tbmap;
            if (table == null)
                throw new InvalidDataException("tbmap 未装载——表链缺 Map 域（TableLayout.Files 应含 "
                    + MapTableProjection.TableFile + "）");
            cfg.map row = table.GetOrDefault(mapId);
            if (row == null)
                throw new InvalidDataException($"tbmap 缺 map_id={mapId} 行——地图数据未生成/行键漂移（跑 Luban/gen.bat）");
            return row;
        }
    }
}
