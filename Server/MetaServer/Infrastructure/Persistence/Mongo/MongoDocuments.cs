using System;
using MongoDB.Bson.Serialization.Attributes;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// Mongo 集合名单源（M0-c 批二）。改集合名必须改这里——迁移步骤、适配器与 L3 断言共用此表，
    /// 分散两份必然漂移（与"协议单源"同一条纪律）。
    /// </summary>
    public static class MongoCollectionNames
    {
        /// <summary>结算账本（_id = operationId；业务键复合唯一索引见迁移 v1）。</summary>
        public const string SettlementLedger = "settlement_ledger";

        /// <summary>玩家修订号/余额文档（_id = playerId；CAS 目标）。</summary>
        public const string ProfileRevision = "profile_revision";

        /// <summary>持久 Outbox 条目（_id = operationId）。</summary>
        public const string Outbox = "outbox";

        /// <summary>Outbox 单调序号计数文档（_id 固定 "outbox"；ListPending 的次序承载）。</summary>
        public const string OutboxSeq = "outbox_seq";

        /// <summary>schema 版本文档（_id 固定 "meta"；迁移确认点）。</summary>
        public const string SchemaVersion = "schema_version";

        /// <summary>账号文档（_id = accountId；deviceId 唯一索引见迁移 v3）。</summary>
        public const string Accounts = "accounts";
    }

    // =====================================================================
    // 文档形状（仅适配器与 L3 断言使用；契约层零 Mongo 依赖——L0 方向：
    // Contracts 不得引用 Infrastructure）。字段公开形态与 MetaConfig 属性形态
    // 是两回事：这里不参与 ConfigurationBinder。
    // =====================================================================

    /// <summary>结算账目文档。幂等两维度：_id（operationId）＋业务键复合唯一索引。</summary>
    [BsonIgnoreExtraElements]
    public sealed class LedgerDoc
    {
        [BsonId]
        public string OperationId;

        public string PlayerId;
        public string MatchId;
        public string SettlementType;
        public long AppliedRevision;
        public long BalanceAfter;
    }

    /// <summary>玩家修订号/余额文档（revision CAS 的目标；_id = playerId）。</summary>
    [BsonIgnoreExtraElements]
    public sealed class ProfileRevisionDoc
    {
        [BsonId]
        public string PlayerId;

        public long Revision;
        public long Balance;
    }

    /// <summary>持久 Outbox 条目。Status 存 <see cref="Contracts.Persistence.OutboxStatus"/> 的 int 值。</summary>
    [BsonIgnoreExtraElements]
    public sealed class OutboxDoc
    {
        [BsonId]
        public string OperationId;

        public string Payload;
        public int Status;
        public int Attempts;

        /// <summary>入队次序（OutboxSeq 原子自增分配；$natural 无排序保证，不使用）。</summary>
        public long Seq;

        /// <summary>入队时刻（UTC，§5.2"时间字段一律 UTC"）——存储端权威赋值，非调用方播种。</summary>
        public DateTime CreatedUtc;

        /// <summary>最近一次失败原因（RecordFailure 记录；重试根因可诊断）。</summary>
        public string LastFailureReason;

        /// <summary>最近一次失败时刻（UTC）。</summary>
        public DateTime LastFailureUtc;
    }

    /// <summary>Outbox 序号计数文档（单文档自增）。</summary>
    [BsonIgnoreExtraElements]
    public sealed class OutboxSeqDoc
    {
        [BsonId]
        public string Name;

        public long Value;
    }

    /// <summary>schema 当前版本文档（单文档）。</summary>
    [BsonIgnoreExtraElements]
    public sealed class SchemaVersionDoc
    {
        [BsonId]
        public string Name;

        public long Version;
    }

    /// <summary>游客账号文档。CreatedUtc 由存储适配器赋值，调用方只提供业务身份。</summary>
    [BsonIgnoreExtraElements]
    public sealed class AccountDoc
    {
        [BsonId]
        public string AccountId;

        public string DeviceId;
        public DateTime CreatedUtc;
    }
}
