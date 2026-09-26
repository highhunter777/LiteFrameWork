using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    ///
    /// U1-③（§6.3 复位契约）：<paramref name="ct"/> 取消（超时/权威取消）时实现方必须**停止自身工作**
    /// 并**复位到目标视觉**——动效失败可降级为立即完成页面操作，但必须先复位；
    /// 不得只吞取消留着半截动画。
    ///
    /// **实现者必读（2026-09-25 实测修正）**：U1-③ 原文写的"<c>Kill(complete=true)</c> 跳终值即复位"
    /// 在本仓 DOTween 上**不成立**——实测 <c>Kill(true)</c> 在裸 Tweener 与 Sequence 上<b>都不跳终值</b>
    /// 且**不派发任何回调**。正确复位是"先 <c>Complete()</c>（跳终值并触发回调）再 <c>Kill(false)</c>（清理）"。
    /// 这是本批修掉的真实缺陷：原实现的取消路径根本没复位，页面停在半透明/偏移状态。</summary>
    public interface ITransitionStrategy
    {
        UniTask PlayShow(UIForm form, CancellationToken ct);
        UniTask PlayClose(UIForm form, CancellationToken ct);

        /// <summary>
        /// 带终态的播放（可选实现位）。默认转场实现它；旧实现只实现上面两个方法——
        /// 壳会退化为按 <see cref="MotionPlayback.Finished"/> 判定（未写终态 = 按完成处理，保持旧语义）。
        /// </summary>
        UniTask PlayShow(UIForm form, MotionPlayback playback) => PlayShow(form, playback.Token);

        /// <summary>带终态的播放（离场；见 <see cref="PlayShow(UIForm, MotionPlayback)"/>）。</summary>
        UniTask PlayClose(UIForm form, MotionPlayback playback) => PlayClose(form, playback.Token);
    }

    /// <summary>
    /// 可选：Replace（同组全屏互斥切换）的"两组并发"定制位（《UI扩展能力设计》§1.5.5）。
    /// 未实现时壳按 <c>WhenAll(PlayClose(outgoing), PlayShow(incoming))</c> 合成——
    /// 交叉淡入淡出 / 共享元素位移这类"必须一起算"的效果才需要实现本接口。
    /// </summary>
    public interface IReplaceTransition
    {
        UniTask PlayReplace(UIForm outgoing, UIForm incoming, CancellationToken ct);

        /// <summary>带终态的 Replace（可选实现位；见 <see cref="ITransitionStrategy.PlayShow(UIForm, MotionPlayback)"/>）。</summary>
        UniTask PlayReplace(UIForm outgoing, UIForm incoming, MotionPlayback playback) => PlayReplace(outgoing, incoming, playback.Token);
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
