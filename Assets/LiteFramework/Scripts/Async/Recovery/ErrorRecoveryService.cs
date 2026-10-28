using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 错误恢复执行器（《客户端依赖注入专项设计》§6.2）：分类 → 动作 → **执行**。
    ///
    /// 与旧 <c>ClientErrorRecovery</c> 的区别：旧件只做“分类 → 枚举”的静态映射、没有执行器，
    /// 也没有生产消费者；本件按分类真正执行有界重试/导出诊断/退出/阻断，并由装配点注入动作实现。
    ///
    /// 取消不是错误：<see cref="OperationCanceledException"/> 直接返回 <see cref="RecoveryResult.Canceled"/>，
    /// 不触发任何动作（与 <c>ClientHost.ShutdownAsync</c> 的取消口径一致）。
    /// </summary>
    public sealed class ErrorRecoveryService
    {
        private readonly IRecoveryActions _actions;
        private readonly RecoveryPolicy _policy;
        private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;

        /// <param name="actions">动作实现（可空 = 只做决策与重试，不落盘/不退出）。</param>
        /// <param name="policy">重试策略（默认 <see cref="RecoveryPolicy.Default"/>）。</param>
        /// <param name="delay">退避等待（测试注入假等待；默认 UniTask.Delay）。</param>
        public ErrorRecoveryService(
            IRecoveryActions actions = null,
            RecoveryPolicy policy = null,
            Func<TimeSpan, CancellationToken, UniTask> delay = null)
        {
            _actions = actions;
            _policy = policy ?? RecoveryPolicy.Default;
            _delay = delay ?? (Func<TimeSpan, CancellationToken, UniTask>)
                (async (duration, token) => await Task.Delay(duration, token));
        }

        /// <summary>错误 → 分类（取消见 <see cref="ErrorClassification.IsCancellation"/>）。</summary>
        public static ErrorKind Classify(Exception error) => ErrorClassification.Of(error);

        /// <summary>分类 → 动作（执行器唯一映射表）。</summary>
        public static RecoveryAction Resolve(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.Transient:
                case ErrorKind.Recoverable:
                    return RecoveryAction.Retry;
                case ErrorKind.Compatibility:
                    return RecoveryAction.Block;
                case ErrorKind.Security:
                    return RecoveryAction.ExportAndBlock;
                case ErrorKind.Fatal:
                default:
                    return RecoveryAction.ExportAndExit;
            }
        }

        /// <summary>
        /// 处理一次错误：按分类执行动作并返回结果。
        /// </summary>
        /// <param name="error">原始错误（分类在抛出点声明；未标记按 Fatal）。</param>
        /// <param name="operation">操作名（诊断/导出原因）。</param>
        /// <param name="retry">重试委托（返回 true = 本次重试成功）；null = 该调用点无法重试 → 阻断。</param>
        /// <param name="ct">调用方取消（取消即中止恢复）。</param>
        public async UniTask<RecoveryOutcome> HandleAsync(
            Exception error, string operation, Func<CancellationToken, UniTask<bool>> retry = null,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested || ErrorClassification.IsCancellation(error))
                return RecoveryOutcome.Of(RecoveryResult.Canceled, ErrorKind.Transient, RecoveryAction.Retry,
                    0, operation, "已取消（取消不是错误）");

            ErrorKind kind = Classify(error);
            RecoveryAction action = Resolve(kind);
            string message = error?.Message ?? "(无异常消息)";

            switch (action)
            {
                case RecoveryAction.Retry:
                    return await RetryAsync(kind, action, operation, message, retry, ct);

                case RecoveryAction.Block:
                    return RecoveryOutcome.Of(RecoveryResult.Blocked, kind, action, 0, operation, message);

                case RecoveryAction.ExportAndBlock:
                    await ExportAsync(kind, operation, message, ct);
                    return RecoveryOutcome.Of(RecoveryResult.Blocked, kind, action, 0, operation, message);

                case RecoveryAction.ExportAndExit:
                default:
                    await ExportAsync(kind, operation, message, ct);
                    await ExitAsync(operation, message, ct);
                    return RecoveryOutcome.Of(RecoveryResult.Fatal, kind, action, 0, operation, message);
            }
        }

        private async UniTask<RecoveryOutcome> RetryAsync(
            ErrorKind kind, RecoveryAction action, string operation, string message,
            Func<CancellationToken, UniTask<bool>> retry, CancellationToken ct)
        {
            if (retry == null)
                return RecoveryOutcome.Of(RecoveryResult.Blocked, kind, action, 0, operation,
                    message + "（调用点未提供重试委托）");

            Exception lastError = null;
            int attempts = 0;
            for (int attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                attempts = attempt;
                try
                {
                    if (await retry(ct))
                        return RecoveryOutcome.Of(RecoveryResult.Recovered, kind, action, attempts, operation, message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                if (attempt < _policy.MaxAttempts)
                    await _delay(_policy.DelayFor(attempt), ct);
            }

            string detail = lastError == null ? message : message + " / 末次：" + lastError.Message;
            return RecoveryOutcome.Of(RecoveryResult.Exhausted, kind, action, attempts, operation, detail);
        }

        private UniTask ExportAsync(ErrorKind kind, string operation, string message, CancellationToken ct)
        {
            if (_actions == null) return UniTask.CompletedTask;
            return _actions.ExportDiagnosticsAsync(
                $"{operation} [{kind}] {message}", ct);
        }

        private UniTask ExitAsync(string operation, string message, CancellationToken ct)
        {
            if (_actions == null) return UniTask.CompletedTask;
            return _actions.ExitAsync(ct);
        }
    }
}
