using System;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>
    /// 一次转场事务的可变状态 + 表现入口（§1.5.2 的 payload 载荷）。
    ///
    /// **为什么是引用类型**：<c>StageMachine</c> 的 <c>OnUpdate/OnLeave</c> 拿不到 payload（只有 <c>OnEnter</c> 有），
    /// 而阶段对象是**无状态单例**（禁实例字段）→ 跨帧状态只能挂在 payload 携带的引用上，
    /// 由 <see cref="UITransitionRunner"/> 在帧末 Tick 里轮询与收尾（机器只管"状态怎么切"）。
    /// </summary>
    public sealed class TransitionContext
    {
        public TransitionMode Mode;

        /// <summary>离场界面（Push 时为 null）。</summary>
        public UIForm Outgoing;

        /// <summary>入场界面（Pop 时为 null）。</summary>
        public UIForm Incoming;

        /// <summary>超时兜底时长（§1.5.4 规则④）。</summary>
        public float MaxDuration;

        /// <summary>本事务的操作作用域（§6.1：超时/权威取消经它停止策略的 Tween/异步工作；
        /// 取消源与状态单处持有，Playback 只读令牌）。</summary>
        public OperationScope Scope;

        /// <summary>表现终态容器（动画专项 §10"结束原因"出口；策略写、收尾读）。</summary>
        public MotionPlayback Playback;

        /// <summary>表现入口（由 Runner 按模式绑定：策略 / IReplaceTransition 合成）。</summary>
        public Func<UniTask> Play;

        /// <summary>表现已结束（含异常/取消；不是"播完"）。</summary>
        public bool Done;

        /// <summary>表现正常完成。</summary>
        public bool Completed;

        /// <summary>策略异常完成（≠ 正常完成/取消）。</summary>
        public bool Failed;

        /// <summary>请求被丢弃/忽略（未开始事务——队列满或目标已是当前 Incoming）。</summary>
        public bool Skipped;

        /// <summary>超时被强制收尾。</summary>
        public bool TimedOut;

        /// <summary>所属页面在事务中被回收/销毁，壳主动取消（策略失去可靠回调源——见 Runner.Tick）。</summary>
        public bool OwnerGone;

        /// <summary>交互门已关（阶段 OnEnter 幂等守卫）。</summary>
        public bool GateClosed;

        /// <summary>事务已收尾（防重复收尾）。</summary>
        public bool Finalized;

        /// <summary>事务完成信号（调用方 await 它拿到 <see cref="TransitionOutcome"/>）。</summary>
        public readonly UniTaskCompletionSource<TransitionOutcome> Tcs = new UniTaskCompletionSource<TransitionOutcome>();
    }

    /// <summary>转场迁移的 payload（只装一个引用：全部状态在 <see cref="TransitionContext"/> 里）。</summary>
    public readonly struct TransitionReq
    {
        public readonly TransitionContext Ctx;

        public TransitionReq(TransitionContext ctx)
        {
            Ctx = ctx;
        }
    }
}
