// 拆自 Display.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>头像框（M4c）：头像 + 框 + 等级角标三件套的组装件（Set 一口喂）。</summary>
    public class AvatarFrame : MonoBehaviour
    {
        public Image Avatar;
        public Image Frame;
        public TMP_Text LevelBadge;

        public void Set(Sprite avatar, Sprite frame, string level)
        {
            if (Avatar != null) Avatar.sprite = avatar;
            if (Frame != null) Frame.sprite = frame;
            if (LevelBadge != null) LevelBadge.text = level ?? string.Empty;
        }
    }
}
