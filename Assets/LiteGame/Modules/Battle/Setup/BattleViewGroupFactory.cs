using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteSim;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Input;
using LiteGame.Modules.Settings;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组工厂（战斗域装配点）：把视图域全部依赖（内容/输入/相机/VFX/世界钟/音频/命令/
    /// 准心手感/主题/开发会话/视角偏好）留在本域，流程只按 <see cref="IBattleViewGroupFactory"/>
    /// 端口取件——每局再注入 account 作用域、开发叠加层与传送分发。
    ///
    /// **最后创建口**（<see cref="LastCreated"/>，仅测试/诊断用）：存**真 rig**、不存装饰器，
    /// 装饰器 Dispose 时同步置空——冒烟用例据此在离场后断言"不再有视图组"，且不会读到已拆的实例。
    /// </summary>
    public sealed class BattleViewGroupFactory : IBattleViewGroupFactory
    {
        private readonly IContentService _content;
        private readonly IInputService _input;
        private readonly ICameraService _camera;
        private readonly IVFXService _vfx;
        private readonly IWorldClock _worldClock;
        private readonly AudioService _audio;
        private readonly CommandCenter _commands;
        private readonly CrosshairFeelProfile _crosshairFeel;
        private readonly Func<UiThemeAsset> _themeProvider;
        private readonly IDevSession _dev;
        private readonly ILookPreferences _lookPreferences;
        private BattleViewRig _last;                     // 真 rig（诊断读口；Dispose 即置空）

        public BattleViewGroupFactory(IContentService content, IInputService input, ICameraService camera,
            IVFXService vfx, IWorldClock worldClock, AudioService audio, CommandCenter commands,
            CrosshairFeelProfile crosshairFeel, Func<UiThemeAsset> themeProvider, IDevSession dev,
            ILookPreferences lookPreferences)
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
        }

        /// <inheritdoc />
        public IBattleViewGroup LastCreated => Volatile.Read(ref _last);

        public IBattleViewGroup Create(ClientScope account, IBattleDevOverlay devOverlay,
            Action<SimVector3> teleportDispatch)
        {
            var rig = new BattleViewRig(_content, _input, _camera, account, _vfx, _worldClock, _audio, _commands,
                _crosshairFeel, _themeProvider, _dev, devOverlay, teleportDispatch, _lookPreferences);
            Volatile.Write(ref _last, rig);
            return new LastCreatedDecorator(this, rig);
        }

        /// <summary>最后创建口的装饰器：转发全部视图组调用，Dispose 时把工厂的 <c>_last</c> 置空
        /// （先拆真 rig 再清引用——离场中断言读到 null 即"视图组已释放"）。</summary>
        private sealed class LastCreatedDecorator : IBattleViewGroup
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
