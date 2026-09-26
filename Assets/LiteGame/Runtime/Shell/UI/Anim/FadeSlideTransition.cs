using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteGame.UI
{

    /// <summary>
    /// 默认转场：淡入淡出 + 轻位移（灰盒版，动效方案附 A.2 形态）。
    /// UI 模块扩展（CanvasGroup.DOFade / DOAnchorPos 系）经 DOTween.Modules asmdef 接入
    /// （Modules 源文件由 firstpass 挪入独立程序集，2026-09-13）。
    /// U1-③：输入锁/恢复由壳统一管（interactable——见 TransitionStageOps/Runner），策略不再碰
    /// blocksRaycasts（遮挡下层射线与"本页可交互"职责分离，§6.2）；
    /// ct 取消时**先 Complete() 跳终值再 Kill(false) 清理**即复位（§6.3 复位契约）——
    /// 注意不是 <c>Kill(true)</c>：本仓 DOTween 1.3.030 实测它不复位（详见 <see cref="ITransitionStrategy"/> 注释）。
    /// </summary>
    public sealed class FadeSlideTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
            => PlayShow(form, new MotionPlayback(null));      // 旧签名：无终态容器（壳按完成处理）

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
            seq.SetUpdate(UpdateType.Manual, true);            // G1 动画时钟：UIClock 轨（时停不停/暂停即停，DotweenUiClockDriver 派发）
            seq.SetLink(form.Root, LinkBehaviour.KillOnDisable);            // 池化回收/隐藏即杀，防泄漏
            return seq;
        }

        /// <summary>
        /// 收尾契约（§6.3 复位 + 终态分类）。**2026-09-25 据实测重写**——原实现有三处错误：
        ///
        /// ① **<c>Kill(true)</c> 不复位**：本仓 DOTween 实测 <c>Kill(true)</c> 既不跳终值也不派发回调
        ///    （裸 Tweener 与 Sequence 均如此，原作者注释所称"跳终值即复位"不成立）。复位改为
        ///    <c>Complete()</c> 跳终值 → 再 <c>Kill(false)</c> 清理。
        /// ② **<c>OnKill</c> 不是完成信号**（动画专项 §2 登记的缺陷）：<c>OnComplete</c>/<c>OnKill</c>
        ///    都直接 <c>TrySetResult</c> 会让"被 KillOnDisable 杀掉"也报成功。现改为 <c>OnComplete</c>
        ///    记 Completed、<c>OnKill</c> 只兜底完成 TCS（并记 Cancelled），迟到回调不覆盖已写终态。
        /// ③ **无终态出口**：调用方无法区分"播完"与"被杀"。现经 <see cref="MotionPlayback"/> 写终态。
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
