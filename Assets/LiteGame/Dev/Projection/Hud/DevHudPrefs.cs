#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LiteGame
{
    /// <summary>
    /// DevHUD 的会话间记忆（宽度/字号/位置/缩放/折叠段/过滤词）。
    /// Editor 用 EditorPrefs、Player 用 PlayerPrefs；键统一前缀，避免与产品设置混。
    /// </summary>
    internal static class DevHudPrefs
    {
        private const string Prefix = "LiteGame.DevHUD.";

        public static bool Has(string key) => HasRaw(Prefix + key);

        public static int GetInt(string key, int fallback) => GetIntRaw(Prefix + key, fallback);

        public static void SetInt(string key, int value) => SetIntRaw(Prefix + key, value);

        public static float GetFloat(string key, float fallback) => GetFloatRaw(Prefix + key, fallback);

        public static void SetFloat(string key, float value) => SetFloatRaw(Prefix + key, value);

        public static string GetString(string key, string fallback) => GetStringRaw(Prefix + key, fallback);

        public static void SetString(string key, string value) => SetStringRaw(Prefix + key, value);

#if UNITY_EDITOR
        private static bool HasRaw(string key) => EditorPrefs.HasKey(key);
        private static int GetIntRaw(string key, int fallback) => EditorPrefs.GetInt(key, fallback);
        private static void SetIntRaw(string key, int value) => EditorPrefs.SetInt(key, value);
        private static float GetFloatRaw(string key, float fallback) => EditorPrefs.GetFloat(key, fallback);
        private static void SetFloatRaw(string key, float value) => EditorPrefs.SetFloat(key, value);
        private static string GetStringRaw(string key, string fallback) => EditorPrefs.GetString(key, fallback);
        private static void SetStringRaw(string key, string value) => EditorPrefs.SetString(key, value);
#else
        private static bool HasRaw(string key) => PlayerPrefs.HasKey(key);
        private static int GetIntRaw(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        private static void SetIntRaw(string key, int value) => PlayerPrefs.SetInt(key, value);
        private static float GetFloatRaw(string key, float fallback) => PlayerPrefs.GetFloat(key, fallback);
        private static void SetFloatRaw(string key, float value) => PlayerPrefs.SetFloat(key, value);
        private static string GetStringRaw(string key, string fallback) => PlayerPrefs.GetString(key, fallback);
        private static void SetStringRaw(string key, string value) => PlayerPrefs.SetString(key, value);
#endif
    }
}
#endif
