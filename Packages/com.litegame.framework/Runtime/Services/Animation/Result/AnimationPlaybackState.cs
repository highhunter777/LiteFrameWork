namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放状态快照（<c>TryGetState</c> 的输出）。终态记录**有界保留**——过期后查询返回未找到，
    /// 不把所有已完成 Handle 永久留在表里。
    /// </summary>
    public readonly struct AnimationPlaybackState
    {
        public readonly AnimationHandle Handle;
        public readonly AnimationId Id;
        public readonly AnimationChannel Channel;
        /// <summary>是否仍在装载（已接受但后端尚未提交成功）。</summary>
        public readonly bool IsLoading;
        /// <summary>是否仍在播放（已提交且无终态）。</summary>
        public readonly bool IsPlaying;
        public readonly AnimationTerminalState Terminal;

        public AnimationPlaybackState(AnimationHandle handle, AnimationId id, AnimationChannel channel,
            bool isLoading, bool isPlaying, AnimationTerminalState terminal)
        {
            Handle = handle;
            Id = id;
            Channel = channel;
            IsLoading = isLoading;
            IsPlaying = isPlaying;
            Terminal = terminal;
        }
    }
}
