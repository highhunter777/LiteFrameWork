using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using XLua;

namespace LiteGame
{
    /// <summary>Lua 侧数据包装：Lua 构造的数据表经 C# 工厂包成 <see cref="IUIData"/>；
    /// 适配器回传时解包出原始 LuaTable——Lua 拿到的永远是自己认识的表，不是 C# 包装对象。</summary>
    public sealed class LuaUIData : IUIData
    {
        public LuaTable Table { get; }

        public LuaUIData(LuaTable table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
        }
    }

    /// <summary>
    /// 生命周期桥（实例与调用契约见《UI框架总设计》§5.1）：
    /// 注册表存的是**模块表**，本适配器即**实例工厂**——构造期执行 `module.new()` 得到页面实例
    /// （无 `new` 的旧式逻辑表退化为"模块自身即实例"），把 IUIFormLogic 七回调翻译成
    /// `self:OnInit/OnShow/...`（冒号定义的生命周期方法显式传 self）。
    /// ① 七个 LuaFunction 构造期一次取齐缓存（OnUpdate 每帧路径禁反复 Get）；
    /// ② 方法缺失 = 静默跳过（界面可只写需要的回调）；
    /// ③ 异常防护由 UIForm 层的 SafeCall 统一承担（单回调抛 = 该界面降级，不炸壳）；
    /// ④ 数据回传解包：LuaUIData → 原始 LuaTable；C# 自定义 IUIData 原样传入（userdata）；
    /// ⑤ 受控 API 面（§2.4）：OnInit 建绑定索引 + 给**实例**挂 `self.ui` 门面表（通用派发器，whitelist 单委托
    ///    Action&lt;string, LuaTable&gt;——EventBridge 同款手法，零新增生成）；OnHide 解绑全部按钮
    ///    （池化复用跨环境的安全垫）；
    /// ⑥ **所有权分开（§5.1）**：注册表拥有模块引用，页面拥有实例与回调引用——<see cref="Release"/>
    ///    只释放实例/回调/ui 门面表，**绝不 Dispose 共享模块**。
    /// </summary>
    public sealed class LuaBehaviourAdapter : IUIFormLogic
    {
        /// <summary>ui-API 通用派发方法名（payload 表协议见 Dispatch）。</summary>
        private const string UiApiShim = @"
local c = __ui_api_c
__ui_api_c = nil
return {
    OnButton = function(_, name, fn) c('onButton', { name = name, fn = fn }) end,
    OffButton = function(_, name) c('offButton', { name = name }) end,
    SetText = function(_, name, text) c('setText', { name = name, text = text }) end,
    SetTextKey = function(_, name, key) c('setTextKey', { name = name, key = key }) end,
    SetTextKeyArgs = function(_, name, key, a0, a1, a2, a3) c('setTextKeyArgs',
        { name = name, key = key, arg0 = a0, arg1 = a1, arg2 = a2, arg3 = a3 }) end,
    SetTextKeyPlural = function(_, name, key, count, a0, a1, a2, a3) c('setTextKeyPlural',
        { name = name, key = key, count = count, arg0 = a0, arg1 = a1, arg2 = a2, arg3 = a3 }) end,
    UnbindTextKey = function(_, name) c('unbindTextKey', { name = name }) end,
    SetVisible = function(_, name, visible) c('setVisible', { name = name, visible = visible }) end,
    SetInteractable = function(_, name, on) c('setInteractable', { name = name, on = on }) end,
    SetProgress = function(_, name, value) c('setProgress', { name = name, value = value }) end,
    SetProgressRange = function(_, name, cur, max) c('setProgressRange', { name = name, cur = cur, max = max }) end,
    SetHp = function(_, name, cur, max) c('setHp', { name = name, cur = cur, max = max }) end,
    StartCountdown = function(_, name, seconds) c('startCountdown', { name = name, seconds = seconds }) end,
    StopCountdown = function(_, name) c('stopCountdown', { name = name }) end,
    ShowToast = function(_, text) c('showToast', { text = text }) end,
    ShowBubble = function(_, name, text, duration) c('showBubble', { name = name, text = text, duration = duration or 1.5 }) end,
    ShowFlyText = function(_, name, text) c('showFlyText', { name = name, text = text }) end,
    Pulse = function(_, name, strength, duration) c('pulse', { name = name, strength = strength or 1.2, duration = duration or 0.16 }) end,
    Flash = function(_, name, duration) c('flash', { name = name, duration = duration or 0.3 }) end,
    Slide = function(_, name, ox, oy, duration) c('slide', { name = name, ox = ox or 0, oy = oy or 0, duration = duration or 0.25 }) end,
}";

        private readonly LuaEnv _env;
        private readonly LuaTable _module;                 // 注册表拥有的共享模块（不 Dispose）
        private readonly LuaTable _instance;               // 本页面实例（module.new() 产物或模块自身）
        private readonly LuaFunction _onInit, _onShow, _onUpdate, _onPause, _onCover, _onReveal, _onHide;
        private readonly object[] _selfArgs = new object[1];    // self 复用（零参回调热路径零分配）
        private readonly object[] _selfArgs2 = new object[2];   // self + data
        private readonly object[] _selfArgs3 = new object[3];   // self + form + data（OnInit）
        private UIBindIndex _index;
        private LuaTable _apiTable;
        private bool _released;

        public LuaBehaviourAdapter(LuaEnv env, LuaTable module)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _instance = CreateInstance(_module);
            _onInit = GetFn("OnInit");
            _onShow = GetFn("OnShow");
            _onUpdate = GetFn("OnUpdate");
            _onPause = GetFn("OnPause");
            _onCover = GetFn("OnCover");
            _onReveal = GetFn("OnReveal");
            _onHide = GetFn("OnHide");
        }

        /// <summary>
        /// 实例工厂（§5.1）：模块有 `new` → 执行 `module.new()`，返回非 table 即注册失败；
        /// 无 `new` 的旧式逻辑表（直接 `return { OnShow = ... }`）= 模块自身即实例。
        /// </summary>
        private static LuaTable CreateInstance(LuaTable module)
        {
            var ctor = module.Get<LuaFunction>("new");
            if (ctor == null) return module;
            try
            {
                var result = ctor.Call(module);
                if (result != null && result.Length > 0 && result[0] is LuaTable instance) return instance;
                throw new InvalidOperationException("模块 new() 未返回 table——实例工厂契约（§5.1）");
            }
            finally
            {
                ctor.Dispose();
            }
        }

        public void OnInit(UIForm form, IUIData data)
        {
            _index = BindIndexBuilder.Build(form.Root);       // 路径 A：Lua 界面恒走标记索引

            var api = _env.NewTable();
            var dispatch = new Action<string, LuaTable>(Dispatch);
            _env.Global.Set<string, Action<string, LuaTable>>("__ui_api_c", dispatch);
            var shim = _env.DoString(UiApiShim, "ui_api_shim");
            _env.DoString("__ui_api_c = nil");
            if (shim != null && shim.Length > 0 && shim[0] is LuaTable apiTable)
            {
                _apiTable?.Dispose();                          // 复用后再跑 OnInit（换表重建）：旧门面表放手
                _apiTable = apiTable;
                _instance.Set<string, LuaTable>("ui", apiTable);   // Lua 侧 self.ui:OnButton / SetText / ...
            }

            _selfArgs3[0] = _instance;
            _selfArgs3[1] = form;
            _selfArgs3[2] = ToLuaArg(data);
            CallRaw(_onInit, _selfArgs3);
        }

        public void OnShow(IUIData data) { _selfArgs2[0] = _instance; _selfArgs2[1] = ToLuaArg(data); CallRaw(_onShow, _selfArgs2); }
        public void OnUpdate(float deltaTime) { _selfArgs2[0] = _instance; _selfArgs2[1] = deltaTime; CallRaw(_onUpdate, _selfArgs2); }
        public void OnPause() => CallSelf(_onPause);
        public void OnCover() => CallSelf(_onCover);
        public void OnReveal() => CallSelf(_onReveal);

        public void OnHide()
        {
            _index?.UnbindAll();                              // 池化复用跨环境安全垫：旧 fn 监听全部移除
            CallSelf(_onHide);
        }

        /// <summary>页面实例（诊断/换表释放用）。</summary>
        public LuaTable Logic => _instance;

        /// <summary>注册表持有的共享模块（所有权在注册表——诊断用，调用方不得 Dispose）。</summary>
        public LuaTable Module => _module;

        /// <summary>是否已释放（幂等守卫 + 测试断言用）。</summary>
        public bool Released => _released;

        /// <summary>
        /// 释放（§2.3 运行期增量重填 / §10.2 env 重建）：解绑按钮监听 + 释放**本页面拥有**的
        /// Lua 引用（实例、已缓存回调、ui 门面表）。**不释放共享模块**（§5.1 所有权分开——模块归注册表）。
        /// 幂等；调用前提 = 本适配器已不被任何界面使用（换表 / 全关 / env 重建前）。
        /// </summary>
        public void Release()
        {
            if (_released) return;
            _released = true;

            _index?.UnbindAll();
            _onInit?.Dispose();
            _onShow?.Dispose();
            _onUpdate?.Dispose();
            _onPause?.Dispose();
            _onCover?.Dispose();
            _onReveal?.Dispose();
            _onHide?.Dispose();
            _apiTable?.Dispose();
            _apiTable = null;
            if (!ReferenceEquals(_instance, _module)) _instance.Dispose();   // 模块自身即实例时不越权释放
        }

        /// <summary>LText 模板参数的 Lua 侧上限（具名 arg0..argN；定长避免引入变长表协议）。</summary>
        public const int MaxLuaFormatArgs = 4;

        /// <summary>
        /// 取 LText 模板参数：约定 Lua 侧传具名 <c>arg0..arg3</c>。
        ///
        /// **为何不用变长表**：xLua 的 <c>LuaTable.Get</c> 对嵌套数组的映射语义不由本项目控制，
        /// 猜错会静默取到空值；具名定长键语义确定，且与既有 payload 协议（全具名）一致。
        /// </summary>
        private static object[] LuaFormatArgs(LuaTable payload)
        {
            var args = new List<object>(MaxLuaFormatArgs);
            for (int i = 0; i < MaxLuaFormatArgs; i++)
            {
                object v = payload.Get<string, object>("arg" + i);
                if (v == null) break;
                args.Add(v);
            }
            return args.Count == 0 ? null : args.ToArray();
        }

        /// <summary>ui-API 通用派发（payload 表协议）：onButton{name,fn} / offButton{name} /
        /// setText{name,text} / setVisible{name,visible} / setInteractable{name,on} /
        /// setProgress{name,value} / setProgressRange{name,cur,max} / setHp{name,cur,max} /
        /// startCountdown{name,seconds} / stopCountdown{name} / showToast{text} /
        /// showBubble{name,text,duration} / showFlyText{name,text} /
        /// pulse{name,strength,duration} / flash{name,duration} / slide{name,ox,oy,duration}。未识别方法静默忽略。</summary>
        private void Dispatch(string method, LuaTable payload)
        {
            if (_index == null || payload == null) return;
            switch (method)
            {
                case "onButton":
                    var name = payload.Get<string, string>("name");
                    _index.BindButton(name, () =>
                    {
                        var fn = payload.Get<LuaFunction>("fn");
                        if (fn != null) fn.Call();
                    });
                    break;
                case "offButton": _index.UnbindButton(payload.Get<string, string>("name")); break;
                case "setText": _index.SetText(payload.Get<string, string>("name"), payload.Get<string, string>("text")); break;
                // ---- LText（《UI框架总设计》§9；Lua API 参考 Bridge.text.Get/Format 的控件面）----
                case "setTextKey":
                    _index.SetTextKey(payload.Get<string, string>("name"), payload.Get<string, string>("key"));
                    break;
                case "setTextKeyPlural":
                    // 参数以具名 arg0/arg1/... 传递（Lua 侧拼表；定长上限见 MaxLuaFormatArgs）
                    _index.SetTextKeyPlural(payload.Get<string, string>("name"), payload.Get<string, string>("key"),
                        (long)payload.Get<string, double>("count"), LuaFormatArgs(payload));
                    break;
                case "setTextKeyArgs":
                    _index.SetTextKey(payload.Get<string, string>("name"), payload.Get<string, string>("key"),
                        LuaFormatArgs(payload));
                    break;
                case "unbindTextKey":
                    _index.UnbindTextKey(payload.Get<string, string>("name"));
                    break;
                case "setVisible": _index.SetVisible(payload.Get<string, string>("name"), payload.Get<string, bool>("visible")); break;
                case "setInteractable": _index.SetInteractable(payload.Get<string, string>("name"), payload.Get<string, bool>("on")); break;
                // ---- 受控 API 扩展 ----
                case "setProgress": _index.SetProgress(payload.Get<string, string>("name"), payload.Get<string, float>("value")); break;
                case "setProgressRange": _index.SetProgress(payload.Get<string, string>("name"), payload.Get<string, float>("cur"), payload.Get<string, float>("max")); break;
                case "setHp": _index.SetHp(payload.Get<string, string>("name"), payload.Get<string, float>("cur"), payload.Get<string, float>("max")); break;
                case "startCountdown": _index.StartCountdown(payload.Get<string, string>("name"), payload.Get<string, float>("seconds")); break;
                case "stopCountdown": _index.StopCountdown(payload.Get<string, string>("name")); break;
                case "showToast": _index.ShowToast(payload.Get<string, string>("text")); break;
                case "showBubble": _index.ShowBubble(payload.Get<string, string>("name"), payload.Get<string, string>("text"), payload.Get<string, float>("duration")); break;
                case "showFlyText": _index.ShowFlyText(payload.Get<string, string>("name"), payload.Get<string, string>("text")); break;
                // ---- 动效口（《动效设计方案》附 A.3）----
                case "pulse":
                    _index.Pulse(payload.Get<string, string>("name"),
                                 payload.Get<string, float>("strength"),
                                 payload.Get<string, float>("duration"));
                    break;
                case "flash":
                    _index.Flash(payload.Get<string, string>("name"),
                                 payload.Get<string, float>("duration"));
                    break;
                case "slide":
                    _index.Slide(payload.Get<string, string>("name"),
                                 new Vector2(payload.Get<string, float>("ox"), payload.Get<string, float>("oy")),
                                 payload.Get<string, float>("duration"));
                    break;
            }
        }

        private LuaFunction GetFn(string name) => _instance.Get<LuaFunction>(name);

        /// <summary>零参回调（冒号定义 → 显式传 self；复用预置数组，热路径零分配）。</summary>
        private void CallSelf(LuaFunction fn)
        {
            if (fn == null) return;                             // 界面未实现该回调：静默跳过
            _selfArgs[0] = _instance;
            CallRaw(fn, _selfArgs);
        }

        /// <summary>
        /// 统一调用口：已释放的适配器不得再访问 env（§10.2 旧 env 零访问——调用已 Dispose 的
        /// LuaFunction 会打进死环境）。
        /// </summary>
        private void CallRaw(LuaFunction fn, object[] args)
        {
            if (fn == null || _released) return;
            fn.Call(args);
        }

        private static object ToLuaArg(IUIData data)
            => data is LuaUIData lua ? (object)lua.Table : data;
    }
}
