#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame.Adapters.Dev.Debug.WeakNet.Commands
{
    /// <summary>切换弱网预设（低频动作，走命令总线审计）。</summary>
    public sealed class SetWeakNetPresetCommand : ICommand
    {
        public string Preset;
    }
}
#endif
