using System;

namespace LiteFramework
{
    /// <summary>
    /// 请求被拒的原因：**只描述"正常路径的拒绝"**—— 编程错误（未 Start / 未注册 / 重入 / OnLeave 窗口）仍然抛异常，不走这里。
    /// </summary>
    public enum RejectReason : byte
    {
        /// <summary>未被拒（最近一次请求被接受）。</summary>
        None = 0,

        /// <summary>优先级不足（来者优先级 &lt; 当前阶段）。</summary>
        Priority = 1,

        /// <summary>当前阶段的中断规则不允许被打断（霸体 / 细粒度规则）。</summary>
        InterruptDisallowed = 2,

        /// <summary><c>TryResume</c>：恢复栈为空。</summary>
        ResumeStackEmpty = 3,

        /// <summary><c>TryResume</c>：栈顶目标已是当前阶段（无需恢复）。</summary>
        ResumeAlreadyCurrent = 4,
    }

    /// <summary>
    /// 表驱动阶段的规格：**一行 = 一个状态的"数据"**，行为由通用 <see cref="TableStage{TId,TReq}"/> 统一承担——这是"状态多而规则同构"（格斗/ARPG 几十上百个动作态） 的表达方。
    /// </summary>
    public sealed class StageSpec<TId, TReq>
        where TId : struct
    {
        /// <summary>该行对应的阶段 id。</summary>
        public TId Id;

        /// <summary>&gt;0 时：本阶段驻留到该帧数即自动迁移（表驱动的"时长"）。</summary>
        public int DurationFrames;

        /// <summary>是否设置了 <see cref="NextId"/>（`TId` 是结构体，需要一个显式开关表达"未设置"）。</summary>
        public bool HasNext;

        /// <summary>时长到点后去哪。</summary>
        public TId NextId;

        /// <summary>驻留看门狗（§3.2）：&gt; 0 且无挂起时驻留到该帧数即强制迁往 <see cref="TimeoutTarget"/>。</summary>
        public int TimeoutFrames;

        /// <summary>看门狗目标（与 <see cref="TimeoutFrames"/> 配对；TimeoutFrames &lt;= 0 时不读它）。</summary>
        public TId TimeoutTarget;

        // ---- 可选钩子（保持"逻辑薄"：重逻辑仍应写 IStage 类）----。
        public Action<StageSpec<TId, TReq>> OnEnterAction;
        public Action<StageSpec<TId, TReq>> OnUpdateAction;
        public Action<StageSpec<TId, TReq>> OnLeaveAction;
    }
}
