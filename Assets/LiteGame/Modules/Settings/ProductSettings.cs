using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 设置门面本体（Lua 面 `Bridge.settings` 在 <c>Adapters/LuaBridge/ProductBridges</c>）：**四组设置的读写 + 立即生效**。
    /// 存储走 <see cref="GameSettings"/>（设置项清单在其单源；业务禁裸用 SettingService），
    /// 生效面按组拆给四个生效器：音频 <see cref="AudioSettingsApplier"/>、图形 <see cref="GraphicsSettingsApplier"/>、
    /// 显示 <see cref="DisplaySettingsApplier"/>、控制 <see cref="LookPreferences"/>（视角倍率端口）。
    /// **语言不在此**（走 `Bridge.text.SetLocale`）。
    ///
    /// 生效纪律（《设置系统专项设计》§4.1）：构造即全量应用（启动生效点 = 装配根）、改值即应用、
    /// 顺序固定（画质档 → 图形覆盖 → 垂直同步 → 分辨率/模式 → 帧率）、幂等、失败降级。
    /// 落盘时机归 SettingService 三处纪律（设置页 OnHide 调 Save；退出经 SettingsModule.ShutdownAsync）。
    /// </summary>
    public sealed class ProductSettings
    {
        /// <summary>UI 画质档数（低/中/高）；GameSettings.Quality 值域 0..3，UI 只暴露 0..2。</summary>
        public const int UiQualityTiers = GraphicsSettingsApplier.UiQualityTiers;

        private static ProductSettings s_instance;          // 装订面（Lua 全局表；与 MetaBridge.s_meta 同范式）

        private readonly GameSettings _settings;
        private readonly SettingService _store;
        private readonly AudioSettingsApplier _audio;
        private readonly GraphicsSettingsApplier _graphics;
        private readonly DisplaySettingsApplier _display;
        private readonly LookPreferences _look;
        private readonly bool _isMobilePlatform;

        private IReadOnlyList<DisplayResolution> _resolutionSnapshot;   // 一次页面会话内复用（Screen.resolutions 每次都分配）

        /// <param name="look">视角偏好**单实例**（由设置段产出；本件只刷新不重建——改灵敏度后对局立即生效）。</param>
        public ProductSettings(GameSettings settings, SettingService store, AudioService audio, LookPreferences look)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _audio = new AudioSettingsApplier(audio);
            _graphics = new GraphicsSettingsApplier();
            _display = new DisplaySettingsApplier();
            _look = look ?? throw new ArgumentNullException(nameof(look));
            _isMobilePlatform = Application.isMobilePlatform;
            ApplyAll();
        }

        /// <summary>装配期装订（组合根 ComposeAfter——Lua 门面后端）。</summary>
        public static void Bind(ProductSettings settings)
            => s_instance = settings ?? throw new ArgumentNullException(nameof(settings));

        /// <summary>视角偏好端口（对局视图装配时注入，供 <see cref="BattleLookController"/> 读）。</summary>
        public ILookPreferences Look => _look;

        /// <summary>已装订的设置门面（Lua 装订面在 <c>ProductBridges</c>；未装订即 fail-fast）。</summary>
        public static ProductSettings Current
            => s_instance ?? throw new InvalidOperationException("Bridge.settings 未装订（装配点未 Bind 设置门面）");

        // ---- 音频 ----

        public float Volume => _settings.MasterVolume.Get();

        public void SetVolume(float value01)
        {
            _settings.MasterVolume.Set(value01);            // 值域 clamp 在 Setting<float> 内（0..1）
            _audio.Apply(_settings);
        }

        public bool SoundEnabled => _settings.SoundEnabled.Get();

        public void SetSoundEnabled(bool on)
        {
            _settings.SoundEnabled.Set(on);
            _audio.Apply(_settings);
        }

        // ---- 图形 ----

        /// <summary>UI 画质档（0=低 / 1=中 / 2=高）。</summary>
        public int Quality => _graphics.Quality(_settings);

        public void SetQuality(int tier) => _graphics.SetQuality(_settings, tier);

        // ---- 显示 ----

        /// <summary>当前平台是否支持窗口类设置（模式/分辨率/垂直同步）；移动端为 false。</summary>
        public bool SupportsWindowSettings => _display.SupportsWindowSettings;

        /// <summary>显示模式（0 独占全屏 / 1 无边框 / 2 窗口）。</summary>
        public int DisplayMode => DisplayOptions.SnapMode(_settings.DisplayMode.Get());

        public void SetDisplayMode(int mode) => _display.SetMode(_settings, mode);

        public int ResolutionCount()
        {
            _resolutionSnapshot = _display.Resolutions();
            return _resolutionSnapshot.Count;
        }

        public string ResolutionLabel(int index)
        {
            if (_resolutionSnapshot == null) _resolutionSnapshot = _display.Resolutions();
            return index >= 0 && index < _resolutionSnapshot.Count ? _resolutionSnapshot[index].Label : string.Empty;
        }

        /// <summary>当前分辨率列表下标；-1 = 未指定（窗口用默认尺寸）。</summary>
        public int ResolutionIndex => _display.IndexOf(_settings);

        public void SetResolutionIndex(int index)
        {
            _display.SetResolutionIndex(_settings, index);
            _resolutionSnapshot = null;
        }

        public int FrameRateOptionCount => FrameRateOptions.For(_isMobilePlatform).Count;

        public int FrameRateOption(int index)
        {
            IReadOnlyList<int> options = FrameRateOptions.For(_isMobilePlatform);
            return index >= 0 && index < options.Count ? options[index] : FrameRateOptions.DefaultFor(_isMobilePlatform);
        }

        public int FrameRate => _settings.FrameRate.Get();

        public void SetFrameRate(int cap) => _display.SetFrameRate(_settings, cap);

        public int VSync => DisplayOptions.SnapVSync(_settings.VSync.Get());

        public void SetVSync(int value) => _display.SetVSync(_settings, value);

        // ---- 控制 ----

        public float LookSensitivity => _settings.LookSensitivity.Get();

        public void SetLookSensitivity(float multiplier)
        {
            _settings.LookSensitivity.Set(multiplier);      // 值域 clamp 在 Setting<float> 内
            _look.Refresh();
        }

        public float AdsSensitivity => _settings.AdsSensitivity.Get();

        public void SetAdsSensitivity(float multiplier)
        {
            _settings.AdsSensitivity.Set(multiplier);
            _look.Refresh();
        }

        public bool InvertLookY => _settings.InvertLookY.Get();

        public void SetInvertLookY(bool on)
        {
            _settings.InvertLookY.Set(on);
            _look.Refresh();
        }

        // ---- 常规 ----

        /// <summary>上次停留的页签（0..4；越界由 <see cref="GameSettings.LastTab"/> 钳制）。</summary>
        public int LastTab => _settings.LastTab.Get();

        public void SetLastTab(int index) => _settings.LastTab.Set(index);

        /// <summary>全部设置回默认并立即应用（页面刷新与语言重建由调用方负责——门面不做本地化）。</summary>
        public void ResetDefaults()
        {
            _settings.ResetAll();
            ApplyAll();
        }

        /// <summary>落盘（SettingService 脏标记协议——未改则零写入）。设置页 OnHide / 退出路径调用。</summary>
        public void Save() => _store.SaveIfDirty();

        /// <summary>全量应用（构造与恢复默认共用；顺序固定——画质档 → 显示，垂直同步依赖档位）。</summary>
        private void ApplyAll()
        {
            _graphics.Apply(_settings);
            _display.Apply(_settings);
            _audio.Apply(_settings);
            _look.Refresh();
        }
    }
}
