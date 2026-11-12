using System;
using System.Collections.Generic;
using LiteFrameworkNext;
using UnityEngine;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置生效器：把音频/图形/显示项真正落到引擎 API；视角与绑定只存值（由输入、相机将来消费）。
    /// 无显示器/编辑器环境下任何引擎调用都不抛。依据旧 `Assets/LiteGame/Modules/Settings/Apply/*Applier.cs`。</summary>
    public sealed class SettingsAppliers
    {
        private static readonly string[] TierNames = { "Low", "Medium", "High" };
        private readonly Dictionary<int, int> _presetVSyncByLevel = new Dictionary<int, int>(4);
        private readonly bool _isMobilePlatform;

        /// <param name="isMobilePlatform">平台差异的唯一来源（R15：类内不写 <c>#if</c> 分支）。</param>
        public SettingsAppliers(bool isMobilePlatform = false)
        {
            _isMobilePlatform = isMobilePlatform;
        }

        /// <summary>全部生效（启动、重载存档、恢复默认后调用）；幂等。
        /// 只在 Play 期写引擎：编辑期这些属性属于 ProjectSettings 资产，写了会持久污染工程设置。</summary>
        public void ApplyAll(GameSettings settings)
        {
            if (settings == null || !Application.isPlaying) return;
            ApplyAudio(settings);
            ApplyGraphics(settings);
            ApplyDisplay(settings);
        }

        /// <summary>音频：主音量 → AudioListener；音效开关无引擎侧落点，由 AudioService 将来读设置值。</summary>
        public void ApplyAudio(GameSettings settings)
        {
            if (settings == null || !Application.isPlaying) return;
            try
            {
                AudioListener.volume = settings.MasterVolume.Value;
            }
            catch (Exception ex)
            {
                Log.Warning("音频设置生效失败（忽略）：" + ex.Message);
            }
        }

        /// <summary>图形：画质预设按名匹配档位；三个覆盖项在 FollowPreset 时不碰（跟随预设即不覆盖）。
        /// 仅运行时写：编辑期这些属性属于 QualitySettings 资产，写了会持久污染工程设置。</summary>
        public void ApplyGraphics(GameSettings settings)
        {
            if (settings == null || !Application.isPlaying) return;
            ApplyQualityLevel(settings.Quality.Value);
            ApplyOverrides(settings);
        }

        /// <summary>显示：垂直同步 → 帧率上限 → 窗口。窗口类设置只在编辑器外生效，垂直同步/帧率只在运行时生效
        /// （编辑期改这两项会污染宿主 QualitySettings 资产）。</summary>
        public void ApplyDisplay(GameSettings settings)
        {
            if (settings == null || !Application.isPlaying) return;
            ApplyVSync(settings.VSync.Value);
            ApplyFrameRate(settings);
            if (!Application.isEditor) ApplyWindow(settings);
        }

        private void ApplyQualityLevel(int tier)
        {
            int level = Mathf.Clamp(tier, 0, TierNames.Length - 1);
            try
            {
                int target = level;
                string[] names = QualitySettings.names;
                if (names != null && names.Length > 0)
                {
                    int found = Array.FindIndex(names,
                        n => string.Equals(n, TierNames[level], StringComparison.OrdinalIgnoreCase));
                    target = found >= 0 ? found : Mathf.Clamp(level, 0, names.Length - 1);
                }

                QualitySettings.SetQualityLevel(target, true);
            }
            catch (Exception ex)
            {
                Log.Warning("画质档生效失败（忽略）：" + ex.Message);
            }
        }

        // 覆盖项在 FollowPreset 时保持预设值；其余值走引擎侧可写通道，不猜档。
        private void ApplyOverrides(GameSettings settings)
        {
            try
            {
                int aa = settings.AntiAliasing.Value;
                if (aa != GraphicsOptions.FollowPreset) QualitySettings.antiAliasing = aa;

                int shadow = settings.ShadowQuality.Value;
                if (shadow != GraphicsOptions.FollowPreset)
                {
                    QualitySettings.shadowDistance = GraphicsOptions.ShadowDistanceOf(shadow);
                    QualitySettings.shadowCascades = GraphicsOptions.ShadowCascadeCountOf(shadow);
                }

                int renderScale = settings.RenderScale.Value;
                if (renderScale != GraphicsOptions.FollowPreset)
                {
                    float scale = Mathf.Clamp(renderScale, 1, 100) / 100f;
                    ScalableBufferManager.ResizeBuffers(scale, scale);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("图形覆盖项生效失败（忽略）：" + ex.Message);
            }
        }

        private void ApplyVSync(int value)
        {
            try
            {
                int snapped = DisplayOptions.SnapVSync(value);
                int target = snapped == DisplayOptions.VSyncFollowPreset
                    ? PresetVSyncOf(QualitySettings.GetQualityLevel())
                    : snapped;
                if (QualitySettings.vSyncCount != target) QualitySettings.vSyncCount = target;
            }
            catch (Exception ex)
            {
                Log.Warning("垂直同步生效失败（忽略）：" + ex.Message);
            }
        }

        // 跟随预设＝该画质档首次应用前抓到的原值（本类此后会改写 vSyncCount）。
        private int PresetVSyncOf(int level)
        {
            if (_presetVSyncByLevel.TryGetValue(level, out int known)) return known;
            int captured = QualitySettings.vSyncCount;
            _presetVSyncByLevel[level] = captured;
            return captured;
        }

        private void ApplyFrameRate(GameSettings settings)
        {
            try
            {
                int vsync = QualitySettings.vSyncCount;
                int target = vsync > 0
                    ? -1
                    : FrameRateOptions.ToAppTarget(settings.FrameRate.Value);
                if (Application.targetFrameRate != target) Application.targetFrameRate = target;
            }
            catch (Exception ex)
            {
                Log.Warning("帧率上限生效失败（忽略）：" + ex.Message);
            }
        }

        // 模式三档 + 分辨率存档串；移动端不支持窗口类设置，只走帧率（与旧 DisplaySettingsApplier 同口径）。
        private void ApplyWindow(GameSettings settings)
        {
            if (_isMobilePlatform) return;
            try
            {
                int mode = DisplayOptions.SnapMode(settings.DisplayMode.Value);
                if (mode == DisplayOptions.ModeWindowed)
                {
                    int width = DisplayOptions.WindowWidth;
                    int height = DisplayOptions.WindowHeight;
                    if (DisplayOptions.TryParseResolution(settings.Resolution.Value, out int w, out int h))
                    {
                        width = w;
                        height = h;
                    }

                    if (Screen.fullScreenMode == FullScreenMode.Windowed
                        && Screen.width == width && Screen.height == height) return;
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
                    return;
                }

                FullScreenMode wanted = mode == DisplayOptions.ModeExclusiveFullscreen
                    ? FullScreenMode.ExclusiveFullScreen
                    : FullScreenMode.FullScreenWindow;
                if (Screen.fullScreenMode == wanted) return;
                Screen.fullScreenMode = wanted;
            }
            catch (Exception ex)
            {
                Log.Warning("显示设置生效失败（忽略）：" + ex.Message);
            }
        }
    }
}
