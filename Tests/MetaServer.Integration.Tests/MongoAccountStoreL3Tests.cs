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
    /// <summary>游客账号真存储 L3：deviceId 唯一键、重建与容器重启后的 get-or-create 事实。</summary>
    [Collection("Mongo")]
    [Trait(TestTrait.Category, TestCategory.Integration)]
    public sealed class MongoAccountStoreL3Tests
    {
        private readonly MongoFixture _fixture;
        private readonly string _database;

        public MongoAccountStoreL3Tests(MongoFixture fixture)
        {
            _fixture = fixture;
            _database = fixture.CreateDatabaseName();
        }

        private async Task<MongoAccountStore> StoreAsync()
        {
            Skip.If(!_fixture.Available, "Mongo 不可达且 docker 不可用（MONGO_TEST_URI / litegame-mongo 容器）");
            await _fixture.MigrateToLatestAsync(_database);
            return new MongoAccountStore(
                _fixture.CreateClient().GetDatabase(_database),
                () => new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc));
        }

        private static AccountRecord Record(string accountId = "a00112233445566778899", string deviceId = "device-a")
            => new AccountRecord { AccountId = accountId, DeviceId = deviceId, CreatedUtc = DateTime.MinValue };

        [SkippableFact]
        public async Task 首次创建_存储端赋时间_按设备可查()
        {
            MongoAccountStore store = await StoreAsync();

            AccountRecord created = await store.CreateAsync(Record(), CancellationToken.None);
            AccountRecord loaded = await store.FindByDeviceIdAsync("device-a", CancellationToken.None);

            Assert.Equal("a00112233445566778899", created.AccountId);
            Assert.Equal(new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc), created.CreatedUtc);
            Assert.Equal(created.AccountId, loaded.AccountId);
            Assert.Equal(created.CreatedUtc, loaded.CreatedUtc);
        }

        [SkippableFact]
        public async Task 重复设备_唯一索引返回null_重读得到首次账号()
        {
            MongoAccountStore store = await StoreAsync();
            await store.CreateAsync(Record(), CancellationToken.None);

            AccountRecord duplicate = await store.CreateAsync(
                Record(accountId: "a99999999999999999999"), CancellationToken.None);
            AccountRecord loaded = await store.FindByDeviceIdAsync("device-a", CancellationToken.None);

            Assert.Null(duplicate);
            Assert.Equal("a00112233445566778899", loaded.AccountId);
        }

        [SkippableFact]
        public async Task 客户端重建后_同设备仍命中首次账号()
        {
            MongoAccountStore first = await StoreAsync();
            await first.CreateAsync(Record(), CancellationToken.None);

            IMongoDatabase database = _fixture.CreateClient().GetDatabase(_database);
            var rebuilt = new MongoAccountStore(
                database, () => new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc));
            AccountRecord loaded = await rebuilt.FindByDeviceIdAsync("device-a", CancellationToken.None);

            Assert.Equal("a00112233445566778899", loaded.AccountId);
        }

        [SkippableFact]
        public async Task 容器重启后_同设备仍命中首次账号()
        {
            MongoAccountStore store = await StoreAsync();
            await store.CreateAsync(Record(), CancellationToken.None);
            if (!await _fixture.TryRestartContainerAsync())
                Skip.If(true, "docker 不可用或容器重启超时——容器级重启恢复未验证");

            var afterRestart = new MongoAccountStore(
                _fixture.CreateClient().GetDatabase(_database),
                () => new DateTime(2026, 10, 8, 1, 2, 3, DateTimeKind.Utc));
            AccountRecord loaded = await afterRestart.FindByDeviceIdAsync("device-a", CancellationToken.None);

            Assert.Equal("a00112233445566778899", loaded.AccountId);
        }
    }
}
