using System;
using System.Collections.Generic;
using System.Linq;
using LiteClient.UI;
using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 主题快照（纯数据）：从令牌源或从盘上 `UiThemeAsset` 读出同一形状，供两处共用——
    /// 生成器的"是否需要写盘"判定与校验规则 `ui.style.token-drift` 的"SO 是否过期/被手改"判定。
    /// 两边只做逐字段比较，不各自实现第二套遍历。
    /// </summary>
    public sealed class UiThemeSnapshot
    {
        public string SchemaVersion;
        public string Theme;
        public string Version;
        public string SourceHash;
        public (string Id, Color32 Value, string Description)[] Colors;
        public (string Id, string FontFamily, float FontSize, float LineHeight, string Weight, float LetterSpacing, string Description)[] Typography;
        public (string Id, float Value, string Description)[] Spacing;
        public (string Id, float Value, string Description)[] Radius;
        public (string Id, Color32 Color, float OffsetX, float OffsetY, float Blur, float Spread, string Description)[] Elevation;
        public (string Id, float DurationMs, string Easing, string Description)[] Motion;
        public (string Id, float Value, string Description)[] Icon;

        /// <summary>从令牌源构建（调用方须先校验；非法颜色按令牌 ID 显式抛错，不静默丢弃）。</summary>
        public static UiThemeSnapshot FromTokens(UiThemeTokens tokens, string sourceHash)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            var snapshot = new UiThemeSnapshot
            {
                SchemaVersion = tokens.SchemaVersion,
                Theme = tokens.Meta?.Theme,
                Version = tokens.Meta?.Version,
                SourceHash = sourceHash,
                Colors = Sorted(tokens.Groups?.Color?.Tokens).Select(pair =>
                {
                    if (!UiThemeColorHex.TryParse(pair.Value?.Value, out Color32 value))
                        throw new InvalidOperationException("颜色令牌格式非法：" + pair.Key);
                    return (pair.Key, value, pair.Value.Description);
                }).ToArray(),
                Typography = Sorted(tokens.Groups?.Typography?.Tokens).Select(pair => (
                    pair.Key,
                    pair.Value?.FontFamily,
                    pair.Value?.FontSize ?? 0f,
                    pair.Value?.LineHeight ?? 0f,
                    pair.Value?.Weight,
                    pair.Value?.LetterSpacing ?? 0f,
                    pair.Value?.Description)).ToArray(),
                Spacing = NumberEntries(tokens.Groups?.Spacing?.Tokens),
                Radius = NumberEntries(tokens.Groups?.Radius?.Tokens),
                Icon = NumberEntries(tokens.Groups?.Icon?.Tokens),
                Elevation = Sorted(tokens.Groups?.Elevation?.Tokens).Select(pair =>
                {
                    if (!UiThemeColorHex.TryParse(pair.Value?.Color, out Color32 color))
                        throw new InvalidOperationException("层级令牌颜色格式非法：" + pair.Key);
                    return (pair.Key, color, pair.Value?.OffsetX ?? 0f, pair.Value?.OffsetY ?? 0f,
                        pair.Value?.Blur ?? 0f, pair.Value?.Spread ?? 0f, pair.Value?.Description);
                }).ToArray(),
                Motion = Sorted(tokens.Groups?.Motion?.Tokens).Select(pair => (
                    pair.Key, pair.Value?.DurationMs ?? 0f, pair.Value?.Easing, pair.Value?.Description)).ToArray(),
            };
            return snapshot;
        }

        /// <summary>从盘上资产构建（数组按 ID 排序后比较；null 视为空组）。</summary>
        public static UiThemeSnapshot FromAsset(UiThemeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return new UiThemeSnapshot
            {
                SchemaVersion = asset.SchemaVersion,
                Theme = asset.Theme,
                Version = asset.Version,
                SourceHash = asset.SourceHash,
                Colors = (asset.Colors ?? Array.Empty<UiThemeColorEntry>())
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => (entry.Id, entry.Value, entry.Description)).ToArray(),
                Typography = (asset.Typography ?? Array.Empty<UiThemeTypographyEntry>())
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => (entry.Id, entry.FontFamily, entry.FontSize, entry.LineHeight,
                        entry.Weight, entry.LetterSpacing, entry.Description)).ToArray(),
                Spacing = NumberEntries(asset.Spacing),
                Radius = NumberEntries(asset.Radius),
                Icon = NumberEntries(asset.Icon),
                Elevation = (asset.Elevation ?? Array.Empty<UiThemeElevationEntry>())
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => (entry.Id, entry.Color, entry.OffsetX, entry.OffsetY,
                        entry.Blur, entry.Spread, entry.Description)).ToArray(),
                Motion = (asset.Motion ?? Array.Empty<UiThemeMotionEntry>())
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry => (entry.Id, entry.DurationMs, entry.Easing, entry.Description)).ToArray(),
            };
        }

        /// <summary>逐字段比较（顺序已规范；浮点按精确相等——两边都来自同一份 JSON 数值）。</summary>
        public bool SameAs(UiThemeSnapshot other)
        {
            if (other == null) return false;
            return string.Equals(SchemaVersion, other.SchemaVersion, StringComparison.Ordinal)
                && string.Equals(Theme, other.Theme, StringComparison.Ordinal)
                && string.Equals(Version, other.Version, StringComparison.Ordinal)
                && string.Equals(SourceHash, other.SourceHash, StringComparison.Ordinal)
                && Colors.SequenceEqual(other.Colors)
                && Typography.SequenceEqual(other.Typography)
                && Spacing.SequenceEqual(other.Spacing)
                && Radius.SequenceEqual(other.Radius)
                && Elevation.SequenceEqual(other.Elevation)
                && Motion.SequenceEqual(other.Motion)
                && Icon.SequenceEqual(other.Icon);
        }

        /// <summary>首个差异的说明（生成报告/规则文案共用；相同返回 null）。</summary>
        public string DescribeDifference(UiThemeSnapshot other)
        {
            if (other == null) return "资产或令牌源缺失";
            if (!string.Equals(SchemaVersion, other.SchemaVersion, StringComparison.Ordinal))
                return "schemaVersion 不一致：盘上 " + other.SchemaVersion + " / 令牌源 " + SchemaVersion;
            if (!string.Equals(Theme, other.Theme, StringComparison.Ordinal))
                return "主题名不一致：盘上 " + other.Theme + " / 令牌源 " + Theme;
            if (!string.Equals(SourceHash, other.SourceHash, StringComparison.Ordinal))
                return "sourceHash 不一致：盘上 " + other.SourceHash + " / 令牌源 " + SourceHash;
            if (!string.Equals(Version, other.Version, StringComparison.Ordinal))
                return "版本不一致：盘上 " + other.Version + " / 令牌源 " + Version;
            if (!Colors.SequenceEqual(other.Colors)) return "颜色组不一致（" + Colors.Length + " vs " + other.Colors.Length + "）";
            if (!Typography.SequenceEqual(other.Typography)) return "文字组不一致（" + Typography.Length + " vs " + other.Typography.Length + "）";
            if (!Spacing.SequenceEqual(other.Spacing)) return "间距组不一致";
            if (!Radius.SequenceEqual(other.Radius)) return "圆角组不一致";
            if (!Elevation.SequenceEqual(other.Elevation)) return "层级组不一致";
            if (!Motion.SequenceEqual(other.Motion)) return "动效组不一致";
            if (!Icon.SequenceEqual(other.Icon)) return "图标组不一致";
            return null;
        }

        private static IEnumerable<KeyValuePair<string, T>> Sorted<T>(Dictionary<string, T> tokens)
            => (tokens ?? new Dictionary<string, T>()).OrderBy(pair => pair.Key, StringComparer.Ordinal);

        private static (string Id, float Value, string Description)[] NumberEntries(Dictionary<string, UiThemeNumberToken> tokens)
            => Sorted(tokens).Select(pair => (pair.Key, pair.Value?.Value ?? 0f, pair.Value?.Description)).ToArray();

        private static (string Id, float Value, string Description)[] NumberEntries(UiThemeNumberEntry[] entries)
            => (entries ?? Array.Empty<UiThemeNumberEntry>())
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(entry => (entry.Id, entry.Value, entry.Description)).ToArray();
    }
}
