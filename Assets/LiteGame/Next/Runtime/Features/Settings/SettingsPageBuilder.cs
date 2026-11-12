using System.Collections.Generic;
using LiteGameNext.Platform.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置页控件生成器：编辑期用模板克隆补齐 14 项 UI，运行期不执行。</summary>
    public static class SettingsPageBuilder
    {
        /// <summary>滑块项：绑定名 → (标签, 最小, 最大, 整型?)</summary>
        public static readonly (string Bind, string Label, float Min, float Max, bool Whole)[] Sliders =
        {
            ("Sld_RenderScale", "渲染缩放", -1f, 3f, true),
            ("Sld_AA", "抗锯齿", -1f, 3f, true),
            ("Sld_Shadow", "阴影质量", -1f, 3f, true),
            ("Sld_DisplayMode", "显示模式", 0f, 2f, true),
            ("Sld_FrameRate", "帧率上限", 0f, 5f, true),
            ("Sld_VSync", "垂直同步", -1f, 1f, true),
            ("Sld_LookSens", "视角灵敏度", 0.2f, 3f, false),
            ("Sld_AdsSens", "开镜灵敏度", 0.5f, 1.5f, false),
        };

        /// <summary>开关项：绑定名 → 标签</summary>
        public static readonly (string Bind, string Label)[] Toggles =
        {
            ("Tgl_Sfx", "音效"),
            ("Tgl_InvertY", "反转Y轴"),
        };

        /// <summary>只读文本项：绑定名 → 标签（值为字符串，用按钮循环切换）</summary>
        public static readonly (string Bind, string Label)[] Texts =
        {
            ("Btn_Language", "语言"),
            ("Txt_Resolution", "分辨率"),
            ("Txt_Bindings", "按键绑定"),
        };

        public static IEnumerable<string> AllBindNames()
        {
            foreach (var s in Sliders) yield return s.Bind;
            foreach (var t in Toggles) yield return t.Bind;
            foreach (var t in Texts) yield return t.Bind;
        }
    }
}
