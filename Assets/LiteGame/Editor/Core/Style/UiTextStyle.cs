using System;
using System.Linq;
using LiteFramework.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>一处文字档的完整定义：字号/字重来自令牌（`font.*`），对齐/换行/溢出是表现档（非令牌）。</summary>
    public readonly struct UiTextStyleDef
    {
        public readonly UiTextRole Role;
        public readonly float FontSize;
        public readonly TextAlignmentOptions TmpAlignment;   // TMP 对齐（完整值——垂直统一中行）
        public readonly TextAnchor LegacyAlignment;          // UGUI Text 对齐
        public readonly bool WordWrap;                        // 换行开/关
        public readonly bool Truncate;                        // 初始溢出策略：true = 截断；false = Overflow（容器约束由布局承担）
        public readonly bool Bold;                            // 字重加粗（TMP fontStyle Bold 位 / UGUI Bold）

        public UiTextStyleDef(UiTextRole role, float fontSize, TextAlignmentOptions tmpAlignment,
            TextAnchor legacyAlignment, bool wordWrap, bool truncate, bool bold)
        {
            Role = role;
            FontSize = fontSize;
            TmpAlignment = tmpAlignment;
            LegacyAlignment = legacyAlignment;
            WordWrap = wordWrap;
            Truncate = truncate;
            Bold = bold;
        }
    }

    /// <summary>
    /// 文字档读口（《UI编辑器工具专项设计》§3.1；P2-3a 起**值不再住在本类**）：
    /// 字号/字重从 <see cref="UiTokenCatalog"/> 的 `font.*` 令牌物化，对齐/换行/溢出仍是表现档常量
    /// （设计令牌词表不含对齐，见总设计 §6.1）。`All`/`TryGet` 保留原签名与返回类型，
    /// 每次调用重新物化（不做静态缓存——域重载/重新生成后必须读到新值）。
    /// </summary>
    public static class UiTextStyle
    {
        /// <summary>角色 → 令牌 ID + 表现档（唯一映射表；新增角色＝加一行，不动读取逻辑）。</summary>
        private static readonly (UiTextRole Role, string TokenId, TextAlignmentOptions TmpAlignment,
            TextAnchor LegacyAlignment, bool WordWrap, bool Truncate)[] Presets =
        {
            (UiTextRole.Title, "font.title", TextAlignmentOptions.Center, TextAnchor.MiddleCenter, false, true),
            (UiTextRole.Body, "font.body", TextAlignmentOptions.Center, TextAnchor.MiddleCenter, true, false),
            (UiTextRole.Caption, "font.caption", TextAlignmentOptions.MidlineLeft, TextAnchor.MiddleLeft, true, false),
            (UiTextRole.LabelSingle, "font.label", TextAlignmentOptions.Center, TextAnchor.MiddleCenter, false, true),
            (UiTextRole.Emphasis, "font.emphasis", TextAlignmentOptions.Center, TextAnchor.MiddleCenter, true, false),
        };

        /// <summary>档位全集（按角色序；每次从默认主题资产物化，顺序稳定供遍历单源）。</summary>
        public static UiTextStyleDef[] All => BuildFrom(UiTokenCatalog.Load());

        /// <summary>从给定目录物化（工具一次装载、逐节点复用，不重复读资产）。</summary>
        public static UiTextStyleDef[] BuildFrom(UiTokenCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return Presets.Select(preset => Build(preset, catalog)).ToArray();
        }

        /// <summary>按角色取档（未知角色不猜——返回 false 由调用方显性处理）。</summary>
        public static bool TryGet(UiTextRole role, out UiTextStyleDef def)
            => TryGet(role, UiTokenCatalog.Load(), out def);

        /// <summary>按角色取档（给定目录版本；工具循环内用这个，避免逐节点装载）。</summary>
        public static bool TryGet(UiTextRole role, UiTokenCatalog catalog, out UiTextStyleDef def)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            foreach (var preset in Presets)
            {
                if (preset.Role != role) continue;
                def = Build(preset, catalog);
                return true;
            }
            def = default;
            return false;
        }

        private static UiTextStyleDef Build(
            (UiTextRole Role, string TokenId, TextAlignmentOptions TmpAlignment, TextAnchor LegacyAlignment,
                bool WordWrap, bool Truncate) preset, UiTokenCatalog catalog)
        {
            if (!catalog.TryTypography(preset.TokenId, out UiThemeTypographyEntry token))
                throw new InvalidOperationException("主题资产缺少文字令牌：" + preset.TokenId);

            return new UiTextStyleDef(preset.Role, token.FontSize, preset.TmpAlignment, preset.LegacyAlignment,
                preset.WordWrap, preset.Truncate, string.Equals(token.Weight, "Bold", StringComparison.Ordinal));
        }
    }
}
