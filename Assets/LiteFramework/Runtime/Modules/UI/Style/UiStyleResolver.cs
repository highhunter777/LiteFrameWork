using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>引用应用结果（《UI 系统总设计》§6.2）：编辑器工具报告、校验规则与运行期 Warning 三方共用的判据单源。</summary>
    public enum UiStyleApplyResult
    {
        /// <summary>已按声明写入令牌现值。</summary>
        Applied,

        /// <summary>声明为空（未令牌化，合法——应用跳过）。</summary>
        NoToken,

        /// <summary>声明的令牌 ID 不在令牌集——不猜默认色，由调用方报告。</summary>
        UnknownToken,

        /// <summary>节点上没有目标 Graphic。</summary>
        NoTarget,
    }

    /// <summary>
    /// 令牌解析核心单源（《UI 系统总设计》§6.1「运行时通过 UiStyleResolver 解析，不读 JSON」）：
    /// 从 <see cref="UiThemeAsset"/>（生成物 SO）读颜色/文字令牌；编辑器读面 <c>UiTokenCatalog</c> 委托本类。
    /// 纯类、实例传递（禁静态缓存——沿 P2-3a 冻结口径：编辑器每次操作装载一次后逐层下传）；
    /// 颜色命中阈值（原 UiStyleTool.HitEpsilon）驻本类为单源，工具/规则/阈值面引用同一常量。
    /// 口径与 P2-3a 的 UiTokenCatalog 一致：重复 ID 后写覆盖、空 ID 跳过、未知 ID 不猜默认色。
    /// </summary>
    public sealed class UiStyleResolver
    {
        /// <summary>颜色命中阈值：逐通道差平方和 ≤ 此值视为同一令牌值（吸收浮点噪声与 8bit 量化差 ≤0.5/255）。</summary>
        public const float HitEpsilon = 1e-4f;

        private readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, UiThemeTypographyEntry> _typography =
            new Dictionary<string, UiThemeTypographyEntry>(StringComparer.Ordinal);

        /// <summary>资产里记录的主题名（诊断/报告用）。</summary>
        public string Theme { get; private set; }

        /// <summary>资产里记录的令牌源指纹（诊断/报告用）。</summary>
        public string SourceHash { get; private set; }

        /// <summary>颜色令牌数。</summary>
        public int ColorCount => _colors.Count;

        /// <summary>文字令牌数。</summary>
        public int TypographyCount => _typography.Count;

        /// <summary>颜色令牌（按 ID `Ordinal` 排序，顺序稳定供最近匹配/遍历/断言）。</summary>
        public IReadOnlyList<KeyValuePair<string, Color>> Colors
            => _colors.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        /// <summary>文字令牌（按 ID `Ordinal` 排序）。</summary>
        public IReadOnlyList<KeyValuePair<string, UiThemeTypographyEntry>> Typography
            => _typography.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        /// <summary>从主题资产构建（编辑器/运行时同源）。</summary>
        public static UiStyleResolver FromAsset(UiThemeAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            var resolver = new UiStyleResolver { Theme = asset.Theme, SourceHash = asset.SourceHash };
            foreach (UiThemeColorEntry entry in asset.Colors ?? Array.Empty<UiThemeColorEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                resolver._colors[entry.Id] = entry.Value;      // 重复 ID 后写覆盖（P2-3a 口径）
            }
            foreach (UiThemeTypographyEntry entry in asset.Typography ?? Array.Empty<UiThemeTypographyEntry>())
            {
                if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                resolver._typography[entry.Id] = entry;
            }
            return resolver;
        }

        /// <summary>按 ID 取颜色令牌；未知 ID 返回 false（由调用方显式处理，不猜默认色）。</summary>
        public bool TryColor(string id, out Color value)
            => _colors.TryGetValue(id ?? string.Empty, out value);

        /// <summary>按 ID 取文字令牌；未知 ID 返回 false。</summary>
        public bool TryTypography(string id, out UiThemeTypographyEntry entry)
            => _typography.TryGetValue(id ?? string.Empty, out entry);

        /// <summary>把 <paramref name="styleRef"/> 声明的令牌现值写到 <paramref name="target"/>（不猜色、不改结构）。</summary>
        public UiStyleApplyResult Apply(UiStyleRef styleRef, Graphic target)
        {
            if (target == null) return UiStyleApplyResult.NoTarget;
            if (styleRef == null || string.IsNullOrEmpty(styleRef.ColorToken)) return UiStyleApplyResult.NoToken;
            if (!TryColor(styleRef.ColorToken, out Color value)) return UiStyleApplyResult.UnknownToken;
            target.color = value;
            return UiStyleApplyResult.Applied;
        }

        /// <summary>取值序（P2-3 B4 单源）：token 非空且可解析 → 令牌现值；否则 fallback（未注入/未令牌化/未知——不猜色）。</summary>
        public Color Resolve(string token, Color fallback)
            => !string.IsNullOrEmpty(token) && TryColor(token, out Color value) ? value : fallback;

        /// <summary>颜色与令牌值的命中判定（逐通道差平方和 ≤ <see cref="HitEpsilon"/>——最近匹配与规则漂移共用单源）。</summary>
        public static bool Hits(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
            return dr * dr + dg * dg + db * db + da * da <= HitEpsilon;
        }
    }
}
