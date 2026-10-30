using System;
using System.Collections.Generic;
using UnityEngine;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 一个可选分辨率（宽高；刷新率不参与存档与展示——见 <see cref="DisplayOptions"/> 的分辨率口径）。
    /// </summary>
    public readonly struct DisplayResolution
    {
        public DisplayResolution(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>存档串（<c>"1920x1080"</c>）。</summary>
        public string Key => DisplayOptions.FormatResolution(Width, Height);

        /// <summary>下拉项文本（<c>"1920 × 1080"</c>）。</summary>
        public string Label => DisplayOptions.FormatResolutionLabel(Width, Height);
    }

    /// <summary>
    /// 显示设置生效器：分辨率 + 显示模式 + 垂直同步 + 帧率上限。
    ///
    /// **两条纪律**：
    /// ① 幂等（E5）——窗口项先比对当前状态，同值不重设，避免页签切换/重复应用时反复重建窗口；
    /// ② 编辑器不碰窗口——<c>Screen.SetResolution</c> 对 Game 视图无效，Editor 下只应用垂直同步与帧率，
    /// 用运行时判断（<c>Application.isEditor</c>）而不是 `#if UNITY_EDITOR`（R5 要求三宏并集）。
    ///
    /// **垂直同步的"跟随预设"**：<c>-1</c> 表示读当前画质档内置的 <c>vSyncCount</c>。由于设置会改写当前档的
    /// <c>vSyncCount</c>，本类按档位**首次应用前**抓取原值并缓存——否则"改过再选跟随"会读到被自己改过的值。
    /// </summary>
    public sealed class DisplaySettingsApplier
    {
        private readonly Dictionary<int, int> _presetVSyncByLevel = new Dictionary<int, int>(4);
        private readonly List<DisplayResolution> _resolutions = new List<DisplayResolution>(32);

        /// <summary>当前平台是否支持窗口类设置（模式/分辨率/垂直同步）；移动端只应用帧率。</summary>
        public bool SupportsWindowSettings => !Application.isMobilePlatform;

        /// <summary>应用全部显示设置（幂等）。</summary>
        public void Apply(GameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (SupportsWindowSettings && !Application.isEditor) ApplyWindow(settings);
            ApplyVSync(settings);
            ApplyFrameRate(settings);
        }

        /// <summary>可选分辨率（宽高去重、按面积降序）；每次重建——显示器热插拔后列表会变。</summary>
        public IReadOnlyList<DisplayResolution> Resolutions()
        {
            _resolutions.Clear();
            Resolution[] raw = Screen.resolutions;
            if (raw == null || raw.Length == 0) return _resolutions;

            var bestRefresh = new Dictionary<(int Width, int Height), double>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i].width <= 0 || raw[i].height <= 0) continue;
                var key = (raw[i].width, raw[i].height);
                double refresh = raw[i].refreshRateRatio.value;
                if (!bestRefresh.TryGetValue(key, out double known) || refresh > known) bestRefresh[key] = refresh;
            }
            foreach (KeyValuePair<(int Width, int Height), double> pair in bestRefresh)
                _resolutions.Add(new DisplayResolution(pair.Key.Width, pair.Key.Height));

            _resolutions.Sort((a, b) =>
            {
                int byArea = (b.Width * b.Height).CompareTo(a.Width * a.Height);
                return byArea != 0 ? byArea : b.Width.CompareTo(a.Width);
            });
            return _resolutions;
        }

        /// <summary>存档串对应的列表下标；空/未指定 = -1（窗口用默认尺寸）。</summary>
        public int IndexOf(GameSettings settings)
        {
            string stored = settings.Resolution.Get();
            if (!DisplayOptions.TryParseResolution(stored, out int width, out int height)) return -1;
            IReadOnlyList<DisplayResolution> list = Resolutions();
            for (int i = 0; i < list.Count; i++)
                if (list[i].Width == width && list[i].Height == height) return i;
            return -1;   // 换显示器后旧值可不在列表里——按"未指定"处理（设计 §4.3）
        }

        /// <summary>写入第 <paramref name="index"/> 项（&lt;0 = 未指定/默认窗口尺寸）并立即应用。</summary>
        public void SetResolutionIndex(GameSettings settings, int index)
        {
            IReadOnlyList<DisplayResolution> list = Resolutions();
            settings.Resolution.Set(index >= 0 && index < list.Count ? list[index].Key : string.Empty);
            if (SupportsWindowSettings && !Application.isEditor) ApplyWindow(settings);
        }

        /// <summary>显示模式写入并立即应用。</summary>
        public void SetMode(GameSettings settings, int mode)
        {
            settings.DisplayMode.Set(DisplayOptions.SnapMode(mode));
            if (SupportsWindowSettings && !Application.isEditor) ApplyWindow(settings);
        }

        /// <summary>帧率上限写入并立即应用（垂直同步开时上限由刷新率决定）。</summary>
        public void SetFrameRate(GameSettings settings, int cap)
        {
            settings.FrameRate.Set(cap);
            ApplyFrameRate(settings);
        }

        /// <summary>垂直同步写入并立即应用。</summary>
        public void SetVSync(GameSettings settings, int value)
        {
            settings.VSync.Set(value);
            ApplyVSync(settings);
            ApplyFrameRate(settings);
        }

        private void ApplyWindow(GameSettings settings)
        {
            int mode = DisplayOptions.SnapMode(settings.DisplayMode.Get());
            if (mode == DisplayOptions.ModeWindowed)
            {
                int width = DisplayOptions.WindowWidth;
                int height = DisplayOptions.WindowHeight;
                if (DisplayOptions.TryParseResolution(settings.Resolution.Get(), out int storedWidth, out int storedHeight))
                {
                    width = storedWidth;
                    height = storedHeight;
                }
                if (Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == width && Screen.height == height) return;
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                return;
            }

            FullScreenMode wanted = mode == DisplayOptions.ModeExclusiveFullscreen
                ? FullScreenMode.ExclusiveFullScreen
                : FullScreenMode.FullScreenWindow;
            if (Screen.fullScreenMode == wanted) return;
            Screen.fullScreenMode = wanted;
        }

        private void ApplyVSync(GameSettings settings)
        {
            int value = DisplayOptions.SnapVSync(settings.VSync.Get());
            int target = value;
            if (value == DisplayOptions.VSyncFollowPreset)
            {
                int level = QualitySettings.GetQualityLevel();
                if (!_presetVSyncByLevel.TryGetValue(level, out target))
                {
                    target = QualitySettings.vSyncCount;      // 本档首次应用前抓原值（此后本类会改写它）
                    _presetVSyncByLevel[level] = target;
                }
            }
            if (QualitySettings.vSyncCount != target) QualitySettings.vSyncCount = target;
        }

        private void ApplyFrameRate(GameSettings settings)
        {
            int target = QualitySettings.vSyncCount > 0
                ? -1                                          // 垂直同步开：上限由显示器刷新率决定
                : FrameRateOptions.ToAppTarget(settings.FrameRate.Get());
            if (Application.targetFrameRate != target) Application.targetFrameRate = target;
        }
    }
}
