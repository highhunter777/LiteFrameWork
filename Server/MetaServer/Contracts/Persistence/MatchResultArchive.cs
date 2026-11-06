using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 结算提交的单玩家条目（《上云测试专项设计》§4 裁决点① "结算载荷补 SeatAccountIds"）：
    /// 账号映射随提交载荷携带——Meta 按账号可查（裁决点③）；RoomServer 侧由宿主从会话取得。
    /// </summary>
    public sealed record MatchResultPlayerEntry(string PlayerId, string AccountId, int Kills, int Deaths);

    /// <summary>
    /// 一局对局结果的**归档记录**（供 <see cref="IMatchResultArchive"/> 落库；《上云测试专项设计》 §4 裁决点①③。）。
    /// </summary>
    public sealed record MatchResultArchiveEntry(
        string MatchId, long Seed, int FinalFrame, int EndReason, int GameplayEndReason, long WinnerEntityId,
        IReadOnlyList<MatchResultPlayerEntry> Players)
    {
    }

    /// <summary>首次落库快照（重复提交**原样返回**这一份，不重新计算——同账本口径）。</summary>
    public sealed record StoredMatchResult(string MatchId, DateTime FinishedUtc);

    /// <summary>账号维度查询视图（《上云测试》§4 裁决点③：matchId/击杀/结束原因/时间）。</summary>
    public sealed record AccountMatchResult(
        string MatchId, int Kills, int Deaths, int EndReason, int GameplayEndReason, DateTime FinishedUtc);

    /// <summary>落库结果（类型化两态；存储不可用以 <see cref="SettlementStoreUnavailableException"/> 表达）。</summary>
    public abstract record MatchResultStoreOutcome
    {
        /// <summary>首次落库成功。</summary>
        public sealed record Stored(StoredMatchResult Record) : MatchResultStoreOutcome;

        /// <summary>重复提交：携带**首次**落库的快照。</summary>
        public sealed record Duplicate(StoredMatchResult Record) : MatchResultStoreOutcome;
    }

    /// <summary>
    /// 对局结果归档端口（Meta 专项 §11.3 "Match Archive"；《上云测试》批A 的落库与可查面）。
    /// </summary>
    public interface IMatchResultArchive
    {
        /// <summary>落库一条对局结果（幂等键 = MatchId）。</summary>
        ValueTask<MatchResultStoreOutcome> StoreAsync(MatchResultArchiveEntry entry, CancellationToken ct);

        /// <summary>按账号列最近 <paramref name="limit"/> 局（完成时刻倒序）。</summary>
        ValueTask<IReadOnlyList<AccountMatchResult>> ListByAccountAsync(string accountId, int limit, CancellationToken ct);
    }
}
