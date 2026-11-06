namespace LiteFramework
{
    /// <summary>
    /// 结构化日志出口（可选）。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>接收一条已入环形缓冲的日志（含会话/对局/帧/席位关联字段）。</summary>
        void Write(in LogEntry entry);
    }
}
