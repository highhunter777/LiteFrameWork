using System;

namespace LiteFramework
{
    /// <summary>
    /// 命令中心业务契约（《命令中心专项设计》§2）：类型化"意图请求"的注册-执行-结果链路。
    /// </summary>
    public interface ICommandCenter
    {
        /// <summary>注册处理器（命令类型唯一处理器契约——同类型重复注册抛）。</summary>
        IDisposable Register<TCommand>(ICommandHandler<TCommand> handler, CommandOptions options = null) where TCommand : ICommand;

        /// <summary>同步执行。无处理器 = 编程错误直接抛；GmBlocked 是运行态拒绝（失败结果不抛）。</summary>
        CommandResult Send<TCommand>(TCommand command) where TCommand : ICommand;

        /// <summary>该命令类型是否已注册处理器。</summary>
        bool HasHandler<TCommand>() where TCommand : ICommand;
    }
}
