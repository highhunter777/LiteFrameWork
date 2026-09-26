using System;
using System.Collections.Generic;
using System.Text;
using LiteFramework;
using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame
{
    /// <summary>开发 HUD（只读快照展示）。**自创建形态**：[RuntimeInitializeOnLoadMethod] 在场景加载后
    /// 自建 GameObject（场景**不挂**组件——Editor-only asmdef 实测会把 play 模式场景组件剥离并报
    /// "not derived from MonoBehaviour"，2026-09-10 回归为三宏 #if 剥离 + 代码创建，同 DebugTuner 手法）。
    /// 自拉取模式：LiteGame.DevHUD → LiteClient.Runtime 单向引用，HUD 在 Start 经
    /// FindAnyObjectByType 拉 `GameEntry.Stats`（只读统计访问器，非解析入口）+ 场景组件型 IModuleStats 合并。
    /// **各段渲染开关 = public 字段**（Inspector 可配 / 代码可改，2026-09-13）：showStats /
    /// statToggles（单模块段 bool 开关）/ showLogRecent / logRecentLines / showErrorsLine。
    /// 0.25s 节流轮询：聚合单串、OnGUI 画一次；F1 总开关。</summary>
    public sealed class DevHUD : MonoBehaviour
    {
        /// <summary>单模块 stats 段开关（按 StatsName 一段一 bool，Inspector 直改；未列出的模块默认显示）。</summary>
        [Serializable]
        public class StatToggle
        {
            public string statsName;
            public bool show = true;
        }

        [Header("渲染开关（Inspector / 代码均可改）")]
        [Tooltip("各模块 stats 段总开关")]
        public bool showStats = true;
        [Tooltip("单模块段 bool 开关（按 StatsName 匹配；未列出的默认显示）")]
        public List<StatToggle> statToggles = new List<StatToggle>
        {
            new StatToggle { statsName = "MainThreadDispatcher" },
            new StatToggle { statsName = "Clock.World" },
            new StatToggle { statsName = "Clock.UI" },
            new StatToggle { statsName = "EventCenter" },
            new StatToggle { statsName = "Game" },
            new StatToggle { statsName = "Lua" },
        };
        [Tooltip("最近日志尾巴（Log.Recent 环缓冲渲染）")]
        public bool showLogRecent = true;
        [Tooltip("Recent 渲染条数（环缓冲容量 32）")]
        [Range(1, 32)] public int logRecentLines = 10;
        [Tooltip("错误计数行（仅 ErrorCount > 0 时渲染）")]
        public bool showErrorsLine = true;

        private IReadOnlyList<IModuleStats> _stats;
        private readonly Dictionary<string, string> _buffer = new Dictionary<string, string>(32);   // 全程复用，零容器分配
        private string _cache = "";
        private float _nextPoll;
        private bool _visible = true;

        private bool ShowSection(string statsName)
        {
            foreach (var t in statToggles)
                if (t.statsName == statsName) return t.show;
            return true;                                    // 未登记的模块默认显示
        }

        private void Start()
        {
            // stats 两个来源合并：容器注册件（骨架）+ 场景组件型（LuaComponent 等 Unity 组件不进 DI，§3.2）。
            // HUD 专用一次性扫描（Start 一次，非每帧）；按引用去重防双重展示。
            var merged = new List<IModuleStats>();
            var entry = FindAnyObjectByType<GameEntry>();
            if (entry != null) merged.AddRange(entry.Stats);
            foreach (var comp in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (comp is IModuleStats s && !merged.Contains(s)) merged.Add(s);
            _stats = merged;
        }

        private void Update()
        {
            if (!_visible || _stats == null || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;

            var sb = new StringBuilder(256);
            if (showErrorsLine && Log.ErrorCount > 0) sb.AppendLine($"── ⚠ Errors: {Log.ErrorCount} ──");
            if (showStats)
            {
                foreach (var s in _stats)
                {
                    if (!ShowSection(s.StatsName)) continue;    // 单模块 bool 开关（"各部分可选"）
                    s.Snapshot(_buffer);                    // 实现 Clear + 填（契约），HUD 不清
                    sb.AppendLine($"── {s.StatsName} ──");
                    foreach (var kv in _buffer) sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
            }
            // Log Recent 尾巴（手册步骤 8：最近 32 条环缓冲的可见尾部）
            if (showLogRecent)
            {
                var recent = Log.Recent;                    // index 0 = 最旧，零分配读
                if (recent.Count > 0)
                {
                    sb.AppendLine("── Log Recent ──");
                    int take = Mathf.Clamp(logRecentLines, 1, 32);
                    for (int i = recent.Count > take ? recent.Count - take : 0; i < recent.Count; i++)
                    {
                        var e = recent[i];
                        sb.AppendLine($"{(e.Level == LiteFramework.LogLevel.Error ? "✗" : e.Level == LiteFramework.LogLevel.Warning ? "⚠" : "·")}{(e.Tag != null ? $"[{e.Tag}]" : "")} {e.Message}");
                    }
                }
            }
            _cache = sb.ToString();
        }

        private void OnGUI()
        {
            if (!_visible || string.IsNullOrEmpty(_cache)) return;
            GUI.Label(new Rect(8, 8, 480, Screen.height - 16), _cache);   // 仅电脑测试用——不做刘海屏适配（设计方案 §1.3 豁免）
        }

        private void LateUpdate() { if (Input.GetKeyDown(KeyCode.F1)) _visible = !_visible; }
    }

    /// <summary>HUD 自创建入口（场景加载后；幂等）。release 构建随外层 #if 整体剥离。</summary>
    internal static class DevHUDBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<DevHUD>() != null) return;
            var go = new GameObject("[DevHUD]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHUD>();
        }
    }
}
#endif
