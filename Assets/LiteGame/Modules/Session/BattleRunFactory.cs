using System;

namespace LiteGame
{
    /// <summary>默认对局运行工厂：会话段持有 Match 域工厂与开发会话端口，流程只拿端口。</summary>
    public sealed class BattleRunFactory : IBattleRunFactory
    {
        private readonly IMatchSessionFactory _matchSessions;
        private readonly IDevSession _dev;

        public BattleRunFactory(IMatchSessionFactory matchSessions, IDevSession dev = null)
        {
            _matchSessions = matchSessions ?? throw new ArgumentNullException(nameof(matchSessions));
            _dev = dev;
        }

        public IBattleRun Create(AccountSession account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            MatchSession match = null;
            try
            {
                match = _matchSessions.Create(account, account.Rules);
                return new BattleSession(match, _dev);
            }
            catch
            {
                // Match 域已建但运行端口构造失败：就地释放，避免服务域/上下文悬挂（文档承诺）。
                match?.Dispose();
                throw;
            }
        }
    }
}
