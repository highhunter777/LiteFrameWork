using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// Unity PlayerLoop 异步原语唯一封装入口。
    /// 业务层不直接调用 UniTask.Delay/NextFrame；所有表现任务必须把取消令牌传入。
    /// </summary>
    public static class UnityAsync
    {
        /// <summary>真实时间等待：不受 Time.timeScale 影响。</summary>
        public static UniTask WaitRealtimeAsync(float seconds, CancellationToken ct = default)
            => UniTask.Delay(TimeSpan.FromSeconds(Math.Max(0f, seconds)),
                DelayType.Realtime, PlayerLoopTiming.Update, ct, cancelImmediately: true);

        /// <summary>等待下一帧；取消立即生效。</summary>
        public static UniTask NextFrameAsync(CancellationToken ct = default)
            => UniTask.NextFrame(ct, cancelImmediately: true);
    }
}
