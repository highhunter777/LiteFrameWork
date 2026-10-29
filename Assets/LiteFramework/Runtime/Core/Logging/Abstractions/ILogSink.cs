namespace LiteFramework
{
    /// <summary>
    /// 结构化日志出口（可选）。**在写日志的调用线程上同步调用**——实现必须非阻塞
    /// （入队即返回，落盘/格式化交给自己的后台线程），且不得抛异常（Log 侧仍有防御性 catch）。
    /// 入口：`Log.SetSink(sink)`；`null` = 关闭（release/测试默认无 sink）。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>接收一条已入环形缓冲的日志（含会话/对局/帧/席位关联字段）。</summary>
        void Write(in LogEntry entry);
    }
}
