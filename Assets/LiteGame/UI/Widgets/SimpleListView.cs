// 拆自 VirtualListSource.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 最小渲染验证件（灰盒，M4 §2.5）：模板克隆 N 项纵向排布，Refresh 逐项回调数据源。
    /// **不做回收/裁剪**——只证明"数据源 → 渲染项"口子可用；SetSource 后数据变更再调 Refresh。
    /// </summary>
    public sealed class SimpleListView : MonoBehaviour
    {
        [Tooltip("渲染项模板（保持非激活态）")]
        public RectTransform Template;
        public int Spacing = 8;

        private IVirtualListSource _source;
        private readonly List<RectTransform> _items = new List<RectTransform>(16);

        public void SetSource(IVirtualListSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Refresh();
        }

        /// <summary>数据变更后手动刷新（验证件无脏标记机制）。</summary>
        public void Refresh()
        {
            if (_source == null || Template == null) return;
            const int cap = 64;                              // 口子验证上限——完整虚拟化 M4c
            int n = Math.Min(_source.Count, cap);
            while (_items.Count < n)
            {
                var item = Instantiate(Template, Template.parent);
                item.gameObject.SetActive(true);
                _items.Add(item);
            }
            for (int i = 0; i < _items.Count; i++)
            {
                bool used = i < n;
                _items[i].gameObject.SetActive(used);
                if (!used) continue;
                _items[i].anchoredPosition = new Vector2(0f, -i * (Template.sizeDelta.y + Spacing));
                _source.Bind(i, _items[i]);
            }
        }
    }
}
