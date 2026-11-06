#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    public sealed class ForceReconnectHandler : ICommandHandler<ForceReconnectCommand>
    {
        private readonly NetworkDebug _network;

        public ForceReconnectHandler(NetworkDebug network)
        {
            _network = network;
        }

        public CommandResult Execute(ForceReconnectCommand command)
        {
            if (_network == null) return CommandResult.Fail(CommandReject.HandlerRejected, "网络调试未装配");
            return _network.TryBeginReconnect(out string error)
                ? CommandResult.Success("session=reconnecting")
                : CommandResult.Fail(CommandReject.HandlerRejected, error);
        }
    }
}
#endif
