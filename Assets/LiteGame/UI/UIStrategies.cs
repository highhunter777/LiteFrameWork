using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;

namespace LiteGame
{
    /// <summary>层级策略：组内 sortingOrder 分配规则。默认 = 组基序 + 递增槽位。</summary>
    public interface ILayerStrategy
    {
        int ResolveSortingOrder(UILayerGroup group, int slot);
    }

    /// <summary>转场策略（动效设计方案 附 A.2）：入场/离场动效的表现位。
    /// 实现纪律（附 A 原语库）：SetUpdate(UpdateType.Manual, true) 走 UIClock 轨；SetLink(KillOnDisable) 防泄漏；
    /// 动效永不携带判定——播完与否不影响状态迁移（UIService 侧容错等待）。
    ///
    /// （§6.3 复位契约）：<paramref name="ct"/> 取消（超时/权威取消）时实现方必须**停止自身工作**
    /// 并**复位到目标视觉**——动效失败可降级为立即完成页面操作，但必须先复位；
    /// 不得只吞取消留着半截动画。
    ///
    /// **实现者必读**：本仓 DOTween 上 <c>Kill(true)</c> 在裸 Tweener 与 Sequence 上<b>都不跳终值</b>
    /// 且**不派发任何回调**。正确复位是"先 <c>Complete()</c>（跳终值并触发回调）再 <c>Kill(false)</c>（清理）"。</summary>
    public interface ITransitionStrategy
    {
        UniTask PlayShow(UIForm form, CancellationToken ct);
        UniTask PlayClose(UIForm form, CancellationToken ct);

        /// <summary>
        /// 带终态的播放（可选实现位）。未实现的方法壳按 <see cref="MotionPlayback.Finished"/> 判定（未写终态 = 按完成处理）。
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

    /// <summary>出栈拦截：Close 的统一闸口——返回键/程序关闭都经此处，可否决。</summary>
    public interface IPopInterceptor
    {
        bool CanClose(UIForm form);
    }
}
