#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    /// <summary>切换弱网预设（低频动作，走命令中心审计）。</summary>
    public sealed class SetWeakNetPresetCommand : ICommand
    {
        public string Preset;
    }

    public sealed class SetWeakNetPresetHandler : ICommandHandler<SetWeakNetPresetCommand>
    {
        private readonly WeakNetRuntime _runtime;

        public SetWeakNetPresetHandler(WeakNetRuntime runtime)
        {
            _runtime = runtime;
        }

        public CommandResult Execute(SetWeakNetPresetCommand command)
        {
            if (_runtime == null) return CommandResult.Fail(CommandReject.HandlerRejected, "弱网运行实例未装配");
            if (!_runtime.TrySetPreset(command.Preset, out string error))
                return CommandResult.Fail(CommandReject.HandlerRejected, error);
            return CommandResult.Success("weak=" + command.Preset);
        }
    }
}
#endif
