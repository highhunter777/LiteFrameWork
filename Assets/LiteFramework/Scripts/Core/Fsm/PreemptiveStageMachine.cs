using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// **抢占型阶段状态机**：在基础机之上补 ARPG/格斗那类"动作密集"所需的三件——
    /// ① **优先级抢占**（<see cref="IPriorityStage"/>）+ **中断规则**（<see cref="IInterruptPolicy{TId}"/>）
    /// ② **被抢占后恢复**（<see cref="IResumeStage"/> + <see cref="ResumeMode"/> + <see cref="TryResume"/>）
    /// ③ **带优先级覆盖的请求**（必杀霸体这类临时越级）
    ///
    /// 选型：**只做基础状态机（流程/UI/转场）→ 用 `StageMachine`；做角色/动作（格斗、ARPG）→ 用本类。**
    ///
    /// 语义：
    /// - 准入 = `当前阶段允许被它打断 && 来者优先级 ≥ 当前优先级`；<see cref="IInterruptPolicy{TId}"/> 未实现 → 默认可打断；
    /// - **阶段钩子内发起的迁移一律放行**（"自身推进"：连段下一段、时长到点回 Idle 不该被自己的优先级挡住）；
    /// - 被拒 → `Request` 返回 **false**（不抛、不改 pending）；
    /// - 被抢占的旧阶段若声明 `Resume` 且允许被打断 → 入恢复栈（后进先出，深度上限见 <see cref="MaxResumeDepth"/>，超限丢最老）；
    /// - <see cref="TryResume"/> **绕过准入判定**（恢复是"回到更早的状态"）。
    ///
    /// 准入判定/恢复栈/拒绝上报由共用件 <see cref="PreemptionCore{TId,TReq}"/> 承担
    /// （《状态机专项设计》§3.3：与层级抢占机同判例），本类只做接线。
    /// </summary>
    public class PreemptiveStageMachine<TId, TReq> : StageMachine<TId, TReq>
        where TId : struct
    {
        /// <summary>恢复栈深度上限（超出丢最老并计入 <see cref="ResumeDropped"/>）。</summary>
        public const int MaxResumeDepth = PreemptionCore<TId, TReq>.MaxResumeDepth;

        private readonly PreemptionCore<TId, TReq> _core = new PreemptionCore<TId, TReq>();

        public PreemptiveStageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, stages) { }

        /// <summary>带迁移表的构造（事件边/条件边经抢占准入裁决——与手动请求同通道）。</summary>
        public PreemptiveStageMachine(string name, TransitionTable<TId, TReq> transitions, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, transitions, stages) { }

        /// <summary>恢复栈是否非空。</summary>
        public bool HasResumePending => _core.HasResumePending;

        /// <summary>恢复栈深度（诊断/HUD）。</summary>
        public int ResumeDepth => _core.ResumeDepth;

        /// <summary>因超深被丢弃的恢复项累计数（持续增长说明抢占过密或深度不足）。</summary>
        public int ResumeDropped => _core.ResumeDropped;

        /// <summary>
        /// 带优先级覆盖的迁移请求：<paramref name="priorityOverride"/> 只作用本次请求
        /// （用 <see cref="int.MinValue"/> 表示"用阶段静态优先级"）。
        /// </summary>
        public bool Request(TId nextId, in TReq req, int priorityOverride)
            => RequestCore(nextId, in req, priorityOverride);

        /// <summary>
        /// 抢占判定（转调共用件）：**阶段自身推进一律放行**；外部请求过
        /// "当前阶段允许被它打断 && 来者优先级 ≥ 当前优先级"。
        /// </summary>
        protected override bool CanAccept(TId incomingId, int priority)
        {
            if (!PreemptionCore<TId, TReq>.CanAdmit(CurrentStage, incomingId, GetStage(incomingId),
                    priority, InStageCallback, out var reason))
            {
                MarkRejected(reason);
                return false;
            }
            return true;
        }

        /// <summary>旧阶段被替换前：若它声明"要恢复"，入恢复栈。</summary>
        protected override void OnStagePreempted(TId outgoingId)
        {
            if (CurrentStage is IResumeStage rs && rs.Resume == ResumeMode.Resume)
                _core.PushResume(outgoingId);
        }

        /// <summary>恢复栈顶（绕过准入判定）：栈空/已在目标 → false（无害）。</summary>
        public bool TryResume()
        {
            if (!_core.TryPopResume(Current, out var target, out var reason))
            {
                MarkRejected(reason);
                return false;
            }
            RecordResumeSuccess();
            return EnqueueRequest(target, default);              // 绕过准入（恢复不该被抢占规则再挡）
        }

        /// <summary>停止并复位：先清恢复栈/计数，再走基类（含 OnLeave 对称收尾）。</summary>
        public override void Reset()
        {
            _core.Reset();
            base.Reset();
        }

        protected override void CaptureCore(FsmCapture<TId> into)
            => _core.CaptureInto(into);

        protected override void RestoreCore(FsmCapture<TId> from)
            => _core.RestoreFrom(from, IsRegistered);

        public override void Snapshot(Dictionary<string, string> into)
        {
            base.Snapshot(into);
            into["恢复栈深"] = _core.ResumeDepth.ToString();
            into["丢弃恢复"] = _core.ResumeDropped.ToString();
        }
    }
}
