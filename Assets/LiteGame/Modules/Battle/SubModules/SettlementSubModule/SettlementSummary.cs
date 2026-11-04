using System.Collections.Generic;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 结算摘要实现（Battle 域）：委托 <see cref="ResultRanking"/>（全序/名次/占位名**单源**，
    /// 与局内排行榜共用 <c>BattleRankOrder</c>），对外只暴露契约行 <see cref="MatchSettlementRow"/>。
    /// </summary>
    public sealed class SettlementSummary : ISettlementSummary
    {
        /// <inheritdoc />
        public IReadOnlyList<MatchSettlementRow> Build(IReadOnlyList<PlayerMatchResult> players)
            => ResultRanking.Build(players);
    }
}
