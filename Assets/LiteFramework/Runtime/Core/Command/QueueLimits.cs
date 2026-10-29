namespace LiteFramework
{
    /// <summary>
    /// 有界队列的共享缺省（命令中心 / 事件中心同口径）：两处"入队超限丢弃并计数"的排障语义一致，
    /// 容量缺省不应各写一份 256。单实例若需不同上限仍可经属性在装配面设置。
    /// </summary>
    public static class QueueLimits
    {
        /// <summary>排队上限缺省（超出丢弃并计数）。</summary>
        public const int DefaultCapacity = 256;
    }
}
