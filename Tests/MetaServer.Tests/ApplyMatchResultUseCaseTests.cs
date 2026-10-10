using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using MetaServer.Contracts.Profile;
using MetaServer.Modules.Profile;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 对局结算落库用例（<see cref="ApplyMatchResultUseCase"/>）L1：《Meta 服务专项设计》§8.2
    /// 结算链 Profile.Apply 半边——验证 → 逐玩家幂等落账 → 类型化终态。
    /// 幂等双维度/追加模式/故障矩阵的端口级用例见 <see cref="SettlementPersistenceTests"/>；
    /// 本组钉**用例编排面**：fan-out、形状边界、Unconfirmed 收敛、conflict 透传。
    /// </summary>
    public sealed class ApplyMatchResultUseCaseTests
    {
        private static MatchResultSubmission Submission(
            string matchId = "m-100", params (string PlayerId, long Delta)[] players)
        {
            var submission = new MatchResultSubmission { RequestId = "req-1", MatchId = matchId };
            submission.Players = new System.Collections.Generic.List<PlayerResultEntry>();
            foreach ((string playerId, long delta) in players)
                submission.Players.Add(new PlayerResultEntry
                {
                    PlayerId = playerId, AccountId = "acc-" + playerId, Delta = delta,
                });
            return submission;
        }

        private static async Task<ApplyMatchResultUseCase.Result> ExecuteAsync(
            ISettlementLedger ledger, MatchResultSubmission submission, IMatchResultArchive archive = null)
        {
            return await new ApplyMatchResultUseCase(ledger, archive ?? new FakeMatchResultArchive())
                .ExecuteAsync(submission, CancellationToken.None);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 合法多玩家提交_逐玩家落账_操作号确定性派生()
        {
            var state = new FakeLedgerState();

            ApplyMatchResultUseCase.Result processed = await ExecuteAsync(
                new FakeSettlementLedger(state), Submission("m-1", ("p1", 10), ("p2", 25)));

            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(processed);
            Assert.False(done.HasConflict);
            Assert.Equal(2, done.Outcomes.Count);
            Assert.All(done.Outcomes, o => Assert.Equal(SettlementOutcomes.Applied, o.Outcome));
            Assert.Equal("match:m-1:p1", done.Outcomes[0].OperationId);   // 派生键稳定可预测
            Assert.Equal("match:m-1:p2", done.Outcomes[1].OperationId);
            Assert.Equal(10, done.Outcomes[0].BalanceAfter);
            Assert.Equal(25, done.Outcomes[1].BalanceAfter);
            Assert.Equal(2, state.ByOperationId.Count);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 同局原样重投_全部duplicate_携带首次快照_不重复记账()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Submission("m-1", ("p1", 10), ("p2", 25)));

            // 响应丢失/Outbox 压实后重枚举——同局重投（载荷甚至被改坏也命中首次）
            ApplyMatchResultUseCase.Result retried = await ExecuteAsync(
                ledger, Submission("m-1", ("p1", 999), ("p2", 999)));

            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(retried);
            Assert.False(done.HasConflict);
            Assert.All(done.Outcomes, o => Assert.Equal(SettlementOutcomes.Duplicate, o.Outcome));
            Assert.Equal(10, done.Outcomes[0].BalanceAfter);   // 首次快照，非重算
            Assert.Equal(25, done.Outcomes[1].BalanceAfter);
            Assert.Equal(2, state.ByOperationId.Count);         // 无新账目
            Assert.Equal(10, state.Balances["p1"]);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 同局换头重提_显式operationId_业务键命中首次()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Submission("m-1", ("p1", 10)));

            var resubmission = Submission("m-1", ("p1", 10));
            resubmission.Players[0].OperationId = "op-upstream-reissue";   // 上游重复签发

            ApplyMatchResultUseCase.Result retried = await ExecuteAsync(ledger, resubmission);

            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(retried);
            Assert.Equal(SettlementOutcomes.Duplicate, done.Outcomes[0].Outcome);
            Assert.Single(state.ByOperationId);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 显式CAS期望不符_conflict透传_整包HasConflict()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            var first = Submission("m-1", ("p1", 10));
            first.Players[0].ExpectedRevision = 0;
            await ExecuteAsync(ledger, first);

            var stale = Submission("m-2", ("p1", 5));
            stale.Players[0].ExpectedRevision = 99;     // 期望陈旧——实际 1

            ApplyMatchResultUseCase.Result result = await ExecuteAsync(ledger, stale);

            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(result);
            Assert.True(done.HasConflict);
            PlayerSettlementOutcome conflict = Assert.Single(done.Outcomes);
            Assert.Equal(SettlementOutcomes.Conflict, conflict.Outcome);
            Assert.Equal(99, conflict.Expected);
            Assert.Equal(1, conflict.Actual);
            Assert.Equal(1, state.Revisions["p1"]);      // 冲突笔不推进
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task fanout中途存储失联_Unconfirmed_整包重试收敛()
        {
            var state = new FakeLedgerState();
            var ledger = new PartialFaultLedger(new FakeSettlementLedger(state), succeedCount: 1);
            var submission = Submission("m-1", ("p1", 10), ("p2", 25));

            // p1 已真实落账、p2 未应用——调用方收到 Unconfirmed（§8.2"已应用也可能没有"）
            ApplyMatchResultUseCase.Result interrupted = await ExecuteAsync(ledger, submission);
            Assert.IsType<ApplyMatchResultUseCase.Result.Unconfirmed>(interrupted);
            Assert.Single(state.ByOperationId);
            Assert.Equal(10, state.Balances["p1"]);
            Assert.False(state.Balances.ContainsKey("p2"));

            // 恢复后整包重试：p1 duplicate 命中首次（不重复发奖）、p2 首次落账——收敛
            ApplyMatchResultUseCase.Result retried =
                await ExecuteAsync(new FakeSettlementLedger(state), submission);
            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(retried);
            Assert.Equal(SettlementOutcomes.Duplicate, done.Outcomes[0].Outcome);
            Assert.Equal(SettlementOutcomes.Applied, done.Outcomes[1].Outcome);
            Assert.Equal(10, done.Outcomes[0].BalanceAfter);   // p1 仍是首次快照
            Assert.Equal(25, done.Outcomes[1].BalanceAfter);
            Assert.Equal(10, state.Balances["p1"]);              // 未被推成 20
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 形状非法_拒绝一次报全_不触碰存储()
        {
            var state = new FakeLedgerState();

            ApplyMatchResultUseCase.Result rejected = await ExecuteAsync(new FakeSettlementLedger(state), null);
            Assert.IsType<ApplyMatchResultUseCase.Result.Rejected>(rejected);

            var bad = new MatchResultSubmission { RequestId = "r", MatchId = "m" };   // players 空
            ApplyMatchResultUseCase.Result emptyPlayers =
                await ExecuteAsync(new FakeSettlementLedger(state), bad);
            Assert.IsType<ApplyMatchResultUseCase.Result.Rejected>(emptyPlayers);

            // 同一玩家重复出现 + 越界 delta + 非法修订模式——一次报全
            var duplicated = Submission("m-1", ("p1", 10), ("p1", 5));
            duplicated.Players[0].Delta = ApplyMatchResultUseCase.MaxDeltaAbs + 1;
            duplicated.Players[1].ExpectedRevision = -2;

            ApplyMatchResultUseCase.Result allErrors =
                await ExecuteAsync(new FakeSettlementLedger(state), duplicated);
            var result = Assert.IsType<ApplyMatchResultUseCase.Result.Rejected>(allErrors);
            Assert.True(result.Errors.Count >= 3);
            Assert.Empty(state.ByOperationId);            // 拒绝先于存储触碰
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 归档面_同局重投_duplicate命中_只落一条()
        {
            var state = new FakeLedgerState();
            var archive = new FakeMatchResultArchive();
            var ledger = new FakeSettlementLedger(state);

            var first = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(
                await ExecuteAsync(ledger, Submission("m-1", ("p1", 10), ("p2", 25)), archive));
            Assert.False(first.Duplicate);

            // 重复提交（响应丢失重试/Outbox 重放）——归档面 duplicate，仍只一条
            var retried = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(
                await ExecuteAsync(ledger, Submission("m-1", ("p1", 10), ("p2", 25)), archive));
            Assert.True(retried.Duplicate);
            Assert.Equal(1, archive.Count);
            Assert.Equal(2, archive.StoreCalls);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 归档面_按账号可查_含击杀死亡与结束原因()
        {
            var archive = new FakeMatchResultArchive();
            var submission = Submission("m-1", ("p1", 10), ("p2", 5));
            submission.Seed = 7; submission.FinalFrame = 1234; submission.EndReason = 3;
            submission.GameplayEndReason = 2; submission.WinnerEntityId = 42;
            submission.Players[0].Kills = 7;
            submission.Players[0].Deaths = 2;

            await ExecuteAsync(new FakeSettlementLedger(new FakeLedgerState()), submission, archive);

            var mine = Assert.Single(await archive.ListByAccountAsync("acc-p1", 20, CancellationToken.None));
            Assert.Equal("m-1", mine.MatchId);
            Assert.Equal(7, mine.Kills);
            Assert.Equal(2, mine.Deaths);
            Assert.Equal(3, mine.EndReason);
            Assert.Equal(2, mine.GameplayEndReason);

            Assert.Empty(await archive.ListByAccountAsync("acc-nobody", 20, CancellationToken.None));
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 缺账号的条目_归档面拒绝_不落无主账目()
        {
            var archive = new FakeMatchResultArchive();
            var submission = Submission("m-1", ("p1", 10));
            submission.Players[0].AccountId = "";

            var rejected = Assert.IsType<ApplyMatchResultUseCase.Result.Rejected>(
                await ExecuteAsync(new FakeSettlementLedger(new FakeLedgerState()), submission, archive));

            Assert.Contains(rejected.Errors, e => e.Contains("accountId"));
            Assert.Equal(0, archive.StoreCalls);          // 拒绝先于存储触碰
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 归档存储失联_Unconfirmed_重试收敛()
        {
            var state = new FakeLedgerState();
            var submission = Submission("m-1", ("p1", 10));
            var archive = new FakeMatchResultArchive();

            ApplyMatchResultUseCase.Result interrupted = await new ApplyMatchResultUseCase(
                    new FakeSettlementLedger(state), new ThrowingOnceArchive(archive))
                .ExecuteAsync(submission, CancellationToken.None);
            Assert.IsType<ApplyMatchResultUseCase.Result.Unconfirmed>(interrupted);
            Assert.Empty(state.ByOperationId);            // 归档失败时不触碰账本（顺序先归档后账本）

            var retried = await ExecuteAsync(new FakeSettlementLedger(state), submission, archive);
            var done = Assert.IsType<ApplyMatchResultUseCase.Result.Processed>(retried);
            Assert.Equal(SettlementOutcomes.Applied, done.Outcomes[0].Outcome);
            Assert.Equal(1, archive.Count);
        }

        /// <summary>首次存储抛未确认、随后放行（注入归档面存储故障）。</summary>
        private sealed class ThrowingOnceArchive : IMatchResultArchive
        {
            private readonly IMatchResultArchive _inner;

            public ThrowingOnceArchive(IMatchResultArchive inner)
            {
                _inner = inner;
            }

            public ValueTask<MatchResultStoreOutcome> StoreAsync(MatchResultArchiveEntry entry, CancellationToken ct)
            {
                throw new SettlementStoreUnavailableException("注入：归档存储失联");
            }

            public ValueTask<System.Collections.Generic.IReadOnlyList<AccountMatchResult>> ListByAccountAsync(
                string accountId, int limit, CancellationToken ct)
            {
                return _inner.ListByAccountAsync(accountId, limit, ct);
            }
        }
    }
}
