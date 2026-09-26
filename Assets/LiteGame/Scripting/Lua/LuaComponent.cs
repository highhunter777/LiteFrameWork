using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// Lua 宿主（M3 步骤 2.3，手册 §五步骤 2——框架唯一新增核心模块）。职责四件：
    /// ① `LuaEnv` + 自定义 loader（**同步签名**，只从 <see cref="LuaPreloader"/> 预载缓存取——
    ///    "任何'运行时再异步加载'的念头都是错的"，设计方案 §4.2；未命中 = 校验漏项，带路径直接抛）；
    /// ② tick 派发开关（`env.Tick()`——Lua 不需要时不调，MonoBehaviour.Update 驱动）；
    /// ③ 执行 `main.lua`（只 require/定义，重复执行抛——重跑走 DevReload §2.7）；
    /// ④ `Shutdown()`：env.Dispose（OnDestroy 兜底，play 退出自然触发）。
    /// 附带：`log` 全局表绑定（info/warning/error → LiteFramework.Log，§4.3 日志收口白名单的首批成员）；
    /// IModuleStats（HUD：已载文件数 / main 执行 / tick 开关）。
    /// 热路径纪律（§4.3）：tick 走 env.Tick()；Bridge 查表等低频入口允许动态转换。
    /// 生命周期：装配点 AddComponent + Init（2.4 接线）；Shutdown 幂等。
    /// </summary>
    public sealed class LuaComponent : MonoBehaviour, IModuleStats
    {
        private LuaEnv _env;
        private LuaPreloader _preloader;
        private EventBridge _eventBridge;
        private bool _tickEnabled;
        private bool _mainExecuted;

        /// <summary>tick 派发开关（Lua 侧无定时器/协程需求时关闭，省每帧调用）。</summary>
        public bool TickEnabled
        {
            get => _tickEnabled;
            set { ThrowIfNotInit(); _tickEnabled = value; }
        }

        /// <summary>main.lua 是否已执行（DevReload 重跑前必为 true，§2.7）。</summary>
        public bool MainExecuted => _mainExecuted;

        /// <summary>LuaEnv 访问器（装配期只读——LuaBehaviourAdapter 构建 ui-API 表用；未初始化访问抛）。</summary>
        public LuaEnv Env
        {
            get
            {
                ThrowIfNotInit();
                return _env;
            }
        }

        /// <summary>初始化 env + 注册自定义 loader + 绑定服务桥/事件桥（AddComponent 后调用一次；重复调用抛）。</summary>
        public void Init(LuaPreloader preloader, IEventCenter eventCenter)
        {
            if (_env != null) throw new InvalidOperationException("LuaComponent 已初始化——重复 Init");
            _preloader = preloader ?? throw new ArgumentNullException(nameof(preloader));

            _env = new LuaEnv();
            _env.AddLoader(LoadFromCache);
            BindLog();
            BindBridge();                                      // 服务桥（§2.5）：Bridge.data/ui/content 三门面
            _eventBridge = new EventBridge(_env, eventCenter); // 事件桥（§2.6）：events.on + 显式映射注册
#if UNITY_EDITOR
            // LuaPanda 断点钩子（手册步骤 9）：仅编辑器启用（宏隔离——hook 进包 = 真机莫名掉帧）。
            // LuaPanda.lua 随 Lua 目录分发进预载缓存；未放入时静默跳过，不阻塞开发流。
            if (_preloader.Scripts.ContainsKey("LuaPanda"))
                DoString("require('LuaPanda').start()", "luaPanda");
#endif
            Log.Info("LuaEnv 初始化完成（loader=预载缓存，桥已绑定）", "Lua");
        }

        /// <summary>
        /// §4.3 服务桥绑定（§2.5）：Bridge.data/ui/content 组装成全局表——"启动时把容器服务显式绑定成
        /// Lua 全局表"字面满足。门面方法全走白名单委托 Func&lt;int, LuaTable&gt;（2.1 生成代码已含，零再生成）；
        /// 只导出三门面，ILuaRegistry/玩法系统一律不导出；禁止 CS. 直引业务类型。
        /// </summary>
        private void BindBridge()
        {
            var data = _env.NewTable();
            data.Set("GetItem", new Func<int, LuaTable>(id => Bridge.Data.GetItemLua(_env, id)));
            data.Set("GetUIForm", new Func<int, LuaTable>(id => Bridge.Data.GetUIFormLua(_env, id)));
            var ui = _env.NewTable();
            ui.Set("GetLogic", new Func<int, LuaTable>(Bridge.Ui.GetLogic));
            ui.Set("Show", new Action<int, LuaTable>(Bridge.Ui.Show));          // 真实门面（M4 §2.3，委托需 Generate Code）
            ui.Set("Close", new Action<int>(Bridge.Ui.Close));
            ui.Set("IsOpen", new Func<int, bool>(Bridge.Ui.IsOpen));
            var content = _env.NewTable();
            content.Set("GetProcessor", new Func<int, LuaTable>(Bridge.Content.GetProcessor));
            var bridge = _env.NewTable();
            bridge.Set("data", data);
            bridge.Set("ui", ui);
            bridge.Set("content", content);
            _env.Global.Set("Bridge", bridge);
        }

        /// <summary>
        /// 自定义 loader：require 路径 → 预载缓存字节。
        /// filepath 先按点分转斜杠（"Core.class" → "Core/class"），再尝试原样；未命中抛（带路径）。
        /// </summary>
        private byte[] LoadFromCache(ref string filepath)
        {
            var key = filepath.Replace('.', '/');
            if (_preloader.Scripts.TryGetValue(key, out var bytes)) return bytes;
            if (_preloader.Scripts.TryGetValue(filepath, out bytes)) return bytes;
            throw new InvalidOperationException(
                $"Lua 预载缓存未命中:{filepath}（require 路径校验漏项——核对 LuaKeys/收集目录）");
        }

        /// <summary>执行 main.lua（只 require/定义，逻辑表按约定路径暴露）。重复执行抛。</summary>
        public void DoMain()
        {
            ThrowIfNotInit();
            if (_mainExecuted) throw new InvalidOperationException("main.lua 已执行——重跑走 DevReload（§2.7），禁止二次 DoMain");

            _env.DoString(_preloader.Scripts["main"], "main.lua");
            _mainExecuted = true;
            Log.Info("main.lua 执行完成", "Lua");
        }

        /// <summary>执行 Lua 片段并返回结果（诊断/面板/后续 DevReload 编排用；低频入口）。</summary>
        public object[] DoString(string chunk, string chunkName = "diag")
        {
            ThrowIfNotInit();
            return _env.DoString(chunk, chunkName);
        }

        /// <summary>
        /// 重预载 Lua 文件（运行期增量重填第 ② 步，M4 §2.3）：**不重建 env**，只把 tag `lua` 清单重读一遍
        /// （LuaPreloader 内部先清 `_scripts`），变更后的 .lua 字节即进缓存，下次 require 命中新内容。
        /// </summary>
        public async UniTask RepreloadAsync(CancellationToken ct = default)
        {
            ThrowIfNotInit();
            await _preloader.PreloadAllAsync(ct);
        }

        /// <summary>
        /// 清 require 缓存（运行期增量重填第 ④ 步）：按**注册表根前缀**清 `package.loaded`
        /// （`UI.` / `Content.` / `Strategies.` —— 根集合由 `gen_lua_keys.py` 校验收口，是封闭集）。
        /// 不清 env 级模块（`Bridge`/`log`/LuaPanda/Core.*）——全清会误伤、且无必要。
        /// 返回实际清掉的键数。**已知边界**：只清三个根下的模块，其跨根依赖（如 `Core.class`）仍走缓存。
        /// </summary>
        public int ClearRequireCacheByRoots(IReadOnlyList<string> roots)
        {
            ThrowIfNotInit();
            if (roots == null || roots.Count == 0) return 0;

            var sb = new System.Text.StringBuilder(roots.Count * 16 + 320);
            sb.Append("local r={");
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (!IsSafeRoot(root)) continue;
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(root).Append('"');
            }
            sb.Append("}\nlocal n=0\nfor k in pairs(package.loaded) do\n")
              .Append("  if type(k)=='string' then\n")
              .Append("    for i=1,#r do\n")
              .Append("      local p=r[i]\n")
              .Append("      if k==p or string.sub(k,1,#p+1)==p..'.' then package.loaded[k]=nil n=n+1 break end\n")
              .Append("    end\n  end\nend\nreturn n");

            var result = _env.DoString(sb.ToString(), "refill_clear_require");
            if (result == null || result.Length == 0) return 0;
            try { return Convert.ToInt32(result[0]); }
            catch (Exception) { return 0; }
        }

        /// <summary>根名白名单（点分/引号等字符不得进 Lua 片段——本方法拼的是 Lua 源码）。</summary>
        private static bool IsSafeRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            for (int i = 0; i < root.Length; i++)
            {
                char c = root[i];
                bool ok = c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                          (i > 0 && c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// require 路径是否已在预载缓存（RegistryFiller 区分"未注册/注册失败"用——§3.4 错误语义，
        /// 不靠 require 抛错后的字符串匹配）。点分转斜杠 + 原样双试，与 loader 同一契约。
        /// </summary>
        public bool HasCached(string requirePath)
        {
            ThrowIfNotInit();
            var key = requirePath.Replace('.', '/');
            return _preloader.Scripts.ContainsKey(key) || _preloader.Scripts.ContainsKey(requirePath);
        }

        /// <summary>§4.3 日志收口：Lua 侧 log.* 白名单进 LiteFramework.Log（首批成员：info/warning/error）。</summary>
        private void BindLog()
        {
            var logTable = _env.NewTable();
            logTable.Set("info", new Action<string>(msg => Log.Info(msg, "Lua")));
            logTable.Set("warning", new Action<string>(msg => Log.Warning(msg, "Lua")));
            logTable.Set("error", new Action<string>(msg => Log.Error(msg, "Lua")));
            _env.Global.Set("log", logTable);
        }

        private void Update()
        {
            if (_tickEnabled && _env != null) _env.Tick();
        }

        /// <summary>释放 env（幂等）。DevReload 场景下由 §2.7 编排调用；play 退出由 OnDestroy 兜底。</summary>
        public void Shutdown()
        {
            if (_env == null) return;
            _eventBridge?.Dispose();                           // 先解事件桥（退订 C# 事件），再销毁 env
            _eventBridge = null;
            _env.Dispose();                                // 全局表随 env 销毁；Bridge 缓存清空是 §2.7 的职责
            _env = null;
            _mainExecuted = false;
            _tickEnabled = false;
            Log.Info("LuaEnv 已释放", "Lua");
        }

        private void OnDestroy() => Shutdown();

        private void ThrowIfNotInit()
        {
            if (_env == null)
                throw new InvalidOperationException("LuaComponent 未初始化——Init(LuaPreloader) 必须先于一切调用（2.4 接线）");
        }

        // ---- IModuleStats（HUD 数据源）----

        string IModuleStats.StatsName => "Lua";

        void IModuleStats.Snapshot(Dictionary<string, string> into)
        {
            into["Files"] = (_preloader?.Count ?? 0).ToString();
            into["MainExecuted"] = _mainExecuted.ToString();
            into["TickEnabled"] = _tickEnabled.ToString();
            into["EnvAlive"] = (_env != null).ToString();
        }
    }
}
