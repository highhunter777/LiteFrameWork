using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 模块图校验与排序（路线图 M1）：重名/依赖缺失/依赖环 fail-fast；通过后按 DependsOn 拓扑排序。
    /// </summary>
    internal static class SegmentGraphValidator
    {
        /// <summary>校验并返回初始化顺序：拓扑序，同层按注册下标稳定。</summary>
        internal static List<IClientSegment> Order(List<IClientSegment> modules)
        {
            Dictionary<string, IClientSegment> byName = IndexByName(modules);
            ValidateDependencies(modules, byName);
            ValidateNoCycle(modules, byName);
            return TopologicalOrder(modules);
        }

        private static Dictionary<string, IClientSegment> IndexByName(List<IClientSegment> modules)
        {
            var byName = new Dictionary<string, IClientSegment>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
            {
                IClientSegment m = modules[i];
                if (byName.ContainsKey(m.Name))
                    throw new InvalidOperationException($"模块名重复：{m.Name}（模块集必须唯一）");
                byName.Add(m.Name, m);
            }
            return byName;
        }

        private static void ValidateDependencies(List<IClientSegment> modules, Dictionary<string, IClientSegment> byName)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (!(modules[i] is IClientSegmentGraph graph) || graph.DependsOn == null) continue;
                for (int d = 0; d < graph.DependsOn.Count; d++)
                {
                    string dep = graph.DependsOn[d];
                    if (string.IsNullOrEmpty(dep) || !byName.ContainsKey(dep))
                        throw new InvalidOperationException(
                            $"模块 {modules[i].Name} 依赖缺失：{(string.IsNullOrEmpty(dep) ? "<null>" : dep)} 未注册（注册序/装配错误）");
                }
            }
        }

        private static void ValidateNoCycle(List<IClientSegment> modules, Dictionary<string, IClientSegment> byName)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未访问 1=在栈 2=完成
            for (int i = 0; i < modules.Count; i++)
                VisitModule(modules[i].Name, byName, state);
        }

        private static void VisitModule(string name, Dictionary<string, IClientSegment> byName, Dictionary<string, int> state)
        {
            if (state.TryGetValue(name, out int s))
            {
                if (s == 1) throw new InvalidOperationException($"模块依赖成环：{name}");
                if (s == 2) return;
            }
            state[name] = 1;
            if (byName.TryGetValue(name, out IClientSegment module)
                && module is IClientSegmentGraph graph && graph.DependsOn != null)
            {
                for (int i = 0; i < graph.DependsOn.Count; i++)
                {
                    string dep = graph.DependsOn[i];
                    if (!string.IsNullOrEmpty(dep)) VisitModule(dep, byName, state);
                }
            }
            state[name] = 2;
        }

        /// <summary>按依赖深度分层：深度 = 依赖最大深度 + 1；同层按注册下标，排序稳定可预测。</summary>
        private static List<IClientSegment> TopologicalOrder(List<IClientSegment> modules)
        {
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++) indexByName[modules[i].Name] = i;

            var depth = new int[modules.Count];
            for (int i = 0; i < depth.Length; i++) depth[i] = -1;
            for (int i = 0; i < modules.Count; i++) ComputeDepth(i, modules, indexByName, depth);

            var indices = new int[modules.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            Array.Sort(indices, (a, b) => depth[a] != depth[b] ? depth[a] - depth[b] : a - b);

            var order = new List<IClientSegment>(modules.Count);
            for (int i = 0; i < indices.Length; i++) order.Add(modules[indices[i]]);
            return order;
        }

        private static int ComputeDepth(int index, List<IClientSegment> modules,
            Dictionary<string, int> indexByName, int[] depth)
        {
            if (depth[index] >= 0) return depth[index];
            depth[index] = 0;                                                          // 防重入（环已由 ValidateNoCycle 拦截）
            int value = 0;
            if (modules[index] is IClientSegmentGraph graph && graph.DependsOn != null)
            {
                for (int d = 0; d < graph.DependsOn.Count; d++)
                {
                    string dep = graph.DependsOn[d];
                    if (string.IsNullOrEmpty(dep)) continue;
                    value = Math.Max(value, ComputeDepth(indexByName[dep], modules, indexByName, depth) + 1);
                }
            }
            depth[index] = value;
            return value;
        }
    }
}
