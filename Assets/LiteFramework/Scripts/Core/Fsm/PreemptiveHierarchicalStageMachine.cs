using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// **抢占型层级状态机**（《状态机专项设计》§3.3）：层级机（复合/历史/冒泡/事务）+ 抢占层
    /// （优先级抢占 / 中断规则 / 恢复栈）——收敛平面抢占机与层级机的 capability 差，两机共用
    /// <see cref="PreemptionCore{TId,TReq}"/>（准入判定/恢复栈/拒绝上报），判例一致。
    ///
    /// 语义：
    /// - 抢占裁决问**最深活动态**：优先级（<see cref="IPriorityStage"/>）/中断规则（<see cref="IInterruptPolicy{TId}"/>）
    ///   /恢复意愿（<see cref="IResumeStage"/>）都读它的可选接口（祖先不参与裁决）；
    /// - 阶段钩子内发起的迁移一律放行（"自身推进"——与平面抢占机同判例）；
    /// - 被抢占的最深活动态若声明 `Resume` → 入恢复栈（**栈存 id**：`TryResume` 走层级机原生
    ///   Request 管线，路径按历史/初始展开，无需路径快照）；
    /// - <see cref="TryResume"/> **绕过准入**；被拒是正常路径（false + <see cref="StageMachineCore{TId,TReq}.LastReject"/>）；
    /// - 事件冒泡 × 抢占并存：sink 消费不产生迁移；迁移裁决只来自迁移边/显式请求/看门狗。
    ///
    /// 选型：复合状态树 + 角色动作抢占（如"战斗根/移动根下的动作态可被受击抢占后恢复"）用本类；
    /// 纯流程/UI 编排用基类即可。
    /// </summary>
    public class PreemptiveHierarchicalStageMachine<TId, TReq> : HierarchicalStageMachine<TId, TReq>
        where TId : struct
    {
        /// <summary>恢复栈深度上限（超出丢最老并计入 <see cref="ResumeDropped"/>）。</summary>
        public const int MaxResumeDepth = PreemptionCore<TId, TReq>.MaxResumeDepth;

        private readonly PreemptionCore<TId, TReq> _core = new PreemptionCore<TId, TReq>();

        public PreemptiveHierarchicalStageMachine(string name,
            (TId id, IStage<TId, TReq> stage)[] stages,
            CompositeSpec<TId>[] composites,
            TransitionTable<TId, TReq> transitions = null)
            : base(name, stages, composites, transitions) { }

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
        public bool Request(TId target, in TReq req, int priorityOverride)
            => RequestCore(target, in req, priorityOverride);

        /// <summary>抢占判定（转调共用件）：问最深活动态；阶段自身推进一律放行。</summary>
        protected override bool CanAccept(TId target, int priority)
        {
            if (!PreemptionCore<TId, TReq>.CanAdmit(DeepestStage, target, GetStage(target),
                    priority, InStageCallback, out var reason))
            {
                MarkRejected(reason);
                return false;
            }
            return true;
        }

        /// <summary>事务退出段开始前：被迁走的最深活动态若声明"要恢复"，入恢复栈（存 id）。</summary>
        protected override void OnStagePreempted(TId outgoingDeepest)
        {
            if (DeepestStage is IResumeStage rs && rs.Resume == ResumeMode.Resume)
                _core.PushResume(outgoingDeepest);
        }

        /// <summary>恢复栈顶（绕过准入判定；路径按历史/初始展开）：栈空/已在目标 → false（无害）。</summary>
        public bool TryResume()
        {
            if (!_core.TryPopResume(Current, out var target, out var reason))
            {
                MarkRejected(reason);
                return false;
            }
            RecordResumeSuccess();
            return EnqueueRequest(target, default);              // 绕过准入，走层级机原生展开管线
        }

        /// <summary>停止并复位：先清恢复栈/计数，再走基类（含深→浅 OnLeave 对称收尾）。</summary>
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
