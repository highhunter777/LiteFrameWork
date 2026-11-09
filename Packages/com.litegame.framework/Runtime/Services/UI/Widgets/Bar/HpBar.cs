// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab。
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 血条控件（模板 `Assets/LiteGame/Modules/UI/Content/Widgets/Tmp_HpBar.prefab`）：双条血格—— **前条**（UGUI <see cref="Slider"/>。
    /// </summary>
    public class HpBar : MonoBehaviour
    {
        public Slider Slider;                            // 前条驱动面（模板内 UGUI Slider）
        public Image BackFill;                           // 后条（半透明克隆层，延迟滑落）
        public TMP_Text ValueText;                       // 可选数值文本
        [UIWidgetProperty("数值文本格式")]
        [Tooltip("数值文本格式；{0}=当前 {1}=上限。留空不显示数值")]
        public string ValueFormat = "{0}/{1}";
        [UIWidgetProperty("后条追赶速度")]
        [Tooltip("后条滑落追赶速度（越大越快贴合前条）")]
        public float BackLerpSpeed = 2f;

        private float _current, _max = 1f;
        private float _back = 1f;                        // 后条当前归一化值（0..1）

        /// <summary>设置血量（0~max）。前条立即到位，后条逐帧追赶。</summary>
        public void Set(float current, float max)
        {
            _current = current;
            _max = max > 0f ? max : 1f;
            Apply();
        }

        private void Apply()
        {
            float v = Mathf.Clamp01(_current / _max);
            if (Slider != null) Slider.SetValueWithoutNotify(v);
            if (v > _back) _back = v;                    // 上升瞬时跟上（不拖尾）
            SetBackAnchor(_back);
            if (ValueText != null && !string.IsNullOrEmpty(ValueFormat))
                ValueText.text = string.Format(ValueFormat, Mathf.RoundToInt(_current), Mathf.RoundToInt(_max));
        }

        private void Update()
        {
            if (BackFill == null) return;
            float target = Slider != null ? Slider.value : 0f;
            if (_back > target)
            {
                _back = Mathf.Lerp(_back, target, UIAnimationClock.Delta * BackLerpSpeed);
                if (_back - target < 0.001f) _back = target;   // 收敛吸附
            }
            else _back = target;
            SetBackAnchor(_back);
        }

        /// <summary>后条锚点手势（与 UGUI Slider 对 fillRect 的可视更新同款——只改 anchorMax.x）。</summary>
        private void SetBackAnchor(float value01)
        {
            if (BackFill == null) return;
            var rt = (RectTransform)BackFill.transform;
            var max = rt.anchorMax;
            if (!Mathf.Approximately(max.x, value01))
            {
                max.x = value01;
                rt.anchorMax = max;
            }
        }
    }
}
