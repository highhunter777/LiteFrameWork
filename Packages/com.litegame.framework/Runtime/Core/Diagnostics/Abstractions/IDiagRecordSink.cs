namespace LiteFramework
{
    /// <summary>
    /// 诊断记录端口（日志和错误码关联版本、 更新事务、Scope、会话和房间）。
    /// </summary>
    public interface IDiagRecordSink
    {
        /// <summary>记一条结构化诊断（实现方自行决定容量/落点；调用方不得依赖其返回值——本接口无返回）。</summary>
        void Record(string stage, string code, string key, string detail);
    }
}