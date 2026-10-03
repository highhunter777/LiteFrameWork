using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;

namespace MetaServer.Tests
{
    // =====================================================================
    // 测试替身（《Meta 服务专项设计》§14、《测试开发框架总设计》分层）。
    //
    // **替身只验端口语义，不构成重启恢复证据**——《框架先行》§5-5"只做 fake 存储
    // 不能证明重启恢复"。重启恢复的完成证据在 L3：真实 Mongo（副本集）容器 + 进程级重启。
    // 替身刻意把持久状态外持（State 类）："新建实例复用同一状态"＝模拟"介质比进程活得久"，
    // 验证的是端口语义在重启边界下的正确性，不是介质持久性本身。
    //
    // 单线程口径（测试串行）——真实并发下的唯一性兜底由 L3 用 Mongo 唯一索引验证。
    // =====================================================================

    /// <summary>FakeSettlementLedger 的外持介质状态。跨实例共享即模拟进程重启。</summary>
    public sealed class FakeLedgerState
    {
        public readonly Dictionary<string, SettlementRecord> ByOperationId =
            new(StringComparer.Ordinal);

        public readonly Dictionary<SettlementKey, SettlementRecord> ByKey = new();

        /// <summary>每 PlayerId 的当前修订号（CAS 目标文档的替身）。</summary>
        public readonly Dictionary<string, long> Revisions = new(StringComparer.Ordinal);

        /// <summary>每 PlayerId 的当前余额。</summary>
        public readonly Dictionary<string, long> Balances = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 内存结算账本替身：实现 <see cref="ISettlementLedger"/> 的全部四条约束
    /// （双维度唯一兜底、原子边界、首次快照、显式异常）。
    /// </summary>
    public sealed class FakeSettlementLedger : ISettlementLedger
    {
        private readonly FakeLedgerState _state;

        public FakeSettlementLedger(FakeLedgerState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ValueTask<SettlementOutcome> ApplyAsync(SettlementWrite write, CancellationToken ct)
        {
            // 幂等维度一：operationId（唯一性兜底是字典本体，先查只是替身的快速路径）
            if (_state.ByOperationId.TryGetValue(write.OperationId, out SettlementRecord byOperation))
            {
                return ValueTask.FromResult<SettlementOutcome>(
                    new SettlementOutcome.Duplicate(byOperation));
            }

            // 幂等维度二：业务键 (playerId, matchId, settlementType)
            if (_state.ByKey.TryGetValue(write.Key, out SettlementRecord byKey))
            {
                return ValueTask.FromResult<SettlementOutcome>(new SettlementOutcome.Duplicate(byKey));
            }

            // CAS：账目写入与修订推进必须同一原子边界——冲突则整笔不落
            long current = _state.Revisions.TryGetValue(write.Key.PlayerId, out long revision)
                ? revision
                : 0;
            if (write.ExpectedRevision != current)
            {
                return ValueTask.FromResult<SettlementOutcome>(
                    new SettlementOutcome.RevisionConflict(write.ExpectedRevision, current));
            }

            long appliedRevision = current + 1;
            long balance = _state.Balances.TryGetValue(write.Key.PlayerId, out long existingBalance)
                ? existingBalance + write.Delta
                : write.Delta;

            var record = new SettlementRecord(write.OperationId, write.Key, appliedRevision, balance);

            // 原子边界的替身表达：四处写入一气呵成（单线程下无中间态可见）
            _state.Revisions[write.Key.PlayerId] = appliedRevision;
            _state.Balances[write.Key.PlayerId] = balance;
            _state.ByOperationId[write.OperationId] = record;
            _state.ByKey[write.Key] = record;

            return ValueTask.FromResult<SettlementOutcome>(new SettlementOutcome.FirstApplied(record));
        }
    }

    /// <summary>
    /// 故障注入点（§14 必备故障矩阵"每个持久化确认点前后终止进程"的 L1 形态——
    /// L1 用异常注入验证调用方语义；进程级终止夹具属 L3）。
    /// </summary>
    public enum LedgerFaultMode
    {
        /// <summary>提交前失败：不触碰内部实现，直接抛——介质上无任何残留。</summary>
        FailBeforeCommit,

        /// <summary>提交后失联：先真实应用（介质已落账），再抛——模拟"已确认但响应丢失"。</summary>
        FailAfterCommit,
    }

    /// <summary>包装内层账本注入提交点故障。</summary>
    public sealed class FaultySettlementLedger : ISettlementLedger
    {
        private readonly ISettlementLedger _inner;
        private readonly LedgerFaultMode _mode;

        public FaultySettlementLedger(ISettlementLedger inner, LedgerFaultMode mode)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _mode = mode;
        }

        public async ValueTask<SettlementOutcome> ApplyAsync(SettlementWrite write, CancellationToken ct)
        {
            if (_mode == LedgerFaultMode.FailBeforeCommit)
            {
                throw new SettlementStoreUnavailableException("注入：提交前存储失联");
            }

            SettlementOutcome outcome = await _inner.ApplyAsync(write, ct);
            // 内层已真实落账；此处丢弃结果并失联——调用方视角是"未确认"
            throw new SettlementStoreUnavailableException("注入：提交成功但响应丢失");
        }
    }

    /// <summary>FakeOutboxStore 的外持介质状态。</summary>
    public sealed class FakeOutboxState
    {
        /// <summary>按入队次序保存的条目（含 Confirmed）。</summary>
        public readonly List<OutboxEnvelope> Items = new();

        public int Capacity;
    }

    /// <summary>内存 Outbox 替身：实现 IOutboxStore 的全部约束（有界/幂等/确认持久/失败可重试）。</summary>
    public sealed class FakeOutboxStore : IOutboxStore
    {
        private readonly FakeOutboxState _state;

        public FakeOutboxStore(FakeOutboxState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ValueTask<OutboxEnqueueOutcome> EnqueueAsync(OutboxEnvelope envelope, CancellationToken ct)
        {
            foreach (OutboxEnvelope item in _state.Items)
            {
                if (string.Equals(item.OperationId, envelope.OperationId, StringComparison.Ordinal))
                {
                    return ValueTask.FromResult<OutboxEnqueueOutcome>(
                        new OutboxEnqueueOutcome.Duplicate(item));
                }
            }

            if (_state.Items.Count >= _state.Capacity)
            {
                return ValueTask.FromResult<OutboxEnqueueOutcome>(
                    new OutboxEnqueueOutcome.RejectedFull(_state.Capacity));
            }

            // 存储端权威：Status/Attempts/CreatedAtUtc 不取调用方值（与 MongoOutboxStore 同语义）
            OutboxEnvelope stored = envelope with
            {
                Status = OutboxStatus.Pending,
                Attempts = 0,
                CreatedAtUtc = DateTime.UtcNow,
                LastFailureReason = null,
            };
            _state.Items.Add(stored);
            return ValueTask.FromResult<OutboxEnqueueOutcome>(
                new OutboxEnqueueOutcome.Enqueued(stored));
        }

        public ValueTask<IReadOnlyList<OutboxEnvelope>> ListPendingAsync(int max, CancellationToken ct)
        {
            var pending = new List<OutboxEnvelope>();
            foreach (OutboxEnvelope item in _state.Items)
            {
                if (item.Status != OutboxStatus.Pending)
                {
                    continue;
                }
                pending.Add(item);
                if (pending.Count >= max)
                {
                    break;
                }
            }

            return ValueTask.FromResult<IReadOnlyList<OutboxEnvelope>>(pending);
        }

        public ValueTask MarkConfirmedAsync(string operationId, CancellationToken ct)
        {
            int index = FindIndex(operationId);
            if (index >= 0)
            {
                _state.Items[index] = _state.Items[index] with { Status = OutboxStatus.Confirmed };
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordFailureAsync(string operationId, string reason, CancellationToken ct)
        {
            int index = FindIndex(operationId);
            if (index >= 0)
            {
                OutboxEnvelope current = _state.Items[index];
                if (current.Status == OutboxStatus.Pending)
                {
                    // 状态保持 Pending（可重试），只累加计数并落原因——§10"未提交项保持可重试状态"；
                    // 已 Confirmed 条目幂等不累加（与 MongoOutboxStore 同语义）
                    _state.Items[index] = current with
                    {
                        Attempts = current.Attempts + 1,
                        LastFailureReason = reason,
                    };
                }
            }
            return ValueTask.CompletedTask;
        }

        private int FindIndex(string operationId)
        {
            for (int i = 0; i < _state.Items.Count; i++)
            {
                if (string.Equals(_state.Items[i].OperationId, operationId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>FakeSchemaVersionStore 的外持介质状态。</summary>
    public sealed class FakeSchemaVersionState
    {
        public long Current;
    }

    public sealed class FakeSchemaVersionStore : ISchemaVersionStore
    {
        private readonly FakeSchemaVersionState _state;

        public FakeSchemaVersionStore(FakeSchemaVersionState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ValueTask<long> ReadCurrentAsync(CancellationToken ct)
        {
            return ValueTask.FromResult(_state.Current);
        }

        public ValueTask WriteAsync(long version, CancellationToken ct)
        {
            _state.Current = version;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 迁移步骤替身：可注入 Migrate/Rollback 两侧失败，并记录调用计数以断言执行顺序与回滚范围。
    /// </summary>
    public sealed class FakeMigrationStep : IMigrationStep
    {
        private readonly Exception _migrateFailure;
        private readonly Exception _rollbackFailure;

        public FakeMigrationStep(
            long version, string description = null,
            Exception migrateFailure = null, Exception rollbackFailure = null)
        {
            Version = version;
            Description = description ?? ("v" + version);
            _migrateFailure = migrateFailure;
            _rollbackFailure = rollbackFailure;
        }

        public long Version { get; }

        public string Description { get; }

        public int MigrateCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public ValueTask MigrateAsync(CancellationToken ct)
        {
            MigrateCalls++;
            if (_migrateFailure != null)
            {
                throw _migrateFailure;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken ct)
        {
            RollbackCalls++;
            if (_rollbackFailure != null)
            {
                throw _rollbackFailure;
            }
            return ValueTask.CompletedTask;
        }
    }
}
