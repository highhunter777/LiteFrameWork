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

        /// <summary>schema 版本文档（_id 固定，见 <see cref="MongoDocumentNames.SchemaVersion"/>；迁移确认点）。</summary>
        public const string SchemaVersion = "schema_version";

        /// <summary>账号文档（_id = accountId；deviceId 唯一索引见迁移 v3）。</summary>
        public const string Accounts = "accounts";

        /// <summary>对局结果归档（_id = matchId——一局一条，幂等由 _id 承载；账号查询索引见迁移 v4）。</summary>
        public const string MatchResults = "match_results";
    }

    /// <summary>
    /// 固定 _id 文档名单源（单文档集合）：适配器与迁移共用——查询/upsert 里散字面量必然漂移，
    /// 与集合名单源同一条纪律。
    /// </summary>
    public static class MongoDocumentNames
    {
        /// <summary>schema 版本文档（<c>schema_version</c> 集合内；迁移确认点）。</summary>
        public const string SchemaVersion = "meta";
    }

    // ===================================================================== 文档形状（仅适配器与 L3 断言使用。）。

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

    /// <summary>
    /// 对局结果归档文档（一局一条；<c>_id</c> = matchId 即幂等裁判）。
    /// <c>FinishedUtc</c> 由存储适配器赋值（服务端时钟），调用方不播种。
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class MatchResultDoc
    {
        [BsonId]
        public string MatchId;

        public long Seed;
        public int FinalFrame;
        public int EndReason;
        public int GameplayEndReason;
        public long WinnerEntityId;

        /// <summary>首次落库时刻（UTC——§5.2"时间字段一律 UTC"）。</summary>
        public DateTime FinishedUtc;

        public MatchResultPlayerDoc[] Players;
    }

    /// <summary>归档文档的单玩家条目（账号映射随提交载荷——裁决点①）。</summary>
    [BsonIgnoreExtraElements]
    public sealed class MatchResultPlayerDoc
    {
        public string PlayerId;
        public string AccountId;
        public int Kills;
        public int Deaths;
    }
}
