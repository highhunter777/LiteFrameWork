using System;
using System.Collections.Generic;
using System.Linq;
using LiteFramework.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>一次主题生成的报告（工具输出、夹具断言与规则文案共用同一形状）。</summary>
    public sealed class UIThemeGenerationResult
    {
        public bool Success;
        public bool Changed;
        public string JsonPath;
        public string AssetPath;
        public string SourceHash;
        public IReadOnlyList<string> Errors = Array.Empty<string>();
        public int ColorCount;
        public int TypographyCount;
        public int SpacingCount;
        public int RadiusCount;
        public int ElevationCount;
        public int MotionCount;
        public int IconCount;

        public string Summary()
        {
            if (!Success) return "UI 主题生成失败：" + string.Join("；", Errors);
            string counts = "色 " + ColorCount + " / 字 " + TypographyCount + " / 距 " + SpacingCount
                + " / 角 " + RadiusCount + " / 层 " + ElevationCount + " / 动 " + MotionCount
                + " / 图 " + IconCount;
            return "UI 主题：" + counts + "；sourceHash=" + SourceHash + "；变更=" + (Changed ? "是" : "否");
        }

        internal static UIThemeGenerationResult Fail(string jsonPath, string assetPath, IEnumerable<string> errors)
            => new UIThemeGenerationResult
            {
                Success = false,
                Changed = false,
                JsonPath = jsonPath,
                AssetPath = assetPath,
                Errors = (errors ?? Enumerable.Empty<string>()).ToList(),
            };
    }

    /// <summary>
    /// 主题资产生成器（《UI 编辑器工具链专项设计》§8.1「令牌源：JSON → 生成 UIThemeAsset（SO）」）： 读令牌源 → 校验（一次收集全部问题）→ 规范文本算 `sourceHash` → 与盘上 SO。
    /// </summary>
    public static class UIThemeAssetGenerator
    {
        /// <summary>生成/刷新主题资产；`jsonPath`/`assetPath` 为工程根相对路径，缺省走 <see cref="UIThemePaths"/>。</summary>
        public static UIThemeGenerationResult Generate(string jsonPath = null, string assetPath = null)
        {
            string json = string.IsNullOrEmpty(jsonPath) ? UIThemePaths.DefaultJsonPath : jsonPath;
            string asset = string.IsNullOrEmpty(assetPath) ? UIThemePaths.DefaultAssetPath : assetPath;

            if (!asset.EndsWith(".asset", StringComparison.Ordinal))
                return UIThemeGenerationResult.Fail(json, asset, new[] { "资产后缀必须是 .asset：" + asset });

            // 归一后校验归属：拒绝 "Assets/../…" 这类前缀合法但实际越界的路径。
            string fullAsset = System.IO.Path.GetFullPath(UIThemePaths.FullPath(asset));
            string assetsRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UIThemePaths.ProjectRoot, "Assets")) + System.IO.Path.DirectorySeparatorChar;
            if (!fullAsset.StartsWith(assetsRoot, StringComparison.Ordinal))
                return UIThemeGenerationResult.Fail(json, asset, new[] { "资产路径必须在 Assets/ 下：" + asset });

            string fullJson = UIThemePaths.FullPath(json);
            if (!System.IO.File.Exists(fullJson))
                return UIThemeGenerationResult.Fail(json, asset, new[] { "令牌源不存在：" + json });

            UIThemeTokens tokens;
            try
            {
                tokens = UIThemeTokenJson.Deserialize(System.IO.File.ReadAllText(fullJson));
            }
            catch (Exception error)
            {
                return UIThemeGenerationResult.Fail(json, asset,
                    new[] { "令牌源 JSON 非法（" + json + "）：" + error.Message });
            }

            IReadOnlyList<string> issues = UIThemeTokenValidator.Validate(tokens);
            if (issues.Count > 0) return UIThemeGenerationResult.Fail(json, asset, issues);

            string sourceHash = UIThemeTokenJson.HashCanonical(UIThemeTokenJson.Serialize(tokens));
            UIThemeSnapshot expected = UIThemeSnapshot.FromTokens(tokens, sourceHash);

            UIThemeAsset existing = AssetDatabase.LoadAssetAtPath<UIThemeAsset>(asset);
            bool changed = true;
            if (existing != null)
            {
                string difference = expected.DescribeDifference(UIThemeSnapshot.FromAsset(existing));
                changed = difference != null;
            }

            if (changed) Write(expected, asset);

            return new UIThemeGenerationResult
            {
                Success = true,
                Changed = changed,
                JsonPath = json,
                AssetPath = asset,
                SourceHash = sourceHash,
                ColorCount = expected.Colors.Length,
                TypographyCount = expected.Typography.Length,
                SpacingCount = expected.Spacing.Length,
                RadiusCount = expected.Radius.Length,
                ElevationCount = expected.Elevation.Length,
                MotionCount = expected.Motion.Length,
                IconCount = expected.Icon.Length,
            };
        }

        /// <summary>把快照写入资产（创建或原地更新）；供生成器内部与夹具复用。</summary>
        internal static bool Write(UIThemeSnapshot snapshot, string assetPath)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("资产路径为空", nameof(assetPath));

            EnsureFolder(assetPath);

            UIThemeAsset asset = AssetDatabase.LoadAssetAtPath<UIThemeAsset>(assetPath);
            bool created = asset == null;
            if (created) asset = ScriptableObject.CreateInstance<UIThemeAsset>();

            Apply(snapshot, asset);

            if (created) AssetDatabase.CreateAsset(asset, assetPath);
            else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>把快照逐字段写入资产（数组按快照顺序；同一快照写两次得到同样内容）。</summary>
        internal static void Apply(UIThemeSnapshot snapshot, UIThemeAsset asset)
        {
            asset.SchemaVersion = snapshot.SchemaVersion;
            asset.Theme = snapshot.Theme;
            asset.Version = snapshot.Version;
            asset.SourceHash = snapshot.SourceHash;
            asset.Colors = snapshot.Colors
                .Select(item => new UIThemeColorEntry { Id = item.Id, Value = item.Value, Description = item.Description })
                .ToArray();
            asset.Typography = snapshot.Typography
                .Select(item => new UIThemeTypographyEntry
                {
                    Id = item.Id, FontFamily = item.FontFamily, FontSize = item.FontSize,
                    LineHeight = item.LineHeight, Weight = item.Weight, LetterSpacing = item.LetterSpacing,
                    Description = item.Description,
                }).ToArray();
            asset.Spacing = snapshot.Spacing
                .Select(item => new UIThemeNumberEntry { Id = item.Id, Value = item.Value, Description = item.Description }).ToArray();
            asset.Radius = snapshot.Radius
                .Select(item => new UIThemeNumberEntry { Id = item.Id, Value = item.Value, Description = item.Description }).ToArray();
            asset.Icon = snapshot.Icon
                .Select(item => new UIThemeNumberEntry { Id = item.Id, Value = item.Value, Description = item.Description }).ToArray();
            asset.Elevation = snapshot.Elevation
                .Select(item => new UIThemeElevationEntry
                {
                    Id = item.Id, Color = item.Color, OffsetX = item.OffsetX, OffsetY = item.OffsetY,
                    Blur = item.Blur, Spread = item.Spread, Description = item.Description,
                }).ToArray();
            asset.Motion = snapshot.Motion
                .Select(item => new UIThemeMotionEntry
                {
                    Id = item.Id, DurationMs = item.DurationMs, Easing = item.Easing, Description = item.Description,
                }).ToArray();
        }

        private static void EnsureFolder(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder)) EnsureFolderRecursive(folder);
        }

        private static void EnsureFolderRecursive(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, folder, StringComparison.Ordinal)) return;
            EnsureFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
