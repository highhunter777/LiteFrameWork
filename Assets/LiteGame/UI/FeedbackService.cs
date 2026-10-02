using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>
    /// 统一反馈入口（《UI框架总设计》§6.1"Loading、空状态、错误/重试、Toast 有统一入口；
    /// 请求失败的反馈可观察，关键错误界面必须可从内置资源启动"）。
    ///
    /// 三面（**视觉一律取控件模板**，见 §7 视觉单一来源与服务层职责）：
    /// - **Loading**（<see cref="BeginLoading"/>）：嵌套计数阻断面——最后一个结束才解除；
    ///   阻断期间平台 Back 经 <see cref="TryCancelLoading"/> 取消当前操作（§6.1 Back 优先级：
    ///   "Loading 阻断期间按操作取消规则处理返回，不能悄悄穿透到下层"——经
    ///   <see cref="UINavigationController.BackInterceptor"/> 接入导航链首位）。
    /// - **错误/重试**（<see cref="ShowErrorAsync"/>）：经 <see cref="DialogService"/> 落地——
    ///   同 MergeKey 的重复错误弹窗自动合并（§4.3"重复断线/错误弹窗按键合并"）；返回是否重试。
    /// - **Toast**（<see cref="ShowToast"/>）：非阻断短提示，**多条并存**各自计时（§6.1），
    ///   由承载控件 <see cref="Toast"/> 做纵向堆叠与最旧淘汰。
    ///
    /// 三者同为 **System 语义层**的 form（layer=3，§6.2"反馈面归 System 层"），经统一排序器分配 order——
    /// 本服务**不自建 Canvas、不使用私有 sortingOrder**，只持有 <see cref="UIService"/> 打开的实例并驱动其状态。
    ///
    /// 层内不互斥：三面均 `full_screen=false`，同组可并行（全屏会触发同组 Replace 语义互相顶掉）。
    /// Loading 的阻断靠遮罩射线（§6.2"输入禁用、射线阻断和焦点是不同职责"），不靠停组。
    /// </summary>
    public sealed class FeedbackService : IDisposable
    {
        /// <summary>Toast 默认时长（秒）。</summary>
        public const float DefaultToastSeconds = 2f;

        private readonly UIService _ui;
        private readonly DialogService _dialogs;
        private readonly UINavigationController _nav;
        private readonly int _errorFormId;
        private readonly int _loadingFormId;
        private readonly int _toastFormId;

        /// <summary>反馈面打开口（默认经 <see cref="UIService.ShowAsync"/>；测试可注入替身以脱离真表与真资源）。</summary>
        private readonly Func<int, UniTask<UIForm>> _open;

        /// <summary>反馈面关闭口（默认经 <see cref="UIService.CloseAsync"/>）。</summary>
        private readonly Func<int, UniTask> _close;

        /// <summary>表单是否仍打开（默认经 <see cref="UIService.IsOpen"/>）。</summary>
        private readonly Func<int, bool> _isOpen;

        private int _loadingDepth;
        private CancellationTokenSource _loadingCts;
        private UIForm _loadingForm;
        private UIForm _toastForm;
        private Toast _toast;
        private bool _disposed;

        /// <param name="errorFormId">错误弹窗表单（UIDialog 结构的表行——专用 ErrorPage prefab 由制作线替换）。</param>
        /// <param name="loadingFormId">加载遮罩表单（LoadingMask 结构）。</param>
        /// <param name="toastFormId">Toast 承载表单（Toast 结构）。</param>
        /// <param name="open">反馈面打开口（null = 用 <see cref="UIService"/> 真实现）。</param>
        /// <param name="close">反馈面关闭口（null = 用 <see cref="UIService"/> 真实现）。</param>
        /// <param name="isOpen">打开态查询口（null = 用 <see cref="UIService"/> 真实现）。</param>
        public FeedbackService(UIService ui, DialogService dialogs, UINavigationController nav,
            int errorFormId = 201, int loadingFormId = 202, int toastFormId = 203,
            Func<int, UniTask<UIForm>> open = null,
            Func<int, UniTask> close = null,
            Func<int, bool> isOpen = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _errorFormId = errorFormId;
            _loadingFormId = loadingFormId;
            _toastFormId = toastFormId;
            _open = open ?? (id => _ui.ShowAsync(id, null));
            _close = close ?? (id => _ui.CloseAsync(id, UIService.CloseReason.User));
            _isOpen = isOpen ?? _ui.IsOpen;

            // Back 链首位：Loading 阻断期消费返回（取消当前操作），其余穿透到模态/页面链
            _nav.BackInterceptor = TryCancelLoading;
        }

        // ---- Loading ----

        /// <summary>是否处于加载阻断（嵌套 &gt; 0）。</summary>
        public bool IsLoading => _loadingDepth > 0;

        /// <summary>阻断期取消源（长操作绑定它——Back 取消即取消操作本体）。</summary>
        public CancellationToken LoadingToken => _loadingCts?.Token ?? CancellationToken.None;

        /// <summary>
        /// 开始一段加载阻断（嵌套安全：计数 &gt; 0 即阻断，最后一个结束才解除）。
        /// 返回作用域——Dispose/Using 收尾；**取消**经 <see cref="LoadingToken"/>（Back 触发），
        /// 由被门控操作自行响应（服务不强行结束操作，只给取消信号）。
        /// 首次进入时经 <see cref="UIService"/> 打开遮罩表单（异步；失败落日志不阻塞调用方）。
        /// </summary>
        public LoadingScope BeginLoading(string message = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FeedbackService));
            if (_loadingDepth == 0)
            {
                _loadingCts = new CancellationTokenSource();
                OpenLoadingAsync(message).Forget();
            }
            _loadingDepth++;
            return new LoadingScope(this);
        }

        private async UniTaskVoid OpenLoadingAsync(string message)
        {
            try
            {
                _loadingForm = await _open(_loadingFormId);
                var mask = _loadingForm.Root.GetComponentInChildren<LoadingMask>(true);
                if (mask != null)
                {
                    mask.SetMessage(message);
                    mask.SetBlocking(true);
                }
                else
                    Log.Error($"加载遮罩表单[{_loadingFormId}] 缺少 LoadingMask 组件——表单装配错误", "UI");
            }
            catch (OperationCanceledException) { /* 遮罩打开被取消（Shutdown/Close）——静默 */ }
            catch (Exception ex)
            {
                // 反馈面失败不该吞掉被门控操作——只落日志（§6.1"请求失败的反馈可观察"）
                Log.Error($"加载遮罩打开失败[{_loadingFormId}]:{ex.Message}", "UI");
            }
        }

        /// <summary>Back 拦截（导航链首位）：阻断中取消当前操作并消费本次返回；未阻断 = 不消费。</summary>
        public bool TryCancelLoading()
        {
            if (!IsLoading) return false;
            _loadingCts?.Cancel();                        // 取消信号给被门控操作（不强行结束——它自行收尾）
            return true;                                  // 消费本次 Back：不穿透到下层页面
        }

        private void EndLoading()
        {
            if (_disposed) return;
            _loadingDepth--;
            if (_loadingDepth <= 0)
            {
                _loadingDepth = 0;
                _loadingCts?.Dispose();
                _loadingCts = null;
                CloseLoadingAsync().Forget();
            }
        }

        private async UniTaskVoid CloseLoadingAsync()
        {
            UIForm form = _loadingForm;
            _loadingForm = null;
            if (form == null || !_isOpen(form.Id)) return;
            try { await _close(form.Id); }
            catch (Exception ex) { Log.Error($"加载遮罩关闭失败:{ex.Message}", "UI"); }
        }

        // ---- 错误/重试 ----

        /// <summary>
        /// 错误弹窗（统一失败反馈）：可重试 = Ok("重试")/Cancel("退出") 双按钮；否则单按钮确认。
        /// 返回 true = 用户选择重试。同 <paramref name="mergeKey"/> 的重复错误弹窗由
        /// <see cref="DialogService"/> 自动合并（§4.3"重复断线/错误弹窗按键合并"）。
        /// </summary>
        public UniTask<bool> ShowErrorAsync(string title, string message, bool retryable,
            string mergeKey = null, CancellationToken ct = default)
        {
            var request = new DialogService.Request(
                _errorFormId, title, message,
                okText: retryable ? "重试" : "确认",
                cancelText: retryable ? "退出" : null,
                group: "error", mergeKey: mergeKey);
            return _dialogs.ShowAsync(request, ct).ContinueWith(r => r == DialogResult.Ok);
        }

        // ---- Toast ----

        /// <summary>非阻断短提示（**多条并存**：新提示不替换旧提示，各自计时独立消失）。</summary>
        public void ShowToast(string message, float seconds = DefaultToastSeconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FeedbackService));
            PushToastAsync(message, seconds).Forget();
        }

        private async UniTaskVoid PushToastAsync(string message, float seconds)
        {
            try
            {
                if (_toast == null)
                {
                    _toastForm = await _open(_toastFormId);
                    _toast = _toastForm.Root.GetComponentInChildren<Toast>(true);
                    if (_toast == null)
                    {
                        Log.Error($"Toast 表单[{_toastFormId}] 缺少 Toast 组件——表单装配错误", "UI");
                        return;
                    }
                }
                _toast.Show(message, seconds);
            }
            catch (OperationCanceledException) { /* Toast 打开被取消——静默 */ }
            catch (Exception ex) { Log.Error($"Toast 打开失败[{_toastFormId}]:{ex.Message}", "UI"); }
        }

        /// <summary>当前首个可见 Toast 文本（诊断/测试面；未显示 = ""）。多条并存下取最早一条。</summary>
        public string CurrentToast => _toast != null ? _toast.FirstVisibleText : "";

        /// <summary>Toast 承载控件（诊断/测试面；未打开 = null）。计时由 <see cref="ToastTicker"/> 经 UIClock 驱动。</summary>
        public Toast ToastHost => _toast;

        /// <summary>Loading 作用域（Dispose = 计数回落；嵌套安全）。</summary>
        public readonly struct LoadingScope : IDisposable
        {
            private readonly FeedbackService _owner;

            internal LoadingScope(FeedbackService owner) => _owner = owner;

            public void Dispose() => _owner?.EndLoading();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nav.BackInterceptor = null;
            _loadingCts?.Dispose();
            _loadingCts = null;
        }
    }
}
