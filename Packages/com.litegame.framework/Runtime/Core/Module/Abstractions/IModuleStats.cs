using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>
    /// 模块统计快照(HUD 数据源)。签名即契约:
    /// ①调用方复用同一个 Dictionary(预分配容量),实现负责 into.Clear() 后填入——容器与迭代器零分配;
    /// ②key 为常量串零分配,value 为插值串(HUD 低频轮询可接受);
    /// ③禁止实现方每次 new 容器/数组——那是旧签名"每帧调用即分配"的坑。
    /// </summary>
    public interface IModuleStats
    {
        string StatsName { get; }
        void Snapshot(Dictionary<string, string> into);
    }
}
