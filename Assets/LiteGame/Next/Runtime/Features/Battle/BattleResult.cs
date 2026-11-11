using System;
using System.Collections.Generic;

namespace LiteGameNext.Features.Battle
{
    /// <summary>对局终态：完成/会话失败/中止/用户离开；调用方只认这个强类型，不认战斗上下文。</summary>
    public enum BattleOutcome
    {
        Completed,
        SessionFailed,
        Aborted,
        LeftByUser,
    }

    /// <summary>单玩家结算行（ResultFeature 消费；BattleFeature 产出）。</summary>
    public sealed class BattleSettlementRow
    {
        public BattleSettlementRow(string playerId, int kills, int deaths, long score)
        {
            PlayerId = playerId;
            Kills = kills;
            Deaths = deaths;
            Score = score;
        }

        public string PlayerId { get; }

        public int Kills { get; }

        public int Deaths { get; }

        public long Score { get; }
    }

    /// <summary>一局结果：终态 + 可展示原因 + 结算行；失败也走同一类型，不抛业务异常。</summary>
    public sealed class BattleResult
    {
        private BattleResult(BattleOutcome outcome, string reason, string playerId,
            IReadOnlyList<BattleSettlementRow> settlement)
        {
            Outcome = outcome;
            Reason = reason ?? string.Empty;
            PlayerId = playerId ?? string.Empty;
            Settlement = settlement ?? Array.Empty<BattleSettlementRow>();
        }

        public BattleOutcome Outcome { get; }

        public string Reason { get; }

        public string PlayerId { get; }

        public IReadOnlyList<BattleSettlementRow> Settlement { get; }

        public bool IsSuccess => Outcome == BattleOutcome.Completed;

        public static BattleResult Completed(string playerId, IReadOnlyList<BattleSettlementRow> settlement = null)
            => new BattleResult(BattleOutcome.Completed, string.Empty, playerId, settlement);

        public static BattleResult Failed(string reason)
            => new BattleResult(BattleOutcome.SessionFailed, reason, string.Empty, null);

        public static BattleResult Aborted(string reason)
            => new BattleResult(BattleOutcome.Aborted, reason, string.Empty, null);

        public static BattleResult Left(string playerId)
            => new BattleResult(BattleOutcome.LeftByUser, string.Empty, playerId, null);
    }
}
