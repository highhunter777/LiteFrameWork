#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// GameView 分辨率切换（P2-3 B3 / D2 用户裁决：反射内部 SetCustomResolution＋容错降级）：先确保
    /// GameView 处于经典分辨率模式（2022.3 的 Device Simulator 是独立窗口，这里显式取 GameView），
    /// 再调内部 `SetCustomResolution`。失败返回 false（调用方降级：安全区/语言照跑，分辨率提示手动切，
    /// 矩阵索引记 resolutionApplied=false）。内部 API 集中在本单点，不散落。
    /// </summary>
    public static class UiPreviewGameView
    {
        private const string GameViewTypeName = "UnityEditor.GameView,UnityEditor";

        private static readonly Type GameViewType = Type.GetType(GameViewTypeName);
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>尝试把 GameView 切到给定分辨率；失败（内部 API 不可用/非经典模式）返回 false 不抛。</summary>
        public static bool TrySetSize(int width, int height)
        {
            if (width <= 0 || height <= 0 || GameViewType == null) return false;
            try
            {
                var gameView = EditorWindow.GetWindow(GameViewType, false, null, true);
                if (gameView == null) return false;

                MethodInfo setCustom = GameViewType.GetMethod("SetCustomResolution", Flags,
                    null, new[] { typeof(Vector2), typeof(string) }, null);
                MethodInfo getSize = GameViewType.GetMethod("GetPlayModeViewSize", Flags);
                if (setCustom == null || getSize == null) return false;

                setCustom.Invoke(gameView, new object[] { new Vector2(width, height), "UiPreviewMatrix" });
                var actual = (Vector2)getSize.Invoke(gameView, null);
                return Mathf.Abs(actual.x - width) <= 1f && Mathf.Abs(actual.y - height) <= 1f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>读取当前 GameView 分辨率（失败返回 false；用于退出预览时还原）。</summary>
        public static bool TryGetSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            if (GameViewType == null) return false;
            try
            {
                var gameView = EditorWindow.GetWindow(GameViewType, false, null, false);
                if (gameView == null) return false;
                MethodInfo getSize = GameViewType.GetMethod("GetPlayModeViewSize", Flags);
                if (getSize == null) return false;
                var size = (Vector2)getSize.Invoke(gameView, null);
                width = Mathf.RoundToInt(size.x);
                height = Mathf.RoundToInt(size.y);
                return width > 0 && height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
#endif
