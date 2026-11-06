using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteFramework.UI
{

    /// <summary>
    /// 默认转场：淡入淡出 + 轻位移（灰盒版，动效方案附 A.2 形态）。
    /// </summary>
    public sealed class FadeSlideTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
            => PlayShow(form, new MotionPlayback());      // 无终态容器（壳按完成处理）

        public UniTask PlayClose(UIForm form, CancellationToken ct)
            => PlayClose(form, new MotionPlayback());

        public UniTask PlayShow(UIForm form, MotionPlayback playback)
        {
            var cg = form.CanvasGroup;
            var seq = BuildBase(form);
            seq.Join(cg.DOFade(1f, 0.25f).From(0f));
            if (form.Root.transform is RectTransform rt)
                seq.Join(rt.DOAnchorPosY(40f, 0.25f).From(true).SetEase(Ease.OutQuad));
            return ToTask(seq, playback);
        }

        public UniTask PlayClose(UIForm form, MotionPlayback playback)
        {
            var cg = form.CanvasGroup;
            var seq = BuildBase(form);
            seq.Join(cg.DOFade(0f, 0.2f).SetEase(Ease.InQuad));
            return ToTask(seq, playback);
        }

        private static Sequence BuildBase(UIForm form)
        {
            var seq = DOTween.Sequence();
            // 生产装配绑定 UIClock：Manual 轨由 DotweenUIClockDriver 派发（时停不停/暂停即停）。
            bool manual = UIAnimationClock.IsBound || !Application.isPlaying;
            seq.SetUpdate(manual ? UpdateType.Manual : UpdateType.Normal, true);
            seq.SetLink(form.Root, LinkBehaviour.KillOnDisable);            // 池化回收/隐藏即杀，防泄漏
            return seq;
        }

        /// <summary>
        /// 收尾契约（§6.3 复位 + 终态分类）： ① **<c>Kill(true)</c> 不复位**：本仓 DOTween 上 <c>Kill(true)</c> 既不跳终值也不派发回调 （裸 Tweener 与 Sequ。
        /// </summary>
        private static UniTask ToTask(Sequence seq, MotionPlayback playback)
        {
            var tcs = new UniTaskCompletionSource();
            CancellationTokenRegistration registration = default;

            void Settle(MotionOutcome outcome)
            {
                playback?.Finish(outcome);                     // 幂等：只记第一次
                tcs.TrySetResult();
            }

            seq.OnComplete(() => Settle(MotionOutcome.Completed));
            // OnKill 兜底：只保证"任务不悬"（正常完成时 autoKill 也会走这里，但 Settle 幂等不会覆盖）。
            seq.OnKill(() => Settle(MotionOutcome.Cancelled));

            if (playback != null && playback.Token.CanBeCanceled)
            {
                registration = playback.Token.Register(() =>
                {
                    Settle(MotionOutcome.Cancelled);           // 先定终态
                    ResetVisual(seq);                          // 再复位（复位触发的回调不会覆盖终态）
                });
            }

            return AwaitAndRelease(tcs.Task, registration);
        }

        /// <summary>复位：跳终值（自动触发 OnComplete/内部 Kill）→ 清理。见 <see cref="ToTask"/> ①。</summary>
        private static void ResetVisual(Sequence seq)
        {
            try
            {
                if (seq.IsActive()) seq.Complete();            // 跳终值：alpha/位置回到目标视觉
                seq.Kill(false);                               // 清理（不重复 complete）
            }
            catch (Exception) { /* 复位失败不阻断收尾：页面操作可降级为立即完成（§6.3） */ }
        }

        private static async UniTask AwaitAndRelease(UniTask task, CancellationTokenRegistration registration)
        {
            try { await task; }
            finally { registration.Dispose(); }                // 收尾即释放取消注册，不留悬挂
        }
    }
}
