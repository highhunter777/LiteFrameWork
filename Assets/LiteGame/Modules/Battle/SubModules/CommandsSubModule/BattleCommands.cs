using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>战斗命令端口的战斗域实现：把语义请求翻成命令入队（撤销/重做/排队派发归命令总线）。</summary>
    internal sealed class BattleCommands : IBattleCommands
    {
        private readonly CommandBus _commands;

        internal BattleCommands(CommandBus commands)
            => _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        public void RequestTeleportToAim() => _commands.SendQueued(new TeleportToAimCommand());
    }
}
