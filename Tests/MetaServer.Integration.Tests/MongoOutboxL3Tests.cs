using System;
using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using MetaServer.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;
using Xunit;

namespace MetaServer.IntegrationTests
{
    /// <summary>
    /// 持久 Outbox 真存储 L3（Meta 专项 §14 L3 行"Outbox 提交前后失败、响应丢失、重复提交；
    /// 进程重启后 Outbox 恢复"、§10"未提交项保持可重试状态"）。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MongoOutboxL3Tests
    {
        private readonly MongoFixture _fixture;
        private readonly string _database;

        public MongoOutboxL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
            _database = fixture.CreateDatabaseName();   // 用例级隔离
        }

        private static OutboxEnvelope Envelope(string operationId, string payload = "payload")
        {
            return new OutboxEnvelope(operationId, payload, OutboxStatus.Pending, 0);
        }

        private async Task<MongoOutboxStore> StoreAsync(int capacity = 16)
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
            await _fixture.MigrateToLatestAsync(_database);
            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            return new MongoOutboxStore(database, capacity, () => DateTime.UtcNow);
        }

        [SkippableFact]
        public async Task 入队_待处理按入队次序列出()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1", "a"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2", "b"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-3", "c"), CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);

            Assert.Equal(3, pending.Count);
            Assert.Equal("op-1", pending[0].OperationId);
            Assert.Equal("op-2", pending[1].OperationId);
            Assert.Equal("op-3", pending[2].OperationId);
        }

        [SkippableFact]
        public async Task 重复入队_真实唯一索引兜底_返回已有条目()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1", "first"), CancellationToken.None);

            var second = await store.EnqueueAsync(Envelope("op-1", "second"), CancellationToken.None);

            var duplicate = Assert.IsType<OutboxEnqueueOutcome.Duplicate>(second);
            Assert.Equal("first", duplicate.Existing.Payload);
            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Single(pending);
        }

        [SkippableFact]
        public async Task 容量满_显式拒绝()
        {
            MongoOutboxStore store = await StoreAsync(capacity: 2);
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2"), CancellationToken.None);

            var third = await store.EnqueueAsync(Envelope("op-3"), CancellationToken.None);

            var rejected = Assert.IsType<OutboxEnqueueOutcome.RejectedFull>(third);
            Assert.Equal(2, rejected.Capacity);
            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Equal(2, pending.Count);
        }

        [SkippableFact]
        public async Task 确认后_不再出现在待处理列表()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-1", CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);
            Assert.Equal("op-2", pending[0].OperationId);
        }

        [SkippableFact]
        public async Task 已确认条目_重复入队_不复活不重投()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-1", CancellationToken.None);

            var again = await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);

            var duplicate = Assert.IsType<OutboxEnqueueOutcome.Duplicate>(again);
            Assert.Equal(OutboxStatus.Confirmed, duplicate.Existing.Status);
            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Empty(pending);
        }

        [SkippableFact]
        public async Task 失败记账_状态保持可重试_计数累加()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);

            await store.RecordFailureAsync("op-1", "下游 503", CancellationToken.None);
            await store.RecordFailureAsync("op-1", "下游 503", CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Single(pending);
            Assert.Equal(OutboxStatus.Pending, pending[0].Status);
            Assert.Equal(2, pending[0].Attempts);
        }

        [SkippableFact]
        public async Task 客户端重建后_待处理仍在_确认不复活()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-1", CancellationToken.None);

            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            var rebuilt = new MongoOutboxStore(database, 16, () => DateTime.UtcNow);
            var pending = await rebuilt.ListPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);
            Assert.Equal("op-2", pending[0].OperationId);
        }

        [SkippableFact]
        public async Task 容器重启后_待处理恢复_确认不复活()
        {
            MongoOutboxStore store = await StoreAsync();
            await store.EnqueueAsync(Envelope("op-pending"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-done"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-done", CancellationToken.None);

            if (!_fixture.Available || !await _fixture.TryRestartContainerAsync())
            {
                Skip.If(true, "docker 不可用或容器重启超时——容器级重启恢复未验证");
            }

            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            var afterRestart = new MongoOutboxStore(database, 16, () => DateTime.UtcNow);
            var pending = await afterRestart.ListPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);                          // 待处理项恢复（仍可重试）
            Assert.Equal("op-pending", pending[0].OperationId);

            var redo = await afterRestart.EnqueueAsync(Envelope("op-done"), CancellationToken.None);
            var duplicate = Assert.IsType<OutboxEnqueueOutcome.Duplicate>(redo);
            Assert.Equal(OutboxStatus.Confirmed, duplicate.Existing.Status);   // 确认项不复活
        }
    }
}
