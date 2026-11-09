namespace LiteFramework.Animation
{
    /// <summary>一次推进判定的动作（<see cref="ClipCompletionTracker.Advance"/> 的输出）。</summary>
    public enum ClipTickAction
    {
        /// <summary>继续播放（未到边界）。</summary>
        None = 0,
        /// <summary>定义为循环、资产未自回绕：调用方须把片段时间重设为 <see cref="ClipCompletionTracker.WrapTarget"/>。</summary>
        Wrap = 1,
        /// <summary>到达结束边界（或检测到资产回卷）→ 该通道收 Completed，**恰好一次**。</summary>
        Completed = 2,
    }
}
