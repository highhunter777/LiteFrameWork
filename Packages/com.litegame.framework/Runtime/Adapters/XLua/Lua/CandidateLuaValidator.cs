using System;
using System.Collections.Generic;
using System.Text;
using LiteFramework;
using XLua;

namespace LiteFramework
{
    /// <summary>单个脚本的验证结论。</summary>
    public readonly struct LuaScriptVerdict
    {
        public readonly string Module;
        public readonly bool SyntaxOk;
        public readonly bool ExecuteOk;
        public readonly string Reason;

        public LuaScriptVerdict(string module, bool syntaxOk, bool executeOk, string reason)
        {
            Module = module;
            SyntaxOk = syntaxOk;
            ExecuteOk = executeOk;
            Reason = reason;
        }

        public bool Ok => SyntaxOk && ExecuteOk;
    }

    /// <summary>
    /// 候选 Lua 脚本验证（《热更与内容发布专项设计》§10"候选阶段：从固定文件清单构建不可变脚本集合 → 检查依赖/语法/导出/Bridge 能力 → **在受控验证环境检查注册表**"）。
    /// </summary>
    public sealed class CandidateLuaValidator : IDisposable
    {
        private readonly LuaEnv _env;
        private LuaTable _sandbox;
        private bool _disposed;

        public CandidateLuaValidator()
        {
            _env = new LuaEnv();
            _sandbox = BuildSandbox(_env);
        }

        /// <summary>
        /// 构造沙箱全局环境。
        /// </summary>
        private static LuaTable BuildSandbox(LuaEnv env)
        {
            LuaTable sandbox = env.NewTable();
            sandbox.Set("_G", sandbox);                       // 自引用（`_G.x = 1` 与 `x = 1` 等价于同一张表）

            LuaTable globals = env.Global;
            foreach (string name in SafeGlobals)
            {
                object value = globals.Get<string, object>(name);
                if (value != null) sandbox.Set(name, value);
            }

            sandbox.Set("require", (Func<string, object>)null);   // 由 Validate 按候选集合替换
            sandbox.Set("_VERSION", globals.Get<string, object>("_VERSION"));

            // 日志桥无副作用桩（与正式 log 同形状：info/warning/error）——见 BuildSandbox 注释的边界说明。
            LuaTable logStub = env.NewTable();
            logStub.Set("info", new Action<string>(_ => { }));
            logStub.Set("warning", new Action<string>(_ => { }));
            logStub.Set("error", new Action<string>(_ => { }));
            sandbox.Set("log", logStub);
            return sandbox;
        }

        /// <summary>沙箱放行的全局名——**白名单而非黑名单**（未列入者一律不可见）。</summary>
        private static readonly string[] SafeGlobals =
        {
            // 基础。
            "print", "type", "tostring", "tonumber", "pairs", "ipairs", "next",
            "select", "error", "assert", "pcall", "xpcall", "unpack", "rawget", "rawset",
            "rawequal", "rawlen", "setmetatable", "getmetatable",
            // 标准库（无 IO/OS/调试面）。
            "table", "string", "math", "coroutine", "bit", "utf8",
        };

        /// <summary>标准库名——`require('math')` 在真实 Lua 走 `package.loaded` 预注册、不落 loader；
        /// 沙箱同语义放行（LuaPanda 的 tools.createJson 即 `require('math')/'string'/'table'`）。
        /// 被回收的 `io`/`os`/`debug` 不在列：require 它们照常按"越出候选集合"拒绝。</summary>
        private static readonly HashSet<string> StandardLibs = new HashSet<string>(StringComparer.Ordinal)
        {
            "table", "string", "math", "coroutine", "bit", "utf8",
        };

        /// <summary>
        /// 逐脚本验证。
        /// </summary>
        public IReadOnlyList<LuaScriptVerdict> Validate(
            CandidateScriptSet scripts,
            IReadOnlyDictionary<string, byte[]> scriptBytes,
            Func<string, string, bool> allowedRequire = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CandidateLuaValidator));
            if (scripts == null) throw new ArgumentNullException(nameof(scripts));
            if (scriptBytes == null) throw new ArgumentNullException(nameof(scriptBytes));

            var results = new List<LuaScriptVerdict>();
            var loaded = new Dictionary<string, object>(StringComparer.Ordinal);            // module → 返回值（已执行）
            var verdicts = new Dictionary<string, LuaScriptVerdict>(StringComparer.Ordinal); // module → 结论（执行断言唯一）
            var executing = new HashSet<string>(StringComparer.Ordinal);                     // require 环守卫
            var byModule = new Dictionary<string, ScriptEntry>(StringComparer.Ordinal);
            foreach (ScriptEntry e in scripts.Entries) byModule[e.Module] = e;

            // 受限 require：只认本批候选集合，且**按需执行**——依赖在清单后位也能解析 （真实候选包：Lua 模块可依赖清单后位的 Core.class），与运行期 loader "全量预载字节 + require 时执行并缓存"同语义；越集即拒、环即拒。
            Func<string, object> restrictedRequire = delegate (string module)
            {
                if (module == null) return null;
                if (allowedRequire != null && !allowedRequire(module, null)) return null;
                if (StandardLibs.Contains(module))
                    return _sandbox.Get<string, object>(module);   // 标准库：同真实 package.loaded 预注册（不落 loader）
                if (!byModule.TryGetValue(module, out ScriptEntry dep))
                    throw new InvalidOperationException("require 越出候选集合：" + module);   // §7 依赖必须完整预载
                if (executing.Contains(module))
                    throw new InvalidOperationException("require 循环依赖：" + module);
                ExecuteEntry(dep);
                return loaded.TryGetValue(module, out object v) ? v : null;
            };
            _sandbox.Set("require", restrictedRequire);

            // 执行一段候选脚本（幂等：已执行即返回）；结论写入 verdicts，返回值供依赖 require 取用。
            void ExecuteEntry(ScriptEntry entry)
            {
                if (verdicts.ContainsKey(entry.Module)) return;

                if (!scriptBytes.TryGetValue(entry.Path, out byte[] bytes) || bytes == null)
                {
                    verdicts[entry.Module] = new LuaScriptVerdict(entry.Module, false, false, "缺脚本字节：" + entry.Path);
                    return;
                }

                executing.Add(entry.Module);
                LuaFunction fn = null;
                try
                {
                    // ① 语法。
                    try
                    {
                        fn = _env.LoadString<LuaFunction>(bytes, entry.Module, _sandbox);
                    }
                    catch (Exception ex)
                    {
                        verdicts[entry.Module] = new LuaScriptVerdict(entry.Module, false, false, "语法：" + Shorten(ex.Message));
                        return;
                    }

                    // ② 模块级执行（沙箱内；不调用 main，不产生业务副作用）。
                    try
                    {
                        object[] ret = fn.Call();
                        // 记录返回值供后续模块 require（Lua 模块惯例：返回 table）。
                        loaded[entry.Module] = (ret != null && ret.Length > 0) ? ret[0] : null;
                        verdicts[entry.Module] = new LuaScriptVerdict(entry.Module, true, true, null);
                    }
                    catch (Exception ex)
                    {
                        verdicts[entry.Module] = new LuaScriptVerdict(entry.Module, true, false, "执行：" + Shorten(ex.Message));
                    }
                }
                finally
                {
                    if (fn != null) fn.Dispose();
                    executing.Remove(entry.Module);
                }
            }

            foreach (ScriptEntry entry in scripts.Entries)          // 逐脚本验证。**不短路**：一次报告全部问题
            {
                ExecuteEntry(entry);
                if (verdicts.TryGetValue(entry.Module, out LuaScriptVerdict vd)) results.Add(vd);
            }
            return results;
        }

        /// <summary>把逐脚本结论合并为一行诊断（全通过时返回 null）。</summary>
        public static string Summarize(IReadOnlyList<LuaScriptVerdict> verdicts)
        {
            if (verdicts == null || verdicts.Count == 0) return "无脚本可验证";
            var sb = new StringBuilder();
            foreach (LuaScriptVerdict v in verdicts)
            {
                if (v.Ok) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(v.Module).Append('：').Append(v.Reason);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static string Shorten(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            message = message.Replace('\n', ' ').Replace('\r', ' ');
            return message.Length <= 200 ? message : message.Substring(0, 200) + "…";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sandbox?.Dispose();
            _sandbox = null;
            _env?.Dispose();
        }
    }
}
