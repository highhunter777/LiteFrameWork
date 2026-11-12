using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiteGameNext.Features.Settings
{
    /// <summary>图形覆盖档值域：跟随预设(-1)/渲染缩放/抗锯齿/阴影。常量与旧实现逐值一致，依据
    /// `Assets/LiteGame/Modules/Settings/Options/GraphicsOptions.cs`（Next 不引旧程序集，故此处重写）。</summary>
    public static class GraphicsOptions
    {
        /// <summary>跟随画质预设（不做任何覆盖）。</summary>
        public const int FollowPreset = -1;

        private static readonly int[] RenderScaleValues = { FollowPreset, 70, 85, 100 };
        private static readonly int[] AntiAliasingValues = { FollowPreset, 0, 2, 4 };
        private static readonly int[] ShadowValues = { FollowPreset, 0, 1, 2 };
        private static readonly float[] ShadowDistances = { 25f, 50f, 80f };
        private static readonly int[] ShadowCascadeCounts = { 1, 2, 4 };

        public static IReadOnlyList<int> RenderScaleOptions => RenderScaleValues;

        public static IReadOnlyList<int> AntiAliasingOptions => AntiAliasingValues;

        public static IReadOnlyList<int> ShadowOptions => ShadowValues;

        public static bool IsRenderScale(int value) => Array.IndexOf(RenderScaleValues, value) >= 0;

        public static bool IsAntiAliasing(int value) => Array.IndexOf(AntiAliasingValues, value) >= 0;

        public static bool IsShadow(int value) => Array.IndexOf(ShadowValues, value) >= 0;

        /// <summary>非法值一律回"跟随预设"（覆盖项不猜档：猜错会改画面观感）。</summary>
        public static int SnapRenderScale(int value) => IsRenderScale(value) ? value : FollowPreset;

        public static int SnapAntiAliasing(int value) => IsAntiAliasing(value) ? value : FollowPreset;

        public static int SnapShadow(int value) => IsShadow(value) ? value : FollowPreset;

        /// <summary>阴影档 → 阴影距离（米）；非阴影档返回 0（调用方只在生效时取）。</summary>
        public static float ShadowDistanceOf(int level)
            => level >= 0 && level < ShadowDistances.Length ? ShadowDistances[level] : 0f;

        /// <summary>阴影档 → 级联数；非阴影档返回 1。</summary>
        public static int ShadowCascadeCountOf(int level)
            => level >= 0 && level < ShadowCascadeCounts.Length ? ShadowCascadeCounts[level] : 1;
    }

    /// <summary>显示值域：显示模式/垂直同步/窗口尺寸/分辨率串。与
    /// `Assets/LiteGame/Modules/Settings/Options/DisplayOptions.cs` 逐值一致（Next 侧重写）。</summary>
    public static class DisplayOptions
    {
        /// <summary>显示模式：独占全屏。</summary>
        public const int ModeExclusiveFullscreen = 0;

        /// <summary>显示模式：无边框全屏（窗口化全屏，Alt+Tab 友好——桌面默认）。</summary>
        public const int ModeBorderless = 1;

        /// <summary>显示模式：窗口。</summary>
        public const int ModeWindowed = 2;

        public const int DefaultMode = ModeBorderless;

        /// <summary>垂直同步：跟随画质档位内置值。</summary>
        public const int VSyncFollowPreset = -1;

        public const int VSyncOff = 0;

        public const int VSyncOn = 1;

        /// <summary>窗口模式默认尺寸（分辨率未指定时使用）。</summary>
        public const int WindowWidth = 1280;

        public const int WindowHeight = 720;

        public static bool IsMode(int value) => value >= ModeExclusiveFullscreen && value <= ModeWindowed;

        public static bool IsVSync(int value) => value >= VSyncFollowPreset && value <= VSyncOn;

        public static int SnapMode(int value) => IsMode(value) ? value : DefaultMode;

        public static int SnapVSync(int value) => IsVSync(value) ? value : VSyncFollowPreset;

        /// <summary>解析 <c>"1920x1080"</c>（大小写不敏感，允许 <c>×</c> 与空白）；空串＝未指定。</summary>
        public static bool TryParseResolution(string text, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string normalized = text.Trim().Replace("×", "x").Replace(" ", string.Empty).ToLowerInvariant();
            int separator = normalized.IndexOf('x');
            if (separator <= 0 || separator >= normalized.Length - 1) return false;
            return int.TryParse(normalized.Substring(0, separator), out width)
                && int.TryParse(normalized.Substring(separator + 1), out height)
                && width > 0 && height > 0;
        }

        /// <summary>宽高 → 存档串（InvariantCulture：区域设置不参与数字格式化）。</summary>
        public static string FormatResolution(int width, int height)
            => width.ToString(CultureInfo.InvariantCulture) + "x"
                + height.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>帧率上限档：可选档/默认值/引擎目标值换算。与
    /// `Packages/com.litegame.framework/Runtime/Services/Settings/FrameRateOptions.cs` 逐值一致（Next 侧重写）。</summary>
    public static class FrameRateOptions
    {
        /// <summary>无限制（引擎侧换算为 -1＝不设 targetFrameRate）。</summary>
        public const int Unlimited = 0;

        /// <summary>移动端既定档：60 帧。</summary>
        public const int MobileDefault = 60;

        private static readonly int[] DesktopOptions = { Unlimited, 30, 60, 90, 120, 144 };
        private static readonly int[] MobileOptions = { 30, MobileDefault };

        /// <summary>当前平台可选档（顺序即 UI 下拉顺序；只读，调用方不得改）。</summary>
        public static IReadOnlyList<int> For(bool isMobile) => isMobile ? MobileOptions : DesktopOptions;

        /// <summary>平台默认档（首启与"恢复默认"同源）。</summary>
        public static int DefaultFor(bool isMobile) => isMobile ? MobileDefault : Unlimited;

        /// <summary>值是否在当前平台档位内。</summary>
        public static bool IsValid(int cap, bool isMobile)
            => Array.IndexOf(isMobile ? MobileOptions : DesktopOptions, cap) >= 0;

        /// <summary>非法值收敛到"最接近的合法档"（不发明平台不支持的帧率）。</summary>
        public static int Snap(int cap, bool isMobile)
        {
            IReadOnlyList<int> options = For(isMobile);
            if (IsValid(cap, isMobile)) return cap;
            int best = options[0];
            int bestDistance = int.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                int distance = Math.Abs(options[i] - cap);
                if (distance < bestDistance) { best = options[i]; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>设置值 → 引擎目标帧率（0/负数 → -1＝不设上限）。</summary>
        public static int ToAppTarget(int cap) => cap <= Unlimited ? -1 : cap;
    }

    /// <summary>视角灵敏度值域。与 `Assets/LiteGame/Modules/Settings/Options/ControlOptions.cs` 逐值一致。</summary>
    public static class ControlOptions
    {
        public const float MinLookSensitivity = 0.2f;
        public const float MaxLookSensitivity = 3f;
        public const float DefaultLookSensitivity = 1f;

        public const float MinAdsSensitivity = 0.5f;
        public const float MaxAdsSensitivity = 1.5f;
        public const float DefaultAdsSensitivity = 1f;

        public static float ClampLookSensitivity(float value)
            => float.IsNaN(value) ? DefaultLookSensitivity : Math.Clamp(value, MinLookSensitivity, MaxLookSensitivity);

        public static float ClampAdsSensitivity(float value)
            => float.IsNaN(value) ? DefaultAdsSensitivity : Math.Clamp(value, MinAdsSensitivity, MaxAdsSensitivity);
    }
}
