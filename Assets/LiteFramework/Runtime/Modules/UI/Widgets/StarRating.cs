using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>星级评分：N 颗星（Image 子节点，顺序即星级），整星粒度；onChange 回调。
    /// 状态色令牌化（P2-3 B2）：伴生 token 字段（空＝未令牌化用序列化色；装配期经 <see cref="IThemeBound"/> 注入解析）。</summary>
    public class StarRating : MonoBehaviour, IThemeBound, IThemeGraphicWriter
    {
        public Image[] Stars;
        public Color OnColor;
        public Color OffColor;

        [Tooltip("点亮色令牌（空＝未令牌化，用序列化色）")]
        public string OnColorToken;
        [Tooltip("熄灭色令牌（空＝未令牌化）")]
        public string OffColorToken;


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
                if (Stars[i] != null) Stars[i].color = i < _value ? Resolve(OnColor, OnColorToken) : Resolve(OffColor, OffColorToken);
        }

        /// <summary>取值序（P2-3 B4）：token 可解析 → 令牌现值；否则序列化 fallback（不猜色）。</summary>
        private Color Resolve(Color fallback, string token)
            => _theme?.Resolve(token, fallback) ?? fallback;

        private UiStyleResolver _theme;

        /// <summary>主题注入（IThemeBound）：持解析器实例并刷新表现；未注入走序列化 fallback，行为不变。</summary>
        public void OnThemeBound(UiStyleResolver resolver)
        {
            _theme = resolver;
            if (Stars != null && Stars.Length > 0) Apply();
        }

        /// <summary>写权：只拥有 Stars 列表里的 Image（不认领子树）。</summary>
        public bool Owns(UnityEngine.UI.Graphic graphic)
        {
            if (graphic == null || Stars == null) return false;
            for (int i = 0; i < Stars.Length; i++)
                if (Stars[i] == graphic) return true;
            return false;
        }
    }

}
