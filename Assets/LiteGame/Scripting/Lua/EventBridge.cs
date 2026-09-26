using System;
using System.Collections.Generic;
using LiteFramework;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// 事件桥（M3 §2.6，手册步骤 6）：C# 事件 → 逐回调 pcall（经 SafeCall）→ Lua 回调表。
    /// ① Lua 端 <c>events.on(name, fn)</c> 返回注销委托——Lua 闭包实现（core/eventer.lua 同款语义，
    ///    经诊断口装配，零白名单新增）；② 映射规则由事件桥认领：桥内维护 string 事件名 → C# 事件类型
    ///    的显式映射注册表（新增桥接事件 = 一行 Map）；③ 回调逐个 SafeCall：单个回调抛异常 →
    ///    C# 日志可见、派发链存活（验收线 6）。生命周期：LuaComponent.Init 创建，Shutdown 先于 env.Dispose 释放。
    /// </summary>
    public sealed class EventBridge : IDisposable
    {
        /// <summary>M3 机制验证事件名（验收线 6 用；M5 起被真实玩法事件映射替代）。</summary>
        public const string ProbeEventName = "bridge.probe";

        /// <summary>M3 机制验证事件——无真实玩法事件，不扩展此词汇（设计方案 §2.6"别造假事件"）。</summary>
        public sealed class BridgeProbeEvent
        {
            public string Message;
        }

        private const string EventOnShim = @"
local cbs = __bridge_callbacks
__bridge_callbacks = nil
return function(name, fn)
    assert(type(fn) == 'function', 'events.on 需要函数')
    local list = cbs[name]
    if list == nil then
        list = {}
        cbs[name] = list
    end
    table.insert(list, fn)
    return function()
        local cur = cbs[name]
        if cur == nil then return end
        for i = 1, #cur do
            if cur[i] == fn then
                table.remove(cur, i)
                break
            end
        end
    end
end";

        private readonly IEventCenter _center;
        private readonly LuaTable _callbacks;                  // 事件名 → { fn, ... }（events.on 写入，C# 派发读取）
        private readonly List<Action> _detachments = new List<Action>(4);
        private bool _disposed;

        public EventBridge(LuaEnv env, IEventCenter center)
        {
            _center = center ?? throw new ArgumentNullException(nameof(center));
            _callbacks = env.NewTable();

            env.Global.Set("__bridge_callbacks", _callbacks);
            var on = (LuaFunction)env.DoString(EventOnShim, "eventbridge_on")[0];
            var events = env.NewTable();
            events.Set("on", on);
            env.Global.Set("events", events);

            // 桥内显式映射注册表（新增桥接事件 = 这里加一行）——M3 最少集：1 个机制验证事件
            Map<BridgeProbeEvent>(ProbeEventName, e => new object[] { e.Message });
        }

        /// <summary>一行注册：C# 事件类型 → Lua 事件名；handler 收事件后按映射名逐回调派发。</summary>
        private void Map<TEvent>(string luaEvent, Func<TEvent, object[]> pack) where TEvent : class
        {
            _detachments.Add(_center.Subscribe<TEvent>(e => Dispatch(luaEvent, pack(e))));
        }

        /// <summary>派发：逐回调 SafeCall（单个抛异常不中断链路，异常经 C# 日志可见）。</summary>
        private void Dispatch(string luaEvent, object[] args)
        {
            if (_disposed || !_callbacks.ContainsKey(luaEvent)) return;
            var list = _callbacks.Get<LuaTable>(luaEvent);
            int n = list.Length;
            for (int i = 1; i <= n; i++)
            {
                var fn = list.Get<int, LuaFunction>(i);
                if (fn == null) continue;
                var index = i;
                SafeCall.Invoke(() => fn.Call(args), $"event[{luaEvent}]#{index}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var detach in _detachments)
                SafeCall.Invoke(detach, "eventbridge.detach");
            _detachments.Clear();
            _callbacks.Dispose();                              // env 释放前先解桥（Shutdown 编排顺序，§2.7）
        }
    }
}
