using System;

namespace LiteFramework
{
    /// <summary>
    /// 命令中心业务契约（《命令中心专项设计》§2）：类型化"意图请求"的注册-执行-结果链路。
    /// 命令 = 有意图的请求（单处理器/可拒绝/有结果）——执行成功后由执行方发事件（命令→事实单向流）。
    /// Register 返回 IDisposable（注销挂 SubscriptionBag/ClientScope）；命令对象生命周期归调用方。
    /// 高级执行形态（排队/异步/拦截器/撤销）在具体类型上（应用层横切——ICommandCenter 只留业务契约）。
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
