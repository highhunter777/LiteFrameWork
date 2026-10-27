using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteClient.UI
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
        private readonly int _confirmFormId;
        private readonly int _loadingFormId;
        private readonly int _toastFormId;

        /// <summary>反馈面打开口（默认经导航单写者 <see cref="UINavigationController.GoAsync"/>——
        /// 产品面打开全走单写者队列，三通道收口口径；测试可注入替身以脱离真表与真资源）。</summary>
        private readonly Func<int, UniTask<UIForm>> _open;

        /// <summary>反馈面关闭口（默认经 <see cref="UIService.CloseAsync"/>；按 id 定向关闭——导航只辖打开序与 Back）。</summary>
        private readonly Func<int, UniTask> _close;

        /// <summary>表单是否仍打开（默认经 <see cref="UIService.IsOpen"/>）。</summary>
        private readonly Func<int, bool> _isOpen;

        private int _loadingDepth;
        private int _loadingEpoch;                    // 开面代次：开场 +1、收场归零 +1——在途开面靠它判"本段是否已结束"
        private CancellationTokenSource _loadingCts;
        private UIForm _loadingForm;
        private string _loadingMessage = "加载中…";   // 加载文案缓存（首写不丢/开面后写全量——通用加载页 §3）
        private float? _loadingProgress;               // null = 不定态
        private UIForm _toastForm;
        private Toast _toast;
        private bool _disposed;

        /// <param name="errorFormId">错误弹窗表单（UIDialog 结构的表行——专用 ErrorPage prefab 由制作线替换）。</param>
        /// <param name="loadingFormId">加载遮罩表单（LoadingMask 结构）。</param>
        /// <param name="toastFormId">Toast 承载表单（Toast 结构）。</param>
        /// <param name="confirmFormId">确认弹窗表单（UIDialog 结构、页面层级——206 与 System 层错误弹窗
        /// 分层：确认/错误互不占组）。</param>
        /// <param name="open">反馈面打开口（null = 用 <see cref="UIService"/> 真实现）。</param>
        /// <param name="close">反馈面关闭口（null = 用 <see cref="UIService"/> 真实现）。</param>
        /// <param name="isOpen">打开态查询口（null = 用 <see cref="UIService"/> 真实现）。</param>
        public FeedbackService(UIService ui, DialogService dialogs, UINavigationController nav,
            int errorFormId = 201, int confirmFormId = 206, int loadingFormId = 202, int toastFormId = 203,
            Func<int, UniTask<UIForm>> open = null,
            Func<int, UniTask> close = null,
            Func<int, bool> isOpen = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _errorFormId = errorFormId;
            _confirmFormId = confirmFormId;
            _loadingFormId = loadingFormId;
            _toastFormId = toastFormId;
            _open = open ?? (id => _nav.GoAsync(id));
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
                _loadingMessage = string.IsNullOrEmpty(message) ? "加载中…" : message;   // 写全量复位（不留上次残留）
                _loadingProgress = null;                                                  // 开场默认不定态
                _loadingEpoch++;                                                          // 新代：本段开面以此认领
                OpenLoadingAsync(_loadingMessage, _loadingEpoch).Forget();
            }
            _loadingDepth++;
            return new LoadingScope(this);
        }

        private async UniTaskVoid OpenLoadingAsync(string message, int epoch)
        {
            try
            {
                UIForm form = await _open(_loadingFormId);
                if (epoch != _loadingEpoch)
                {
                    // 本段阻断已在开面落地前结束（快路径进房：Begin→End 快于异步入队的开面）：
                    // - 新段已在场且尚无认领面 → 本面转给当前段（同一表单实例——收养，不留孤儿）；
                    // - 无新段且无认领面 → 落地即收（不留永久阻断）；
                    // - 已有认领面 → 不动（可能就是同一实例，抢收会误关活面）。
                    if (_loadingDepth > 0 && _loadingForm == null)
                    {
                        _loadingForm = form;
                        ApplyLoadingState(form);
                    }
                    else if (_loadingForm == null)
                    {
                        await CloseFormAsync(form);
                    }
                    return;
                }
                _loadingForm = form;
                ApplyLoadingState(form);
            }
            catch (OperationCanceledException) { /* 遮罩打开被取消（Shutdown/Close）——静默 */ }
            catch (Exception ex)
            {
                // 反馈面失败不该吞掉被门控操作——只落日志（§6.1"请求失败的反馈可观察"）
                Log.Error($"加载遮罩打开失败[{_loadingFormId}]:{ex.Message}", LogTag.UI);
            }
        }

        /// <summary>把当前文案/进度/阻断态写到遮罩（开面落地、旧代面转认领两路共用）。</summary>
        private void ApplyLoadingState(UIForm form)
        {
            var mask = form.Root.GetComponentInChildren<LoadingMask>(true);
            if (mask == null)
            {
                Log.Error($"加载遮罩表单[{_loadingFormId}] 缺少 LoadingMask 组件——表单装配错误", LogTag.UI);
                return;
            }
            mask.SetStatus(_loadingMessage, _loadingProgress);   // 首写不丢：开面完成写缓存全量
            mask.SetBlocking(true);
        }

        /// <summary>当前加载文案（工作台/诊断回读——缓存值，非活动期回落默认文案）。</summary>
        public string CurrentLoadingMessage => _loadingMessage ?? string.Empty;

        /// <summary>
        /// 推送加载状态（<see cref="LoadingScope.Report"/>——主线程推送式，服务不建每帧泵）。
        /// 语义（《通用加载页设计》§3）：嵌套期各作用域写同一面**后写胜出**；开面前调用落缓存
        /// （首写不丢——开面完成时写全量）；文案 null/空回落"加载中…"，progress01 null = 不定态。
        /// </summary>
        public void ReportLoading(string message, float? progress01)
        {
            _loadingMessage = string.IsNullOrEmpty(message) ? "加载中…" : message;
            _loadingProgress = progress01;
            if (_loadingForm == null) return;
            _loadingForm.Root.GetComponentInChildren<LoadingMask>(true)
                ?.SetStatus(_loadingMessage, _loadingProgress);
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
                _loadingEpoch++;                  // 收场换代：在途开面落地时——新段已在场则转认领，否则自收
                _loadingMessage = "加载中…";      // 复位（下次开面写全量）
                _loadingProgress = null;
                _loadingCts?.Dispose();
                _loadingCts = null;
                CloseLoadingAsync().Forget();
            }
        }

        private async UniTaskVoid CloseLoadingAsync()
        {
            UIForm form = _loadingForm;
            _loadingForm = null;
            await CloseFormAsync(form);
        }

        /// <summary>收一张面（null/未开 = 安全幂等）——正常收口与**在途孤儿面**落地自收共用。</summary>
        private async UniTask CloseFormAsync(UIForm form)
        {
            if (form == null || !_isOpen(form.Id)) return;
            try { await _close(form.Id); }
            catch (Exception ex) { Log.Error($"加载遮罩关闭失败:{ex.Message}", LogTag.UI); }
        }

        // ---- 错误/重试 ----

        /// <summary>错误弹窗表单 id（装配面读——模态登记等消费单源取值，不另写数字）。</summary>
        public int ErrorFormId => _errorFormId;

        /// <summary>加载面表单 id（只读口——与 <see cref="ErrorFormId"/>/<see cref="ConfirmFormId"/> 同款单源）。</summary>
        public int LoadingFormId => _loadingFormId;

        /// <summary>加载面**已真正打开**（区别于 <see cref="IsLoading"/>＝作用域在场：Begin 的开面是异步
        /// 入队（转场/prefab 加载），"作用域已真、面尚在途"时若走到 End，排队中的开面随后落地会**无人收**
        /// （遮罩永久阻断）——消费方要"等面开再压重活"时读本口。</summary>
        public bool LoadingVisible => _loadingForm != null && _isOpen(_loadingForm.Id);

        /// <summary>确认弹窗表单 id（同上——单源取值口）。</summary>
        public int ConfirmFormId => _confirmFormId;

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

        /// <summary>
        /// 确认弹窗（双按钮：Ok＝确认 / Cancel＝取消；被外部关闭＝false）：经 <see cref="DialogService"/>
        /// 落地（互斥组 "confirm"、同 <paramref name="mergeKey"/> 在途合并——重复请求共享同一结果任务）。
        /// 按钮文案可传 null＝保持 prefab 原文案。**异步结果**：true＝用户确认。
        /// </summary>
        public UniTask<bool> ConfirmAsync(string title, string message,
            string okText = null, string cancelText = null, string mergeKey = null, CancellationToken ct = default)
        {
            var request = new DialogService.Request(
                _confirmFormId, title, message, okText: okText, cancelText: cancelText,
                group: "confirm", mergeKey: mergeKey);
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
                        Log.Error($"Toast 表单[{_toastFormId}] 缺少 Toast 组件——表单装配错误", LogTag.UI);
                        return;
                    }
                }
                _toast.Show(message, seconds);
            }
            catch (OperationCanceledException) { /* Toast 打开被取消——静默 */ }
            catch (Exception ex) { Log.Error($"Toast 打开失败[{_toastFormId}]:{ex.Message}", LogTag.UI); }
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

            /// <summary>推送加载状态（文案/进度——语义见 <see cref="FeedbackService.ReportLoading"/>）。</summary>
            public void Report(string message = null, float? progress01 = null)
                => _owner?.ReportLoading(message, progress01);
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
