using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 候选脚本集合中的一项（《热更与内容发布专项设计》§10"从固定文件清单构建不可变脚本集合"）。
    /// </summary>
    public sealed class ScriptEntry
    {
        /// <summary>模块名（Lua require 用的点分名，如 "ui.main"）。</summary>
        public string Module;

        /// <summary>相对候选根的文件路径（正斜杠）。</summary>
        public string Path;

        /// <summary>本脚本 require 的其他模块名（**必须同批提供**——§7"同步 require 的依赖必须完整预载"）。</summary>
        public List<string> Requires = new List<string>();

        public ScriptEntry() { }

        public ScriptEntry(string module, string path, params string[] requires)
        {
            Module = module;
            Path = path;
            if (requires != null) Requires = new List<string>(requires);
        }
    }

    /// <summary>脚本集合校验结果。</summary>
    public readonly struct ScriptSetVerdict
    {
        public readonly bool Accepted;
        public readonly string Reason;

        private ScriptSetVerdict(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason;
        }

        public static ScriptSetVerdict Accept() => new ScriptSetVerdict(true, null);
        public static ScriptSetVerdict Reject(string reason) => new ScriptSetVerdict(false, reason);

        public override string ToString() => Accepted ? "接受" : "拒绝：" + Reason;
    }

    /// <summary>
    /// 候选脚本集合（§10）。**不可变**：构造后只读，激活前不被就地修改——
    /// 这是"先构建和验证候选集合，失败保留完整旧版本，不发布半套内容"（§1 冲突裁决）的前提。
    ///
    /// 与运行期 <c>LuaPreloader</c> 的区别：那个的清单来自"当前包内容"且字典可被重预载清空；
    /// 本类持有的是**固定 Release 的文件清单**派生的脚本集合，带依赖闭包与摘要身份。
    ///
    /// 零 Unity/Lua 依赖——只做模块名、路径、依赖闭包的**静态**校验，L1 全覆盖。
    /// **真实语法/执行/Bridge 能力检查需 Lua VM**（H3-d 的 Unity 侧），不在本类范围。
    /// </summary>
    public sealed class CandidateScriptSet
    {
        private readonly List<ScriptEntry> _entries;
        private readonly Dictionary<string, ScriptEntry> _byModule;

        /// <summary>脚本集合身份（对"模块→路径 + 依赖"规范化后的摘要；§5 ScriptDigest 的落点）。</summary>
        public string ScriptDigest { get; }

        public IReadOnlyList<ScriptEntry> Entries => _entries;

        private CandidateScriptSet(List<ScriptEntry> entries, Dictionary<string, ScriptEntry> byModule, string digest)
        {
            _entries = entries;
            _byModule = byModule;
            ScriptDigest = digest;
        }

        /// <summary>按模块名取脚本；不存在返回 null。</summary>
        public ScriptEntry Find(string module)
            => module != null && _byModule.TryGetValue(module, out ScriptEntry e) ? e : null;

        /// <summary>
        /// 从固定清单构建并校验（§10"检查依赖/语法/导出/Bridge 能力"里**可静态做**的部分）。
        ///
        /// 拒绝项：模块名/路径非法或重复、依赖不成环、**依赖必须在同批提供**（§7 同步 require 必须完整预载）。
        /// </summary>
        public static ScriptSetVerdict TryBuild(IEnumerable<ScriptEntry> entries, out CandidateScriptSet set)
        {
            set = null;
            if (entries == null) return ScriptSetVerdict.Reject("脚本集合为空");

            var list = new List<ScriptEntry>();
            var byModule = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);
            var byPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ScriptEntry e in entries)
            {
                if (e == null) return ScriptSetVerdict.Reject("含空条目");

                ScriptSetVerdict nameOk = ValidateModuleName(e.Module);
                if (!nameOk.Accepted) return nameOk;

                if (!IsValidScriptPath(e.Path))
                    return ScriptSetVerdict.Reject($"路径非法：{e.Path ?? "(null)"}");

                if (byModule.ContainsKey(e.Module))
                    return ScriptSetVerdict.Reject($"模块名重复：{e.Module}");
                if (!byPath.Add(e.Path.Replace('\\', '/')))
                    return ScriptSetVerdict.Reject($"路径重复（含大小写归一）：{e.Path}");

                byModule.Add(e.Module, e);
                list.Add(e);
            }

            if (list.Count == 0) return ScriptSetVerdict.Reject("脚本集合为空");

            // 依赖闭包：**每个依赖都必须同批提供**（§7）——缺一个就不能整体激活
            foreach (ScriptEntry e in list)
            {
                if (e.Requires == null) continue;
                foreach (string dep in e.Requires)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    if (!byModule.ContainsKey(dep))
                        return ScriptSetVerdict.Reject($"{e.Module} 依赖 {dep} 不在本批清单内（§7 依赖必须完整预载）");
                }
            }

            // 环检测：require 环在运行期会形成加载死锁或初始化顺序不确定
            if (TryFindCycle(byModule, out string cycle))
                return ScriptSetVerdict.Reject("依赖成环：" + cycle);

            set = new CandidateScriptSet(list, byModule, ComputeDigest(list));
            return ScriptSetVerdict.Accept();
        }

        /// <summary>
        /// 规范化摘要（§5"降级到可重建口径"）：模块名升序 → 模块|路径|依赖（依赖亦排序），
        /// 用 \n 分隔后取 SHA-256。**不依赖输入顺序**——同一集合以不同顺序给出必须同摘要。
        /// </summary>
        private static string ComputeDigest(List<ScriptEntry> entries)
        {
            var modules = new List<string>(entries.Count);
            foreach (ScriptEntry e in entries) modules.Add(e.Module);
            modules.Sort(StringComparer.Ordinal);

            var sb = new System.Text.StringBuilder(entries.Count * 48);
            foreach (string m in modules)
            {
                ScriptEntry e = null;
                foreach (ScriptEntry x in entries)
                    if (string.Equals(x.Module, m, StringComparison.Ordinal)) { e = x; break; }

                sb.Append(m).Append('|').Append(e.Path.Replace('\\', '/'));

                if (e.Requires != null && e.Requires.Count > 0)
                {
                    var deps = new List<string>(e.Requires);
                    deps.Sort(StringComparer.Ordinal);
                    sb.Append('|');
                    for (int i = 0; i < deps.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(deps[i]);
                    }
                }
                sb.Append('\n');
            }
            return ContentHash.Sha256Hex(sb.ToString());
        }

        private static ScriptSetVerdict ValidateModuleName(string module)
        {
            if (string.IsNullOrEmpty(module)) return ScriptSetVerdict.Reject("模块名为空");
            if (module.Length > 128) return ScriptSetVerdict.Reject($"模块名过长：{module.Length}");

            // 点分标识符，禁止空段（"a..b"）、前导/尾随点、路径分隔符与 .. 穿越
            if (module[0] == '.' || module[module.Length - 1] == '.')
                return ScriptSetVerdict.Reject($"模块名首尾有点：{module}");
            if (module.IndexOf('/') >= 0 || module.IndexOf('\\') >= 0)
                return ScriptSetVerdict.Reject($"模块名含路径分隔符：{module}");

            string[] parts = module.Split('.');
            foreach (string p in parts)
            {
                if (p.Length == 0) return ScriptSetVerdict.Reject($"模块名含空段：{module}");
                if (p == "..") return ScriptSetVerdict.Reject($"模块名含越界段：{module}");
                foreach (char c in p)
                {
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9') || c == '_';
                    if (!ok) return ScriptSetVerdict.Reject($"模块名含非法字符 '{c}'：{module}");
                }
            }
            return ScriptSetVerdict.Accept();
        }

        /// <summary>脚本路径约束与 <see cref="ReleaseManifestValidator"/> 同口径（相对、正斜杠、无越界）。</summary>
        private static bool IsValidScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.IndexOf('\\') >= 0) return false;
            if (path[0] == '/') return false;
            if (path.Length >= 2 && path[1] == ':') return false;

            foreach (string seg in path.Split('/'))
            {
                if (seg.Length == 0 || seg == "." || seg == "..") return false;
            }
            return true;
        }

        /// <summary>深度优先环检测（迭代实现，避免深依赖链爆栈）。</summary>
        private static bool TryFindCycle(Dictionary<string, ScriptEntry> byModule, out string cycle)
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 0=未访问 1=在栈 2=已完成
            var stack = new List<string>();

            foreach (string start in byModule.Keys)
            {
                if (state.TryGetValue(start, out int s0) && s0 == 2) continue;

                var work = new Stack<(string module, int index)>();
                work.Push((start, 0));
                state[start] = 1;
                stack.Add(start);

                while (work.Count > 0)
                {
                    (string module, int index) = work.Pop();
                    ScriptEntry entry = byModule[module];
                    List<string> deps = entry.Requires ?? new List<string>();

                    if (index >= deps.Count)
                    {
                        state[module] = 2;
                        stack.RemoveAt(stack.Count - 1);
                        continue;
                    }

                    work.Push((module, index + 1));
                    string dep = deps[index];
                    if (string.IsNullOrEmpty(dep) || !byModule.ContainsKey(dep)) continue;

                    state.TryGetValue(dep, out int ds);
                    if (ds == 1)
                    {
                        cycle = string.Join(" → ", stack) + " → " + dep;
                        return true;
                    }
                    if (ds == 2) continue;

                    state[dep] = 1;
                    stack.Add(dep);
                    work.Push((dep, 0));
                }
            }

            cycle = null;
            return false;
        }
    }
}
