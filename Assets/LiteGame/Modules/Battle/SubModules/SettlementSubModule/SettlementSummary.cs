using System;
using System.Collections.Generic;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 结算摘要实现（Battle 域）：委托 <see cref="ResultRanking"/>（全序/名次/占位名**单源**，
    /// 与局内排行榜共用 <see cref="BattleRankPolicy"/>），对外只暴露契约行 <see cref="MatchSettlementRow"/>。
    /// 策略在每次构建时取（装配根注入的延迟读口——配置装载早于对局，策略投影随表快照就绪）。
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
