using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// ⑥ 事件域：根 <see cref="EventBus"/> 的**唯一**产出方。
    ///
    /// 为什么独立成段：总线过去由时钟模块顺带创建（"双轨时钟 + 事件中心"），导致
    /// 事件订阅失败/关闭顺序问题的排查入口落在时钟文件里——事件与时钟是两个能力域，
    /// 装配粒度应与领域一致（事件总线只需引用池，不需要任何时钟）。
    /// </summary>
    public sealed class EventsModule : IClientModule, IClientModuleGraph
    {
        private EventBus _events;

        public string Name => "Events";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure",   // 总线池来源（IReferencePool）
        };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _events = FrameworkEvents.Create(context.Require<IReferencePool>(), DevBuildFacts.IsDevelopment);
            context.Put(_events);
            context.Put<IEventBus>(_events);   // 接口键（消费者只经端口取；与具体键同实例）
            context.Scope.Register(_events);   // Root 总线随本段关闭 Dispose（清订阅/队列）
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 释放随 Scope
    }
}
