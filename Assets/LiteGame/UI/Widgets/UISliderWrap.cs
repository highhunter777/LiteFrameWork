// 拆自 Input.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>滑条封装：Set（免回调）与 onChanged 分离。</summary>
    public class UISliderWrap : MonoBehaviour
    {
        public Slider Slider;
        public event Action<float> OnChanged;

        private void Awake()
        {
            if (Slider == null) Slider = GetComponent<Slider>();
            if (Slider != null) Slider.onValueChanged.AddListener(v => OnChanged?.Invoke(v));
        }

        public void Set(float value) { if (Slider != null) Slider.SetValueWithoutNotify(value); }
        public float Value => Slider != null ? Slider.value : 0f;
    }

}
