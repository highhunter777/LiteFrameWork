using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>Toggle 页签组：新仓 prefab 的 Toggle 页签适配到旧 UI 的 SetTab/OnTabChanged 语义。</summary>
    public sealed class ToggleTabGroup : MonoBehaviour
    {
        public List<Toggle> Tabs = new List<Toggle>();

        public event Action<int> OnTabChanged;

        private int _selected = -1;

        public int Selected => _selected;

        private void Awake() => Bind();

        /// <summary>重新绑定 Tabs 的点击监听（导入时先设列表再调用；运行时 Awake 也会调用一次）。</summary>
        public void Bind()
        {
            _selected = -1;
            for (int i = 0; i < Tabs.Count; i++)
            {
                int index = i;
                Tabs[i].onValueChanged.RemoveAllListeners();
                Tabs[i].onValueChanged.AddListener(on => OnToggle(index, on));
                if (Tabs[i].isOn) _selected = index;
            }
        }

        /// <summary>编程切换（静默，不触发 OnTabChanged）。</summary>
        public void SelectSilent(int index)
        {
            if (index < 0 || index >= Tabs.Count) return;
            _selected = index;
            Apply(index);
        }

        /// <summary>用户点击切换（由 Toggle.onValueChanged 进入）。</summary>
        private void OnToggle(int index, bool on)
        {
            if (!on || _selected == index) return;
            _selected = index;
            Apply(index);
            OnTabChanged?.Invoke(index);
        }

        private void Apply(int selected)
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                Tabs[i].SetIsOnWithoutNotify(i == selected);
            }
        }
    }
}
