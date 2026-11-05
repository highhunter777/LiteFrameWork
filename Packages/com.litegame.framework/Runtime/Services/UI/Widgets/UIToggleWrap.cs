// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>开关封装。</summary>
    public class UIToggleWrap : MonoBehaviour, IUIInteractable
    {
        public Toggle Toggle;
        public event Action<bool> OnChanged;

        private void Awake()
        {
            if (Toggle == null) Toggle = GetComponent<Toggle>();
            if (Toggle != null) Toggle.onValueChanged.AddListener(v => OnChanged?.Invoke(v));
        }

        public void Set(bool value) { if (Toggle != null) Toggle.SetIsOnWithoutNotify(value); }
        public bool Value => Toggle != null ? Toggle.isOn : false;

        /// <summary>可交互透传 inner Toggle（<see cref="IUIInteractable"/>——受控 API SetInteractable 的解析面）。</summary>
        public bool Interactable
        {
            get => Toggle != null && Toggle.interactable;
            set { if (Toggle != null) Toggle.interactable = value; }
        }
    }

}
