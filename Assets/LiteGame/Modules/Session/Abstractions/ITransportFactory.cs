namespace LiteGame
{
    /// <summary>
    /// 传输工厂端口（每局产一条传输 + 本局规则实例）：会话段只认这个口，不认具体传输/部署形态。
    /// 实现留在装配点（本批随会话段：传输裁决依赖联机部署快照与本局测试规则）。
    /// </summary>
    public interface ITransportFactory
    {
        /// <param name="testRoom">是否测试房（本地服形态；生产 = 联机传输）。</param>
        MatchTransport Create(bool testRoom);
    }
}
