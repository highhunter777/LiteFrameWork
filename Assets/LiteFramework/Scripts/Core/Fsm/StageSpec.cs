using System;

namespace LiteFramework
{
    /// <summary>
    /// 被抢占时原阶段的处置：`Cancel` = 直接丢弃（默认）；`Resume` = 入恢复栈，
    /// 事后可 <c>TryResume()</c> 回来（受击硬直结束回到被打断的动作这类）。
    /// </summary>
    public enum ResumeMode : byte
    {
        Cancel = 0,
        Resume = 1,
    }

    /// <summary>
    /// 请求被拒的原因：**只描述"正常路径的拒绝"**——
    /// 编程错误（未 Start / 未注册 / 重入 / OnLeave 窗口）仍然抛异常，不走这里。
    /// 读点：`StageMachine.LastReject`（每次请求后刷新，被接受则回 <see cref="None"/>）。
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

    /// <summary>优先级声明（可选接口）：值大者胜，同值可互相抢占；未实现 = 0。</summary>
    public interface IPriorityStage
    {
        int Priority { get; }
    }

    /// <summary>
    /// 中断规则（可选接口）：**"谁能打断我"**。未实现 = 默认可被打断。
    /// 实现后，`StageMachine` 的抢占判定会问它（而不是只看"是否有优先级"）。
    /// </summary>
    public interface IInterruptPolicy<TId>
    {
        bool CanBeInterruptedBy(TId incoming);
    }

    /// <summary>恢复意愿声明（可选接口）：被抢占时是否入恢复栈（见 <see cref="ResumeMode"/>）。</summary>
    public interface IResumeStage
    {
        ResumeMode Resume { get; }
    }

    /// <summary>
    /// 表驱动阶段的规格：**一行 = 一个状态的"数据"**，行为由通用
    /// <see cref="TableStage{TId,TReq}"/> 统一承担——这是"状态多而规则同构"（格斗/ARPG 几十上百个动作态）
    /// 的表达方式：60 个状态 = 1 个实现 + 60 行 spec，而不是 60 个类。
    ///
    /// 与"类即状态"的分工判据：**状态少而每个状态的逻辑都不一样 → 写 `IStage` 类；状态多而规则同构 → 写 spec 行。**
    /// </summary>
    public sealed class StageSpec<TId, TReq>
        where TId : struct
    {
        /// <summary>该行对应的阶段 id。</summary>
        public TId Id;

        /// <summary>静态优先级：大者胜；`Request` 的 `priorityOverride` 可临时覆盖。</summary>
        public int Priority;

        /// <summary>被抢占后是否入恢复栈。</summary>
        public ResumeMode Resume = ResumeMode.Cancel;

        /// <summary>是否可被打断（简化规则）；`CanBeInterruptedBy` 非空时以它为准。霸体 = false。</summary>
        public bool CanBeInterrupted = true;

        /// <summary>细粒度中断规则：谁能打断我（null = 只看 <see cref="CanBeInterrupted"/>）。</summary>
        public Func<TId, bool> CanBeInterruptedBy;

        /// <summary>&gt;0 时：本阶段驻留到该帧数即自动迁移（表驱动的"时长"）。</summary>
        public int DurationFrames;

        /// <summary>是否设置了 <see cref="NextId"/>（`TId` 是结构体，需要一个显式开关表达"未设置"）。</summary>
        public bool HasNext;

        /// <summary>时长到点后去哪。</summary>
        public TId NextId;

        /// <summary>时长到点时优先"恢复栈顶"（而不是 <see cref="NextId"/>）。</summary>
        public bool AutoResumeOnEnd;

        // ---- 可选钩子（保持"逻辑薄"：重逻辑仍应写 IStage 类）----
        public Action<StageSpec<TId, TReq>> OnEnterAction;
        public Action<StageSpec<TId, TReq>> OnUpdateAction;
        public Action<StageSpec<TId, TReq>> OnLeaveAction;
    }
}
