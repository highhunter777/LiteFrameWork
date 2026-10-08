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
    /// 真实迁移 L3（Meta 专项 §9.1"索引与迁移显式版本化；迁移失败有明确结果与回滚方式，
    /// 不静默半迁移"、§14 必备故障矩阵"迁移失败"）。
    /// </summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MongoMigrationL3Tests
    {
        private readonly MongoFixture _fixture;
        private readonly string _database;

        public MongoMigrationL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
            _database = fixture.CreateDatabaseName();   // 用例级隔离
        }

        private IMongoDatabase Database()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
            return _fixture.CreateClient().GetDatabase(_database);
        }

        private static SchemaMigrationRunner Runner(IMongoDatabase database)
        {
            return new SchemaMigrationRunner(
                MongoMigrations.All(database),
                new MongoSchemaVersionStore(database));
        }

        [SkippableFact]
        public async Task 全新库_迁移到最新_版本写回_索引就位()
        {
            IMongoDatabase database = Database();

            MigrationOutcome outcome = await Runner(database)
                .MigrateAsync(MongoMigrations.LatestVersion, CancellationToken.None);

            var completed = Assert.IsType<MigrationOutcome.Completed>(outcome);
            Assert.Equal(new long[] { 1, 2, 3 }, completed.AppliedVersions);

            // 真实结构事实：唯一索引由迁移创建（集合自动建不算证据）
            using IAsyncCursor<MongoDB.Bson.BsonDocument> indexes =
                await database.GetCollection<MongoDB.Bson.BsonDocument>(
                    MongoCollectionNames.SettlementLedger).Indexes.ListAsync();
            var names = new System.Collections.Generic.List<string>();
            while (await indexes.MoveNextAsync())
            {
                foreach (MongoDB.Bson.BsonDocument index in indexes.Current)
                {
                    names.Add(index["name"].AsString);
                }
            }
            Assert.Contains("ux_settlement_key", names);

            using IAsyncCursor<MongoDB.Bson.BsonDocument> accountIndexes =
                await database.GetCollection<MongoDB.Bson.BsonDocument>(
                    MongoCollectionNames.Accounts).Indexes.ListAsync();
            var accountNames = new System.Collections.Generic.List<string>();
            while (await accountIndexes.MoveNextAsync())
            {
                foreach (MongoDB.Bson.BsonDocument index in accountIndexes.Current)
                    accountNames.Add(index["name"].AsString);
            }
            Assert.Contains("ux_account_device", accountNames);
        }

        [SkippableFact]
        public async Task 重复迁移_UpToDate_索引不重建()
        {
            IMongoDatabase database = Database();
            await Runner(database).MigrateAsync(MongoMigrations.LatestVersion, CancellationToken.None);

            MigrationOutcome again = await Runner(database)
                .MigrateAsync(MongoMigrations.LatestVersion, CancellationToken.None);

            Assert.IsType<MigrationOutcome.UpToDate>(again);
        }

        [SkippableFact]
        public async Task 客户端重建后_版本持久()
        {
            IMongoDatabase database = Database();
            await Runner(database).MigrateAsync(MongoMigrations.LatestVersion, CancellationToken.None);

            IMongoDatabase rebuilt = _fixture.CreateClient().GetDatabase(_database);
            long version = await new MongoSchemaVersionStore(rebuilt).ReadCurrentAsync(CancellationToken.None);

            Assert.Equal(MongoMigrations.LatestVersion, version);
        }

        [SkippableFact]
        public async Task 中途失败_真实回滚_索引移除_版本归零()
        {
            IMongoDatabase database = Database();

            // v1 用真实步骤（建集合+唯一索引）；v2 注入失败——真实回滚路径
            var steps = new IMigrationStep[]
            {
                MongoMigrations.All(database)[0],
                new ThrowingMigrationStep(2),
            };
            var runner = new SchemaMigrationRunner(steps, new MongoSchemaVersionStore(database));

            MigrationOutcome outcome = await runner.MigrateAsync(2, CancellationToken.None);

            var failed = Assert.IsType<MigrationOutcome.Failed>(outcome);
            Assert.Equal(2, failed.FailedAtVersion);
            Assert.False(failed.RollbackFailed);
            Assert.Equal(0, failed.RecoveredVersion);

            // 结构回退事实：v1 建的唯一索引被回滚移除；版本归零
            using IAsyncCursor<MongoDB.Bson.BsonDocument> indexes =
                await database.GetCollection<MongoDB.Bson.BsonDocument>(
                    MongoCollectionNames.SettlementLedger).Indexes.ListAsync();
            var names = new System.Collections.Generic.List<string>();
            while (await indexes.MoveNextAsync())
            {
                foreach (MongoDB.Bson.BsonDocument index in indexes.Current)
                {
                    names.Add(index["name"].AsString);
                }
            }
            Assert.DoesNotContain("ux_settlement_key", names);

            long version = await new MongoSchemaVersionStore(database)
                .ReadCurrentAsync(CancellationToken.None);
            Assert.Equal(0, version);
        }

        /// <summary>v2 失败注入（Migrate 抛错、Rollback 正常——隔离"回滚也失败"的变量）。</summary>
        private sealed class ThrowingMigrationStep : IMigrationStep
        {
            public ThrowingMigrationStep(long version)
            {
                Version = version;
            }

            public long Version { get; }

            public string Description => "注入失败的 v" + Version;

            public ValueTask MigrateAsync(CancellationToken ct) =>
                throw new InvalidOperationException("注入：v" + Version + " 迁移失败");

            public ValueTask RollbackAsync(CancellationToken ct) => default;
        }
    }
}
