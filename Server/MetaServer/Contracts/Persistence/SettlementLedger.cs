using System;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// 结算幂等业务键（服务端总设计 §11.3 推荐唯一键之二）：(playerId, matchId, settlementType)。
    ///
    /// 与 <see cref="SettlementWrite.OperationId"/> 构成幂等的**两个维度**：
    /// - 同一操作重试（同 operationId）——响应丢失/超时重发场景；
    /// - 同一笔结算换头重提（同业务键、不同 operationId）——上游重复签发场景。
    /// 两种都必须命中同一条账目、返回同一份首次结果。
    /// </summary>
    public sealed record SettlementKey(string PlayerId, string MatchId, string SettlementType);

    /// <summary>
    /// 一次结算写的全部事实（服务端总设计 §11.3"Profile 事务内写唯一 Ledger + 更新库存/进度"）。
    ///
    /// 增量而非绝对值——Meta 只接受受验证的 RewardDelta，不接受客户端上报绝对数量（§11.2）。
    /// <see cref="ExpectedRevision"/> 两种模式：
    /// - **≥0：revision CAS**（§11.2）——期望值与实际不符即整笔回滚（<see cref="SettlementOutcome.RevisionConflict"/>），
    ///   供并发写竞争的库存/进度操作使用（P4）；
    /// - **-1：追加模式**——不做期望比对，修订号只增不比（适配器按 <c>$inc</c> 推进）；
    ///   供**结算落账**使用：提交方（Outbox 后台提交器）不持有玩家修订号，幂等完全由
    ///   账目唯一索引（<see cref="OperationId"/> ＋ <see cref="SettlementKey"/> 双维度）在
    ///   同一事务内承载——§11.3"推荐唯一键"即幂等裁判，结算无 CAS 要求。
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
    /// 结算应用结果（类型化三态）。第四态"未确认"以
    /// <see cref="SettlementStoreUnavailableException"/> 表达——不吞、不冒充成功；
    /// 调用方按"可能已应用也可能没有"对待，幂等保证两种情况的重试结果一致。
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
    ///
    /// 实现约束（L1 以测试替身验证**语义**，L3 以真实存储验证**持久性与并发兜底**）：
    /// 1. **唯一索引是幂等的最终实现**（Meta 专项 §9.1）——operationId 与业务键两个维度都必须由
    ///    存储端唯一性兜底，不得只靠应用层先查后写；
    /// 2. 首次应用 = 唯一账目写入 + revision CAS 推进，**同一原子边界**（§11.2"事务只围绕必须
    ///    原子化的 Ledger/Inventory 更新"）；CAS 冲突则整笔回滚，账目不得残留；
    /// 3. 返回 <see cref="SettlementOutcome.Duplicate"/> 时携带首次记录的快照；
    /// 4. 存储不可达/提交失败抛 <see cref="SettlementStoreUnavailableException"/>，
    ///    不得静默返回旧值或部分结果。
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
