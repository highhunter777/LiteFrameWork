using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>页签组（M4c）：N 个按钮互斥选中；选中态 = 可交互反转 + 高亮色。onTabChanged(index)。</summary>
    public class TabGroup : MonoBehaviour
    {
        [Tooltip("页签按钮（顺序即索引）")]
        public List<Button> Tabs = new List<Button>();
        public Color SelectedColor = new Color(1f, 0.8f, 0.3f);
        public Color NormalColor = Color.white;

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
            for (int i = 0; i < Tabs.Count; i++)
            {
                var img = Tabs[i].GetComponent<Image>();
                if (img != null) img.color = i == _selected ? SelectedColor : NormalColor;
            }
            OnTabChanged?.Invoke(index);
        }
    }

}
