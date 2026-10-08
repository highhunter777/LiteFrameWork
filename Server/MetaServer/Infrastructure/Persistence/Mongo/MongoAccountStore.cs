using System;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// 游客账号 Mongo 适配器（R3 Auth 首批）。唯一索引裁决 deviceId 的 get-or-create 竞态；
    /// 插入撞设备键返回 null，由用例重读并收敛到已存在账号。CreatedUtc 由适配器取存储侧时钟，
    /// 不接受调用方播种的时间。
    /// </summary>
    public sealed class MongoAccountStore : IAccountStore
    {
        private readonly IMongoDatabase _database;
        private readonly Func<DateTime> _utcNow;

        public MongoAccountStore(IMongoDatabase database, Func<DateTime> utcNow)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public async ValueTask<AccountRecord> FindByDeviceIdAsync(string deviceId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            AccountDoc doc = await _database.GetCollection<AccountDoc>(MongoCollectionNames.Accounts)
                .Find(d => d.DeviceId == deviceId)
                .FirstOrDefaultAsync(ct);
            return doc == null ? null : ToRecord(doc);
        }

        public async ValueTask<AccountRecord> CreateAsync(AccountRecord record, CancellationToken ct)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.AccountId))
                throw new ArgumentException("AccountId 不得为空", nameof(record));
            if (string.IsNullOrWhiteSpace(record.DeviceId))
                throw new ArgumentException("DeviceId 不得为空", nameof(record));

            var doc = new AccountDoc
            {
                AccountId = record.AccountId,
                DeviceId = record.DeviceId,
                CreatedUtc = _utcNow().ToUniversalTime(),
            };
            try
            {
                await _database.GetCollection<AccountDoc>(MongoCollectionNames.Accounts)
                    .InsertOneAsync(doc, cancellationToken: ct);
                return ToRecord(doc);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                // 设备唯一键撞入是并发 get-or-create 的正常分支；若是 accountId 撞键，
                // 先按设备重读，仍不存在则把异常交给上层作为未确认故障。
                AccountRecord existing = await FindByDeviceIdAsync(record.DeviceId, ct);
                if (existing != null) return null;
                throw;
            }
        }

        private static AccountRecord ToRecord(AccountDoc doc)
        {
            return new AccountRecord
            {
                AccountId = doc.AccountId,
                DeviceId = doc.DeviceId,
                CreatedUtc = DateTime.SpecifyKind(doc.CreatedUtc, DateTimeKind.Utc),
            };
        }
    }
}
