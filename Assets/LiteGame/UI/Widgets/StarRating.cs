using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>星级评分（M4c）：N 颗星（Image 子节点，顺序即星级），整星粒度；onChange 回调。</summary>
    public class StarRating : MonoBehaviour
    {
        public Image[] Stars;
        public Color OnColor = new Color(1f, 0.8f, 0.2f);
        public Color OffColor = new Color(0.3f, 0.3f, 0.3f);

        private int _value;
        public int Value => _value;
        public event Action<int> OnChanged;

        private void Awake()
        {
            // 运行时 AddComponent 先于字段赋值——Stars 尚为 null，跳过首刷（Set 时再刷）
            if (Stars != null && Stars.Length > 0) Apply();
        }

        /// <summary>设置星级（0~Stars.Length，超界钳制；静默同值）。</summary>
        public void Set(int value)
        {
            if (Stars == null || Stars.Length == 0) return;
            value = Mathf.Clamp(value, 0, Stars.Length);
            if (_value == value) return;
            _value = value;
            Apply();
            OnChanged?.Invoke(value);
        }

        private void Apply()
        {
            if (Stars == null) return;
            for (int i = 0; i < Stars.Length; i++)
                if (Stars[i] != null) Stars[i].color = i < _value ? OnColor : OffColor;
        }
    }

}
