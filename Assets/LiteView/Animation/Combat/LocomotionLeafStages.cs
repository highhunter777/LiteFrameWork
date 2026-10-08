using LiteFramework;
using LiteFramework.Animation;

namespace LiteSim.View.Animation
{
    /// <summary>移动根·静止叶（形态归移动根——本叶只做迟滞轴迁移）。</summary>
    internal sealed class IdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal IdleStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (_ctx.IsMoving) m.Request(CharacterAnimId.Moving);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }

    /// <summary>移动根·移动叶（同上——Idle↔Moving 同形态 MoveBlend，跨越不重提交）。</summary>
    internal sealed class MovingStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal MovingStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving) m.Request(CharacterAnimId.Idle);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }
}
