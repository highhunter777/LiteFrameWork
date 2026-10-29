using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>页签组：N 个按钮互斥选中；选中态 = 可交互反转 + 高亮色。onTabChanged(index)。
    /// 状态色令牌化（P2-3 B2）：伴生 token 字段（空＝未令牌化用序列化色；装配期经 <see cref="IThemeBound"/> 注入解析）。</summary>
    public class TabGroup : MonoBehaviour, IThemeBound, IThemeGraphicWriter
    {
        [Tooltip("页签按钮（顺序即索引）")]
        public List<Button> Tabs = new List<Button>();
        public Color SelectedColor;
        public Color NormalColor;

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
                if (img != null) img.color = i == _selected ? Resolve(SelectedColor, SelectedColorToken) : Resolve(NormalColor, NormalColorToken);
            }
        }

        /// <summary>取值序（P2-3 B4）：token 可解析 → 令牌现值；否则序列化 fallback（不猜色）。</summary>
        private Color Resolve(Color fallback, string token)
            => _theme?.Resolve(token, fallback) ?? fallback;

        private UiStyleResolver _theme;

        /// <summary>主题注入（IThemeBound）：持解析器实例并刷新表现；未注入走序列化 fallback，行为不变。</summary>
        public void OnThemeBound(UiStyleResolver resolver)
        {
            _theme = resolver;
            if (_selected >= 0) RefreshTabs();
        }

        /// <summary>写权：只拥有 Tabs 列表上的按钮 Graphic（不认领子树）。</summary>
        public bool Owns(UnityEngine.UI.Graphic graphic)
        {
            if (graphic == null) return false;
            for (int i = 0; i < Tabs.Count; i++)
                if (Tabs[i] != null && Tabs[i].GetComponent<UnityEngine.UI.Image>() == graphic) return true;
            return false;
        }
    }

}
