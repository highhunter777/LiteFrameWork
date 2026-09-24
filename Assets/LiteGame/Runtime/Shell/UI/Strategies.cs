using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>层级策略（M4 §2.2）：组内 sortingOrder 分配规则。默认 = 组基序 + 递增槽位。</summary>
    public interface ILayerStrategy
    {
        int ResolveSortingOrder(UILayerGroup group, int slot);
    }

    /// <summary>转场策略（M4 §2.2，动效设计方案 附 A.2）：入场/离场动效的表现位。
    /// 实现纪律（附 A 原语库）：SetUpdate(UpdateType.Manual, true) 走 UIClock 轨；SetLink(KillOnDisable) 防泄漏；
    /// 动效永不携带判定——播完与否不影响状态迁移（UIService 侧容错等待）。
    /// U1-③（§6.3 复位契约）：<paramref name="ct"/> 取消（超时/权威取消）时实现方必须**停止自身工作**
    /// （Tween Kill/异步终止）并**复位到目标视觉**（Kill(complete=true) 跳终值即复位）——
    /// 动效失败可降级为立即完成页面操作，但必须先复位；不得只吞取消留着半截动画。</summary>
    public interface ITransitionStrategy
    {
        UniTask PlayShow(UIForm form, CancellationToken ct);
        UniTask PlayClose(UIForm form, CancellationToken ct);
    }

    /// <summary>
    /// 可选：Replace（同组全屏互斥切换）的"两组并发"定制位（《UI扩展能力设计》§1.5.5）。
    /// 未实现时壳按 <c>WhenAll(PlayClose(outgoing), PlayShow(incoming))</c> 合成——
    /// 交叉淡入淡出 / 共享元素位移这类"必须一起算"的效果才需要实现本接口。
    /// </summary>
    public interface IReplaceTransition
    {
        UniTask PlayReplace(UIForm outgoing, UIForm incoming, CancellationToken ct);
    }

    /// <summary>出栈拦截（M4 §2.2）：Close 的统一闸口——返回键/程序关闭都经此处，可否决。</summary>
    public interface IPopInterceptor
    {
        bool CanClose(UIForm form);
    }

    /// <summary>默认层级策略：组基序 + 槽位（与 2.1 灰盒行为一致）。</summary>
    public sealed class DefaultLayerStrategy : ILayerStrategy
    {
        public int ResolveSortingOrder(UILayerGroup group, int slot) => group.BaseDepth + slot;
    }

    /// <summary>
    /// 默认转场：淡入淡出 + 轻位移（灰盒版，动效方案附 A.2 形态）。
    /// UI 模块扩展（CanvasGroup.DOFade / DOAnchorPos 系）经 DOTween.Modules asmdef 接入
    /// （Modules 源文件由 firstpass 挪入独立程序集，2026-09-13）。
    /// U1-③：输入锁/恢复由壳统一管（interactable——见 TransitionStageOps/Runner），策略不再碰
    /// blocksRaycasts（遮挡下层射线与"本页可交互"职责分离，§6.2）；
    /// ct 取消时 Kill(complete=true)——跳到终值即复位（§6.3 复位契约）。
    /// </summary>
    public sealed class FadeSlideTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct)
        {
            var cg = form.CanvasGroup;
            var seq = BuildBase(form);
            seq.Join(cg.DOFade(1f, 0.25f).From(0f));
            if (form.Root.transform is RectTransform rt)
                seq.Join(rt.DOAnchorPosY(40f, 0.25f).From(true).SetEase(Ease.OutQuad));
            return ToTask(seq, ct);
        }

        public UniTask PlayClose(UIForm form, CancellationToken ct)
        {
            var cg = form.CanvasGroup;
            var seq = BuildBase(form);
            seq.Join(cg.DOFade(0f, 0.2f).SetEase(Ease.InQuad));
            return ToTask(seq, ct);
        }

        private static Sequence BuildBase(UIForm form)
        {
            var seq = DOTween.Sequence();
            seq.SetUpdate(UpdateType.Manual, true);            // G1 动画时钟：UIClock 轨（时停不停/暂停即停，DotweenUiClockDriver 派发）
            seq.SetLink(form.Root, LinkBehaviour.KillOnDisable);            // 池化回收/隐藏即杀，防泄漏
            return seq;
        }

        /// <summary>序列完成/被杀都放行（不依赖 DOTween UniTask 模块，UniTaskCompletionSource 直连）。
        /// ct 取消：Kill(complete=true)——杀掉 Tween 并跳到终值（复位到目标视觉，§6.3）。</summary>
        private static UniTask ToTask(Sequence seq, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            seq.OnComplete(() => tcs.TrySetResult());
            seq.OnKill(() => tcs.TrySetResult());
            if (ct.CanBeCanceled) ct.Register(() => seq.Kill(true));
            return tcs.Task;
        }
    }

    /// <summary>默认出栈拦截：全放行（灰盒；具体界面的"未保存拦截"由业务替换策略实现）。</summary>
    public sealed class DefaultPopInterceptor : IPopInterceptor
    {
        public bool CanClose(UIForm form) => true;
    }
}
