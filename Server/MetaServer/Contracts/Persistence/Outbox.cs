using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// Outbox 条目状态（Meta 专项 §10 优雅关闭第 3 步、§14 L3 矩阵）。
    /// </summary>
    public enum OutboxStatus
    {
        /// <summary>待提交（含失败后保持可重试的条目）。</summary>
        Pending = 0,

        /// <summary>已确认完成。重启后不得重新投递。</summary>
        Confirmed = 1,
    }

    /// <summary>
    /// 持久 Outbox 条目。
    /// </summary>
    public sealed record OutboxEnvelope(
        string OperationId,
        string Payload,
        OutboxStatus Status = OutboxStatus.Pending,
        int Attempts = 0,
        DateTime CreatedAtUtc = default,
        string LastFailureReason = null);

    /// <summary>入队结果三态——满与重复都必须显式，不得静默丢弃或静默追加。</summary>
    public abstract record OutboxEnqueueOutcome
    {
        public sealed record Enqueued(OutboxEnvelope Envelope) : OutboxEnqueueOutcome;

        /// <summary>重复入队：返回已存在条目（含已确认的），不重复投递。</summary>
        public sealed record Duplicate(OutboxEnvelope Existing) : OutboxEnqueueOutcome;

        /// <summary>
        /// 容量已满的显式拒绝（§11.2"任何队列、缓存、票据窗口与重试缓冲都必须有显式容量与清理策略"）。
        /// 丢弃/降级/重试策略由调用方决定，不静默。Confirmed 条目的保留与清理策略归 G3/R4 显式定义。
        /// </summary>
        public sealed record RejectedFull(int Capacity) : OutboxEnqueueOutcome;
    }

    /// <summary>
    /// 持久 Outbox 端口（§11.3 结算 Outbox / §10 优雅关闭第 3 步"刷新 Outbox 到持久介质"）。
    /// </summary>
    public interface IOutboxStore
    {
        ValueTask<OutboxEnqueueOutcome> EnqueueAsync(OutboxEnvelope envelope, CancellationToken ct);

        /// <summary>按入队次序列出待处理条目；max 是本次列举的上界。</summary>
        ValueTask<IReadOnlyList<OutboxEnvelope>> ListPendingAsync(int max, CancellationToken ct);

        ValueTask MarkConfirmedAsync(string operationId, CancellationToken ct);

        /// <summary>
        /// 失败记账：**只对 Pending 条目生效**（Confirmed 幂等不累加、未知条目无操作）；
        /// 状态保持 Pending（可重试），Attempts + 1 并记录原因与时刻（UTC）——重试根因可诊断。
        /// </summary>
        ValueTask RecordFailureAsync(string operationId, string reason, CancellationToken ct);
    }
}
