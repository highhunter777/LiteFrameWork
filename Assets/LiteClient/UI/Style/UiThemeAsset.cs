using System;
using UnityEngine;

namespace LiteClient.UI
{
    /// <summary>
    /// 主题颜色令牌（《UI 编辑器工具链专项设计》§8.1/§8.3）：`UiThemeAsset` 内的一条记录。
    /// `Id` 为冻结的设计令牌 ID（如 `color.bg.screen`），`Value` 为 8bit 量化值（JSON 的 `#RRGGBBAA`）。
    /// </summary>
    [Serializable]
    public sealed class UiThemeColorEntry
    {
        public string Id;
        public Color32 Value;
        public string Description;
    }

    /// <summary>主题文字令牌：字体族/字号/行高/字重/字距（字形与排版由令牌声明，应用器在 B2）。</summary>
    [Serializable]
    public sealed class UiThemeTypographyEntry
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
    public sealed class UiThemeNumberEntry
    {
        public string Id;
        public float Value;
        public string Description;
    }

    /// <summary>主题层级令牌（阴影/描边）。</summary>
    [Serializable]
    public sealed class UiThemeElevationEntry
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
    public sealed class UiThemeMotionEntry
    {
        public string Id;
        public float DurationMs;
        public string Easing;
        public string Description;
    }

    /// <summary>
    /// UI 主题资产（**生成物，禁手改**；《UI 编辑器工具链专项设计》§8.1「令牌源：JSON → 生成 UiThemeAsset（SO）」、
    /// §8.3 生成物表）。由编辑器工具 `ui.style.tokens` 从 `UiStyleTokens.json` 确定性生成；
    /// 数组按令牌 ID `Ordinal` 排序，`SourceHash` 为规范 JSON 文本的 SHA-256。
    ///
    /// **纯数据**：本类不注册容器、不读运行时状态、无运行期消费者（B2 起由样式解析/引用组件消费）；
    /// 编辑器读面走 `<c>UiTokenCatalog</c>`（不静态缓存）。
    /// </summary>
    public sealed class UiThemeAsset : ScriptableObject
    {
        public string SchemaVersion;
        public string Theme;
        public string Version;
        public string SourceHash;

        public UiThemeColorEntry[] Colors = Array.Empty<UiThemeColorEntry>();
        public UiThemeTypographyEntry[] Typography = Array.Empty<UiThemeTypographyEntry>();
        public UiThemeNumberEntry[] Spacing = Array.Empty<UiThemeNumberEntry>();
        public UiThemeNumberEntry[] Radius = Array.Empty<UiThemeNumberEntry>();
        public UiThemeElevationEntry[] Elevation = Array.Empty<UiThemeElevationEntry>();
        public UiThemeMotionEntry[] Motion = Array.Empty<UiThemeMotionEntry>();
        public UiThemeNumberEntry[] Icon = Array.Empty<UiThemeNumberEntry>();
    }
}
