using LiteNet.Transport;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 进房传输装配结果（路线图 §4.4）：传输 + 本局规则持有件。
    /// 规则实例随会话显式传递（权威侧与客户端预测同实例），不经任何"当前局"静态登记口。
    /// </summary>
    public readonly struct MatchTransport
    {
        /// <summary>本局传输（本地测试形态＝进程内本地服；其余＝真 KCP）。</summary>
        public readonly IClientTransport Transport;

        /// <summary>本局规则持有件（常规房 = null：无测试规则）。</summary>
        public readonly MatchTestRules Rules;

        public MatchTransport(IClientTransport transport, MatchTestRules rules)
        {
            Transport = transport;
            Rules = rules;
        }
    }
}
