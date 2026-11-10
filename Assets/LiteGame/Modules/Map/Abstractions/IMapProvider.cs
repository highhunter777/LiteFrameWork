using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework;
using LiteSim;
using LiteGame.Modules.Config;

namespace LiteGame.Modules.Map
{
    /// <summary>
    /// 客户端地图提供者（D9——地图装载自成一件事）：按 map_id 从已装载表链产出判定图， 与数值/武器同一条"装载产物显式传递"链（技术债 #1 纪律）。
    /// </summary>
    public interface IMapProvider
    {
        /// <summary>取判定图（表未装载/缺行/坏值即抛——fail-fast，不静默兜底）。</summary>
        SimMapData GetMap(int mapId = MapTableProjection.DefaultMapId);

        /// <summary>取地图场景路径（表行 <c>scene</c> 列；缺行/空值即抛——"视觉＝判定同图"不可回落）。</summary>
        string ScenePath(int mapId);

        /// <summary>全部地图场景路径（表内全部行——离场卸载面按表枚举：加图不改卸载代码）。</summary>
        IReadOnlyList<string> ScenePaths();

        /// <summary>选图行（表内全部行按表序；开房/选图 UI 的唯一来源）。</summary>
        IReadOnlyList<MapOption> Options { get; }
    }
}
