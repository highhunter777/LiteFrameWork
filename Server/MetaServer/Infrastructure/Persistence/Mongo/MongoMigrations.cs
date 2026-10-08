using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// schema 版本存储的真 Mongo 适配器（M0-c 批二；§9.1"索引与迁移显式版本化"）。
    /// 单文档（_id 固定 "meta"）承载当前版本；**版本写回 = 迁移确认点**——
    /// 任意一步后进程终止，重启从最后确认版本继续。
    /// </summary>
    public sealed class MongoSchemaVersionStore : ISchemaVersionStore
    {
        private readonly IMongoDatabase _database;

        public MongoSchemaVersionStore(IMongoDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public async ValueTask<long> ReadCurrentAsync(CancellationToken ct)
        {
            IMongoCollection<SchemaVersionDoc> versions =
                _database.GetCollection<SchemaVersionDoc>(MongoCollectionNames.SchemaVersion);

            SchemaVersionDoc doc = await versions
                .Find(d => d.Name == "meta")
                .FirstOrDefaultAsync(ct);
            return doc == null ? 0 : doc.Version;
        }

        public async ValueTask WriteAsync(long version, CancellationToken ct)
        {
            IMongoCollection<SchemaVersionDoc> versions =
                _database.GetCollection<SchemaVersionDoc>(MongoCollectionNames.SchemaVersion);

            // upsert：条件钉住单文档，避免多版本文档并存
            await versions.UpdateOneAsync(
                d => d.Name == "meta",
                Builders<SchemaVersionDoc>.Update.Set(d => d.Version, version),
                new UpdateOptions { IsUpsert = true },
                ct);
        }
    }

    /// <summary>
    /// MongoStep：以委托承载 Migrate/Rollback 的最小迁移步骤（版本由表序保证连续，由 Runner 校验）。
    /// </summary>
    public sealed class MongoStep : IMigrationStep
    {
        private readonly Func<CancellationToken, Task> _migrate;
        private readonly Func<CancellationToken, Task> _rollback;

        public MongoStep(long version, string description,
            Func<CancellationToken, Task> migrate, Func<CancellationToken, Task> rollback)
        {
            Version = version;
            Description = description ?? ("v" + version);
            _migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
            _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
        }

        public long Version { get; }

        public string Description { get; }

        public ValueTask MigrateAsync(CancellationToken ct) => new ValueTask(_migrate(ct));

        public ValueTask RollbackAsync(CancellationToken ct) => new ValueTask(_rollback(ct));
    }

    /// <summary>
    /// 真实迁移步骤表（v1 起，连续）。启动时由 <see cref="SchemaMigrationRunner"/> 按序应用；
    /// 每步可重复应用（版本确认点失败后重跑同一版本）——CreateCollection 已存在即跳过、
    /// CreateIndex 同名同键为幂等无操作。
    ///
    /// **回滚口径（如实登记）**：Migrate 的完全逆操作是 drop collection，但那会丢弃已写数据——
    /// 回滚取**结构回退**（移除索引），集合保留。回滚的目的是恢复"未迁移"的结构约束，
    /// 不是销毁数据。
    /// </summary>
    public static class MongoMigrations
    {
        /// <summary>目标版本＝步骤表末位。宿主启动迁移用此值（失败即拒绝启动，§9.1）。</summary>
        public const long LatestVersion = 3;

        public static IReadOnlyList<IMigrationStep> All(IMongoDatabase database)
        {
            return new IMigrationStep[]
            {
                new MongoStep(
                    version: 1,
                    description: "settlement_ledger 建集 + 业务键复合唯一索引（operationId 幂等由 _id 承载）",
                    migrate: ct => EnsureCollectionWithIndexAsync(
                        database,
                        MongoCollectionNames.SettlementLedger,
                        "ux_settlement_key",
                        Builders<LedgerDoc>.IndexKeys
                            .Ascending(d => d.PlayerId)
                            .Ascending(d => d.MatchId)
                            .Ascending(d => d.SettlementType),
                        unique: true,
                        ct),
                    rollback: ct => DropIndexIfExistsAsync<LedgerDoc>(
                        database, MongoCollectionNames.SettlementLedger, "ux_settlement_key", ct)),

                new MongoStep(
                    version: 2,
                    description: "outbox 建集 + 待处理扫描索引（status + seq）",
                    migrate: ct => EnsureCollectionWithIndexAsync(
                        database,
                        MongoCollectionNames.Outbox,
                        "ix_outbox_pending",
                        Builders<OutboxDoc>.IndexKeys
                            .Ascending(d => d.Status)
                            .Ascending(d => d.Seq),
                        unique: false,
                        ct),
                    rollback: ct => DropIndexIfExistsAsync<OutboxDoc>(
                        database, MongoCollectionNames.Outbox, "ix_outbox_pending", ct)),

                new MongoStep(
                    version: 3,
                    description: "accounts 建集 + deviceId 唯一索引（游客登录 get-or-create）",
                    migrate: ct => EnsureCollectionWithIndexAsync(
                        database,
                        MongoCollectionNames.Accounts,
                        "ux_account_device",
                        Builders<AccountDoc>.IndexKeys.Ascending(d => d.DeviceId),
                        unique: true,
                        ct),
                    rollback: ct => DropIndexIfExistsAsync<AccountDoc>(
                        database, MongoCollectionNames.Accounts, "ux_account_device", ct)),
            };
        }

        private static async Task EnsureCollectionWithIndexAsync<TDocument>(
            IMongoDatabase database,
            string collectionName,
            string indexName,
            IndexKeysDefinition<TDocument> keys,
            bool unique,
            CancellationToken ct)
        {
            if (!await CollectionExistsAsync(database, collectionName, ct))
            {
                await database.CreateCollectionAsync(collectionName, null, ct);
            }

            IMongoCollection<TDocument> collection = database.GetCollection<TDocument>(collectionName);
            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<TDocument>(keys,
                    new CreateIndexOptions { Name = indexName, Unique = unique }),
                null,
                ct);
        }

        private static async Task DropIndexIfExistsAsync<TDocument>(
            IMongoDatabase database, string collectionName, string indexName, CancellationToken ct)
        {
            // 回滚是尽力而为的结构回退：集合不存在即视为已回退；索引不存在按"已回退"处理，
            // 其余失败向上抛（Runner 会把回滚失败显式上报，不吞）
            if (!await CollectionExistsAsync(database, collectionName, ct))
            {
                return;
            }

            IMongoCollection<TDocument> collection = database.GetCollection<TDocument>(collectionName);
            try
            {
                await collection.Indexes.DropOneAsync(indexName, ct);
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexNotFound")
            {
                // 索引本就不存在：已处于回退态
            }
        }

        private static async Task<bool> CollectionExistsAsync(
            IMongoDatabase database, string collectionName, CancellationToken ct)
        {
            var options = new ListCollectionNamesOptions
            {
                Filter = Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("name", collectionName),
            };
            using IAsyncCursor<string> names = await database.ListCollectionNamesAsync(options, ct);
            return await names.AnyAsync(ct);
        }
    }
}
