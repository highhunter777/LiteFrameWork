using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 结算账本接缝契约（《框架先行》§5-5"存储与幂等"、服务端总设计 §11.2/§11.3、
    /// Meta 专项 §14 L1 行"幂等键语义；revision CAS 冲突路径"）。
    ///
    /// 走 <see cref="SettlementSampleUseCase"/>（样例⑤的命令消费者）——测的是交付物，
    /// 不是另起的旁路。L1 验证**端口语义**；介质持久性/并发兜底归 L3 真 Mongo 容器
    /// （§5-5"只做 fake 存储不能证明重启恢复"）。
    /// </summary>
    public sealed class SettlementPersistenceTests
    {
        private static SampleSettlementCommand Command(
            string operationId = "op-1", string playerId = "player-1",
            string matchId = "match-1", string type = "settle",
            long revision = 0, long delta = 10)
        {
            return new SampleSettlementCommand(operationId, playerId, matchId, type, revision, delta);
        }

        private static ValueTask<SampleCommandResult> ExecuteAsync(
            ISettlementLedger ledger, SampleSettlementCommand command)
        {
            return new SettlementSampleUseCase(ledger).ExecuteAsync(command, CancellationToken.None);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 首次提交_确认落账_修订号与余额推进()
        {
            var state = new FakeLedgerState();

            SampleCommandResult result = await ExecuteAsync(new FakeSettlementLedger(state), Command());

            var accepted = Assert.IsType<SampleCommandResult.Accepted>(result);
            Assert.Equal(1, accepted.Record.AppliedRevision);
            Assert.Equal(10, accepted.Record.BalanceAfter);
            Assert.Single(state.ByOperationId);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 重复提交_同操作号_返回首次结果不重复落账()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            SampleCommandResult first = await ExecuteAsync(ledger, Command());
            // 换载荷重发也必须命中首次——重复提交不得重新计算
            SampleCommandResult second = await ExecuteAsync(ledger, Command(delta: 999));

            var accepted = Assert.IsType<SampleCommandResult.Accepted>(first);
            var duplicate = Assert.IsType<SampleCommandResult.DuplicateHit>(second);
            Assert.Equal(accepted.Record, duplicate.Record);
            Assert.Equal(10, duplicate.Record.BalanceAfter);   // 999 未被采纳
            Assert.Single(state.ByOperationId);        // 无第二条账目
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 重复提交_同业务键换操作号_仍命中首次账目()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Command(operationId: "op-1", delta: 10));
            // 上游重复签发：同 (playerId, matchId, type)，换了 operationId
            SampleCommandResult second = await ExecuteAsync(ledger, Command(operationId: "op-retry", delta: 10));

            var duplicate = Assert.IsType<SampleCommandResult.DuplicateHit>(second);
            Assert.Equal("op-1", duplicate.Record.OperationId);   // 返回首次的账目身份
            Assert.Single(state.ByOperationId);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 修订号冲突_整笔不落账_无残留()
        {
            // 冲突场景必须是**同账号、不同业务键**（同键换操作号按契约属重复提交，不属冲突）
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Command());   // player-1：修订号 0 → 1

            SampleCommandResult conflict = await ExecuteAsync(
                ledger, Command(operationId: "op-2", matchId: "match-2", revision: 5));

            var result = Assert.IsType<SampleCommandResult.Conflict>(conflict);
            Assert.Equal(5, result.Expected);
            Assert.Equal(1, result.Actual);               // 携带实际修订号供重试
            Assert.Single(state.ByOperationId);            // 冲突笔无残留
            Assert.Equal(1, state.Revisions["player-1"]);  // 修订号未被推进
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 修订号冲突后_按实际修订号重试_成功()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Command());

            SampleCommandResult retry = await ExecuteAsync(
                ledger, Command(operationId: "op-2", matchId: "match-2", revision: 1, delta: 5));

            var accepted = Assert.IsType<SampleCommandResult.Accepted>(retry);
            Assert.Equal(2, accepted.Record.AppliedRevision);
            Assert.Equal(15, accepted.Record.BalanceAfter);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 提交前失败_未确认_介质无残留_恢复后重试成功()
        {
            var state = new FakeLedgerState();
            var inner = new FakeSettlementLedger(state);
            SampleCommandResult lost = await ExecuteAsync(
                new FaultySettlementLedger(inner, LedgerFaultMode.FailBeforeCommit), Command());

            Assert.IsType<SampleCommandResult.Unconfirmed>(lost);   // 不冒充成功
            Assert.Empty(state.ByOperationId);             // 介质无账目

            SampleCommandResult recovered = await ExecuteAsync(inner, Command());
            Assert.IsType<SampleCommandResult.Accepted>(recovered);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 提交后失联_调用方未确认_重试返回首次结果()
        {
            var state = new FakeLedgerState();
            var inner = new FakeSettlementLedger(state);
            SampleCommandResult lost = await ExecuteAsync(
                new FaultySettlementLedger(inner, LedgerFaultMode.FailAfterCommit), Command());

            Assert.IsType<SampleCommandResult.Unconfirmed>(lost);   // 调用方视角：未确认
            Assert.Single(state.ByOperationId);             // 介质上其实已落账

            // 响应丢失后的重试：不重复落账，拿回首次结果
            SampleCommandResult retry = await ExecuteAsync(inner, Command());
            var duplicate = Assert.IsType<SampleCommandResult.DuplicateHit>(retry);
            Assert.Equal(1, duplicate.Record.AppliedRevision);
            Assert.Single(state.ByOperationId);             // 不重复落账
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 非法命令_拒绝_一次报全_不触碰存储()
        {
            var state = new FakeLedgerState();
            SampleCommandResult result = await ExecuteAsync(
                new FakeSettlementLedger(state),
                new SampleSettlementCommand("   ", "player", "match", "type", -1, 5));

            var rejected = Assert.IsType<SampleCommandResult.Rejected>(result);
            Assert.Equal(2, rejected.Errors.Count);          // 空白操作号 + 负修订号，一次报全
            Assert.Empty(state.ByOperationId);     // 拒绝发生在触碰存储之前
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 超长字段_被拒()
        {
            var state = new FakeLedgerState();
            string overlong = new string('x', SampleSettlementCommand.MaxFieldLength + 1);

            SampleCommandResult result = await ExecuteAsync(
                new FakeSettlementLedger(state), Command(playerId: overlong));

            var rejected = Assert.IsType<SampleCommandResult.Rejected>(result);
            Assert.Single(rejected.Errors);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 多账号互不干扰()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ExecuteAsync(ledger, Command(playerId: "player-1", matchId: "match-1", delta: 10));
            await ExecuteAsync(ledger, Command(
                operationId: "op-2", playerId: "player-2", matchId: "match-1", delta: 7));

            Assert.Equal(1, state.Revisions["player-1"]);
            Assert.Equal(1, state.Revisions["player-2"]);
            Assert.Equal(10, state.Balances["player-1"]);
            Assert.Equal(7, state.Balances["player-2"]);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 实例重建后_同介质状态_重复提交仍命中首次账目()
        {
            // §5-5"重启恢复"的 L1 语义面：介质比进程活得久——同一状态上重建实例，
            // 重复提交仍返回首次结果。**介质持久性本身归 L3 真 Mongo 容器验证。**
            var state = new FakeLedgerState();
            await ExecuteAsync(new FakeSettlementLedger(state), Command());

            var afterRestart = await ExecuteAsync(new FakeSettlementLedger(state), Command());

            Assert.IsType<SampleCommandResult.DuplicateHit>(afterRestart);
            Assert.Single(state.ByOperationId);
        }

        // ---- 追加模式（ExpectedRevision = -1：结算落账路径——§11.3 幂等由唯一索引承载，无 CAS 期望）----

        private const long AppendMode = -1L;

        private static ValueTask<SettlementOutcome> ApplyAsync(
            ISettlementLedger ledger, string operationId, string playerId, string matchId,
            long expectedRevision, long delta)
        {
            return ledger.ApplyAsync(new SettlementWrite(
                operationId, new SettlementKey(playerId, matchId, "match"), expectedRevision, delta),
                CancellationToken.None);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 追加模式_首笔建文档_次笔不同局推进_余额累积()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);

            SettlementOutcome first = await ApplyAsync(ledger, "op-1", "p1", "m1", AppendMode, 10);
            SettlementOutcome second = await ApplyAsync(ledger, "op-2", "p1", "m2", AppendMode, 5);

            var firstApplied = Assert.IsType<SettlementOutcome.FirstApplied>(first);
            var secondApplied = Assert.IsType<SettlementOutcome.FirstApplied>(second);
            Assert.Equal(1, firstApplied.Record.AppliedRevision);
            Assert.Equal(2, secondApplied.Record.AppliedRevision);   // 只增不比——不持修订号也推进
            Assert.Equal(10, firstApplied.Record.BalanceAfter);
            Assert.Equal(15, secondApplied.Record.BalanceAfter);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 追加模式_不产生冲突_而显式期望不符仍走CAS()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ApplyAsync(ledger, "op-1", "p1", "m1", AppendMode, 10);

            // 追加模式在任意修订号上都推进（提交方不持修订号也不会陷入冲突循环）
            SettlementOutcome appended = await ApplyAsync(ledger, "op-2", "p1", "m2", AppendMode, 3);
            Assert.IsType<SettlementOutcome.FirstApplied>(appended);

            // 显式 CAS 期望不符（期望 99，实际 2）——仍按冲突整笔不落
            SettlementOutcome conflict = await ApplyAsync(ledger, "op-3", "p1", "m3", 99, 1);
            var rejected = Assert.IsType<SettlementOutcome.RevisionConflict>(conflict);
            Assert.Equal(99, rejected.Expected);
            Assert.Equal(2, rejected.Actual);
            Assert.Equal(2, state.Revisions["p1"]);          // 冲突笔不推进
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 追加模式_同局重投_双维度命中首次账目不重复发奖()
        {
            var state = new FakeLedgerState();
            var ledger = new FakeSettlementLedger(state);
            await ApplyAsync(ledger, "match:m1:p1", "p1", "m1", AppendMode, 10);

            // 同操作号重投（响应丢失重试）
            SettlementOutcome byOperation = await ApplyAsync(ledger, "match:m1:p1", "p1", "m1", AppendMode, 999);
            // 同业务键换头重提（Outbox 压实后重枚举——派生键恒同；此处显式换号模拟上游重复签发）
            SettlementOutcome byKey = await ApplyAsync(ledger, "op-reissue", "p1", "m1", AppendMode, 999);

            Assert.IsType<SettlementOutcome.Duplicate>(byOperation);
            Assert.IsType<SettlementOutcome.Duplicate>(byKey);
            Assert.Equal(10, state.Balances["p1"]);           // 999 均未采纳——不重复发奖
            Assert.Single(state.ByOperationId);               // 介质上只有首笔（重提均不落账）
        }
    }
}
