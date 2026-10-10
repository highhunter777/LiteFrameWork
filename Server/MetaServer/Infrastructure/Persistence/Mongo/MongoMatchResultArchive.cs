using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// 对局结果归档的真 Mongo 适配器（Meta 专项 §11.3 Match Archive；《上云测试》批A 落库面）。
    ///
    /// 与账本适配器同款纪律：
    /// - **唯一性是幂等的最终实现**：一局一条，<c>_id</c> = matchId——撞键即重复，读回首次快照原样返回；
    /// - <c>FinishedUtc</c> 由**存储端赋值**（注入时钟；调用方不播种）；
    /// - 存储不可达/提交失败抛 <see cref="SettlementStoreUnavailableException"/>（未确认语义，重试安全）。
    ///
    /// 查询（按账号，完成时刻倒序）走迁移 v4 建的 <c>Players.AccountId</c> 索引；
    /// 多值键 + 排序组合不做复合索引（封闭测试规模下按账号命中集在内存排序即可——
    /// 容量口径随 R4 容量测试再定，不在此虚构）。
    /// </summary>
    public sealed class MongoMatchResultArchive : IMatchResultArchive
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<MatchResultDoc> _collection;
        private readonly Func<DateTime> _utcNow;

        public MongoMatchResultArchive(IMongoDatabase database, Func<DateTime> utcNow)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _collection = database.GetCollection<MatchResultDoc>(MongoCollectionNames.MatchResults);
        }

        public async ValueTask<MatchResultStoreOutcome> StoreAsync(MatchResultArchiveEntry entry, CancellationToken ct)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            // 幂等快路径：已存在即原样返回首次快照（与账本"幂等判定先于写入"同序）
            MatchResultDoc existing = await _collection
                .Find(d => d.MatchId == entry.MatchId)
                .FirstOrDefaultAsync(ct);
            if (existing != null)
            {
                return new MatchResultStoreOutcome.Duplicate(ToRecord(existing));
            }

            var doc = new MatchResultDoc
            {
                MatchId = entry.MatchId,
                Seed = entry.Seed,
                FinalFrame = entry.FinalFrame,
                EndReason = entry.EndReason,
                GameplayEndReason = entry.GameplayEndReason,
                WinnerEntityId = entry.WinnerEntityId,
                FinishedUtc = _utcNow(),
                Players = ToDocuments(entry.Players),
            };

            try
            {
                await _collection.InsertOneAsync(doc, null, ct);
            }
            catch (MongoWriteException ex) when (IsDuplicateKey(ex))
            {
                // 并发同局提交：唯一索引是最终裁判——读回首次快照
                MatchResultDoc first = await _collection
                    .Find(d => d.MatchId == entry.MatchId)
                    .FirstOrDefaultAsync(ct);
                if (first != null)
                {
                    return new MatchResultStoreOutcome.Duplicate(ToRecord(first));
                }
                // 理论不可达（撞键却读不到）：按未确认处理，不吞
                throw new SettlementStoreUnavailableException(
                    "结果撞键后无法读回首次记录：" + entry.MatchId, ex);
            }
            catch (MongoException ex)
            {
                throw new SettlementStoreUnavailableException("对局结果落库失败：" + ex.Message, ex);
            }

            return new MatchResultStoreOutcome.Stored(ToRecord(doc));
        }

        public async ValueTask<IReadOnlyList<AccountMatchResult>> ListByAccountAsync(
            string accountId, int limit, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(accountId)) throw new ArgumentException("accountId 不能为空", nameof(accountId));
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit 必须为正");

            try
            {
                List<MatchResultDoc> docs = await _collection
                    .Find(Builders<MatchResultDoc>.Filter.Eq("Players.AccountId", accountId))
                    .Sort(Builders<MatchResultDoc>.Sort.Descending(d => d.FinishedUtc))
                    .Limit(limit)
                    .ToListAsync(ct);

                var results = new List<AccountMatchResult>(docs.Count);
                for (int i = 0; i < docs.Count; i++)
                {
                    MatchResultDoc doc = docs[i];
                    MatchResultPlayerDoc mine = FindPlayer(doc, accountId);
                    if (mine == null) continue;                    // 防御：过滤命中但条目缺失（不吐错行）
                    results.Add(new AccountMatchResult(doc.MatchId, mine.Kills, mine.Deaths,
                        doc.EndReason, doc.GameplayEndReason, doc.FinishedUtc));
                }
                return results;
            }
            catch (MongoException ex)
            {
                throw new SettlementStoreUnavailableException("对局结果查询失败：" + ex.Message, ex);
            }
        }

        private static MatchResultPlayerDoc FindPlayer(MatchResultDoc doc, string accountId)
        {
            if (doc.Players == null) return null;
            for (int i = 0; i < doc.Players.Length; i++)
            {
                if (string.Equals(doc.Players[i].AccountId, accountId, StringComparison.Ordinal))
                    return doc.Players[i];
            }
            return null;
        }

        private static MatchResultPlayerDoc[] ToDocuments(IReadOnlyList<MatchResultPlayerEntry> players)
        {
            var docs = new MatchResultPlayerDoc[players.Count];
            for (int i = 0; i < docs.Length; i++)
            {
                MatchResultPlayerEntry p = players[i];
                docs[i] = new MatchResultPlayerDoc
                {
                    PlayerId = p.PlayerId,
                    AccountId = p.AccountId,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                };
            }
            return docs;
        }

        private static StoredMatchResult ToRecord(MatchResultDoc doc)
        {
            return new StoredMatchResult(doc.MatchId, doc.FinishedUtc);
        }

        private static bool IsDuplicateKey(MongoWriteException ex)
        {
            return ex.WriteError != null
                   && ex.WriteError.Category == ServerErrorCategory.DuplicateKey;
        }
    }
}
