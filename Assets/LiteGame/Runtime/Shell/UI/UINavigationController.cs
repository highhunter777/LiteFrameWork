using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 导航协调者（《UI框架总设计》§4.3 首版导航：**单写者串行** + 队列上限 + 等待超时 + 可观测拒绝）。
    /// 落在 <see cref="UIService"/> 之上的唯一导航入口：页面/流程的 Go/Back 一律经本类排队串行执行
    /// ——直接 <see cref="UIService.ShowAsync"/> 仍可用（工具/测试），但产品导航必须走单写者，
    /// 否则"同帧连点两个按钮"会在转场中途互相打断。
    ///
    /// 契约（§4.3）：
    /// - **队列满在创建/入栈/OnShow 之前拒绝**（<see cref="UIOpenFailure.Rejected"/>——排队计数不含正在执行的操作）；
    /// - **等待超时可观测**（<see cref="UIOpenFailure.Timeout"/>，出队与入队两个安全调度点判定——首版不建
    ///   定时器泵，运行中的单个操作自身有界：转场超时 2s 兜底 + 加载取消链）；
    /// - **已接受的操作必须完成、失败或取消**：排队期取消 = 标记放弃（出队时跳过、不加载）；执行期取消 = 贯穿
    ///   <see cref="UIService.ShowAsync"/> 的取消语义（半成品由其回滚）；
    /// - Back（§6.2 平台返回统一处理）：**最顶模态优先**，其次最高非空层级组的栈顶——关闭提交
    ///   <see cref="UIService.CloseReason.Back"/>（可被出栈拦截，拦截是合法确定结果）。
    /// </summary>
    public sealed class UINavigationController
    {
        /// <summary>候选配置（§4.3，目标设备与真实包验证后调整）。</summary>
        public const int DefaultQueueCapacity = 8;
        public const float DefaultOpenTimeoutSeconds = 10f;

        private readonly UIService _ui;
        private readonly Queue<NavOp> _queue = new Queue<NavOp>(4);
        private readonly Func<long> _nowMs;
        private bool _running;

        /// <summary>排队中操作数上限（不含正在执行的操作）。</summary>
        public int QueueCapacity { get; set; }

        /// <summary>排队等待上限（秒；≤0 = 不超时）。</summary>
        public float OpenTimeoutSeconds { get; set; }

        // ---- 诊断（§4.3"可观测"）----
        public int QueuedCount => _queue.Count;
        public int Executed { get; private set; }
        public int RejectedByCapacity { get; private set; }
        public int TimedOut { get; private set; }
        public int CancelledWhileQueued { get; private set; }

        /// <summary>
        /// Back 前置拦截（§6.1 Back 优先级链首位——"Loading 阻断期间按操作取消规则处理返回，
        /// 不能悄悄穿透到下层"）：返回 true = 已消费本次 Back（取消当前阻断操作，不进入模态/页面关闭链）。
        /// 注册方（FeedbackService）必须是确定行为、不弹 UI。null = 无拦截（默认）。
        /// </summary>
        public Func<bool> BackInterceptor { get; set; }

        /// <param name="ui">UI 壳服务（导航的执行引擎）。</param>
        /// <param name="nowMs">单调毫秒源（默认 Stopwatch；测试注入假时钟驱动等待超时判定）。</param>
        public UINavigationController(UIService ui, int queueCapacity = DefaultQueueCapacity,
            float openTimeoutSeconds = DefaultOpenTimeoutSeconds, Func<long> nowMs = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            QueueCapacity = queueCapacity > 0 ? queueCapacity : DefaultQueueCapacity;
            OpenTimeoutSeconds = openTimeoutSeconds;
            _nowMs = nowMs ?? DefaultNowMs;
        }

        private static long DefaultNowMs()
            => System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;

        // ---- 导航 API ----

        /// <summary>前往页面（串行）：已打开时幂等返回；排队等待期间超时/取消抛类型化异常。</summary>
        public UniTask<UIForm> GoAsync(int formId, IUIData data = null, CancellationToken ct = default)
        {
            var op = Enqueue(NavKind.Go, formId, data, ct);
            return WaitFormAsync(op, ct);
        }

        /// <summary>
        /// 返回（§6.2 平台返回统一处理）：**Back 前置拦截**（Loading 阻断期取消，消费即返回 true）
        /// → 关闭最顶模态（无模态则最高非空组的栈顶）。返回 false = 当前无可返回目标（确定结果，不抛）。
        /// 关闭可被出栈拦截（<see cref="UIService.CloseReason.Back"/> 语义）——拦截时返回 true（已提交关闭）。
        /// </summary>
        public UniTask<bool> BackAsync(CancellationToken ct = default)
        {
            if (BackInterceptor != null && BackInterceptor())
                return UniTask.FromResult(true);          // 阻断期消费：不穿透到下层（§6.1）

            var op = Enqueue(NavKind.Back, 0, null, ct);
            return WaitBoolAsync(op, ct);
        }

        // ---- 排队与执行（单写者）----

        private NavOp Enqueue(NavKind kind, int formId, IUIData data, CancellationToken ct)
        {
            // 安全调度点①：入队前清点过期排队项（可观测地尽早失败，不等出队）
            SweepExpired();

            if (_running && _queue.Count >= QueueCapacity)
            {
                RejectedByCapacity++;
                throw new UIOpenException(UIOpenFailure.Rejected, formId,
                    $"导航队列已满（{QueueCapacity}）——拒绝排队（§4.3 队列满在创建/入栈/OnShow 之前拒绝）");
            }

            var op = new NavOp(kind, formId, data, _nowMs());
            _queue.Enqueue(op);

            if (ct.CanBeCanceled)
            {
                op.Registration = ct.Register(() => Abandon(op, ct));
            }

            Log.Info($"[Nav] 入队 {kind} id={formId} 排队深度={_queue.Count} 执行中={_running}", "UI");
            PumpAsync().Forget();
            return op;
        }

        /// <summary>排队期取消：标记放弃（出队时跳过——不执行、不加载）+ 等待者收类型化取消。
        /// 只故障该操作种类的完成源——另一完成源无人等待，故障它会成为 UniTask 未观察异常发布。</summary>
        private void Abandon(NavOp op, CancellationToken ct)
        {
            if (op.Abandoned) return;                     // 幂等：取消登记可能在多个调度点被触发
            CancelledWhileQueued++;
            op.Abandoned = true;
            Log.Info($"[Nav] 放弃排队项 id={op.FormId}（排队期取消）", "UI");
            op.Cts.Cancel();

            var fail = new UIOpenException(UIOpenFailure.Canceled, op.FormId,
                op.Kind == NavKind.Back ? "导航 Back 被取消（排队期）" : $"导航 Go[{op.FormId}] 被取消（排队期）");
            op.Fail(fail);
        }

        private async UniTaskVoid PumpAsync()
        {
            if (_running) return;                          // 单写者：已在跑的操作链继续消费队列
            _running = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var op = _queue.Dequeue();
                    if (op.Abandoned) { op.Registration.Dispose(); continue; }

                    // 安全调度点②：出队判等待超时（排队期被取消的项由 Abandon 标记跳过）
                    if (IsExpired(op))
                    {
                        TimedOut++;
                        op.Cts.Cancel();
                        op.Fail(new UIOpenException(UIOpenFailure.Timeout, op.FormId,
                            op.Kind == NavKind.Back
                                ? $"导航 Back 排队等待超时（{OpenTimeoutSeconds}s）"
                                : $"导航 Go[{op.FormId}] 排队等待超时（{OpenTimeoutSeconds}s）——队列被长操作阻塞"));
                        op.Registration.Dispose();
                        continue;
                    }

                    op.Started = true;
                    try
                    {
                        switch (op.Kind)
                        {
                            case NavKind.Go:
                                var form = await _ui.ShowAsync(op.FormId, op.Data, op.Cts.Token);
                                op.FormCompletion.TrySetResult(form);
                                break;
                            case NavKind.Back:
                                bool hasTarget = _ui.TryGetBackTarget(out int target);
                                if (hasTarget)
                                    await _ui.CloseAsync(target, UIService.CloseReason.Back);
                                op.BoolCompletion.TrySetResult(hasTarget);
                                break;
                        }
                        Executed++;
                    }
                    catch (Exception ex)
                    {
                        op.Fail(ex);
                    }
                    finally
                    {
                        op.Registration.Dispose();       // 该 op 已消费完——登记随 NavOp 生命周期释放
                    }
                }
            }
            finally
            {
                _running = false;
            }
        }

        /// <summary>清点排队项的等待超时（入队/出队两个调度点共用）。</summary>
        private void SweepExpired()
        {
            if (OpenTimeoutSeconds <= 0f) return;
            if (_queue.Count == 0) return;

            var keep = new List<NavOp>(_queue.Count);
            long limitMs = (long)(OpenTimeoutSeconds * 1000f);
            while (_queue.Count > 0)
            {
                var op = _queue.Dequeue();
                if (!op.Abandoned && (_nowMs() - op.EnqueuedAtMs) > limitMs)
                {
                    TimedOut++;
                    op.Cts.Cancel();
                    op.Fail(new UIOpenException(UIOpenFailure.Timeout, op.FormId,
                        op.Kind == NavKind.Back
                            ? $"导航 Back 排队等待超时（{OpenTimeoutSeconds}s）"
                            : $"导航 Go[{op.FormId}] 排队等待超时（{OpenTimeoutSeconds}s）——队列被长操作阻塞"));
                    continue;
                }
                keep.Add(op);
            }
            foreach (var op in keep) _queue.Enqueue(op);
        }

        private bool IsExpired(NavOp op)
            => OpenTimeoutSeconds > 0f && (_nowMs() - op.EnqueuedAtMs) > (long)(OpenTimeoutSeconds * 1000f);

        // ---- 等待侧 ----

        private async UniTask<UIForm> WaitFormAsync(NavOp op, CancellationToken ct)
        {
            try
            {
                return await op.FormCompletion.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                // 刻意**不**注销 op.Registration：它是 **NavOp 生命周期**的登记，由 PumpAsync 消费完才释放。
                // 等待者先返回是常态——取消时 AttachExternalCancellation 的续延**先于** Abandon 执行
                //（取消回调 LIFO，ATI 后注册故先跑），若在此 Dispose 会把 Abandon 的回调一并注销，
                // 于是 op.Abandoned 永不置位、排队项照常执行加载——破坏"排队期取消 = 不执行、不加载"（§4.3）。
            }
        }

        private async UniTask<bool> WaitBoolAsync(NavOp op, CancellationToken ct)
        {
            try
            {
                return await op.BoolCompletion.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                // 同上：登记归 NavOp 生命周期，不在此注销。
            }
        }

        private enum NavKind { Go, Back }

        private sealed class NavOp
        {
            public readonly NavKind Kind;
            public readonly int FormId;
            public readonly IUIData Data;
            public readonly long EnqueuedAtMs;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public readonly UniTaskCompletionSource<UIForm> FormCompletion = new UniTaskCompletionSource<UIForm>();
            public readonly UniTaskCompletionSource<bool> BoolCompletion = new UniTaskCompletionSource<bool>();
            public CancellationTokenRegistration Registration;
            public bool Started;
            public bool Abandoned;

            public NavOp(NavKind kind, int formId, IUIData data, long enqueuedAtMs)
            {
                Kind = kind;
                FormId = formId;
                Data = data;
                EnqueuedAtMs = enqueuedAtMs;
            }

            /// <summary>只故障本种类的完成源（另一源无人等待——故障它会成为 UniTask 未观察异常）。</summary>
            public void Fail(Exception ex)
            {
                if (Kind == NavKind.Go) FormCompletion.TrySetException(ex);
                else BoolCompletion.TrySetException(ex);
            }
        }
    }
}
