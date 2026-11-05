using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>异步命令处理器（《命令中心专项设计》§4）：RegisterAsync 单独注册——同步/异步注册互斥
    /// （同键同桶，类型错配 = 编程错误 fail-fast）。Core 零第三方依赖：ValueTask（BCL）不引 UniTask。</summary>
    public interface IAsyncCommandHandler<in TCommand> where TCommand : ICommand
    {
        ValueTask<CommandResult> ExecuteAsync(TCommand command, CancellationToken ct);
    }
}
