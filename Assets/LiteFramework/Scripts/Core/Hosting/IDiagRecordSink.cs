namespace LiteFramework
{
    /// <summary>
    /// 诊断记录端口（《框架先行建设与业务接入专项设计》§8「可诊断」：日志和错误码关联版本、
    /// 更新事务、Scope、会话和房间）。
    ///
    /// **为什么是端口**：Core 在**掌握稳定分类码的判定点**（典型：激活事务失败/恢复，见
    /// <see cref="ActivationTransactionStore.DiagSink"/>）需要把事实交给诊断面；但 Core 是零依赖件，
    /// **不认识具体诊断实现**——由组合根接线到实际诊断面（客户端形态：LiteNet 的结构化记录面
    /// `DiagTrace`；服务端形态：宿主自己的日志/记录面）。
    ///
    /// 字段语义（与诊断面逐字对齐，便于产物合并与断言）：
    /// - <c>stage</c>：稳定域段名（词表见诊断面，如 Build/Content/Txn/Session/Room）；
    /// - <c>code</c>：稳定分类码（可断言、可工单化；不写自由文本）；
    /// - <c>key</c>：**关联键**——把同一次失败在各域/各端对齐的复合键；
    /// - <c>detail</c>：细节（原因长句/对照值）；**不得携带凭据**（票据/token/密钥，同《Meta 专项》§13.1）。
    /// </summary>
    public interface IDiagRecordSink
    {
        /// <summary>记一条结构化诊断（实现方自行决定容量/落点；调用方不得依赖其返回值——本接口无返回）。</summary>
        void Record(string stage, string code, string key, string detail);
    }
}