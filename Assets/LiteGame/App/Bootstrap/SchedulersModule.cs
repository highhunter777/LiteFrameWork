using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>⑤ 时序执行：逻辑/UI 调度器 + 时间轴执行器（依赖 ④ Clocks——模块序即依赖序）。</summary>
    internal sealed class SchedulersModule : IClientModule
    {
        public string Name => "Schedulers";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            context.Put<ILogicScheduler>(new LogicScheduler(context.Require<IWorldClock>()));
            context.Put<IUIScheduler>(new UIScheduler(context.Require<IUIClock>()));
            context.Put(new GameTimelineRunner(context.Require<IWorldClock>()));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 同上——U1 统一取消
    }
}
