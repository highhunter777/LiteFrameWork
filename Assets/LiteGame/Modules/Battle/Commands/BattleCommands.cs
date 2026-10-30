using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>战斗命令端口的战斗域实现：把语义请求翻成命令入队（撤销/重做/排队派发归命令中心）。</summary>
    internal sealed class BattleCommands : IBattleCommands
    {
        private readonly CommandCenter _commands;

        internal BattleCommands(CommandCenter commands)
            => _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        public void RequestTeleportToAim() => _commands.SendQueued(new TeleportToAimCommand());
    }
}
