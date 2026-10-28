using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>④ 事件与时钟：双轨时钟分域（逻辑轨受时停/变速，UI 轨不受）+ 墙钟。
    ///
    /// 注册序见 <see cref="GameEntry"/>（⑤ Schedulers 依赖本模块产物——模块序即依赖序）。</summary>
    internal sealed class ClocksModule : IClientModule, IClientModuleGraph
    {
        private EventBus _events;
        private WorldClock _worldClock;
        private UIClock _uiClock;
        private SystemWallClock _wallClock;

        public string Name => "Clocks";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn => System.Array.Empty<System.Type>();
        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _events = new EventBus(pool: context.Require<IReferencePool>(), strictMode: DevBuildFacts.IsDevelopment);
            _worldClock = new WorldClock();
            _uiClock = new UIClock();
            _wallClock = new SystemWallClock();
            context.Put(_events);
            context.Scope.Register(_events);               // Root 总线随 ⑤ 模块关闭 Dispose（清订阅/队列）
            context.Put<IWorldClock>(_worldClock);
            context.Put<IUIClock>(_uiClock);
            context.Put<IWallClock>(_wallClock);
            UiAnimationClock.Bind(_uiClock);               // 动画时钟：DOTween Manual 轨/序列帧接入 UIClock
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 时钟无关闭面（Scope 统一取消）
    }
}
