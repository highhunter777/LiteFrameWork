using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// 持久 Outbox 的真 Mongo 适配器（M0-c 批二；§10"刷新 Outbox 到持久介质；未提交项保持可重试状态"、
    /// §11.2"任何队列必须有显式容量与清理策略"）。
    ///
    /// - 入队幂等：_id（operationId）唯一索引最终裁判；快路径先查、撞键回读；
    /// - 有界容量：入队前 CountDocuments 对界——并发窗口内可短暂越界（检查与插入非原子），
    ///   容量是显式承诺不是硬不变量；超出即 <see cref="OutboxEnqueueOutcome.RejectedFull"/>；
    /// - 次序：入队时经 OutboxSeq 单文档原子自增取 Seq——$natural 无排序保证，不用；
    /// - 确认/失败：条件更新（已 Confirmed 不回退；失败只对 Pending 累加 Attempts 并落原因/时刻）；
    /// - **元数据权威**：Status/Attempts/CreatedUtc 由存储端赋值，调用方传入值一律忽略（防播种）；
    ///   时刻经构造函数注入的时钟取——纯化层不直读墙钟。
    /// </summary>
    public sealed class MongoOutboxStore : IOutboxStore
    {
        private readonly IMongoDatabase _database;
        private readonly int _capacity;
        private readonly Func<DateTime> _utcNow;

        public MongoOutboxStore(IMongoDatabase database, int capacity, Func<DateTime> utcNow)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Outbox 容量必须为正");
            }

            _database = database ?? throw new ArgumentNullException(nameof(database));
            _capacity = capacity;
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public async ValueTask<OutboxEnqueueOutcome> EnqueueAsync(OutboxEnvelope envelope, CancellationToken ct)
        {
            IMongoCollection<OutboxDoc> outbox =
                _database.GetCollection<OutboxDoc>(MongoCollectionNames.Outbox);
            IMongoCollection<OutboxSeqDoc> seq =
                _database.GetCollection<OutboxSeqDoc>(MongoCollectionNames.OutboxSeq);

            OutboxDoc existing = await outbox
                .Find(d => d.OperationId == envelope.OperationId)
                .FirstOrDefaultAsync(ct);
            if (existing != null)
            {
                return new OutboxEnqueueOutcome.Duplicate(ToEnvelope(existing));
            }

            long count = await outbox.CountDocumentsAsync(
                Builders<OutboxDoc>.Filter.Empty, null, ct);
            if (count >= _capacity)
            {
                return new OutboxEnqueueOutcome.RejectedFull(_capacity);
            }

            // 单调序号：FindOneAndUpdate 原子自增（upsert 首次创建）。
            // 静态调用显式类型参数——该扩展的表达式重载在驱动 3.x 有二义性。
            OutboxSeqDoc counter = await IMongoCollectionExtensions
                .FindOneAndUpdateAsync<OutboxSeqDoc, OutboxSeqDoc>(
                    seq,
                    d => d.Name == "outbox",
                    Builders<OutboxSeqDoc>.Update.Inc(d => d.Value, 1),
                    new FindOneAndUpdateOptions<OutboxSeqDoc, OutboxSeqDoc>
                    {
                        IsUpsert = true,
                        ReturnDocument = ReturnDocument.After,
                    },
                    ct);

            var doc = new OutboxDoc
            {
                OperationId = envelope.OperationId,
                Payload = envelope.Payload,
                // 存储端权威：Status/Attempts/CreatedUtc 不取调用方值（防播种）；时刻取注入时钟
                Status = (int)OutboxStatus.Pending,
                Attempts = 0,
                CreatedUtc = _utcNow(),
                Seq = counter.Value,
            };

            try
            {
                await outbox.InsertOneAsync(doc, null, ct);
            }
            catch (MongoWriteException ex) when (IsDuplicateKey(ex))
            {
                // 并发重复入队：唯一 _id 是最终裁判，返回已存在条目
                OutboxDoc raced = await outbox
                    .Find(d => d.OperationId == envelope.OperationId)
                    .FirstOrDefaultAsync(ct);
                return new OutboxEnqueueOutcome.Duplicate(ToEnvelope(raced));
            }

            return new OutboxEnqueueOutcome.Enqueued(ToEnvelope(doc));
        }

        public async ValueTask<IReadOnlyList<OutboxEnvelope>> ListPendingAsync(int max, CancellationToken ct)
        {
            IMongoCollection<OutboxDoc> outbox =
                _database.GetCollection<OutboxDoc>(MongoCollectionNames.Outbox);

            List<OutboxDoc> pending = await outbox
                .Find(d => d.Status == (int)OutboxStatus.Pending)
                .SortBy(d => d.Seq)
                .Limit(max)
                .ToListAsync(ct);

            var result = new List<OutboxEnvelope>(pending.Count);
            foreach (OutboxDoc doc in pending)
            {
                result.Add(ToEnvelope(doc));
            }
            return result;
        }

        public async ValueTask MarkConfirmedAsync(string operationId, CancellationToken ct)
        {
            IMongoCollection<OutboxDoc> outbox =
                _database.GetCollection<OutboxDoc>(MongoCollectionNames.Outbox);

            // 条件更新：只把 Pending 置 Confirmed——已确认条目幂等不回退；不存在的条目无操作
            await outbox.UpdateOneAsync(
                d => d.OperationId == operationId && d.Status == (int)OutboxStatus.Pending,
                Builders<OutboxDoc>.Update.Set(d => d.Status, (int)OutboxStatus.Confirmed),
                null,
                ct);
        }

        public async ValueTask RecordFailureAsync(string operationId, string reason, CancellationToken ct)
        {
            IMongoCollection<OutboxDoc> outbox =
                _database.GetCollection<OutboxDoc>(MongoCollectionNames.Outbox);

            // 失败记账：**只对 Pending 生效**（已 Confirmed 幂等不累加、未知条目无操作）；
            // 状态保持 Pending（可重试），只累加计数并落原因/时刻——§10"未提交项保持可重试状态"
            await outbox.UpdateOneAsync(
                d => d.OperationId == operationId && d.Status == (int)OutboxStatus.Pending,
                Builders<OutboxDoc>.Update.Combine(
                    Builders<OutboxDoc>.Update.Inc(d => d.Attempts, 1),
                    Builders<OutboxDoc>.Update.Set(d => d.LastFailureReason, reason),
                    Builders<OutboxDoc>.Update.Set(d => d.LastFailureUtc, _utcNow())),
                null,
                ct);
        }

        private static bool IsDuplicateKey(MongoWriteException ex)
        {
            return ex.WriteError != null
                   && ex.WriteError.Category == ServerErrorCategory.DuplicateKey;
        }

        private static OutboxEnvelope ToEnvelope(OutboxDoc doc)
        {
            return new OutboxEnvelope(
                doc.OperationId, doc.Payload, (OutboxStatus)doc.Status, doc.Attempts,
                doc.CreatedUtc, doc.LastFailureReason);
        }
    }
}
