using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>
    /// Outbox 条目状态（Meta 专项 §10 优雅关闭第 3 步、§14 L3 矩阵）。
    ///
    /// 刻意只有两态：失败不落独立状态——§10"未提交项保持可重试状态"，
    /// 失败记账只累加尝试计数、状态保持 Pending，重试语义由状态本身承载。
    /// </summary>
    public enum OutboxStatus
    {
        /// <summary>待提交（含失败后保持可重试的条目）。</summary>
        Pending = 0,

        /// <summary>已确认完成。重启后不得重新投递。</summary>
        Confirmed = 1,
    }

    /// <summary>
    /// 持久 Outbox 条目。<see cref="OperationId"/> 是入队幂等键（§11.2"所有外部写带 requestId/operationId"）。
    ///
    /// **调用方只声明 <see cref="OperationId"/>/<see cref="Payload"/>**；<see cref="Status"/>/
    /// <see cref="Attempts"/>/<see cref="CreatedAtUtc"/>/<see cref="LastFailureReason"/> 是**存储读回字段**——
    /// 入队时由存储端权威赋值（Pending/0/当前 UTC/null），调用方传入的值一律忽略
    /// （防 Attempts 播种与状态伪造——硬化批 2026-10-03）。
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
    ///
    /// 实现约束（L1 以替身验证语义，L3 以真实存储验证持久性）：
    /// 1. **有界容量**——满则 <see cref="OutboxEnqueueOutcome.RejectedFull"/>，不得无限增长；
    /// 2. **入队幂等**——同 OperationId 返回 <see cref="OutboxEnqueueOutcome.Duplicate"/>（含 Confirmed 条目）；
    /// 3. **确认持久**——Confirmed 条目跨进程重启仍可查（审计面），但不得出现在待处理列表；
    /// 4. **失败保持可重试**——失败记账不改变状态（保持 Pending），只累加 Attempts；
    /// 5. drain 期间未提交项保持可重试状态（§10），这是排空第 4 步"刷新 Outbox"的语义基础。
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
