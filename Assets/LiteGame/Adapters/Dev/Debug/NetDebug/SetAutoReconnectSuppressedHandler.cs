#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame.Adapters.Dev.Debug.NetDebug
{
    public sealed class SetAutoReconnectSuppressedHandler : ICommandHandler<SetAutoReconnectSuppressedCommand>
    {
        private readonly NetworkDebug _network;

        public SetAutoReconnectSuppressedHandler(NetworkDebug network)
        {
            _network = network;
        }

        public CommandResult Execute(SetAutoReconnectSuppressedCommand command)
        {
            if (_network == null) return CommandResult.Fail(CommandReject.HandlerRejected, "网络调试未装配");
            return _network.TrySetSuppressAutoReconnect(command.Suppressed, out string error)
                ? CommandResult.Success("autoReconnect=" + (command.Suppressed ? "suppressed" : "on"))
                : CommandResult.Fail(CommandReject.HandlerRejected, error);
        }
    }
}
#endif
