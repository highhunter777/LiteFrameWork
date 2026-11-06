using System;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 结算幂等业务键（服务端总设计 §11.3 推荐唯一键之二）：(playerId, matchId, settlementType)。
    /// </summary>
    public sealed record SettlementKey(string PlayerId, string MatchId, string SettlementType);

    /// <summary>
    /// 一次结算写的全部事实（服务端总设计 §11.3"Profile 事务内写唯一 Ledger + 更新库存/进度"）。
    /// </summary>
    public sealed record SettlementWrite(
        string OperationId, SettlementKey Key, long ExpectedRevision, long Delta);

    /// <summary>
    /// 首次应用落账后的快照。重复提交必须**原样返回**这一份，不得重新计算——
    /// 这是"重复提交不重复发奖/不二次改账"的承载形式。
    /// </summary>
    public sealed record SettlementRecord(
        string OperationId, SettlementKey Key, long AppliedRevision, long BalanceAfter);

    /// <summary>
    /// 结算应用结果（类型化三态）。
    /// </summary>
    public abstract record SettlementOutcome
    {
        /// <summary>首次应用成功：账目写入与修订号推进已在同一原子边界内确认。</summary>
        public sealed record FirstApplied(SettlementRecord Record) : SettlementOutcome;

        /// <summary>重复提交：携带的是**首次**的 <see cref="SettlementRecord"/>。</summary>
        public sealed record Duplicate(SettlementRecord Record) : SettlementOutcome;

        /// <summary>修订号 CAS 冲突：整笔未落账（原子边界内回滚），携带实际修订号供调用方重试。</summary>
        public sealed record RevisionConflict(long Expected, long Actual) : SettlementOutcome;
    }

    /// <summary>
    /// 结算账本端口（《框架先行建设与业务接入专项设计》§4"持久化"行、§5-5；Meta 专项 §11.3）。
    /// </summary>
    public interface ISettlementLedger
    {
        ValueTask<SettlementOutcome> ApplyAsync(SettlementWrite write, CancellationToken ct);
    }

    /// <summary>
    /// 存储不可用/提交失败：未确认语义。重试是安全的——可能已应用（提交后失联）也可能没有
    /// （提交前失败），双维度唯一性保证重试命中同一条账目、拿回同一份首次结果。
    /// </summary>
    public sealed class SettlementStoreUnavailableException : Exception
    {
        public SettlementStoreUnavailableException(string message) : base(message)
        {
        }

        public SettlementStoreUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
