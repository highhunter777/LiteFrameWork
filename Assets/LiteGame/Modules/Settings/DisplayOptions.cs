using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 显示设置的值域与换算（显示模式 / 垂直同步 / 窗口尺寸 / 分辨率字符串），单源。
    ///
    /// 分辨率以 <c>"宽x高"</c> 字符串存档（不带刷新率）：刷新率随显示器走，存进去反而在换显示器时
    /// 变成"匹配不上的死值"。<see cref="WindowWidth"/>/<see cref="WindowHeight"/> 是"未指定分辨率"时
    /// 窗口模式的默认尺寸。
    /// </summary>
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
            => width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x"
                + height.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>宽高 → 展示串（<c>"1920 × 1080"</c>，只用于下拉项文本）。</summary>
        public static string FormatResolutionLabel(int width, int height)
            => width.ToString(System.Globalization.CultureInfo.InvariantCulture) + " × "
                + height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
