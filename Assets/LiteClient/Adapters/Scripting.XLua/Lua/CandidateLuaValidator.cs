using System;
using System.Collections.Generic;
using System.Text;
using LiteFramework;
using XLua;

namespace LiteClient
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
    /// 候选 Lua 脚本验证（《热更与内容发布专项设计》§10"候选阶段：从固定文件清单构建不可变脚本集合 →
    /// 检查依赖/语法/导出/Bridge 能力 → **在受控验证环境检查注册表**"）。
    ///
    /// **受控验证环境的硬要求**（§10 逐条）：
    /// "验证环境不得绑定正式全局 Bridge 注册表，不得打开 UI、订阅正式事件、发业务网络请求或写存档。"
    ///
    /// 落点：
    /// - **独立 <see cref="LuaEnv"/>**：与运行时 <c>LuaComponent</c> 的 env 完全隔离——候选脚本在任何路径上
    ///   都拿不到正式 Bridge 表（那是在另一个 env 里的对象）。
    /// - **独立 fenv 沙箱**：每段候选脚本用 <c>LoadString(..., env: sandbox)</c> 装载，
    ///   其全局环境是**新建的沙箱表**而非 `_G`——从而回收掉 `CS`/`require`/`io`/`os`/`debug` 等能力面
    ///   （§10"Bridge 白名单是能力设计，不自动构成沙箱；还需收口 CS/反射、文件/网络/系统库、动态加载与调试入口"）。
    /// - **受限 require**：沙箱内的 `require` 只解析**本批候选集合**内的模块（§7"同步 require 的依赖必须完整预载"），
    ///   不落到文件系统或原生加载器。
    /// - **不执行 main**：本类只做语法与**模块级**加载验证；正式激活路径仍由 <c>LuaComponent</c> 承担，
    ///   候选验证不产生业务副作用。
    ///
    /// **能力边界（如实标注）**：沙箱回收是**能力收口**而非形式化安全边界——
    /// 未执行 <c>debug</c> 库的加固，也不声称可抵御恶意构造的 Lua 字节码；
    /// §10 要求的"错误熔断、内存与执行预算"中，本类只做**逐脚本**报告，不含指令计数/超时中断
    /// （那需 Lua VM 的 hook 接缝，本类不实现）。
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
        ///
        /// 与 xLua 默认 `_G` 的差异（即**被回收的能力**）：
        /// `CS`（C# 类型直通/反射入口）、`require`（原生加载器 → 文件/StreamingAssets）、
        /// `io`/`os`/`debug`/`package`/`loadlib`/`dofile`/`loadfile`/`load`（文件与动态加载面）。
        ///
        /// 保留仅够"定义模块"的最小集：`table`/`string`/`math`/`tonumber`/`tostring`/`type`/`pairs`/`ipairs`/
        /// `pcall`/`error`/`assert`/`select`/`setmetatable`/`getmetatable`/`rawget`/`rawset`/`rawequal`/`unpack`。
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
            return sandbox;
        }

        /// <summary>沙箱放行的全局名——**白名单而非黑名单**（未列入者一律不可见）。</summary>
        private static readonly string[] SafeGlobals =
        {
            // 基础
            "print", "type", "tostring", "tonumber", "pairs", "ipairs", "next",
            "select", "error", "assert", "pcall", "xpcall", "unpack", "rawget", "rawset",
            "rawequal", "rawlen", "setmetatable", "getmetatable",
            // 标准库（无 IO/OS/调试面）
            "table", "string", "math", "coroutine", "bit", "utf8",
        };

        /// <summary>
        /// 逐脚本验证。**不短路**：一次报告全部问题（与 <see cref="CompositeHealthCheck"/> 同款纪律）。
        ///
        /// 对每段脚本：
        /// ① 语法——<c>luaL_loadbuffer</c> 能否装载；
        /// ② 模块级执行——在沙箱内 pcall 一次，捕获顶层错误。
        /// 依赖的模块由本次候选集合内已装载者提供（受限 require）。
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
            var loaded = new Dictionary<string, object>(StringComparer.Ordinal);

            // 受限 require：只认本批候选集合，且**只认已成功装载的模块**
            var batchModules = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (ScriptEntry e in scripts.Entries) batchModules[e.Module] = true;

            Func<string, object> restrictedRequire = delegate (string module)
            {
                if (module == null) return null;
                if (allowedRequire != null && !allowedRequire(module, null)) return null;
                if (!batchModules.ContainsKey(module))
                    throw new InvalidOperationException("require 越出候选集合：" + module);   // §7 依赖必须完整预载
                return loaded.TryGetValue(module, out object v) ? v : null;
            };
            _sandbox.Set("require", restrictedRequire);

            foreach (ScriptEntry entry in scripts.Entries)
            {
                if (!scriptBytes.TryGetValue(entry.Path, out byte[] bytes) || bytes == null)
                {
                    results.Add(new LuaScriptVerdict(entry.Module, false, false, "缺脚本字节：" + entry.Path));
                    continue;
                }

                // ① 语法
                LuaFunction fn = null;
                try
                {
                    fn = _env.LoadString<LuaFunction>(bytes, entry.Module, _sandbox);
                }
                catch (Exception ex)
                {
                    results.Add(new LuaScriptVerdict(entry.Module, false, false, "语法：" + Shorten(ex.Message)));
                    continue;
                }

                // ② 模块级执行（沙箱内；不调用 main，不产生业务副作用）
                try
                {
                    object[] ret = fn.Call();
                    // 记录返回值供后续模块 require（Lua 模块惯例：返回 table）
                    loaded[entry.Module] = (ret != null && ret.Length > 0) ? ret[0] : null;
                    results.Add(new LuaScriptVerdict(entry.Module, true, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new LuaScriptVerdict(entry.Module, true, false, "执行：" + Shorten(ex.Message)));
                }
                finally
                {
                    fn.Dispose();
                }
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
