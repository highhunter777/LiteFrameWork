namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放终态（一个已接受请求只产生一次终态）。
    /// </summary>
    public enum AnimationTerminalState
    {
        /// <summary>尚无终态（进行中或装载中）。</summary>
        None = 0,
        /// <summary>满足定义的视觉结束条件。</summary>
        Completed = 1,
        Interrupted = 2,
        Cancelled = 3,
        OwnerDisposed = 4,
        Failed = 5,
    }
}
