#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    /// <summary>强制重连（仅 SuspectedLost 有效——其余相位如实拒绝，不抑制自动重连）。</summary>
    public sealed class ForceReconnectCommand : ICommand { }
}
#endif
