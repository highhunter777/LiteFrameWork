#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 调参描述发现面：容器 provider + 场景组件 provider 合并、按 Id 去重、按 Group/Label 排序，
    /// 并剔除已销毁组件的描述符。内核持有；编辑器页/运行期面板只读 <see cref="Tunables"/>。
    /// </summary>
    public sealed class DebugTunableDiscovery
    {
        private readonly List<DebugTunable> _tunables = new List<DebugTunable>(32);
        private readonly List<IDebugTunableProvider> _containerProviders;

        public DebugTunableDiscovery(IEnumerable<IDebugTunableProvider> containerProviders)
        {
            _containerProviders = containerProviders != null
                ? new List<IDebugTunableProvider>(containerProviders)
                : new List<IDebugTunableProvider>();
        }

        public IReadOnlyList<DebugTunable> Tunables => _tunables;

        public void Rebuild()
        {
            _tunables.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _containerProviders.Count; i++)
                Collect(_containerProviders[i], seen);

            foreach (MonoBehaviour comp in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (comp is IDebugTunableProvider provider)
                    Collect(provider, seen);

            _tunables.Sort((a, b) =>
            {
                int group = string.CompareOrdinal(a.Group, b.Group);
                if (group != 0) return group;
                int label = string.CompareOrdinal(a.Label, b.Label);
                return label != 0 ? label : string.CompareOrdinal(a.Id, b.Id);
            });
        }

        /// <summary>剔除已销毁场景组件提供的参数（局部 UI/对局件卸载后旧引用失效）。</summary>
        public void Prune()
        {
            for (int i = _tunables.Count - 1; i >= 0; i--)
                if (_tunables[i].Owner is UnityEngine.Object owner && owner == null)
                    _tunables.RemoveAt(i);
        }

        public bool TryGet(string id, out DebugTunable tunable)
        {
            for (int i = 0; i < _tunables.Count; i++)
            {
                if (!string.Equals(_tunables[i].Id, id, StringComparison.Ordinal)) continue;
                tunable = _tunables[i];
                return true;
            }
            tunable = null;
            return false;
        }

        public bool TrySet(string id, float value, out string error)
        {
            if (!TryGet(id, out DebugTunable tunable)) { error = "未找到调参项：" + id; return false; }
            if (value < tunable.Min || value > tunable.Max)
            {
                error = $"{id} 超出范围 [{tunable.Min}, {tunable.Max}]";
                return false;
            }
            try
            {
                tunable.Set?.Invoke(value);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{id} 写入失败：{ex.Message}";
                return false;
            }
        }

        public bool Reset(string id, out string error)
        {
            if (!TryGet(id, out DebugTunable tunable)) { error = "未找到调参项：" + id; return false; }
            try
            {
                if (tunable.Reset != null) tunable.Reset();
                else tunable.Set?.Invoke(tunable.Default);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"{id} 复位失败：{ex.Message}";
                return false;
            }
        }

        private void Collect(IDebugTunableProvider provider, HashSet<string> seen)
        {
            if (provider == null) return;
            if (provider is UnityEngine.Object owner && owner == null) return;

            var found = new List<DebugTunable>(8);
            try { provider.CollectDebugTunables(found); }
            catch (Exception ex)
            {
                Log.Warning("调试参数收集失败（" + provider.GetType().Name + "）：" + ex.Message, LogTag.UI);
                return;
            }

            for (int i = 0; i < found.Count; i++)
            {
                DebugTunable tunable = found[i];
                if (tunable == null || string.IsNullOrEmpty(tunable.Id)
                    || tunable.Get == null || tunable.Set == null)
                {
                    Log.Warning("调试参数描述非法（Id/Get/Set 必填）", LogTag.UI);
                    continue;
                }
                if (tunable.Min > tunable.Max
                    || tunable.Default < tunable.Min || tunable.Default > tunable.Max)
                {
                    Log.Warning("调试参数范围/默认值非法：" + tunable.Id, LogTag.UI);
                    continue;
                }
                if (tunable.Kind == DebugTunableKind.Bool
                    && (tunable.Min != 0f || tunable.Max != 1f))
                {
                    Log.Warning("Bool 调参范围必须为 0..1：" + tunable.Id, LogTag.UI);
                    continue;
                }
                tunable.Decimals = Mathf.Clamp(tunable.Decimals, 0, 6);
                if (!seen.Add(tunable.Id))
                {
                    Log.Warning("调试参数 Id 重复，忽略后者：" + tunable.Id, LogTag.UI);
                    continue;
                }
                tunable.Owner ??= provider;
                _tunables.Add(tunable);
            }
        }
    }
}
#endif
