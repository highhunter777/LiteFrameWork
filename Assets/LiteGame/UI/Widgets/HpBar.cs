// 拆自 Progress.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>血条（M4c）：双条血格——前条瞬时响应，后条延迟滑落（打击感标准做法）。底层即两条 ProgressBar。</summary>
    public class HpBar : MonoBehaviour
    {
        public Image FrontFill;                          // 前条（瞬时）
        public Image BackFill;                           // 后条（延迟滑落）
        public TMP_Text ValueText;
        public float BackLerpSpeed = 2f;

        private float _current, _max = 1f;

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
            if (FrontFill != null) FrontFill.fillAmount = v;
            if (ValueText != null) ValueText.text = $"{Mathf.RoundToInt(_current)}/{Mathf.RoundToInt(_max)}";
        }

        private void Update()
        {
            float target = Mathf.Clamp01(_current / _max);
            if (BackFill != null && BackFill.fillAmount > target)
                BackFill.fillAmount = Mathf.Lerp(BackFill.fillAmount, target, Time.deltaTime * BackLerpSpeed);
        }
    }
}
