using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// Lua 宿主端口（框架服务侧契约）：产品模块只经本端口消费脚本环境，
    /// **不出现 XLua 适配器类型**（LuaComponent / LuaEnv / LuaPreloader）。
    /// 实现归 <c>LiteFramework.Adapters</c> 的 XLua 适配层，由装配点登记进容器。
    /// 绑定委托的环境参数以 <see cref="object"/> 表达（实现侧转 <c>LuaEnv</c>），
    /// 使本契约所在程序集不引 XLua——模块签名因此也不出现脚本后端类型。
    /// </summary>
    public interface ILuaHost
    {
        /// <summary>main.lua 是否已执行（DevReload 重跑前必为 true）。</summary>
        bool MainExecuted { get; }

        /// <summary>tick 派发开关（Lua 侧无定时器/协程需求时关闭，省每帧调用）。</summary>
        bool TickEnabled { get; set; }

        /// <summary>
        /// 启动序列（预载 → 建 env → 执行 main → 开 tick）：产品流程的预载锚点只调本方法。
        /// </summary>
        UniTask StartAsync(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider,
            Func<string[]> listLuaFiles, string luaDir, IEventBus eventBus,
            Action<object> bindGlobals = null, CancellationToken ct = default);

        /// <summary>执行 main.lua（只 require/定义；重复执行抛）。</summary>
        void DoMain();

        /// <summary>执行 Lua 片段并返回结果（诊断/注册表填充等低频入口）。</summary>
        object[] DoString(string chunk, string chunkName = "diag");

        /// <summary>重预载 Lua 文件（不重建 env）。</summary>
        UniTask RepreloadAsync(CancellationToken ct = default);

        /// <summary>按根前缀清 require 缓存，返回实际清掉的键数。</summary>
        int ClearRequireCacheByRoots(IReadOnlyList<string> roots);

        /// <summary>require 路径是否已在预载缓存。</summary>
        bool HasCached(string requirePath);

        /// <summary>释放 env（幂等）。</summary>
        void Shutdown();
    }
}
