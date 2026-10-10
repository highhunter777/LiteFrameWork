using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MetaServer.Contracts.Profile;

namespace MetaServer.Modules.Profile
{
    /// <summary>
    /// 对局结算落库用例（《Meta 服务专项设计》§8.2 结算链的 Profile.Apply 半边；
    /// 服务端总设计 §11.3"Profile 事务内写唯一 Ledger"）——吸收
    /// <see cref="MetaServer.Infrastructure.Persistence.SettlementSampleUseCase"/> 的
    /// "验证 → 幂等应用 → 类型化终态"形状（样例注释钉住的 G3 承接）。
    ///
    /// - **幂等**：账本端口双维度（operationId ＋ (playerId, matchId, settlementType)）——
    ///   重复提交/响应丢失重试/Outbox 压实后重投恒命中同账目，返回**首次**结果（§8.2）；
    /// - **追加模式**：结算提交方不持有玩家修订号——默认走 <see cref="SettlementWrite"/>
    ///   追加模式（ExpectedRevision=-1，修订号只增不比）；显式供给 ≥0 时按 CAS 期望，
    ///   不符即 conflict（供需要并发写竞争控制的调用方）；
    /// - **fan-out 语义**：逐玩家独立落账（不同玩家互不共享文档，无跨玩家原子需求）；
    ///   中途存储失败 → <c>Unconfirmed</c>（已应用的玩家项不回滚）——调用方整包重试时
    ///   已落账项 duplicate 命中，收敛且不重复发奖；
    /// - **操作数上限**：一次提交 ≤ <see cref="MaxPlayers"/> 名玩家——防单请求放大存储事务量。
    ///
    /// **一条提交、双侧落库**（《上云测试》§4 裁决点①③）：
    /// ① **归档面**（<see cref="IMatchResultArchive"/>，幂等键 = matchId）：对局结果事实
    ///    （击杀/死亡/结束原因/完成时刻）——"按账号可查"与"重复提交 100 次只落一条"的承载；
    /// ② **账本面**（<see cref="ISettlementLedger"/>，逐玩家双维度幂等）：受验证的 RewardDelta。
    /// 归档面先落（幂等命中即 <see cref="Result.Processed.Duplicate"/>），账本面随后 fan-out；
    /// 任一面存储失联 → Unconfirmed（重投时已落部分按幂等命中，收敛）。
    /// </summary>
    public sealed class ApplyMatchResultUseCase
    {
        /// <summary>本端点的结算类型（§11.3 推荐唯一键的第三维；后续局外发放类型按需扩表）。</summary>
        public const string SettlementType = "match";

        /// <summary>单字段长度上界（§P0-3"解析后限制长度"同款纪律）。</summary>
        public const int MaxFieldBytes = 64;

        /// <summary>单提交玩家数上界（对局上限 8 人留余量；防单请求放大事务量）。</summary>
        public const int MaxPlayers = 64;

        /// <summary>奖励增量绝对值上界（卡异常载荷，非经济设计）。</summary>
        public const long MaxDeltaAbs = 1_000_000L;

        private readonly ISettlementLedger _ledger;
        private readonly IMatchResultArchive _archive;

        public ApplyMatchResultUseCase(ISettlementLedger ledger, IMatchResultArchive archive)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
        }

        /// <summary>执行结果（端点映射 HTTP：Processed→200/409（按 conflict 位）；Rejected→400；Unconfirmed→503）。</summary>
        public abstract class Result
        {
            private Result() { }

            /// <summary>全部玩家项已处理（applied/duplicate/conflict 逐项携带）。</summary>
            public sealed class Processed : Result
            {
                public string MatchId;
                public List<PlayerSettlementOutcome> Outcomes;
                public bool HasConflict;

                /// <summary>归档面幂等命中（该局结果已在库——重复提交）。</summary>
                public bool Duplicate;
            }

            /// <summary>形状非法——未触碰存储，一次报全（携带字段名，不回显字段值）。</summary>
            public sealed class Rejected : Result
            {
                public IReadOnlyList<string> Errors;
            }

            /// <summary>存储未确认（部分玩家项可能已落账）——重试安全（§8.2）。</summary>
            public sealed class Unconfirmed : Result
            {
                public string Reason;
            }
        }

        public async Task<Result> ExecuteAsync(MatchResultSubmission submission, CancellationToken ct)
        {
            IReadOnlyList<string> errors = Validate(submission);
            if (errors.Count > 0)
            {
                return new Result.Rejected { Errors = errors };
            }

            // 归档面先行（"可查"是封闭测试的第一消费面；幂等键 = matchId——重投命中首次快照）
            bool archivedDuplicate;
            try
            {
                MatchResultStoreOutcome archive = await _archive.StoreAsync(ToArchiveEntry(submission), ct);
                archivedDuplicate = archive is MatchResultStoreOutcome.Duplicate;
            }
            catch (SettlementStoreUnavailableException ex)
            {
                return new Result.Unconfirmed { Reason = ex.Message };
            }
            catch (OperationCanceledException)
            {
                throw;                                     // 取消随请求管道传播（调用方重试走原路径）
            }

            var outcomes = new List<PlayerSettlementOutcome>(submission.Players.Count);
            bool hasConflict = false;
            try
            {
                foreach (PlayerResultEntry entry in submission.Players)
                {
                    string operationId = ResolveOperationId(submission.MatchId, entry);
                    SettlementOutcome outcome = await _ledger.ApplyAsync(new SettlementWrite(
                        operationId,
                        new SettlementKey(entry.PlayerId, submission.MatchId, SettlementType),
                        entry.ExpectedRevision,
                        entry.Delta), ct);

                    PlayerSettlementOutcome item = ToOutcome(entry.PlayerId, operationId, outcome);
                    if (SettlementOutcomes.Conflict == item.Outcome) hasConflict = true;
                    outcomes.Add(item);
                }
            }
            catch (SettlementStoreUnavailableException ex)
            {
                return new Result.Unconfirmed { Reason = ex.Message };
            }
            catch (OperationCanceledException)
            {
                throw;                                     // 取消随请求管道传播（调用方重试走原路径）
            }

            return new Result.Processed
            {
                MatchId = submission.MatchId,
                Outcomes = outcomes,
                HasConflict = hasConflict,
                Duplicate = archivedDuplicate,
            };
        }

        /// <summary>提交载荷 → 归档记录（账号映射/结果事实；账本增量不进归档——查询面不需要）。</summary>
        private static MatchResultArchiveEntry ToArchiveEntry(MatchResultSubmission submission)
        {
            var players = new MatchResultPlayerEntry[submission.Players.Count];
            for (int i = 0; i < players.Length; i++)
            {
                PlayerResultEntry p = submission.Players[i];
                players[i] = new MatchResultPlayerEntry(p.PlayerId, p.AccountId, p.Kills, p.Deaths);
            }
            return new MatchResultArchiveEntry(submission.MatchId, submission.Seed, submission.FinalFrame,
                submission.EndReason, submission.GameplayEndReason, submission.WinnerEntityId, players);
        }

        /// <summary>幂等键解析：显式供给优先；缺省按 (matchId, playerId) 确定性派生——
        /// 同局重投恒命中同账目（压实后重枚举/响应丢失重试均收敛）。</summary>
        private static string ResolveOperationId(string matchId, PlayerResultEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.OperationId)) return entry.OperationId;
            return "match:" + matchId + ":" + entry.PlayerId;
        }

        private static PlayerSettlementOutcome ToOutcome(string playerId, string operationId,
            SettlementOutcome outcome)
        {
            var item = new PlayerSettlementOutcome { PlayerId = playerId, OperationId = operationId };
            switch (outcome)
            {
                case SettlementOutcome.FirstApplied first:
                    item.Outcome = SettlementOutcomes.Applied;
                    item.AppliedRevision = first.Record.AppliedRevision;
                    item.BalanceAfter = first.Record.BalanceAfter;
                    break;
                case SettlementOutcome.Duplicate duplicate:
                    item.Outcome = SettlementOutcomes.Duplicate;
                    item.AppliedRevision = duplicate.Record.AppliedRevision;
                    item.BalanceAfter = duplicate.Record.BalanceAfter;
                    break;
                case SettlementOutcome.RevisionConflict conflict:
                    item.Outcome = SettlementOutcomes.Conflict;
                    item.Expected = conflict.Expected;
                    item.Actual = conflict.Actual;
                    break;
                default:
                    throw new InvalidOperationException("未知结算结果形态：" + outcome.GetType().Name);
            }
            return item;
        }

        /// <summary>形状边界（落库前拒绝，一次报全；不回显字段值）。</summary>
        private static IReadOnlyList<string> Validate(MatchResultSubmission submission)
        {
            var errors = new List<string>();
            if (submission == null)
            {
                errors.Add("payload");
                return errors;
            }

            ValidateField(errors, "requestId", submission.RequestId, 128);
            ValidateField(errors, "matchId", submission.MatchId, MaxFieldBytes);
            if (submission.Seed < 0) errors.Add("seed 不得为负");
            if (submission.FinalFrame < 0) errors.Add("finalFrame 不得为负");
            if (submission.EndReason < 0) errors.Add("endReason 不得为负");
            if (submission.GameplayEndReason < 0) errors.Add("gameplayEndReason 不得为负");
            if (submission.WinnerEntityId < 0) errors.Add("winnerEntityId 不得为负");

            if (submission.Players == null || submission.Players.Count == 0)
            {
                errors.Add("players 不得为空");
            }
            else if (submission.Players.Count > MaxPlayers)
            {
                errors.Add("players 不得超过 " + MaxPlayers + " 条，实际：" + submission.Players.Count);
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < submission.Players.Count; i++)
                {
                    PlayerResultEntry entry = submission.Players[i];
                    if (entry == null)
                    {
                        errors.Add("players[" + i + "] 为空");
                        continue;
                    }
                    ValidateField(errors, "players[" + i + "].playerId", entry.PlayerId, MaxFieldBytes);
                    ValidateField(errors, "players[" + i + "].accountId", entry.AccountId, MaxFieldBytes);
                    if (entry.Kills < 0)
                    {
                        errors.Add("players[" + i + "].kills 不得为负");
                    }
                    if (entry.Deaths < 0)
                    {
                        errors.Add("players[" + i + "].deaths 不得为负");
                    }
                    if (entry.Delta > MaxDeltaAbs || entry.Delta < -MaxDeltaAbs)
                    {
                        errors.Add("players[" + i + "].delta 超出 ±" + MaxDeltaAbs);
                    }
                    if (entry.ExpectedRevision < -1L)
                    {
                        errors.Add("players[" + i + "].expectedRevision 只允许 -1（追加）或 ≥0（CAS 期望）");
                    }
                    if (!string.IsNullOrEmpty(entry.OperationId)
                        && Encoding.UTF8.GetByteCount(entry.OperationId) > 256)
                    {
                        errors.Add("players[" + i + "].operationId 不得超过 256 UTF-8 字节");
                    }
                    if (!string.IsNullOrEmpty(entry.PlayerId) && !seen.Add(entry.PlayerId))
                    {
                        errors.Add("players[" + i + "].playerId 与前项重复（同一玩家单局只结算一次）");
                    }
                }
            }

            return errors;
        }

        private static void ValidateField(List<string> errors, string name, string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value))
            {
                errors.Add(name + " 不得为空");
            }
            else if (Encoding.UTF8.GetByteCount(value) > maxBytes)
            {
                errors.Add(name + " 不得超过 " + maxBytes + " UTF-8 字节");
            }
        }
    }
}
