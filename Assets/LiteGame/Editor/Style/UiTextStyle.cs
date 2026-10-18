using LiteClient.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.EditorTools.UI
{
    /// <summary>一处文字档基线的完整定义（§3.1 基线表的一行）。</summary>
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
    /// 文字样式档**单源**（《UI编辑器工具专项设计》§3.1——与 <see cref="UiStyle"/>（颜色 token）分工：
    /// 本档只辖文字（字号/对齐/换行/初始溢出策略/字重），不复制颜色值）。值 = §3.1 初始基线；
    /// 改基线＝改本表后重跑应用（幂等收敛），已声明的 <see cref="UiTextStyleRef"/> 标记不受影响。
    ///
    /// 对齐口径：**完整对齐**（水平按基线，垂直统一中行——档位是完整声明面，不是只动水平的模糊建议）。
    /// 溢出策略：Title/LabelSingle 初始 Truncate；Body/Caption/Emphasis 初始 Overflow
    /// （"仅滚动/自适应容器内允许扩展"由容器承担——工具不揣测容器）。
    /// 字重：TMP 无独立字重属性，加粗＝<see cref="FontStyles.Bold"/> 位；UGUI＝<see cref="FontStyle.Bold"/>。
    /// 基线的语言/可访问性扩展覆盖随真实需求声明（§3.1），当前即本表。
    /// </summary>
    public static class UiTextStyle
    {
        public static readonly UiTextStyleDef Title =
            new UiTextStyleDef(UiTextRole.Title, 24f, TextAlignmentOptions.Center, TextAnchor.MiddleCenter,
                wordWrap: false, truncate: true, bold: false);

        public static readonly UiTextStyleDef Body =
            new UiTextStyleDef(UiTextRole.Body, 20f, TextAlignmentOptions.Center, TextAnchor.MiddleCenter,
                wordWrap: true, truncate: false, bold: false);

        public static readonly UiTextStyleDef Caption =
            new UiTextStyleDef(UiTextRole.Caption, 14f, TextAlignmentOptions.MidlineLeft, TextAnchor.MiddleLeft,
                wordWrap: true, truncate: false, bold: false);

        public static readonly UiTextStyleDef LabelSingle =
            new UiTextStyleDef(UiTextRole.LabelSingle, 20f, TextAlignmentOptions.Center, TextAnchor.MiddleCenter,
                wordWrap: false, truncate: true, bold: false);

        public static readonly UiTextStyleDef Emphasis =
            new UiTextStyleDef(UiTextRole.Emphasis, 20f, TextAlignmentOptions.Center, TextAnchor.MiddleCenter,
                wordWrap: true, truncate: false, bold: true);

        /// <summary>档位全集（顺序稳定，供应用/对账遍历；§3.1 五角色基线）。</summary>
        public static readonly UiTextStyleDef[] All =
        {
            Title, Body, Caption, LabelSingle, Emphasis,
        };

        /// <summary>按角色取档（未知角色不猜——返回 false 由调用方显性处理）。</summary>
        public static bool TryGet(UiTextRole role, out UiTextStyleDef def)
        {
            foreach (var d in All)
                if (d.Role == role) { def = d; return true; }
            def = default;
            return false;
        }
    }
}
