using System;

namespace LiteFramework
{
    /// <summary>池淘汰策略（《对象池专项设计》§3.1）：空闲数达上限时的归还处置。</summary>
    public enum PoolEviction
    {
        /// <summary>销毁新归还件（默认 = 历史行为）。DroppedCount 记账。</summary>
        DropNewest = 0,
        /// <summary>淘汰队首最旧空闲件、保留新归还的热点件。EvictedCount 记账。</summary>
        EvictOldest = 1,
    }
}
