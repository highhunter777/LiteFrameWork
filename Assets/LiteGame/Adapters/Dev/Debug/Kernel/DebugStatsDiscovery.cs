#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 调试读模型的模块统计发现面：容器注册件 + 场景组件型合并去重、剔除销毁件、按名称排序。
    /// 内核持有；HUD/控制台/编辑器页只读 <see cref="Stats"/>，不各持一套扫描逻辑。
    /// </summary>
    public sealed class DebugStatsDiscovery
    {
        private readonly List<IModuleStats> _stats = new List<IModuleStats>(32);

        public IReadOnlyList<IModuleStats> Stats => _stats;

        public void Rebuild()
        {
            _stats.Clear();
            GameEntry entry = Object.FindAnyObjectByType<GameEntry>();
            if (entry != null)
                foreach (IModuleStats stat in entry.Stats) Add(stat);
            foreach (MonoBehaviour comp in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (comp is IModuleStats stat) Add(stat);
            _stats.Sort((a, b) => string.CompareOrdinal(a.StatsName, b.StatsName));
        }

        /// <summary>剔除已销毁的场景组件（页面关闭/对局卸载后旧引用不能继续 Snapshot）。</summary>
        public void Prune()
        {
            for (int i = _stats.Count - 1; i >= 0; i--)
                if (_stats[i] is Object o && o == null) _stats.RemoveAt(i);
        }

        private void Add(IModuleStats stat)
        {
            if (stat == null) return;
            if (stat is Object unityObj && unityObj == null) return;
            if (!_stats.Contains(stat)) _stats.Add(stat);
        }
    }
}
#endif
