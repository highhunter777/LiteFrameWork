using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteClient.UI
{
    /// <summary>页签组：N 个按钮互斥选中；选中态 = 可交互反转 + 高亮色。onTabChanged(index)。
    /// 状态色令牌化（P2-3 B2）：伴生 token 字段（空＝未令牌化用序列化色；装配期经 <see cref="IThemeBound"/> 注入解析）。</summary>
    public class TabGroup : MonoBehaviour, IThemeBound
    {
        [Tooltip("页签按钮（顺序即索引）")]
        public List<Button> Tabs = new List<Button>();
        public Color SelectedColor = new Color(1f, 0.8f, 0.3f);
        public Color NormalColor = Color.white;

        [Tooltip("选中色令牌（空＝未令牌化，用序列化色）")]
        public string SelectedColorToken;
        [Tooltip("常态色令牌（空＝未令牌化）")]
        public string NormalColorToken;


        private int _selected = -1;
        public int Selected => _selected;
        public event Action<int> OnTabChanged;

        private void Awake()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                int index = i;
                Tabs[i].onClick.AddListener(() => Select(index));
            }
        }

        /// <summary>选中页签（编程切换与点击同一入口；不触发 changed 的静默版见 SelectSilent）。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= Tabs.Count) return;
            if (_selected == index) return;
            _selected = index;
            RefreshTabs();
            OnTabChanged?.Invoke(index);
        }

        private void RefreshTabs()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                var img = Tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _selected ? Pick(SelectedColor, SelectedColorToken) : Pick(NormalColor, NormalColorToken);
            }
        }

        /// <summary>取值序（P2-3 B2）：token 已填且可解析 → 令牌现值；否则序列化 fallback（不猜色）。</summary>
        private Color Pick(Color fallback, string token)
            => _theme != null && !string.IsNullOrEmpty(token) && _theme.TryColor(token, out Color value)
                ? value
                : fallback;

        private UiStyleResolver _theme;

        /// <summary>主题注入（IThemeBound）：持解析器实例并刷新表现；未注入走序列化 fallback，行为不变。</summary>
        public void OnThemeBound(UiStyleResolver resolver)
        {
            _theme = resolver;
            if (_selected >= 0) RefreshTabs();
        }
    }

}
