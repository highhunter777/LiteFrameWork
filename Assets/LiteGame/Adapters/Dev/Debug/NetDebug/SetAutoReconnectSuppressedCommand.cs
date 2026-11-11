#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame.Adapters.Dev.Debug.NetDebug
{
    /// <summary>抑制/恢复自动重连（调试开关：抑制后断线停在 SuspectedLost，等待手动重连）。</summary>
    public sealed class SetAutoReconnectSuppressedCommand : ICommand
    {
        public bool Suppressed;
    }
}
#endif
