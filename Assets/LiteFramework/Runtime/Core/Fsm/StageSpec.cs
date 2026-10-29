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

    /// <summary>
    /// 驻留看门狗声明（可选接口，《状态机专项设计》§3.2）：当前阶段驻留帧数超限且无挂起时，
    /// 机器在 Tick 统一裁决并 <see cref="StageMachineCore{TId,TReq}.ForceState"/> 强制迁往 <see cref="TimeoutTarget"/>。
    /// 定位是**驻留异常升级**（流程卡死、AI 卡态）——编排级超时兜底（如 UI 转场收尾）归各自编排层，两者正交。
    /// </summary>
    public interface IStageTimeout<TId> where TId : struct
    {
        /// <summary>驻留上限（帧）；&lt;= 0 = 不启用看门狗。</summary>
        int TimeoutFrames { get; }

        /// <summary>超时目标（须已注册；超时触发时经 ForceState 通道，绕准入不绕重入/未注册检查）。</summary>
        TId TimeoutTarget { get; }
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

        /// <summary>表数据兼容字段（M3-7 删抢占机后**无读取方**；保留以免表源/用例大改）。</summary>
        public int Priority;

        /// <summary>表数据兼容字段（无读取方——恢复栈随抢占机删除）。</summary>
        public ResumeMode Resume = ResumeMode.Cancel;

        /// <summary>表数据兼容字段（无读取方——抢占准入已删除）。</summary>
        public bool CanBeInterrupted = true;

        /// <summary>表数据兼容字段（无读取方）。</summary>
        public Func<TId, bool> CanBeInterruptedBy;

        /// <summary>&gt;0 时：本阶段驻留到该帧数即自动迁移（表驱动的"时长"）。</summary>
        public int DurationFrames;

        /// <summary>是否设置了 <see cref="NextId"/>（`TId` 是结构体，需要一个显式开关表达"未设置"）。</summary>
        public bool HasNext;

        /// <summary>时长到点后去哪。</summary>
        public TId NextId;

        /// <summary>表数据兼容字段（M3-7 起无读取方：抢占机删除后无恢复栈；`TableStage` 恒走 <see cref="NextId"/>）。</summary>
        public bool AutoResumeOnEnd;

        /// <summary>驻留看门狗（§3.2）：&gt; 0 且无挂起时驻留到该帧数即强制迁往 <see cref="TimeoutTarget"/>。</summary>
        public int TimeoutFrames;

        /// <summary>看门狗目标（与 <see cref="TimeoutFrames"/> 配对；TimeoutFrames &lt;= 0 时不读它）。</summary>
        public TId TimeoutTarget;

        // ---- 可选钩子（保持"逻辑薄"：重逻辑仍应写 IStage 类）----
        public Action<StageSpec<TId, TReq>> OnEnterAction;
        public Action<StageSpec<TId, TReq>> OnUpdateAction;
        public Action<StageSpec<TId, TReq>> OnLeaveAction;
    }
}
