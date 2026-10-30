using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGame
{
    /// <summary>
    /// 一次性事件等待（"订阅 → 完成源 → await → finally 必退订"的**单一实现**）：流程层等 Reliable
    /// 信令（JoinAck / StartGame）与对局结束共用。同一模式手写多份的两个真实风险——漏退订（订阅泄漏）
    /// 与退订时序抄错——由此结构性消除；持久订阅（BattleContext 那一侧）不归此面。
    /// </summary>
    internal static class StageSignal
    {
        /// <summary>
        /// 等一次**无载荷**事件。<paramref name="alreadyDone"/> = 事件已发生（不订阅直接返回——
        /// 迟到订阅者不重等）。<paramref name="timeoutMs"/> &gt; 0 时叠加超时（超时走异常路径，与取消同形）。
        /// </summary>
        internal static async UniTask Wait<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe,
            bool alreadyDone, CancellationToken ct, int timeoutMs = 0)
        {
            if (alreadyDone) return;

            var ready = new UniTaskCompletionSource();
            void Handler(T _) => ready.TrySetResult();
            Action<T> onSignal = Handler;
            subscribe(onSignal);
            try
            {
                UniTask task = ready.Task;
                if (timeoutMs > 0) task = task.Timeout(TimeSpan.FromMilliseconds(timeoutMs));
                await task.AttachExternalCancellation(ct);
            }
            finally
            {
                unsubscribe(onSignal);        // 临时订阅必须退
            }
        }

        /// <summary>等一次**带载荷**事件并取载荷（如对局结束原因）。</summary>
        internal static async UniTask<T> WaitValue<T>(Action<Action<T>> subscribe, Action<Action<T>> unsubscribe,
            Func<T?> earlyResult, CancellationToken ct) where T : struct
        {
            T? early = earlyResult();
            if (early.HasValue) return early.Value;

            var ready = new UniTaskCompletionSource<T>();
            void Handler(T v) => ready.TrySetResult(v);
            Action<T> onSignal = Handler;
            subscribe(onSignal);
            try
            {
                return await ready.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                unsubscribe(onSignal);        // 一次性等待：结束即退订
            }
        }
    }
}
