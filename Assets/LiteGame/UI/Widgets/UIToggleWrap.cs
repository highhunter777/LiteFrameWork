// 拆自 Input.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>开关封装。</summary>
    public class UIToggleWrap : MonoBehaviour
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
    }

}
