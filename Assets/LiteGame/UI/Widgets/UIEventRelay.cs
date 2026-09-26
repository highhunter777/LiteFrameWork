// 拆自 Input.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>数值事件转发辅助（灰盒：UIEventTrigger 位——Pointer 点击转发为 UnityEvent）。</summary>
    public class UIEventRelay : MonoBehaviour, IPointerClickHandler
    {
        public UnityEvent OnClicked;
        public void OnPointerClick(PointerEventData e) => OnClicked?.Invoke();
    }
}
