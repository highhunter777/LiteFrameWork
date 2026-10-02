using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;

namespace LiteGame
{
    /// <summary>④ 事件与时钟：双轨时钟分域（逻辑轨受时停/变速，UI 轨不受）+ 墙钟。
    ///
    /// 注册序见 <see cref="GameEntry"/>（⑤ Schedulers 依赖本模块产物——模块序即依赖序）。</summary>
    internal sealed class ClocksModule : IClientModule
    {
        private EventCenter _events;
        private WorldClock _worldClock;
        private UIClock _uiClock;
        private SystemWallClock _wallClock;

        public string Name => "Clocks";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _events = new EventCenter();
            _worldClock = new WorldClock();
            _uiClock = new UIClock();
            _wallClock = new SystemWallClock();
            context.Put(_events);
            context.Put<IWorldClock>(_worldClock);
            context.Put<IUIClock>(_uiClock);
            context.Put<IWallClock>(_wallClock);
            UiAnimationClock.Bind(_uiClock);               // 动画时钟：DOTween Manual 轨/序列帧接入 UIClock
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 时钟无关闭面（Scope 统一取消）
    }
}
