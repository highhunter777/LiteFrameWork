// 拆自 TabGroup.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>底部导航（M4c）：TabGroup 的横排预设——收集子按钮组成页签，选中回调即页切换。</summary>
    public class BottomNav : MonoBehaviour
    {
        public TabGroup Tabs;                            // 指向子物体上的 TabGroup（或留空自动找）
        public event Action<int> OnNavChanged;

        private void Awake()
        {
            if (Tabs == null) Tabs = GetComponentInChildren<TabGroup>();
            if (Tabs != null) Tabs.OnTabChanged += i => OnNavChanged?.Invoke(i);
        }
    }
}
