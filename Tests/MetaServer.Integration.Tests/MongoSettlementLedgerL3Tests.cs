using System;
using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence;
using MetaServer.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// 结算账本真存储 L3（《框架先行》§5-5"只做 fake 存储不能证明重启恢复"、Meta 专项 §14 L3 行
    /// "唯一 Ledger 与事务重试"、样例⑤"实际持久化确认 → 重复请求/故障 → 进程恢复"）。
    ///
    /// 与 L1（SettlementPersistenceTests，替身验语义）的分工：此处验证**存储端事实**——
    /// 唯一索引兜底、事务原子边界、介质比进程活得久。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MongoSettlementLedgerL3Tests
    {
        private readonly MongoFixture _fixture;
        private readonly string _database;

        public MongoSettlementLedgerL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
            _database = fixture.CreateDatabaseName();   // 用例级隔离：xunit 每用例新建本实例
        }

        private static SettlementWrite Write(
            string operationId = "op-1", string playerId = "player-1",
            string matchId = "match-1", long revision = 0, long delta = 10)
        {
            return new SettlementWrite(
                operationId, new SettlementKey(playerId, matchId, "settle"), revision, delta);
        }

        private async Task<MongoSettlementLedger> LedgerAsync()
        {
            AssertSkipIfUnavailable();
            await _fixture.MigrateToLatestAsync(_database);
            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            return new MongoSettlementLedger(database);
        }

        private void AssertSkipIfUnavailable()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
        }

        [SkippableFact]
        public async Task 首次提交_真实存储确认_修订与余额推进()
        {
            MongoSettlementLedger ledger = await LedgerAsync();

            SettlementOutcome outcome = await ledger.ApplyAsync(Write(), CancellationToken.None);

            var first = Assert.IsType<SettlementOutcome.FirstApplied>(outcome);
            Assert.Equal(1, first.Record.AppliedRevision);
            Assert.Equal(10, first.Record.BalanceAfter);
        }

        [SkippableFact]
        public async Task 重复提交_同操作号_真实唯一索引兜底_返回首次快照()
        {
            MongoSettlementLedger ledger = await LedgerAsync();
            SettlementOutcome first = await ledger.ApplyAsync(Write(delta: 10), CancellationToken.None);
            SettlementOutcome second = await ledger.ApplyAsync(Write(delta: 999), CancellationToken.None);

            Assert.IsType<SettlementOutcome.FirstApplied>(first);
            var duplicate = Assert.IsType<SettlementOutcome.Duplicate>(second);
            Assert.Equal(10, duplicate.Record.BalanceAfter);   // 999 未被采纳——首次快照
        }

        [SkippableFact]
        public async Task 重复提交_同业务键换操作号_仍命中首次账目()
        {
            MongoSettlementLedger ledger = await LedgerAsync();
            await ledger.ApplyAsync(Write(operationId: "op-1", delta: 10), CancellationToken.None);

            SettlementOutcome second =
                await ledger.ApplyAsync(Write(operationId: "op-retry", delta: 10), CancellationToken.None);

            var duplicate = Assert.IsType<SettlementOutcome.Duplicate>(second);
            Assert.Equal("op-1", duplicate.Record.OperationId);
        }

        [SkippableFact]
        public async Task CAS冲突_真实事务整笔回滚_无残留()
        {
            MongoSettlementLedger ledger = await LedgerAsync();
            await ledger.ApplyAsync(Write(matchId: "m1"), CancellationToken.None);   // player-1 → 修订 1

            SettlementOutcome conflict = await ledger.ApplyAsync(
                Write(operationId: "op-2", matchId: "m2", revision: 5), CancellationToken.None);

            var result = Assert.IsType<SettlementOutcome.RevisionConflict>(conflict);
            Assert.Equal(5, result.Expected);
            Assert.Equal(1, result.Actual);

            // 整笔回滚：冲突笔无账目、修订号未动
            SettlementOutcome retryRead = await ledger.ApplyAsync(
                Write(operationId: "op-2", matchId: "m2", revision: 1, delta: 5), CancellationToken.None);
            var applied = Assert.IsType<SettlementOutcome.FirstApplied>(retryRead);
            Assert.Equal(2, applied.Record.AppliedRevision);
            Assert.Equal(15, applied.Record.BalanceAfter);
        }

        [SkippableFact]
        public async Task 客户端重建后_重复提交仍命中首次账目()
        {
            // "进程/连接重建"形态：新 IMongoClient（全新连接池与拓扑）读同一介质
            MongoSettlementLedger first = await LedgerAsync();
            await first.ApplyAsync(Write(), CancellationToken.None);

            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            var rebuilt = new MongoSettlementLedger(database);
            SettlementOutcome retry = await rebuilt.ApplyAsync(Write(delta: 999), CancellationToken.None);

            var duplicate = Assert.IsType<SettlementOutcome.Duplicate>(retry);
            Assert.Equal(10, duplicate.Record.BalanceAfter);
        }

        [SkippableFact]
        public async Task 容器重启后_账目仍在_重复提交命中首次()
        {
            MongoSettlementLedger ledger = await LedgerAsync();
            await ledger.ApplyAsync(Write(), CancellationToken.None);
            AssertSkipIfUnavailable();

            if (!await _fixture.TryRestartContainerAsync())
            {
                Skip.If(true, "docker 不可用或容器重启超时——容器级重启恢复未验证");
            }

            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            var afterRestart = new MongoSettlementLedger(database);
            SettlementOutcome retry =
                await afterRestart.ApplyAsync(Write(delta: 999), CancellationToken.None);

            var duplicate = Assert.IsType<SettlementOutcome.Duplicate>(retry);
            Assert.Equal(1, duplicate.Record.AppliedRevision);
            Assert.Equal(10, duplicate.Record.BalanceAfter);
        }

        [SkippableFact]
        public async Task 重复提交一百次_只生效一次()
        {
            // §17 商业门禁"幂等"行（结算重复提交 100 次只生效一次）的样例级闭环
            MongoSettlementLedger ledger = await LedgerAsync();
            SettlementOutcome first = await ledger.ApplyAsync(Write(delta: 10), CancellationToken.None);
            var accepted = Assert.IsType<SettlementOutcome.FirstApplied>(first);

            for (int i = 1; i < 100; i++)
            {
                SettlementOutcome outcome =
                    await ledger.ApplyAsync(Write(delta: 10 + i), CancellationToken.None);
                var duplicate = Assert.IsType<SettlementOutcome.Duplicate>(outcome);
                Assert.Equal(accepted.Record, duplicate.Record);   // 每次都拿回同一份首次快照
            }

            Assert.Equal(10, accepted.Record.BalanceAfter);   // 后 99 次的载荷从未生效
        }

        [SkippableFact]
        public async Task 顺序多笔_修订链推进_余额累计()
        {
            MongoSettlementLedger ledger = await LedgerAsync();
            await ledger.ApplyAsync(Write(operationId: "op-1", matchId: "m1", delta: 10), CancellationToken.None);
            await ledger.ApplyAsync(Write(operationId: "op-2", matchId: "m2", revision: 1, delta: 5), CancellationToken.None);
            SettlementOutcome third = await ledger.ApplyAsync(
                Write(operationId: "op-3", matchId: "m3", revision: 2, delta: 3), CancellationToken.None);

            var applied = Assert.IsType<SettlementOutcome.FirstApplied>(third);
            Assert.Equal(3, applied.Record.AppliedRevision);
            Assert.Equal(18, applied.Record.BalanceAfter);
        }
    }
}
