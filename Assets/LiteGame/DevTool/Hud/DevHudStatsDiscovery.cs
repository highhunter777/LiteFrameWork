#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// Stats 发现面：容器注册件 + 场景组件型（场景加载后重扫）合并去重、剔除销毁件、按名称排序。
    /// DevHUD 只读 `Stats`；扫描/生命周期不在宿主里。
    /// </summary>
    internal sealed class DevHudStatsDiscovery
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
