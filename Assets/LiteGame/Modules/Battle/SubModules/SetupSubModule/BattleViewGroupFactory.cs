using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Animation;
using LiteFramework.UI;
using LiteSim;
using LiteGame.Modules.Battle.Arena;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Input;
using LiteGame.Modules.Settings;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组工厂（战斗域装配点）：把视图域全部依赖（内容/输入/相机/VFX/世界钟/音频/命令/ 准心手感/主题/开发会话/视角偏好）留在本域。
    /// </summary>
    public sealed class BattleViewGroupFactory : IBattleViewGroupFactory
    {
        private readonly IContentService _content;
        private readonly IInputService _input;
        private readonly ICameraService _camera;
        private readonly IVfxService _vfx;
        private readonly IWorldClock _worldClock;
        private readonly AudioService _audio;
        private readonly CommandBus _commands;
        private readonly CrosshairFeelProfile _crosshairFeel;
        private readonly Func<UIThemeAsset> _themeProvider;
        private readonly IDevSession _dev;
        private readonly ILookPreferences _lookPreferences;
        private readonly RootMotionDigestHolder _rootMotionDigest;   // 根位移摘要登记口（容器装配期单实例）
        private readonly Func<IBattleDevOverlay> _devOverlayOf;      // 开发叠加层来源（装配期注入；发布 null）
        private readonly IAnimationBackendFactory _animationBackendFactory;   // 动画后端工厂（装配根注入）
        private readonly Func<BattleStrategyProfile> _strategy;               // 策略表投影（延迟到对局创建）
        private BattleViewRig _last;                     // 真 rig（诊断读口；Dispose 即置空）

        public BattleViewGroupFactory(IContentService content, IInputService input, ICameraService camera,
            IVfxService vfx, IWorldClock worldClock, AudioService audio, CommandBus commands,
            CrosshairFeelProfile crosshairFeel, Func<UIThemeAsset> themeProvider, IDevSession dev,
            ILookPreferences lookPreferences, RootMotionDigestHolder rootMotionDigest,
            Func<IBattleDevOverlay> devOverlayOf = null, IAnimationBackendFactory animationBackendFactory = null,
            Func<BattleStrategyProfile> strategy = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _input = input;
            _camera = camera;
            _vfx = vfx;
            _worldClock = worldClock;
            _audio = audio;
            _commands = commands;
            _crosshairFeel = crosshairFeel;
            _themeProvider = themeProvider;
            _dev = dev;
            _lookPreferences = lookPreferences;
            _rootMotionDigest = rootMotionDigest;
            _devOverlayOf = devOverlayOf;
            _animationBackendFactory = animationBackendFactory;
            _strategy = strategy;
        }

        /// <inheritdoc />
        public IBattleViewGroup LastCreated => Volatile.Read(ref _last);

        public IBattleViewGroup Create(ClientScope account, Action<SimVector3> teleportDispatch)
        {
            IBattleDevOverlay devOverlay = _devOverlayOf?.Invoke();   // 每局取一次（开发装配按局建实例）
            BattleStrategyProfile strategy = _strategy?.Invoke() ?? BattleStrategyProfile.Default;   // Preload 后必已就绪
            var rig = new BattleViewRig(_content, _input, _camera, account, _vfx, _worldClock, _audio, _commands,
                _crosshairFeel, _themeProvider, _dev, devOverlay, teleportDispatch, _lookPreferences,
                _rootMotionDigest, animationBackendFactory: _animationBackendFactory, strategy: strategy);
            Volatile.Write(ref _last, rig);
            return new LastCreatedDecorator(this, rig);
        }

        /// <summary>最后创建口的装饰器：转发全部视图组调用，Dispose 时把工厂的 <c>_last</c> 置空
        /// （先拆真 rig 再清引用——离场中断言读到 null 即"视图组已释放"）。</summary>
        private sealed class LastCreatedDecorator : IBattleViewGroup, IBattleViewAttachment
        {
            private readonly BattleViewGroupFactory _owner;
            private readonly BattleViewRig _rig;

            public LastCreatedDecorator(BattleViewGroupFactory owner, BattleViewRig rig)
            {
                _owner = owner;
                _rig = rig;
            }

            public UniTask LoadAsync(CancellationToken ct) => _rig.LoadAsync(ct);

            public void Attach(BattleContext context, Action openBattleMenu = null)
                => _rig.Attach(context, openBattleMenu);

            public void Tick(float elapseSeconds) => _rig.Tick(elapseSeconds);

            public void TickPreSample() => _rig.TickPreSample();

            public void Dispose()
            {
                try { _rig.Dispose(); }
                finally { Interlocked.CompareExchange(ref _owner._last, null, _rig); }
            }
        }
    }
}
