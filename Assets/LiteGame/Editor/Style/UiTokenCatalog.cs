using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 令牌读面（《UI 编辑器工具链专项设计》§8.1「工具只读生成物与 prefab 引用」）：
    /// 从盘上 `UiThemeAsset` 装载颜色/文字令牌，供样式工具、校验规则与夹具共用。
    ///
    /// 纪律：**不做静态缓存**——编辑器域重载后静态缓存会读到旧值；样式工具每次操作只装载一次，
    /// 之后把实例逐层下传（不逐节点重新装载）。资产缺失或类型不符时显式失败，不返回空集冒充成功。
    /// </summary>
    public sealed class UiTokenCatalog
    {
        private readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, UiThemeTypographyEntry> _typography =
            new Dictionary<string, UiThemeTypographyEntry>(StringComparer.Ordinal);

        /// <summary>资产里记录的主题名（诊断/报告用）。</summary>
        public string Theme { get; private set; }

        /// <summary>资产里记录的令牌源指纹（诊断/报告用）。</summary>
        public string SourceHash { get; private set; }

        /// <summary>颜色令牌（按 ID `Ordinal` 排序，顺序稳定供遍历/断言）。</summary>
        public IReadOnlyList<KeyValuePair<string, Color>> Colors
            => _colors.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        /// <summary>文字令牌（按 ID `Ordinal` 排序）。</summary>
        public IReadOnlyList<KeyValuePair<string, UiThemeTypographyEntry>> Typography
            => _typography.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        /// <summary>按工程根相对路径装载（缺省＝<see cref="UiThemePaths.DefaultAssetPath"/>）。</summary>
        public static UiTokenCatalog Load(string assetPath = null)
        {
            string path = string.IsNullOrEmpty(assetPath) ? UiThemePaths.DefaultAssetPath : assetPath;
            UiThemeAsset asset = AssetDatabase.LoadAssetAtPath<UiThemeAsset>(path);
            if (asset == null)
            {
                string full = UiThemePaths.FullPath(path);
                if (!File.Exists(full)) throw new FileNotFoundException("主题资产不存在：" + path, full);
                throw new InvalidOperationException("主题资产类型不符（不是 UiThemeAsset）：" + path);
            }
            return FromAsset(asset);
        }

        /// <summary>从已加载资产构建（夹具可直接喂内存资产，不必落盘）。</summary>
        public static UiTokenCatalog FromAsset(UiThemeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            var catalog = new UiTokenCatalog { Theme = asset.Theme, SourceHash = asset.SourceHash };
            foreach (UiThemeColorEntry entry in asset.Colors ?? Array.Empty<UiThemeColorEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                catalog._colors[entry.Id] = entry.Value;
            }
            foreach (UiThemeTypographyEntry entry in asset.Typography ?? Array.Empty<UiThemeTypographyEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                catalog._typography[entry.Id] = entry;
            }
            return catalog;
        }

        /// <summary>取颜色令牌；未知 ID 返回 false（由调用方显式处理，不猜默认色）。</summary>
        public bool TryColor(string id, out Color value)
            => _colors.TryGetValue(id ?? string.Empty, out value);

        /// <summary>取文字令牌；未知 ID 返回 false。</summary>
        public bool TryTypography(string id, out UiThemeTypographyEntry entry)
            => _typography.TryGetValue(id ?? string.Empty, out entry);
    }
}
