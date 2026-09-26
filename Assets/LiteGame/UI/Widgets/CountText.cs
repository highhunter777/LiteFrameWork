// 拆自 Display.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>数值滚动文本（M4c）：CountUp 原语驱动，format 自定义（金币/伤害数字）。</summary>
    public class CountText : MonoBehaviour
    {
        public TMP_Text Label;
        public float Duration = 0.5f;

        /// <summary>从当前值滚动到目标值（首调从 0 起）。</summary>
        public void Roll(float target, string format = "N0")
        {
            if (Label == null) return;
            float from = float.TryParse(Label.text, out var cur) ? cur : 0f;
            UiFx.CountUp(Label, from, target, Duration, v => v.ToString(format));
        }

        /// <summary>直接定格（不走滚动）。</summary>
        public void Set(float value, string format = "N0") { if (Label != null) Label.text = value.ToString(format); }
    }

}
