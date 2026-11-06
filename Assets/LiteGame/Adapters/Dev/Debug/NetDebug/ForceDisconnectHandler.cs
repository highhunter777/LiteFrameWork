#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    public sealed class ForceDisconnectHandler : ICommandHandler<ForceDisconnectCommand>
    {
        private readonly NetworkDebug _network;

        public ForceDisconnectHandler(NetworkDebug network)
        {
            _network = network;
        }

        public CommandResult Execute(ForceDisconnectCommand command)
        {
            if (_network == null) return CommandResult.Fail(CommandReject.HandlerRejected, "网络调试未装配");
            return _network.TryForceDisconnect(out string error)
                ? CommandResult.Success("session=disconnected")
                : CommandResult.Fail(CommandReject.HandlerRejected, error);
        }
    }
}
#endif
