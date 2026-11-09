using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame.Modules.Composition
{
    /// <summary>时间轴装配：使用 Clocks 段产出的世界时钟推进动作序列。</summary>
    public sealed class TimelineSegment : IClientSegment, IClientSegmentGraph
    {
        public string Name => "Timeline";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "Clocks" };
        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
        {
            var timeline = new GameTimelineRunner(context.Require<IWorldClock>());
            context.Put(timeline);
            context.Put<ITimelineRunner>(timeline);   // 接口键（时间轴用户只经端口取；与具体键同实例）
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 无关闭面（Scope 统一取消）
    }
}
