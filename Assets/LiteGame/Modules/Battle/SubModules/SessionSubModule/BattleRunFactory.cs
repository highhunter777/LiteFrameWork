using System;

namespace LiteGame
{
    /// <summary>默认对局运行工厂：Battle 域持有 Match 域工厂与开发会话端口，流程只拿端口。</summary>
    public sealed class BattleRunFactory : IBattleRunFactory
    {
        private readonly IBattleDomainFactory _matchSessions;
        private readonly IDevSession _dev;
        private readonly IBattleDevDiagnostics _diagnostics;

        public BattleRunFactory(IBattleDomainFactory matchSessions, IDevSession dev = null,
            IBattleDevDiagnostics diagnostics = null)
        {
            _matchSessions = matchSessions ?? throw new ArgumentNullException(nameof(matchSessions));
            _dev = dev;
            _diagnostics = diagnostics;
        }

        public IBattleRun Create(IMatchRuntime account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            BattleDomain match = null;
            try
            {
                match = _matchSessions.Create(account, account.Rules);
                return new BattleSession(match, _dev, _diagnostics);
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
