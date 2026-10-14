using System;
using LiteClient;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 客户端地图提供者（D9——地图装载自成一件事）：按 <c>map_id</c> 从已装载表链产出判定图，
    /// 与数值/武器同一条"装载产物显式传递"链（技术债 #1 纪律）。
    /// </summary>
    public interface IMapProvider
    {
        /// <summary>取判定图（表未装载/缺行/坏值即抛——fail-fast，不静默兜底）。</summary>
        SimMapData GetMap(int mapId = MapTableProjection.DefaultMapId);
    }

    /// <summary>
    /// 默认实现：读 <see cref="IConfigService"/> 已发布的 Luban 表快照，经**共享投影**
    /// <see cref="MapTableProjection"/> 产出（与服务端同代码同数据——同一份 `Map/tbmap.bytes`）。
    /// 取值点（`MatchSessionFactory` 进房装配 / `ProcedureMatch` 本地服建房）恒在 Preload 装载之后
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
    }
}
