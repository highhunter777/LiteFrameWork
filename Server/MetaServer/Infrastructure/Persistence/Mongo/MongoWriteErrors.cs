using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// Mongo 写错误的**唯一判据**（P2 单源）：幂等链（账本/归档/Outbox）都靠"唯一索引撞键 → 读回首次" 收口。
    /// </summary>
    public static class MongoWriteErrors
    {
        /// <summary>唯一索引撞键（并发同笔提交/重复入队的最终裁判路径）。</summary>
        public static bool IsDuplicateKey(MongoWriteException ex)
        {
            return ex?.WriteError != null
                   && ex.WriteError.Category == ServerErrorCategory.DuplicateKey;
        }
    }
}
