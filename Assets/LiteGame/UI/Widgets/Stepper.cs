// 拆自 Input.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>步进器（M4c）：-/值/+，min/max/step 钳制；onChanged 出整数值。</summary>
    public class Stepper : MonoBehaviour
    {
        public Button Minus;
        public Button Plus;
        public TMP_Text ValueLabel;
        public int Min;
        public int Max = 10;
        public int Step = 1;

        private int _value;
        public int Value => _value;
        public event Action<int> OnChanged;

        private void Awake()
        {
            if (Minus != null) Minus.onClick.AddListener(() => Set(_value - Step));
            if (Plus != null) Plus.onClick.AddListener(() => Set(_value + Step));
            Render();
        }

        public void Set(int value)
        {
            value = Mathf.Clamp(value, Min, Max);
            if (_value == value) return;
            _value = value;
            Render();
            OnChanged?.Invoke(value);
        }

        private void Render()
        {
            if (ValueLabel != null) ValueLabel.text = _value.ToString();
            if (Minus != null) Minus.interactable = _value > Min;
            if (Plus != null) Plus.interactable = _value < Max;
        }
    }

}
