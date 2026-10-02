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
    /// </summary>
    public class PreemptiveStageMachine<TId, TReq> : StageMachine<TId, TReq>
        where TId : struct
    {
        /// <summary>恢复栈深度上限（超出丢最老并计入 <see cref="ResumeDropped"/>）。</summary>
        public const int MaxResumeDepth = 4;

        private static readonly IEqualityComparer<TId> Cmp = EqualityComparer<TId>.Default;

        private readonly List<TId> _resumeStack = new List<TId>(MaxResumeDepth);

        public PreemptiveStageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
            : base(name, stages) { }

        /// <summary>恢复栈是否非空。</summary>
        public bool HasResumePending => _resumeStack.Count > 0;

        /// <summary>恢复栈深度（诊断/HUD）。</summary>
        public int ResumeDepth => _resumeStack.Count;

        /// <summary>因超深被丢弃的恢复项累计数（持续增长说明抢占过密或深度不足）。</summary>
        public int ResumeDropped { get; private set; }

        /// <summary>
        /// 带优先级覆盖的迁移请求：<paramref name="priorityOverride"/> 只作用本次请求
        /// （用 <see cref="int.MinValue"/> 表示"用阶段静态优先级"）。
        /// </summary>
        public bool Request(TId nextId, in TReq req, int priorityOverride)
            => RequestCore(nextId, in req, priorityOverride);

        /// <summary>
        /// 抢占判定：**阶段自身推进一律放行**（否则"攻击(优先级10)播完回 Idle(优先级0)"会被自己挡住）；
        /// 外部请求则要过"当前阶段允许被它打断 && 来者优先级 ≥ 当前优先级"。
        /// </summary>
        protected override bool CanAccept(TId incomingId, int priority)
        {
            if (InStageCallback) return true;

            var current = CurrentStage;
            if (current == null) return true;

            bool allowed = current is IInterruptPolicy<TId> policy ? policy.CanBeInterruptedBy(incomingId) : true;
            if (!allowed)
            {
                MarkRejected(RejectReason.InterruptDisallowed);   // 霸体 / 细粒度规则不允许
                return false;
            }

            int incomingPriority = priority != int.MinValue
                ? priority
                : GetStage(incomingId) is IPriorityStage ip ? ip.Priority : 0;
            int currentPriority = current is IPriorityStage cp ? cp.Priority : 0;

            if (incomingPriority < currentPriority)
            {
                MarkRejected(RejectReason.Priority);
                return false;
            }
            return true;
        }

        /// <summary>旧阶段被替换前：若它声明"要恢复"，入恢复栈。</summary>
        protected override void OnStagePreempted(TId outgoingId)
        {
            if (CurrentStage is IResumeStage rs && rs.Resume == ResumeMode.Resume)
                PushResume(outgoingId);
        }

        /// <summary>恢复栈顶（绕过准入判定）：栈空/已在目标 → false（无害）。</summary>
        public bool TryResume()
        {
            if (_resumeStack.Count == 0)
            {
                MarkRejected(RejectReason.ResumeStackEmpty);
                return false;
            }

            TId target = _resumeStack[_resumeStack.Count - 1];
            _resumeStack.RemoveAt(_resumeStack.Count - 1);

            if (Cmp.Equals(target, Current))
            {
                MarkRejected(RejectReason.ResumeAlreadyCurrent);  // 已在目标：丢弃这条
                return false;
            }
            return EnqueueRequest(target, default);              // 绕过准入（恢复不该被抢占规则再挡）
        }

        /// <summary>停止并复位：先清恢复栈/计数，再走基类（含 OnLeave 对称收尾）。</summary>
        public override void Reset()
        {
            _resumeStack.Clear();
            ResumeDropped = 0;
            base.Reset();
        }

        private void PushResume(TId id)
        {
            if (_resumeStack.Count >= MaxResumeDepth)
            {
                _resumeStack.RemoveAt(0);                        // 丢最老
                ResumeDropped++;
            }
            _resumeStack.Add(id);
        }

        public override void Snapshot(Dictionary<string, string> into)
        {
            base.Snapshot(into);
            into["恢复栈深"] = _resumeStack.Count.ToString();
            into["丢弃恢复"] = ResumeDropped.ToString();
        }
    }
}
