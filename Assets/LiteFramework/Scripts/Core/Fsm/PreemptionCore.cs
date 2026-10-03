using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 抢占共用件（《状态机专项设计》§3.3）：准入判定（纯函数）+ 恢复栈 + 深度/丢弃计数——
    /// 平面抢占机（<see cref="PreemptiveStageMachine{TId,TReq}"/>）与层级抢占机
    /// （<see cref="PreemptiveHierarchicalStageMachine{TId,TReq}"/>）各持一份实例转调，两机判例一致。
    ///
    /// 准入 = 当前阶段允许被它打断 && 来者优先级 ≥ 当前（阶段钩子内"自身推进"一律放行）；
    /// 被拒是正常路径（返回 false + <see cref="RejectReason"/>，不抛不改性）。
    /// </summary>
    internal sealed class PreemptionCore<TId, TReq> where TId : struct
    {
        /// <summary>恢复栈深度上限（超出丢最老并计入 <see cref="ResumeDropped"/>）。</summary>
        public const int MaxResumeDepth = 4;

        private readonly List<TId> _resumeStack = new List<TId>(MaxResumeDepth);

        public bool HasResumePending => _resumeStack.Count > 0;

        public int ResumeDepth => _resumeStack.Count;

        /// <summary>因超深被丢弃的恢复项累计数（持续增长说明抢占过密或深度不足）。</summary>
        public int ResumeDropped { get; private set; }

        /// <summary>
        /// 准入判定（纯逻辑，两机同判例）：<paramref name="inStageCallback"/> = 阶段钩子执行中——
        /// 自身推进一律放行（连段下一段、时长到点回 Idle 不该被自己的优先级挡住）。
        /// <paramref name="priority"/> 为 int.MinValue 时取来者阶段的静态优先级（未实现 <see cref="IPriorityStage"/> = 0）。
        /// </summary>
        public static bool CanAdmit(IStage<TId, TReq> current, TId incomingId, IStage<TId, TReq> incoming,
            int priority, bool inStageCallback, out RejectReason reason)
        {
            reason = RejectReason.None;
            if (inStageCallback || current == null) return true;

            bool allowed = current is IInterruptPolicy<TId> policy ? policy.CanBeInterruptedBy(incomingId) : true;
            if (!allowed)
            {
                reason = RejectReason.InterruptDisallowed;       // 霸体 / 细粒度规则不允许
                return false;
            }

            int incomingPriority = priority != int.MinValue
                ? priority
                : incoming is IPriorityStage ip ? ip.Priority : 0;
            int currentPriority = current is IPriorityStage cp ? cp.Priority : 0;

            if (incomingPriority < currentPriority)
            {
                reason = RejectReason.Priority;
                return false;
            }
            return true;
        }

        /// <summary>被抢占阶段入恢复栈（后进先出；超深丢最老并计数）。</summary>
        public void PushResume(TId id)
        {
            if (_resumeStack.Count >= MaxResumeDepth)
            {
                _resumeStack.RemoveAt(0);                        // 丢最老
                ResumeDropped++;
            }
            _resumeStack.Add(id);
        }

        /// <summary>
        /// 弹出恢复栈顶（供 <c>TryResume</c> 走各自机器的原生请求管线）：
        /// 栈空 → <see cref="RejectReason.ResumeStackEmpty"/>；栈顶已是当前态 → 丢弃该条并回
        /// <see cref="RejectReason.ResumeAlreadyCurrent"/>（其余情况 true）。
        /// </summary>
        public bool TryPopResume(TId current, out TId target, out RejectReason reason)
        {
            target = default;
            reason = RejectReason.None;
            if (_resumeStack.Count == 0)
            {
                reason = RejectReason.ResumeStackEmpty;
                return false;
            }

            target = _resumeStack[_resumeStack.Count - 1];
            _resumeStack.RemoveAt(_resumeStack.Count - 1);

            if (EqualityComparer<TId>.Default.Equals(target, current))
            {
                reason = RejectReason.ResumeAlreadyCurrent;      // 已在目标：丢弃这条
                return false;
            }
            return true;
        }

        /// <summary>清恢复栈与丢弃计数（机器 Reset 时转调）。</summary>
        public void Reset()
        {
            _resumeStack.Clear();
            ResumeDropped = 0;
        }

        /// <summary>恢复栈写入快照（§3.4：捕获时一次性分配）。</summary>
        public void CaptureInto(FsmCapture<TId> into)
        {
            into.ResumeStack = _resumeStack.ToArray();
            into.ResumeDropped = ResumeDropped;
        }

        /// <summary>从快照置回恢复栈；栈内 id 未注册（结构已变）→ 抛（"重建后恢复"由调用方保证结构一致）。</summary>
        public void RestoreFrom(FsmCapture<TId> from, System.Predicate<TId> isRegistered)
        {
            if (from.ResumeStack != null)
                foreach (var id in from.ResumeStack)
                    if (!isRegistered(id))
                        throw new System.InvalidOperationException($"恢复栈 id 未注册:{id}（机器结构已变，快照失效）");
            _resumeStack.Clear();
            if (from.ResumeStack != null) _resumeStack.AddRange(from.ResumeStack);
            ResumeDropped = from.ResumeDropped;
        }
    }
}
