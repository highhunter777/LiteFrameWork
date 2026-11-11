#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame.Adapters.Dev.Debug.NetDebug
{
    /// <summary>强制断线（调试动作：断链后由会话相位策略自动重连）。</summary>
    public sealed class ForceDisconnectCommand : ICommand { }
}
#endif
