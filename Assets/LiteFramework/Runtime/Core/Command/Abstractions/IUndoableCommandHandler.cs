namespace LiteFramework
{
    /// <summary>可撤销处理器（《命令中心专项设计》§4 opt-in 能力件）：追加 Undo(TCommand)。
    /// 仅同步 Send 成功的 undoable 命令进历史栈（容量 64，排队/异步命令无回滚点）；
    /// 重做 = 重新 Execute。**入栈即移交撤销所有权**——调用方此后不得复用/回收该命令对象
    /// （Undo/Redo 持其引用；"生命周期归调用方"契约的可撤销例外，见设计 §4）。</summary>
    public interface IUndoableCommandHandler<in TCommand> : ICommandHandler<TCommand> where TCommand : ICommand
    {
        /// <summary>撤销（成功执行后的回滚动作）。抛异常 = 隔离（记日志，该条撤销半途即弃——不回塞防死循环）。</summary>
        void Undo(TCommand command);
    }
}
