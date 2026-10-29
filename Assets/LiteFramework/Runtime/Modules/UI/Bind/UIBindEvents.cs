using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 绑定索引的**输入控件订阅域**：滑块/开关/下拉的用户变化回调登记与退订
    /// （同名替换、<c>UnbindAll</c> 全退——池化复用不留旧回调）。
    /// 控件解析仍走索引的受控查询面（保持 <c>UIDropdownWrap</c> 优先语义）。
    /// </summary>
    internal sealed class UIBindEvents
    {
        private readonly UIBindIndex _index;
        private readonly Dictionary<string, Action> _controlUnsub = new Dictionary<string, Action>(8);

        public UIBindEvents(UIBindIndex index)
            => _index = index ?? throw new ArgumentNullException(nameof(index));

        /// <summary>滑块变化通知（用户拖动；回调带值）。重复登记替换；<c>null</c> = 仅退订。G15。</summary>
        public void OnSliderChanged(string name, Action<float> handler)
        {
            var slider = _index.Get<Slider>(name);
            SubscribeControl(name, () => slider.onValueChanged.RemoveAllListeners());
            if (handler == null) return;
            UnityEngine.Events.UnityAction<float> h =
                v => SafeCall.Invoke(() => handler(v), $"OnSliderChanged[{name}]");
            slider.onValueChanged.AddListener(h);
        }

        /// <summary>开关变化通知（用户点击；回调带值）。重复登记替换；<c>null</c> = 仅退订。G16。</summary>
        public void OnToggleChanged(string name, Action<bool> handler)
        {
            var toggle = _index.Get<Toggle>(name);
            SubscribeControl(name, () => toggle.onValueChanged.RemoveAllListeners());
            if (handler == null) return;
            UnityEngine.Events.UnityAction<bool> h =
                v => SafeCall.Invoke(() => handler(v), $"OnToggleChanged[{name}]");
            toggle.onValueChanged.AddListener(h);
        }

        /// <summary>下拉变化通知（用户选择；回调带索引）。重复登记替换；<c>null</c> = 仅退订。G17。</summary>
        public void OnDropdownChanged(string name, Action<int> handler)
        {
            var control = _index.ResolveDropdown(name);
            if (control is UIDropdownWrap wrap)
            {
                Action<int> h = v => SafeCall.Invoke(() => handler(v), $"OnDropdownChanged[{name}]");
                SubscribeControl(name, () => wrap.OnIndexChanged -= h);
                if (handler != null) wrap.OnIndexChanged += h;
                return;
            }

            var dropdown = (Dropdown)control;
            SubscribeControl(name, () => dropdown.onValueChanged.RemoveAllListeners());
            if (handler == null) return;
            UnityEngine.Events.UnityAction<int> uh =
                v => SafeCall.Invoke(() => handler(v), $"OnDropdownChanged[{name}]");
            dropdown.onValueChanged.AddListener(uh);
        }

        /// <summary>全部退订（<c>UnbindAll</c>：池化复用不留旧回调）。</summary>
        public void ClearAll()
        {
            foreach (KeyValuePair<string, Action> kv in _controlUnsub) kv.Value();
            _controlUnsub.Clear();
        }

        /// <summary>控件订阅登记（同名替换：先退旧再登记——池化复用/重开不留双份回调）。</summary>
        private void SubscribeControl(string name, Action unsubscribe)
        {
            if (_controlUnsub.TryGetValue(name, out var old)) old();
            _controlUnsub[name] = unsubscribe;
        }
    }
}
