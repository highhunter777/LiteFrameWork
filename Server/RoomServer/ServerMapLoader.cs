using System;
using System.IO;
using LiteFramework;
using LiteSim;
using LiteGame;

namespace RoomServer
{
    /// <summary>
    /// 服务端地图装载（《地图数据专项设计》§7；仿 <see cref="ServerTableLoader"/>）：
    /// 直读客户端同一份 `Assets/GameData/Config/Map/tbmap.bytes` → 经**共享投影**
    /// <see cref="MapTableProjection"/> 产出 <see cref="SimMapData"/>（判定图）——两端同代码（源链接共编）
    /// 同数据（同一份二进制），物理上不可能漂移。
    ///
    /// **装载即产出实例**：返回值必须显式传给宿主/房间（`HostAssembly.Inputs.Map` → `ServerHost` →
    /// `RoomConfig.Map` → `RoomRuntime`）——技术债 #1 同纪律：不回填任何全局静态面；
    /// 丢弃返回值 = "房间无图"（`RoomRuntime` 构造 fail-fast，不静默兜底）。
    /// </summary>
    public static class ServerMapLoader
    {
        /// <summary>从仓库根装载（定位失败或缺表 → 抛，fail-fast——无图不得起权威局）。</summary>
        public static SimMapData LoadFromRepo(int mapId = MapTableProjection.DefaultMapId)
        {
            string root = ServerTableLoader.FindRepoRoot()
                          ?? throw new InvalidOperationException(
                              $"找不到仓库根（需含 Assets 与 Tests/Tests.slnx）——无法装载 {ServerTableLoader.RelativeDir}");
            return LoadTableBytes(
                Path.Combine(root, ServerTableLoader.RelativeDir.Replace('/', Path.DirectorySeparatorChar)), mapId);
        }

        /// <summary>从指定表目录装载（表目录 = 客户端 GameData/Config 根；文件按共享投影的表路径取）。</summary>
        public static SimMapData LoadTableBytes(string configDir, int mapId = MapTableProjection.DefaultMapId)
        {
            if (configDir == null) throw new ArgumentNullException(nameof(configDir));
            string path = Path.Combine(configDir,
                MapTableProjection.TableFile.Replace('/', Path.DirectorySeparatorChar) + ".bytes");
            if (!File.Exists(path))
                throw new FileNotFoundException($"地图表缺失：{path}（跑 Luban/gen.bat——无图不得起权威局）", path);

            var table = new cfg.Tbmap(new Luban.ByteBuf(File.ReadAllBytes(path)));
            cfg.map row = table.GetOrDefault(mapId)
                ?? throw new InvalidDataException($"tbmap 缺 map_id={mapId} 行——地图数据未生成/行键漂移");
            SimMapData map = MapTableProjection.Project(row);
            HostLog.Info("Map",
                $"地图装载：map_id={mapId} name={row.Name} 障碍={map.ObstacleCount} 出生={map.SpawnPointCount} "
                + $"digest={row.ContentDigest}（两端同源复算锚）");
            return map;
        }
    }
}
