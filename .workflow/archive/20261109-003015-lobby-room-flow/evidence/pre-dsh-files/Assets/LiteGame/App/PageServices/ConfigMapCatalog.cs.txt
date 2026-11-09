using System;
using System.Collections.Generic;
using LiteGame.Modules.Config;
using LiteGame.Modules.UI;

namespace LiteGame
{
    /// <summary><see cref="IMapCatalog"/> 的生产实现：读 <c>#map</c> 表（与联机入口选图同源）。</summary>
    public sealed class ConfigMapCatalog : IMapCatalog
    {
        private readonly IConfigService _config;

        public ConfigMapCatalog(IConfigService config)
            => _config = config ?? throw new ArgumentNullException(nameof(config));

        public IReadOnlyList<MapOption> All
        {
            get
            {
                cfg.Tbmap table = _config.Tables.Tbmap;
                IReadOnlyList<cfg.map> rows = table?.DataList;
                var list = new List<MapOption>(rows?.Count ?? 0);
                if (rows == null) return list;
                for (int i = 0; i < rows.Count; i++) list.Add(new MapOption(rows[i].MapId, rows[i].Name));
                return list;
            }
        }
    }
}
