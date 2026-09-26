// 拆自 FlyText.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>引导高亮位（M4c）：把高亮框对齐到目标控件（灰盒=跟随目标矩形；全屏遮罩挖孔随美术）。</summary>
    public class GuideHighlight : MonoBehaviour
    {
        public RectTransform Frame;                          // 高亮框（Image 外框）

        /// <summary>高亮框对齐目标控件（位置与尺寸同步；目标隐藏即隐藏框）。</summary>
        public void Target(RectTransform target)
        {
            if (Frame == null || target == null) return;
            Frame.position = target.position;
            Frame.sizeDelta = target.rect.size;
            Frame.gameObject.SetActive(target.gameObject.activeInHierarchy);
        }
    }
}
