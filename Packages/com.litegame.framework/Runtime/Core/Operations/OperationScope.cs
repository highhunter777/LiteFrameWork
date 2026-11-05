using System;
using System.Threading;

namespace LiteFramework
{
    /// <summary>一次操作的生命周期状态（<see cref="OperationScope"/> 持有）。</summary>
    public enum OperationState
    {
        /// <summary>已创建、尚未开始（排队期）。</summary>
        Pending = 0,

        /// <summary>已开始执行。</summary>
        Running = 1,

        /// <summary>正常完成。</summary>
        Succeeded = 2,

        /// <summary>执行失败（异常路径；分类见 M3-5 错误面）。</summary>
        Failed = 3,

        /// <summary>被取消（调用方取消 / 全员退出 / 超时 / 作用域释放）。</summary>
        Canceled = 4,
    }

    /// <summary>
    /// 调用方拥有的一次操作上下文（《客户端依赖注入专项设计》§6.1）：
    /// 同时持有<b>取消、超时、合并键、所有者与状态</b>，<see cref="Dispose"/> 释放链接令牌与计时器。
    ///
    /// **不注册进容器**——谁发起谁持有；作用域只提供统一语义，合并/拒绝由消费方按 <see cref="MergeKey"/> 自行裁决
    /// （例：UI 打开按 formId 合并、导航按 kind+formId 串行）。
    ///
    /// **取消口径**（§6.1）：取消只约束"还没开始的步骤"；已开始的清理由消费方照常执行
    /// （与 <c>ClientHost.ShutdownAsync</c> 同口径——令牌取消不等于跳过释放）。
    /// </summary>
    public sealed class OperationScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _token;
        private readonly CancellationTokenRegistration _tokenRegistration;
        private readonly Timer _timeoutTimer;
        private readonly object _gate = new object();
        private int _state;
        private int _timedOut;
        private bool _disposed;

        private OperationScope(string owner, string mergeKey, TimeSpan? timeout, CancellationToken[] parents)
        {
            Owner = owner;
            MergeKey = mergeKey;
            Timeout = timeout;

            int linked = 0;
            if (parents != null)
                foreach (CancellationToken parent in parents)
                    if (parent.CanBeCanceled) linked++;

            var effective = new CancellationToken[linked];
            int index = 0;
            if (parents != null)
                foreach (CancellationToken parent in parents)
                    if (parent.CanBeCanceled) effective[index++] = parent;

            _cts = linked == 0
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(effective);

            _token = _cts.Token;
            _tokenRegistration = _token.Register(OnTokenCanceled);
            if (timeout.HasValue)
                _timeoutTimer = new Timer(OnTimeout, null, timeout.Value, System.Threading.Timeout.InfiniteTimeSpan);
        }

        /// <summary>发起者标识（诊断/审计；不进容器）。</summary>
        public string Owner { get; }

        /// <summary>合并键（同一键的并发操作由消费方合并或拒绝；null = 不合并）。</summary>
        public string MergeKey { get; }

        /// <summary>超时（null = 不超时）；到点取消 <see cref="Token"/>。</summary>
        public TimeSpan? Timeout { get; }

        /// <summary>本操作的取消令牌（链接父令牌；超时到点自动取消）。</summary>
        public CancellationToken Token => _token;

        /// <summary>是否因超时被取消（区分“调用方取消”与“超时”——下载/加载据此选错误分类）。</summary>
        public bool IsTimedOut => Volatile.Read(ref _timedOut) != 0;

        /// <summary>当前状态。</summary>
        public OperationState State => (OperationState)Volatile.Read(ref _state);

        /// <summary>是否已进入终态（Succeeded/Failed/Canceled）。</summary>
        public bool IsTerminal
        {
            get
            {
                OperationState state = State;
                return state == OperationState.Succeeded
                    || state == OperationState.Failed
                    || state == OperationState.Canceled;
            }
        }

        /// <summary>是否已释放（释放后不得再用 <see cref="Token"/> 发起新等待）。</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// 创建一次性操作作用域。
        /// </summary>
        /// <param name="owner">发起者（必填；空/空白即调用方错误）。</param>
        /// <param name="mergeKey">合并键（可选）。</param>
        /// <param name="timeout">超时（可选；必须为正）。</param>
        /// <param name="parent">父令牌（可选；取消级联）。</param>
        public static OperationScope Start(
            string owner, string mergeKey = null, TimeSpan? timeout = null, CancellationToken parent = default)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("OperationScope.Owner 不能为空", nameof(owner));
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正时长");
            return new OperationScope(owner, mergeKey, timeout, new[] { parent });
        }

        /// <summary>
        /// 创建一次性操作作用域（多父令牌：调用方取消 + 宿主生命周期等，任一取消即级联）。
        /// </summary>
        public static OperationScope StartLinked(
            string owner, string mergeKey = null, TimeSpan? timeout = null, params CancellationToken[] parents)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("OperationScope.Owner 不能为空", nameof(owner));
            if (timeout.HasValue && timeout.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正时长");
            return new OperationScope(owner, mergeKey, timeout, parents);
        }

        /// <summary>标记进入执行（Pending → Running；终态后调用无效）。</summary>
        public void MarkRunning()
        {
            lock (_gate)
            {
                if (_state == (int)OperationState.Pending) _state = (int)OperationState.Running;
            }
        }

        /// <summary>标记成功（终态）。</summary>
        public void MarkSucceeded()
        {
            lock (_gate)
            {
                if (IsTerminalLocked()) return;
                _state = (int)OperationState.Succeeded;
            }
        }

        /// <summary>标记失败（终态；不做错误分类——分类属 M3-5 错误面）。</summary>
        public void MarkFailed()
        {
            lock (_gate)
            {
                if (IsTerminalLocked()) return;
                _state = (int)OperationState.Failed;
            }
        }

        /// <summary>
        /// 取消（幂等）：取消令牌并把未进入终态的操作记为 <see cref="OperationState.Canceled"/>。
        /// 已释放后调用为 no-op。
        /// </summary>
        public void Cancel()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_disposed) return;
                if (IsTerminalLocked()) return;
                _state = (int)OperationState.Canceled;
                cts = _cts;
            }
            CancelCore(cts);
        }

        /// <summary>
        /// 释放：未进入终态则先取消（放弃的操作不得让工作悬空），随后释放 CTS/计时器。幂等。
        /// </summary>
        public void Dispose()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (IsTerminalLocked())
                {
                    cts = null;
                }
                else
                {
                    _state = (int)OperationState.Canceled;
                    cts = _cts;
                }
            }

            _timeoutTimer?.Dispose();
            _tokenRegistration.Dispose();
            if (cts != null) CancelCore(cts);
            _cts.Dispose();
        }

        /// <summary>超时回调（Timer 线程）：标记超时原因后取消；已终态则忽略。</summary>
        private void OnTimeout(object _)
        {
            lock (_gate)
            {
                if (IsTerminalLocked()) return;
                Volatile.Write(ref _timedOut, 1);
            }
            Cancel();
        }

        /// <summary>令牌取消（超时/父令牌/显式）→ 未终态者记 Canceled（状态与令牌不漂移）。</summary>
        private void OnTokenCanceled()
        {
            lock (_gate)
            {
                if (!IsTerminalLocked()) _state = (int)OperationState.Canceled;
            }
        }

        private static void CancelCore(CancellationTokenSource cts)
        {
            try
            {
                if (!cts.IsCancellationRequested) cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 释放与取消的竞态：CTS 已释放即视为已取消。
            }
        }

        private bool IsTerminalLocked()
        {
            int state = _state;
            return state == (int)OperationState.Succeeded
                || state == (int)OperationState.Failed
                || state == (int)OperationState.Canceled;
        }
    }
}
