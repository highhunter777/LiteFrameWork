namespace LiteFramework.Animation
{
    /// <summary>
    /// 停止原因（终态表的非 Completed 项；<see cref="AnimationTerminalState"/> 由其派生）。
    /// </summary>
    public enum AnimationStopReason
    {
        /// <summary>被接受的新播放或表现接管替换。</summary>
        Interrupted = 0,
        /// <summary>调用方主动取消。</summary>
        Cancelled = 1,
        /// <summary>所属角色/展示作用域/场景结束。</summary>
        OwnerDisposed = 2,
        /// <summary>装载或后端执行失败（带稳定错误码见 <see cref="AnimationStartResult"/>）。</summary>
        Failed = 3,
    }
}
