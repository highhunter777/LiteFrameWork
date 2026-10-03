namespace LiteFramework
{
    /// <summary>同步命令处理器（《命令中心专项设计》§2）：单处理器、可拒绝（返回失败结果而非抛）。
    /// Execute 抛异常 = 处理器作者代码缺陷——中心隔离为 Failed 结果（记日志 + 审计留痕，不炸调用方）。</summary>
    public interface ICommandHandler<in TCommand> where TCommand : ICommand
    {
        CommandResult Execute(TCommand command);
    }
}
