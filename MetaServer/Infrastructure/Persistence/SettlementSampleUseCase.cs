using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;

namespace MetaServer.Infrastructure.Persistence
{
    /// <summary>
    /// 样例命令结果——与 <see cref="SettlementOutcome"/> 对齐，并补齐命令层的两态：
    /// 形状拒绝（未触碰存储）与未确认（存储异常，不冒充成功）。
    /// </summary>
    public abstract record SampleCommandResult
    {
        /// <summary>首次应用成功（已确认）。</summary>
        public sealed record Accepted(SettlementRecord Record) : SampleCommandResult;

        /// <summary>重复提交——返回**首次**结果，不重新计算、不重复落账。</summary>
        public sealed record DuplicateHit(SettlementRecord Record) : SampleCommandResult;

        /// <summary>修订号冲突——整笔未落账，携带实际修订号供重试。</summary>
        public sealed record Conflict(long Expected, long Actual) : SampleCommandResult;

        /// <summary>形状非法——未触碰存储，一次报全。</summary>
        public sealed record Rejected(IReadOnlyList<string> Errors) : SampleCommandResult;

        /// <summary>未确认（存储异常）——不冒充成功；重试安全（见 ISettlementLedger 约束 4）。</summary>
        public sealed record Unconfirmed(string Reason) : SampleCommandResult;
    }

    /// <summary>
    /// 框架先行样例⑤的命令消费者：验证 → 原子应用 → 类型化终态。
    ///
    /// **非业务模块**——Auth/Lobby/Profile 归 G3（Meta 专项 §15），本类只证明存储接缝可被真实
    /// 消费者按"验证 → 幂等应用 → 类型化终态"驱动，G3 时由 Profile 模块吸收该形状。
    /// 生产装配缺真实存储时必须拒绝启动，不得悄悄退回替身（《框架先行》§6 末条）。
    /// </summary>
    public sealed class SettlementSampleUseCase
    {
        private readonly ISettlementLedger _ledger;

        public SettlementSampleUseCase(ISettlementLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public async ValueTask<SampleCommandResult> ExecuteAsync(
            SampleSettlementCommand command, CancellationToken ct)
        {
            IReadOnlyList<string> errors = command.Validate();
            if (errors.Count > 0)
            {
                return new SampleCommandResult.Rejected(errors);
            }

            var write = new SettlementWrite(
                command.OperationId,
                new SettlementKey(command.PlayerId, command.MatchId, command.SettlementType),
                command.ExpectedRevision,
                command.Delta);

            SettlementOutcome outcome;
            try
            {
                outcome = await _ledger.ApplyAsync(write, ct);
            }
            catch (SettlementStoreUnavailableException ex)
            {
                return new SampleCommandResult.Unconfirmed(ex.Message);
            }

            return outcome switch
            {
                SettlementOutcome.FirstApplied first => new SampleCommandResult.Accepted(first.Record),
                SettlementOutcome.Duplicate duplicate => new SampleCommandResult.DuplicateHit(duplicate.Record),
                SettlementOutcome.RevisionConflict conflict =>
                    new SampleCommandResult.Conflict(conflict.Expected, conflict.Actual),
                _ => new SampleCommandResult.Unconfirmed("未知结果形态：" + outcome.GetType().Name),
            };
        }
    }
}
