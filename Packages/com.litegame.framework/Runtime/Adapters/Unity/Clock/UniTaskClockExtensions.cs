using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace LiteFramework
{
    public static class UniTaskClockExtensions 
    {
        public static UniTask WaitAsync(this IScheduler clock, float seconds, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            var source = AutoResetUniTaskCompletionSource.Create();
            var id = 0;                                        // 闭包捕获:取消回调要能反查 Schedule 返回的 id
            var reg = ct.Register(() => { clock.Cancel(id); source.TrySetCanceled(ct); });
            id = clock.Schedule(seconds, () => { reg.Dispose(); source.TrySetResult(); });
            return source.Task;                                // 已派发后 Cancel(id) 由调度器侧幂等;双完成 Try* 均为 no-op
        }
        // LiteFramework.Unity · UniTask 适配扩展(封装层内部用原语,业务禁止)
        public static async UniTask WaitAsync(this IGameClock clock, float seconds, CancellationToken ct = default)
        {
            float remaining = seconds;
            while (remaining > 0f)
            {
                ct.ThrowIfCancellationRequested();
                await UnityAsync.NextFrameAsync(ct);
                remaining -= clock.ScaledDelta;   // 恢复时机与时钟 tick 的先后由 DefaultExecutionOrder 固定(装配步)
            }
        }
    }
}
