using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Composition
{
    /// <summary>
    /// ⑥ 事件域：根 EventBus 的**唯一**产出方。
    /// </summary>
    public sealed class EventsSegment : IClientSegment, IClientSegmentGraph
    {
        private EventBus _events;

        public string Name => "Events";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            _events = FrameworkEvents.Create(DevBuildFacts.IsDevelopment);
            context.Put(_events);
            context.Put<IEventBus>(_events);   // 接口键（消费者只经端口取；与具体键同实例）
            context.Scope.Register(_events);   // Root 总线随本段关闭 Dispose（清订阅/队列）
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 释放随 Scope
    }
}
