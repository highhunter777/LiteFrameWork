using System;
using LiteFramework;
using LiteFramework.UI;
using LiteSim;

namespace LiteGame
{
    /// <summary>
    /// 对局视图组工厂（表现层装配点）：把视图域全部依赖（内容/输入/相机/VFX/世界钟/音频/命令/
    /// 准心手感/主题/开发会话/视角偏好）留在本层，流程只按 <see cref="IBattleViewGroupFactory"/>
    /// 端口取件——每局再注入 account 作用域、开发叠加层与传送分发。
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

        public IBattleViewGroup Create(ClientScope account, IBattleDevOverlay devOverlay,
            Action<SimVector3> teleportDispatch)
            => new BattleViewRig(_content, _input, _camera, account, _vfx, _worldClock, _audio, _commands,
                _crosshairFeel, _themeProvider, _dev, devOverlay, teleportDispatch, _lookPreferences);
    }
}