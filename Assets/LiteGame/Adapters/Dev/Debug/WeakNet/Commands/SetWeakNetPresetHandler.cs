#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using LiteGame.Adapters.Dev.Debug.WeakNet;
using LiteGame.Adapters.Dev.Debug.WeakNet.Profile;

namespace LiteGame.Adapters.Dev.Debug.WeakNet.Commands
{
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
