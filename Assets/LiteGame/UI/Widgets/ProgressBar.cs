using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>进度条（M4c）：直线（Filled Horizontal）与环形（Filled Radial）共用本件——
    /// 形态由 Image 的 fillMethod/精灵决定，组件只驱动 fillAmount 与可选数值文本。</summary>
    public class ProgressBar : MonoBehaviour
    {
        public Image Fill;                               // Image type=Filled
        public TMP_Text ValueText;                           // 可选
        [Tooltip("数值文本格式；{0}=当前 {1}=上限。留空不显示数值")]
        public string ValueFormat = "{0}/{1}";

        private float _current, _max = 1f;

        /// <summary>按归一化值设置（0~1）。</summary>
        public void Set(float value01)
        {
            _current = value01;
            _max = 1f;
            Apply();
        }

        /// <summary>按当前/上限设置（HpBar/经验条等）。</summary>
        public void Set(float current, float max)
        {
            _current = current;
            _max = max > 0f ? max : 1f;
            Apply();
        }

        private void Apply()
        {
            float v = _current / _max;
            if (Fill != null) Fill.fillAmount = Mathf.Clamp01(v);
            if (ValueText != null && !string.IsNullOrEmpty(ValueFormat))
                ValueText.text = string.Format(ValueFormat, Mathf.RoundToInt(_current), Mathf.RoundToInt(_max));
        }
    }

}
