using System;
using System.Collections.Generic;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 服务端地图目录（多图批；《地图数据专项设计》§7 装配注入链的"按行取图"面）：
    /// 行键 → <see cref="SimMapData"/> 的解析与**进程内缓存**——每个行键只装载一次
    /// （<see cref="ServerMapLoader"/> 直读同一份 `Map/tbmap.bytes` 经共享投影产出，
    /// 两端同代码同数据）。
    ///
    /// **语义（fail-closed）**：缺行/坏值由装载面抛，异常**不外吞**——由调用方（`ServerHost.OpenRoom`
    /// 建房路径）转成"拒绝进房"，绝不静默回落默认图（"请求训练图静默进首图"是判定面分叉源）。
    ///
    /// **线程**：建房发生在传输/宿主线程，缓存读写经单锁（低频路径——每个新行键一次装载，
    /// 不在权威循环热路径）。装载只产出实例，不触碰任何全局静态面（技术债 #1 同纪律）。
    /// </summary>
    public sealed class ServerMapCatalog
    {
        private readonly Func<int, SimMapData> _load;
        private readonly SimMapData _defaultMap;
        private readonly object _gate = new object();
        private readonly Dictionary<int, SimMapData> _cache = new Dictionary<int, SimMapData>();

        /// <param name="load">按行键装载（生产 = <see cref="ServerMapLoader.LoadTableBytes"/>/<c>LoadFromRepo</c> 绑定）。</param>
        /// <param name="defaultMap">宿主默认图（行键 0/未指定 与首行键同实例——避免重复装载）。</param>
        /// <param name="defaultMapId">宿主默认行键（装载面的缺省行；0 = 未知——仅作缓存别名，不影响解析）。</param>
        public ServerMapCatalog(Func<int, SimMapData> load, SimMapData defaultMap, int defaultMapId)
        {
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _defaultMap = defaultMap ?? throw new ArgumentNullException(nameof(defaultMap));
            if (defaultMapId > 0) _cache[defaultMapId] = defaultMap;
        }

        /// <summary>0/负 = 宿主默认图；&gt;0 = 按行键装载（缓存）；缺行/坏值抛（fail-closed）。</summary>
        public SimMapData Resolve(int mapId)
        {
            if (mapId <= 0) return _defaultMap;
            lock (_gate)
            {
                if (_cache.TryGetValue(mapId, out SimMapData cached)) return cached;
                SimMapData loaded = _load(mapId);
                if (loaded == null)
                    throw new InvalidOperationException($"地图目录装载返回空：map_id={mapId}");
                _cache[mapId] = loaded;
                return loaded;
            }
        }

        /// <summary>已装载行键（启动日志/诊断用；顺序不保证）。</summary>
        public IReadOnlyCollection<int> LoadedIds
        {
            get { lock (_gate) return new List<int>(_cache.Keys); }
        }
    }
}
