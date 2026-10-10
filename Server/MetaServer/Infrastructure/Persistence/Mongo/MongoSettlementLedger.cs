using System;
using System.Threading;
using System.Threading.Tasks;
using MetaServer.Contracts.Persistence;
using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// 结算账本的真 Mongo 适配器（M0-c 批二；Meta 专项 §9.1"唯一索引是幂等的最终实现"、
    /// §11.3"事务内写唯一 Ledger + 更新库存/进度"、§8.1"副本集模式；事务只围绕必须原子化的更新"）。
    ///
    /// 与 L1 替身的语义对应关系：
    /// - 幂等双维度：_id（operationId）＋业务键复合唯一索引（迁移 v1 建）——存储端唯一性兜底；
    /// - 原子边界：账目写入与 revision CAS 推进在同一副本集事务内提交，CAS 冲突/账目撞键则整笔回滚；
    /// - 未确认：存储异常/提交冲突抛 <see cref="SettlementStoreUnavailableException"/>——重试安全
    ///   （可能已应用也可能没有，唯一索引保证重试命中同一条账目）。
    ///
    /// 事务内并发冲突（同一玩家文档 write conflict）不在适配器内自动重试：以未确认抛出、由调用方
    /// 重试——驱动 3.x 的可重试写不覆盖多语句事务的提交冲突。
    /// </summary>
    public sealed class MongoSettlementLedger : ISettlementLedger
    {
        /// <summary>追加模式哨兵（<see cref="SettlementWrite.ExpectedRevision"/> = -1：不做 CAS 期望比对）。</summary>
        public const long AppendMode = -1L;

        private readonly IMongoDatabase _database;

        public MongoSettlementLedger(IMongoDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public async ValueTask<SettlementOutcome> ApplyAsync(SettlementWrite write, CancellationToken ct)
        {
            IMongoCollection<LedgerDoc> ledger =
                _database.GetCollection<LedgerDoc>(MongoCollectionNames.SettlementLedger);
            IMongoCollection<ProfileRevisionDoc> revision =
                _database.GetCollection<ProfileRevisionDoc>(MongoCollectionNames.ProfileRevision);

            // 幂等快路径（无事务）：契约优先序——幂等判定先于 CAS
            LedgerDoc byOperation = await ledger
                .Find(d => d.OperationId == write.OperationId)
                .FirstOrDefaultAsync(ct);
            if (byOperation != null)
            {
                return new SettlementOutcome.Duplicate(ToRecord(byOperation));
            }

            LedgerDoc byKey = await ledger
                .Find(d => d.PlayerId == write.Key.PlayerId
                           && d.MatchId == write.Key.MatchId
                           && d.SettlementType == write.Key.SettlementType)
                .FirstOrDefaultAsync(ct);
            if (byKey != null)
            {
                return new SettlementOutcome.Duplicate(ToRecord(byKey));
            }

            // 原子边界：副本集事务
            IClientSessionHandle session = await _database.Client.StartSessionAsync(cancellationToken: ct);
            try
            {
                session.StartTransaction();

                ProfileRevisionDoc current = await revision
                    .Find(session, d => d.PlayerId == write.Key.PlayerId)
                    .FirstOrDefaultAsync(ct);

                long appliedRevision;
                long balanceAfter;
                if (current == null)
                {
                    if (write.ExpectedRevision != 0 && write.ExpectedRevision != AppendMode)
                    {
                        // 账目无残留（事务回滚）：文档不存在即实际修订号为 0
                        await AbortQuietlyAsync(session, ct);
                        return new SettlementOutcome.RevisionConflict(write.ExpectedRevision, 0);
                    }

                    appliedRevision = 1;
                    balanceAfter = write.Delta;
                    await revision.InsertOneAsync(
                        session,
                        new ProfileRevisionDoc { PlayerId = write.Key.PlayerId, Revision = 1, Balance = write.Delta },
                        null,
                        ct);
                }
                else if (write.ExpectedRevision != AppendMode && current.Revision != write.ExpectedRevision)
                {
                    await AbortQuietlyAsync(session, ct);
                    return new SettlementOutcome.RevisionConflict(write.ExpectedRevision, current.Revision);
                }
                else
                {
                    // 条件推进：CAS 模式 filter 钉住期望修订号，并发下匹配不到即 CAS 失败；
                    // 追加模式（-1）不做期望比对——修订号只增不比，幂等由账目唯一索引承载。
                    // 事务快照隔离下 ModifiedCount==1 即文档确在期望修订号上（FindOneAndUpdate
                    // 的表达式重载在驱动 3.x 有二义性，UpdateOneAsync 无此问题且语义等价）。
                    FilterDefinition<ProfileRevisionDoc> advanceFilter = write.ExpectedRevision == AppendMode
                        ? Builders<ProfileRevisionDoc>.Filter.Eq(d => d.PlayerId, write.Key.PlayerId)
                        : Builders<ProfileRevisionDoc>.Filter.Where(
                            d => d.PlayerId == write.Key.PlayerId && d.Revision == write.ExpectedRevision);
                    UpdateResult advanced = await revision.UpdateOneAsync(
                        session,
                        advanceFilter,
                        Builders<ProfileRevisionDoc>.Update
                            .Inc(d => d.Revision, 1)
                            .Inc(d => d.Balance, write.Delta),
                        null,
                        ct);

                    if (advanced.ModifiedCount == 0)
                    {
                        await AbortQuietlyAsync(session, ct);
                        return new SettlementOutcome.RevisionConflict(write.ExpectedRevision, current.Revision);
                    }

                    appliedRevision = current.Revision + 1;
                    balanceAfter = current.Balance + write.Delta;
                }

                var record = new SettlementRecord(
                    write.OperationId, write.Key, appliedRevision, balanceAfter);

                try
                {
                    await ledger.InsertOneAsync(session, ToDocument(record), null, ct);
                }
                catch (MongoWriteException ex) when (IsDuplicateKey(ex))
                {
                    // 并发同笔落账：唯一索引是最终裁判——整笔回滚后返回首次的账目
                    await AbortQuietlyAsync(session, ct);
                    LedgerDoc first = await ResolveFirstAsync(ledger, write, ct);
                    if (first != null)
                    {
                        return new SettlementOutcome.Duplicate(ToRecord(first));
                    }
                    // 理论不可达（撞键却读不到）：按未确认处理，不吞
                    throw new SettlementStoreUnavailableException("账目撞键后无法读回首次记录：" + write.OperationId, ex);
                }

                await session.CommitTransactionAsync(ct);
                return new SettlementOutcome.FirstApplied(record);
            }
            catch (SettlementStoreUnavailableException)
            {
                await AbortQuietlyAsync(session, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                await AbortQuietlyAsync(session, CancellationToken.None);
                throw new SettlementStoreUnavailableException("存储事务失败：" + ex.Message, ex);
            }
            finally
            {
                session.Dispose();
            }
        }

        private static async Task<LedgerDoc> ResolveFirstAsync(
            IMongoCollection<LedgerDoc> ledger, SettlementWrite write, CancellationToken ct)
        {
            LedgerDoc first = await ledger
                .Find(d => d.OperationId == write.OperationId)
                .FirstOrDefaultAsync(ct);
            if (first != null)
            {
                return first;
            }

            return await ledger
                .Find(d => d.PlayerId == write.Key.PlayerId
                           && d.MatchId == write.Key.MatchId
                           && d.SettlementType == write.Key.SettlementType)
                .FirstOrDefaultAsync(ct);
        }

        private static bool IsDuplicateKey(MongoWriteException ex)
        {
            return ex.WriteError != null
                   && ex.WriteError.Category == ServerErrorCategory.DuplicateKey;
        }

        private static async Task AbortQuietlyAsync(IClientSessionHandle session, CancellationToken ct)
        {
            try
            {
                if (session.IsInTransaction)
                {
                    await session.AbortTransactionAsync(ct);
                }
            }
            catch
            {
                // 回滚失败不掩盖主结果：冲突/异常路径上事务已在服务端终止
            }
        }

        private static LedgerDoc ToDocument(SettlementRecord record)
        {
            return new LedgerDoc
            {
                OperationId = record.OperationId,
                PlayerId = record.Key.PlayerId,
                MatchId = record.Key.MatchId,
                SettlementType = record.Key.SettlementType,
                AppliedRevision = record.AppliedRevision,
                BalanceAfter = record.BalanceAfter,
            };
        }

        private static SettlementRecord ToRecord(LedgerDoc doc)
        {
            return new SettlementRecord(
                doc.OperationId,
                new SettlementKey(doc.PlayerId, doc.MatchId, doc.SettlementType),
                doc.AppliedRevision,
                doc.BalanceAfter);
        }
    }
}
