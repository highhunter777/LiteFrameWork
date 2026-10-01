using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using LiteGame.UI;

namespace LiteGame
{
    /// <summary>默认层级策略：组基序 + 槽位（与 2.1 灰盒行为一致）。</summary>
    public sealed class DefaultLayerStrategy : ILayerStrategy
    {
        public int ResolveSortingOrder(UILayerGroup group, int slot) => group.BaseDepth + slot;
    }

    /// <summary>
    /// 零动效转场（**默认策略**；《客户端总设计》§5.1"形成逻辑边界"）：立刻把视觉落到终态并报告完成。
    ///
    /// **为什么要有它**：原 <c>UIService</c> 把 <see cref="FadeSlideTransition"/>（DOTween 实现）
    /// 当默认值——通用 UI 运行时因此**硬依赖动效适配器**，asmdef 拆不开（成环）。
    /// 改为默认零动效、由**装配根**显式注入真实现（生产装配传 `FadeSlideTransition`）。
    ///
    /// **语义与真实现对齐**（含取消路径）：Enter 落 `alpha=1`、Exit 落 `alpha=0`；
    /// 取消已请求时报 <see cref="MotionOutcome.Cancelled"/>，否则 `Completed`——
    /// 与 <c>FadeSlideTransition</c> 的"先定终态、再复位到终值"同一结果。
    /// **本类零引擎动效依赖**（只碰 <c>CanvasGroup.alpha</c>），故可住在通用 UI 层。
    /// </summary>
    public sealed class InstantTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
            => PlayShow(form, new MotionPlayback(null));      // 旧签名：无终态容器（壳按完成处理）

        public UniTask PlayClose(UIForm form, CancellationToken ct)
            => PlayClose(form, new MotionPlayback(null));

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
