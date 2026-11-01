using System.Collections.Generic;
using LiteNet.Proto;

namespace LiteGame
{
    /// <summary>
    /// 结算摘要端口（Battle 域产物）：冻结结果行 → 结算页排行行表。
    /// 全序/名次/占位名口径**单源**（与局内排行榜同一实现 <c>BattleRankOrder</c>）——
    /// 流程段只经本口取行表，不认战斗域内部投影件。
    /// </summary>
    public interface ISettlementSummary
    {
        /// <summary>构建排行行表（已按全序排好：击杀降序 → EntityId 升序；并列同名次）。
        /// <paramref name="players"/> null/空 → 空表。</summary>
        IReadOnlyList<MatchSettlementRow> Build(IReadOnlyList<PlayerMatchResult> players);
    }
}
