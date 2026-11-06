using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>
    /// 模块统计快照(HUD 数据源)。
    /// </summary>
    public interface IModuleStats
    {
        string StatsName { get; }
        void Snapshot(Dictionary<string, string> into);
    }
}
