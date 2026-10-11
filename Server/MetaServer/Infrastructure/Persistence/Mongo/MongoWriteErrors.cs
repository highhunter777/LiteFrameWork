using MongoDB.Driver;

namespace MetaServer.Infrastructure.Persistence.Mongo
{
    /// <summary>
    /// Mongo 写错误的**唯一判据**（P2 单源）：幂等链（账本/归档/Outbox）都靠"唯一索引撞键 → 读回首次"
    /// 收口，而"这是不是撞键"此前在三个适配器里各手写一份逐字相同的判定——判据一旦漂移（例如换成
    /// 按错误码数字判），三条幂等链会**分别**失效，且失效表现是"并发下偶发非幂等"，最难定位。
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
