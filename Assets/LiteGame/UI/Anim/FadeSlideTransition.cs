using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

using LiteGame.UI;
namespace LiteGame.UI
{

    /// <summary>
    /// 默认转场：淡入淡出 + 轻位移（灰盒版，动效方案附 A.2 形态）。
    /// UI 模块扩展（CanvasGroup.DOFade / DOAnchorPos 系）经 DOTween.Modules asmdef 接入。
    /// 输入锁/恢复由壳统一管（interactable——见 TransitionStageOps/Runner），策略不碰
    /// blocksRaycasts（遮挡下层射线与"本页可交互"职责分离，§6.2）；
    /// ct 取消时**先 Complete() 跳终值再 Kill(false) 清理**即复位（§6.3 复位契约）——
    /// 注意不是 <c>Kill(true)</c>：本仓 DOTween 该调用不复位（详见 <see cref="ITransitionStrategy"/> 注释）。
    /// </summary>
    public sealed class FadeSlideTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
            => PlayShow(form, new MotionPlayback(null));      // 无终态容器（壳按完成处理）

        public UniTask PlayClose(UIForm form, CancellationToken ct)
            => PlayClose(form, new MotionPlayback(null));

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
            seq.SetUpdate(UpdateType.Manual, true);            // UIClock 轨（时停不停/暂停即停，DotweenUiClockDriver 派发）
            seq.SetLink(form.Root, LinkBehaviour.KillOnDisable);            // 池化回收/隐藏即杀，防泄漏
            return seq;
        }

        /// <summary>
        /// 收尾契约（§6.3 复位 + 终态分类）：
        ///
        /// ① **<c>Kill(true)</c> 不复位**：本仓 DOTween 上 <c>Kill(true)</c> 既不跳终值也不派发回调
        ///    （裸 Tweener 与 Sequence 均如此）。复位为 <c>Complete()</c> 跳终值 → 再 <c>Kill(false)</c> 清理。
        /// ② **<c>OnKill</c> 不是完成信号**：<c>OnComplete</c>/<c>OnKill</c> 都直接 <c>TrySetResult</c>
        ///    会让"被 KillOnDisable 杀掉"也报成功。现为 <c>OnComplete</c> 记 Completed、<c>OnKill</c>
        ///    只兜底完成 TCS（并记 Cancelled），迟到回调不覆盖已写终态。
        /// ③ 终态经 <see cref="MotionPlayback"/> 写：调用方可区分"播完"与"被杀"。
        ///
        /// 取消路径顺序：先写终态（Cancelled）→ 再复位（Complete + Kill）——避免复位触发的回调
        /// 覆盖已定终态。取消注册在收尾时释放，不留悬挂注册。
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
            // OnKill 兜底：只保证"任务不悬"（正常完成时 autoKill 也会走这里，但 Settle 幂等不会覆盖）
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
