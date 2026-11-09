using System.Collections.Generic;

namespace LiteGame.Modules.UI
{
    /// <summary>开房选图行（表 <c>#map</c> 全行按表序）。</summary>
    public sealed class MapOption
    {
        public readonly int Id;
        public readonly string Name;

        public MapOption(int id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }
    }

    /// <summary>地图目录端口（页面只读候选面；实现住组合根，读配置表）。</summary>
    public interface IMapCatalog
    {
        IReadOnlyList<MapOption> All { get; }
    }
}
