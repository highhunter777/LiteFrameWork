using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;

namespace LiteGame.Modules.Framework
{
    /// <summary>⑦ 时钟域：双轨时钟分域（逻辑轨受时停/变速，UI 轨不受）+ 墙钟。
    ///
    /// **事件总线不在这里产出**（唯一产出方是 ⑥ <see cref="EventsComposition"/>）——时钟模块
    /// 只负责时间，装配粒度与领域保持一致。
    ///
    /// 注册序见 <see cref="GameEntry"/>（⑧ Schedulers 依赖本模块产物——模块序即依赖序）。</summary>
    public sealed class ClocksModule : IClientModule, IClientModuleGraph
    {
        private FrameworkClocks _clocks;

        public string Name => "Clocks";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => System.Array.Empty<string>();

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _clocks = FrameworkClocks.Create();
            context.Put<IWorldClock>(_clocks.World);
            context.Put<IUIClock>(_clocks.Ui);
            context.Put<IWallClock>(_clocks.Wall);
            UiAnimationClock.Bind(_clocks.Ui);             // 动画时钟：DOTween Manual 轨/序列帧接入 UIClock
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 时钟无关闭面（Scope 统一取消）
    }
}
