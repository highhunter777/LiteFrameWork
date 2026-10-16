using System;
using System.Collections.Generic;
using System.IO;
using LiteClient;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 客户端地图提供者（D9——地图装载自成一件事）：按 <c>map_id</c> 从已装载表链产出判定图，
    /// 与数值/武器同一条"装载产物显式传递"链（技术债 #1 纪律）。
    ///
    /// 多图批：表行另带 <c>scene</c>（地图场景路径）——**场景选择同走表行**（加图＝加文件 + 加场景，
    /// 不改代码；`BattleScenes` 不再持有逐图路径常量）。
    /// </summary>
    public interface IMapProvider
    {
        /// <summary>取判定图（表未装载/缺行/坏值即抛——fail-fast，不静默兜底）。</summary>
        SimMapData GetMap(int mapId = MapTableProjection.DefaultMapId);

        /// <summary>取地图场景路径（表行 <c>scene</c> 列；缺行/空值即抛——"视觉＝判定同图"不可回落）。</summary>
        string ScenePath(int mapId);

        /// <summary>全部地图场景路径（表内全部行——离场卸载面按表枚举：加图不改卸载代码）。</summary>
        IReadOnlyList<string> ScenePaths();
    }

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
