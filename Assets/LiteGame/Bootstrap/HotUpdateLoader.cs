using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiteGame
{
    /// <summary>AOT 侧 hot 程序集装载入口；P2 只支持编辑器/独立平台的目录装载。</summary>
    public static class HotUpdateLoader
    {
        private const string HotDirName = "HybridCLR/hot";
        private static readonly List<System.Reflection.Assembly> s_loaded = new List<System.Reflection.Assembly>();

        public static int Count => s_loaded.Count;

        /// <summary>装载失败返回 false（目录缺失），仅编辑器允许回退到 AOT 直编。</summary>
        public static bool TryEnsureLoaded()
        {
            if (s_loaded.Count > 0)
            {
                return true;
            }
            string dir = Path.Combine(Application.streamingAssetsPath, HotDirName);
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[HotUpdate] hot 程序集目录缺失：{dir}");
                return false;
            }
            string[] files = Directory.GetFiles(dir, "*.dll.bytes");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                s_loaded.Add(System.Reflection.Assembly.Load(File.ReadAllBytes(file)));
            }
            Debug.Log($"[HotUpdate] loaded {s_loaded.Count} assembly(ies) from {dir}");
            return s_loaded.Count > 0;
        }
    }
}
