#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;

namespace LiteGame
{
    /// <summary>强制断线（调试动作：断链后由会话相位策略自动重连）。</summary>
    public sealed class ForceDisconnectCommand : ICommand { }

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

    /// <summary>强制重连（仅 SuspectedLost 有效——其余相位如实拒绝，不抑制自动重连）。</summary>
    public sealed class ForceReconnectCommand : ICommand { }

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

    /// <summary>抑制/恢复自动重连（调试开关：抑制后断线停在 SuspectedLost，等待手动重连）。</summary>
    public sealed class SetAutoReconnectSuppressedCommand : ICommand
    {
        public bool Suppressed;
    }

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
