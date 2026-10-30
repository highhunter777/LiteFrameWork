using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using UnityEngine;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// 生命周期桥（实例与调用契约见《UI框架总设计》§5.1）：
    /// 注册表存的是**模块表**，本适配器即**实例工厂**——构造期执行 `module.new()` 得到页面实例
    /// （无 `new` 的旧式逻辑表退化为"模块自身即实例"），把 IUIFormLogic 七回调翻译成
    /// `self:OnInit/OnShow/...`（冒号定义的生命周期方法显式传 self）。
    /// ① 七个 LuaFunction 构造期一次取齐缓存（OnUpdate 每帧路径禁反复 Get）；
    /// ② 方法缺失 = 静默跳过（界面可只写需要的回调）；
    /// ③ 异常防护由 UIForm 层的 SafeCall 统一承担（单回调抛 = 该界面降级，不波及其他界面）；
    /// ④ 数据回传解包：LuaUIData → 原始 LuaTable；C# 自定义 IUIData 原样传入（userdata）；
    /// ⑤ 受控 API 面（§2.4）：OnInit 建绑定索引 + 给**实例**挂 `self.ui` 门面表（通用派发器，whitelist 单委托
    ///    Action&lt;string, LuaTable&gt;——EventBridge 同款手法，零新增生成）；OnHide 解绑全部按钮
    ///    （池化复用跨环境的安全垫）；
    /// ⑥ **所有权分开（§5.1）**：注册表拥有模块引用，页面拥有实例与回调引用——<see cref="Release"/>
    ///    只释放实例/回调/ui 门面表，**绝不 Dispose 共享模块**。
    /// </summary>
    public sealed partial class LuaBehaviourAdapter : IUIFormLogic, ILocaleBound
    {
        /// <summary>ui-API 的 Lua 门面模板（生成物 <see cref="UiApiShim.LuaTemplate"/>；
        /// 缺省时长占位符在此替换为 <see cref="UiFxDefaults"/> 的值）。</summary>
        private static readonly string UiApiShimSource = UiApiShim.LuaTemplate
            .Replace("%FxPulseStrength%", ToLuaLiteral(UiFxDefaults.PulseStrength))
            .Replace("%FxPulseDuration%", ToLuaLiteral(UiFxDefaults.PulseDuration))
            .Replace("%FxFlashDuration%", ToLuaLiteral(UiFxDefaults.FlashDuration))
            .Replace("%FxSlideDuration%", ToLuaLiteral(UiFxDefaults.SlideDuration))
            .Replace("%FxBubbleDuration%", ToLuaLiteral(UiFxDefaults.BubbleDuration));

        /// <summary>float → Lua 数字字面量（InvariantCulture：跨区域环境小数点恒定）。</summary>
        private static string ToLuaLiteral(float v)
            => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private readonly LuaEnv _env;
        private readonly LuaTable _module;                 // 注册表拥有的共享模块（不 Dispose）
        private readonly LuaTable _instance;               // 本页面实例（module.new() 产物或模块自身）
        private readonly LuaFunction _onInit, _onShow, _onUpdate, _onPause, _onCover, _onReveal, _onHide;
        private readonly object[] _selfArgs = new object[1];    // self 复用（零参回调热路径零分配）
        private readonly object[] _selfArgs2 = new object[2];   // self + data
        private readonly object[] _selfArgs3 = new object[3];   // self + form + data（OnInit）
        private UIBindIndex _index;
        private LuaTable _apiTable;
        private readonly Func<string, string, string, string, UniTask<bool>> _confirm;   // 确认缝（FeedbackService→DialogService；null=测试/工具装配）
        private bool _released;

        public LuaBehaviourAdapter(LuaEnv env, LuaTable module,
            Func<string, string, string, string, UniTask<bool>> confirm = null)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _module = module ?? throw new ArgumentNullException(nameof(module));
            _confirm = confirm;
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
            var dispatch = new Func<string, LuaTable, object>(Dispatch);
            _env.Global.Set<string, Func<string, LuaTable, object>>("__ui_api_c", dispatch);
            var shim = _env.DoString(UiApiShimSource, "ui_api_shim");
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

        /// <summary>语言服务注入（<see cref="ILocaleBound"/> 可选能力）：转发绑定索引——
        /// 注入时机归 <see cref="UIForm"/>（OnInit 后、OnShow 前）；index 未建/已释放 = no-op。
        /// 池化复用不重跑 OnInit——绑定随索引留存；换表重建索引后由 UIForm 再注入一次。</summary>
        public void BindLocale(ILocalizationService localization)
            => _index?.BindLocale(localization);

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

        /// <summary>ui-API 派发入口：生成表命中即走 <see cref="TryDispatchApi"/>；
        /// 未命中交给手工处理器；仍未知则开发期告警（拼写错误不得静默吞）。</summary>
        private object Dispatch(string method, LuaTable payload)
        {
            if (_index == null || payload == null) return null;
            if (TryDispatchApi(method, payload, out object result)) return result;
            // 未知方法 = Lua 侧拼写错误（一次性告警，不在热路径）：发布也保留，便于线上排查。
            Log.Warning($"ui-API 未知方法:{method}（拼写错误不得静默吞——《UI控件Lua API 参考》§3）", LogTag.UI);
            return null;
        }

        /// <summary>手工处理器（spec 标 manual 的条目）：Lua 闭包按钮、确认弹窗、列表行转换、行点击。</summary>
        private bool TryDispatchManual(string method, LuaTable payload, out object result)
        {
            result = null;
            switch (method)
            {
                case "onButton":
                    var name = payload.Get<string, string>("name");
                    _index.BindButton(name, () =>
                    {
                        var fn = payload.Get<LuaFunction>("fn");
                        if (fn != null) fn.Call();
                    });
                    return true;
                case "showConfirm":
                    ShowConfirmAsync(payload);
                    return true;
                case "setList":
                    _index.SetList(Text(payload, "name"),
                        new LuaListRows(payload.Get<string, LuaTable>("rows")));
                    return true;
                case "onListClick":
                    {
                        LuaFunction fn = payload.Get<LuaFunction>("fn");
                        _index.OnListClick(Text(payload, "name"),
                            fn == null ? (Action<string, string>)null : (key, action) => fn.Call(key, action));
                    }
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>确认弹窗（payload：<c>showConfirm{title,message,okText,cancelText,fn}</c>）：经装配注入的
        /// 确认缝（生产＝FeedbackService.ConfirmAsync → DialogService）等结果并回 Lua <c>fn(ok)</c>。
        /// 无缝（测试/工具装配）/取消/异常一律回调 false（页面链不悬挂）——fire-and-forget 与 Toast 同口径。</summary>
        private async UniTaskVoid ShowConfirmAsync(LuaTable payload)
        {
            LuaFunction fn = payload.Get<LuaFunction>("fn");
            try
            {
                if (_confirm == null)
                {
                    Log.Warning("ShowConfirm 无确认服务（测试/工具装配）——回调 false", LogTag.UI);
                    fn?.Call(false);
                    return;
                }
                bool ok = await _confirm(
                    Text(payload, "title"), Text(payload, "message"),
                    Text(payload, "okText"), Text(payload, "cancelText"));
                fn?.Call(ok);
            }
            catch (OperationCanceledException)
            {
                fn?.Call(false);
            }
            catch (Exception ex)
            {
                Log.Error($"ShowConfirm 失败:{ex.Message}", LogTag.UI);
                fn?.Call(false);
            }
        }

        /// <summary>取字符串载荷位（缺 = 空串）。</summary>
        private static string Text(LuaTable payload, string field)
            => payload.Get<string, string>(field) ?? string.Empty;

        /// <summary>取 Lua 字符串数组载荷位（空表/缺省 = 空列表）。</summary>
        private static IReadOnlyList<string> ReadStrings(LuaTable payload, string field)
        {
            LuaTable table = payload.Get<string, LuaTable>(field);
            if (table == null) return Array.Empty<string>();
            int n = table.Length;
            var list = new List<string>(n);
            for (int i = 1; i <= n; i++) list.Add(table.Get<int, string>(i) ?? string.Empty);
            return list;
        }

        /// <summary>回调取件（缺 fn = null → 退订语义）；单参回调形（带值）。</summary>
        private static Action<T> Arg1<T>(LuaTable payload, Action<LuaFunction, T> invoke)
        {
            LuaFunction fn = payload.Get<LuaFunction>("fn");
            return fn == null ? null : (Action<T>)(v => invoke(fn, v));
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
