using System.Threading;
using System.Threading.Tasks;
using LiteTesting;
using MetaServer.Contracts.Persistence;
using Xunit;

namespace MetaServer.Tests
{
    /// <summary>
    /// 持久 Outbox 端语契约（Meta 专项 §10 优雅关闭第 3 步"刷新 Outbox 到持久介质；未提交项
    /// 保持可重试状态"、§11.3、§14 L3 行"进程重启后 Outbox 恢复"的 L1 语义面）。
    ///
    /// §11.2"任何队列、缓存、票据窗口与重试缓冲都必须有显式容量与清理策略"——
    /// 有界与显式拒绝在此验证；**介质持久性本身归 L3 真 Mongo 容器验证**。
    /// </summary>
    public sealed class OutboxPersistenceTests
    {
        private static OutboxEnvelope Envelope(string operationId, string payload = "payload")
        {
            return new OutboxEnvelope(operationId, payload, OutboxStatus.Pending, 0);
        }

        private static FakeOutboxStore Store(FakeOutboxState state)
        {
            return new FakeOutboxStore(state);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 入队_待处理可按序列出()
        {
            var state = new FakeOutboxState { Capacity = 8 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1", "a"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2", "b"), CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);

            Assert.Equal(2, pending.Count);
            Assert.Equal("op-1", pending[0].OperationId);   // 入队次序
            Assert.Equal("op-2", pending[1].OperationId);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 重复入队_幂等返回已有条目_不重复投递()
        {
            var state = new FakeOutboxState { Capacity = 8 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1", "first"), CancellationToken.None);

            var second = await store.EnqueueAsync(Envelope("op-1", "second"), CancellationToken.None);

            var duplicate = Assert.IsType<OutboxEnqueueOutcome.Duplicate>(second);
            Assert.Equal("first", duplicate.Existing.Payload);   // 已存在条目原样返回
            Assert.Single(state.Items);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 容量满_显式拒绝()
        {
            var state = new FakeOutboxState { Capacity = 2 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2"), CancellationToken.None);

            var third = await store.EnqueueAsync(Envelope("op-3"), CancellationToken.None);

            var rejected = Assert.IsType<OutboxEnqueueOutcome.RejectedFull>(third);
            Assert.Equal(2, rejected.Capacity);   // 拒绝必须携带容量事实
            Assert.Equal(2, state.Items.Count);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 确认后_不再出现在待处理列表()
        {
            var state = new FakeOutboxState { Capacity = 8 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.EnqueueAsync(Envelope("op-2"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-1", CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);
            Assert.Equal("op-2", pending[0].OperationId);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 已确认条目_重复入队_仍返回Duplicate_不复活()
        {
            var state = new FakeOutboxState { Capacity = 8 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await store.MarkConfirmedAsync("op-1", CancellationToken.None);

            var again = await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);

            var duplicate = Assert.IsType<OutboxEnqueueOutcome.Duplicate>(again);
            Assert.Equal(OutboxStatus.Confirmed, duplicate.Existing.Status);   // 不降级回 Pending
            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Empty(pending);   // 已确认条目不得重新投递
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 失败记账_保持可重试_计数累加()
        {
            var state = new FakeOutboxState { Capacity = 8 };
            var store = Store(state);
            await store.EnqueueAsync(Envelope("op-1"), CancellationToken.None);

            await store.RecordFailureAsync("op-1", "下游 503", CancellationToken.None);
            await store.RecordFailureAsync("op-1", "下游 503", CancellationToken.None);

            var pending = await store.ListPendingAsync(10, CancellationToken.None);
            Assert.Single(pending);
            Assert.Equal(OutboxStatus.Pending, pending[0].Status);   // 未提交项保持可重试
            Assert.Equal(2, pending[0].Attempts);
        }

        [Fact]
        [Trait(TestTrait.Category, TestCategory.Contract)]
        public async Task 实例重建后_待处理仍可列出_确认项不复活()
        {
            // §14"进程重启后 Outbox 恢复"的 L1 语义面：介质比进程活得久——同一状态上重建实例，
            // Pending 仍在、Confirmed 不复活。**介质持久性本身归 L3 真 Mongo 容器验证。**
            var state = new FakeOutboxState { Capacity = 8 };
            var first = Store(state);
            await first.EnqueueAsync(Envelope("op-1"), CancellationToken.None);
            await first.EnqueueAsync(Envelope("op-2"), CancellationToken.None);
            await first.MarkConfirmedAsync("op-1", CancellationToken.None);

            var restarted = Store(state);   // "进程重启"：新实例、同一介质状态
            var pending = await restarted.ListPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);
            Assert.Equal("op-2", pending[0].OperationId);
        }
    }
}
