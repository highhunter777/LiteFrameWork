using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiteGame
{
    /// <summary>AOT 侧 hot 程序集装载入口：按槽位回退（current → previous → builtin）。</summary>
    public static class HotUpdateLoader
    {
        private static readonly string[] Slots = { "HybridCLR/hot", "HybridCLR/hot.previous", "HybridCLR/hot.builtin" };
        private static readonly List<System.Reflection.Assembly> s_loaded = new List<System.Reflection.Assembly>();

        public static int Count => s_loaded.Count;

        /// <summary>实际装载的槽位（诊断/回滚证据；未装载为 null）。</summary>
        public static string ActiveSlot { get; private set; }

        /// <summary>装载失败返回 false（各槽位都不可用），仅编辑器允许回退到 AOT 直编。</summary>
        public static bool TryEnsureLoaded()
        {
            if (s_loaded.Count > 0)
            {
                return true;
            }
            foreach (string slot in Slots)
            {
                string dir = Path.Combine(Application.streamingAssetsPath, slot);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                var staged = new List<System.Reflection.Assembly>();
                try
                {
                    string[] files = Directory.GetFiles(dir, "*.dll.bytes");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string file in files)
                    {
                        staged.Add(System.Reflection.Assembly.Load(File.ReadAllBytes(file)));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[HotUpdate] 槽位装载失败，尝试下一槽：{slot}：{e}");
                    continue;
                }
                if (staged.Count == 0)
                {
                    continue;
                }
                s_loaded.AddRange(staged);
                ActiveSlot = slot;
                Debug.Log($"[HotUpdate] loaded {s_loaded.Count} assembly(ies) from {slot}");
                return true;
            }
            Debug.LogWarning("[HotUpdate] 无可用 hot 槽位（current/previous/builtin 均缺失或不可装载）");
            return false;
        }
    }
}
