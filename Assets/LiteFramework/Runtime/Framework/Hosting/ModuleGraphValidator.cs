using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 模块图校验（路线图 M1）：模块名重复、依赖缺失、依赖环——全部 fail-fast，且早于任何副作用。
    /// **只校验已声明依赖**（实现 <see cref="IClientModuleGraph"/> 的模块）；未声明者按注册序初始化。
    /// 依赖按**模块名**寻址（同一模块类可有多个实例，名字才是唯一身份）。
    /// </summary>
    internal static class ModuleGraphValidator
    {
        internal static void Validate(List<IClientModule> modules)
        {
            var byName = new Dictionary<string, IClientModule>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Count; i++)
            {
                IClientModule m = modules[i];
                if (byName.ContainsKey(m.Name))
                    throw new InvalidOperationException($"模块名重复：{m.Name}（模块集必须唯一）");
                byName.Add(m.Name, m);
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (!(modules[i] is IClientModuleGraph graph) || graph.DependsOn == null) continue;
                for (int d = 0; d < graph.DependsOn.Count; d++)
                {
                    string dep = graph.DependsOn[d];
                    if (string.IsNullOrEmpty(dep) || !byName.ContainsKey(dep))
                        throw new InvalidOperationException(
                            $"模块 {modules[i].Name} 依赖缺失：{(string.IsNullOrEmpty(dep) ? "<null>" : dep)} 未注册（注册序/装配错误）");
                }
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未访问 1=在栈 2=完成
            for (int i = 0; i < modules.Count; i++)
                VisitModule(modules[i].Name, byName, state);
        }

        private static void VisitModule(string name, Dictionary<string, IClientModule> byName, Dictionary<string, int> state)
        {
            if (state.TryGetValue(name, out int s))
            {
                if (s == 1) throw new InvalidOperationException($"模块依赖成环：{name}");
                if (s == 2) return;
            }
            state[name] = 1;
            if (byName.TryGetValue(name, out IClientModule module)
                && module is IClientModuleGraph graph && graph.DependsOn != null)
            {
                for (int i = 0; i < graph.DependsOn.Count; i++)
                {
                    string dep = graph.DependsOn[i];
                    if (!string.IsNullOrEmpty(dep)) VisitModule(dep, byName, state);
                }
            }
            state[name] = 2;
        }
    }
}
