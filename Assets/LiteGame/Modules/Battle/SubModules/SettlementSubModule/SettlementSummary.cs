using System;
using System.Collections.Generic;
using LiteNet.Proto;
using LiteGame.Modules.Battle.Abstractions.Settlement;

namespace LiteGame
{
    /// <summary>
    /// 结算摘要实现（Battle 域）：委托 ResultRanking（全序/名次/占位名**单源**， 与局内排行榜共用 BattleRankPolicy），对外只暴露契约行 MatchSettlementRow。
    /// </summary>
    public sealed class SettlementSummary : ISettlementSummary
    {
        private readonly Func<BattleRankPolicy> _policy;

        /// <param name="policy">排行策略提供者；null = 缺省全序（测试/降级装配）。</param>
        public SettlementSummary(Func<BattleRankPolicy> policy = null)
            => _policy = policy;

        /// <inheritdoc />
        public IReadOnlyList<MatchSettlementRow> Build(IReadOnlyList<PlayerMatchResult> players)
            => ResultRanking.Build(players, _policy?.Invoke() ?? BattleRankPolicy.Default);
    }
}
