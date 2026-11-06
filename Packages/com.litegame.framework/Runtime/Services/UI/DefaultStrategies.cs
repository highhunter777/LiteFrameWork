using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteFramework.UI
{
    /// <summary>默认层级策略：组基序 + 槽位（与 2.1 灰盒行为一致）。</summary>
    public sealed class DefaultLayerStrategy : ILayerStrategy
    {
        public int ResolveSortingOrder(UILayerGroup group, int slot) => group.BaseDepth + slot;
    }

    /// <summary>
    /// 零动效转场（**默认策略**；《客户端总设计》§5.1"形成逻辑边界"）：立刻把视觉落到终态并报告完成。
    /// </summary>
    public sealed class InstantTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
            => PlayShow(form, new MotionPlayback());      // 无终态容器（壳按完成处理）

        public UniTask PlayClose(UIForm form, CancellationToken ct)
            => PlayClose(form, new MotionPlayback());

        public UniTask PlayShow(UIForm form, MotionPlayback playback)
        {
            form.CanvasGroup.alpha = 1f;                      // 终态：可见
            return Settle(playback);
        }

        public UniTask PlayClose(UIForm form, MotionPlayback playback)
        {
            form.CanvasGroup.alpha = 0f;                      // 终态：不可见
            return Settle(playback);
        }

        private static UniTask Settle(MotionPlayback playback)
        {
            bool cancelled = playback != null && playback.Token.IsCancellationRequested;
            playback?.Finish(cancelled ? MotionOutcome.Cancelled : MotionOutcome.Completed);
            return UniTask.CompletedTask;
        }
    }

    /// <summary>默认出栈拦截：全放行（灰盒；具体界面的"未保存拦截"由业务替换策略实现）。</summary>
    public sealed class DefaultPopInterceptor : IPopInterceptor
    {
        public bool CanClose(UIForm form) => true;
    }
}
