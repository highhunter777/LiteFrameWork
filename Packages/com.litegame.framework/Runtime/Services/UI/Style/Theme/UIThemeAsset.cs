using System;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>
    /// 主题颜色令牌（《UI 编辑器工具链专项设计》§8.1/§8.3）：`UIThemeAsset` 内的一条记录。
    /// `Id` 为冻结的设计令牌 ID（如 `color.bg.screen`），`Value` 为 8bit 量化值（JSON 的 `#RRGGBBAA`）。
    /// </summary>
    [Serializable]
    public sealed class UIThemeColorEntry
    {
        public string Id;
        public Color32 Value;
        public string Description;
    }

    /// <summary>主题文字令牌：字体族/字号/行高/字重/字距（字形与排版由令牌声明，应用器在 B2）。</summary>
    [Serializable]
    public sealed class UIThemeTypographyEntry
    {
        public string Id;
        public string FontFamily;
        public float FontSize;
        public float LineHeight;
        public string Weight;
        public float LetterSpacing;
        public string Description;
    }

    /// <summary>主题数值令牌（间距/圆角/图标尺寸）。</summary>
    [Serializable]
    public sealed class UIThemeNumberEntry
    {
        public string Id;
        public float Value;
        public string Description;
    }

    /// <summary>主题层级令牌（阴影/描边）。</summary>
    [Serializable]
    public sealed class UIThemeElevationEntry
    {
        public string Id;
        public Color32 Color;
        public float OffsetX;
        public float OffsetY;
        public float Blur;
        public float Spread;
        public string Description;
    }

    /// <summary>主题动效令牌（时长/缓动）。</summary>
    [Serializable]
    public sealed class UIThemeMotionEntry
    {
        public string Id;
        public float DurationMs;
        public string Easing;
        public string Description;
    }

    /// <summary>主题链共享路径单源：生成器（编辑器侧写入）与运行期装载（内容服务 Acquire）引用同一常量。</summary>
    public static class UIThemePaths
    {
        /// <summary>生成物 SO 路径（随 `Assets/GameData/Config` 收集组进包；编辑器侧另有 JSON 设计态源路径，见 Editor 程序集）。</summary>
        public const string DefaultAssetPath = "Assets/GameData/Config/UI/UITheme.asset";
    }

    /// <summary>
    /// UI 主题资产（**生成物，禁手改**；《UI 编辑器工具链专项设计》§8.1「令牌源：JSON → 生成 UIThemeAsset（SO）」、 §8.3 生成物表）。
    /// </summary>
    public sealed class UIThemeAsset : ScriptableObject
    {
        public string SchemaVersion;
        public string Theme;
        public string Version;
        public string SourceHash;

        public UIThemeColorEntry[] Colors = Array.Empty<UIThemeColorEntry>();
        public UIThemeTypographyEntry[] Typography = Array.Empty<UIThemeTypographyEntry>();
        public UIThemeNumberEntry[] Spacing = Array.Empty<UIThemeNumberEntry>();
        public UIThemeNumberEntry[] Radius = Array.Empty<UIThemeNumberEntry>();
        public UIThemeElevationEntry[] Elevation = Array.Empty<UIThemeElevationEntry>();
        public UIThemeMotionEntry[] Motion = Array.Empty<UIThemeMotionEntry>();
        public UIThemeNumberEntry[] Icon = Array.Empty<UIThemeNumberEntry>();
    }
}
