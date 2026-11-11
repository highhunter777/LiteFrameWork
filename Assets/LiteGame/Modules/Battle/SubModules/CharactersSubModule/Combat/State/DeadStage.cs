using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Profiles;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.State
{
    /// <summary>
    /// 死亡叶：死亡期间保持 Death 末帧。
    /// </summary>
    internal sealed class DeadStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal DeadStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
        }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }

        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
        {
            // 只此一次：异 ID 提交替换（覆盖在播的开火/瞄准/移动形态），此后不再发任何请求。
            _ctx.PlayBodySingle(CharacterAnimationIds.Death);
        }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds) { }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m)
        {
            // 复活/被更高优先级打断：主动停掉死亡片段并清通道状态。
            if (_ctx.BodyHandle.IsValid)
                _ctx.Player.Stop(_ctx.BodyHandle, AnimationStopReason.Interrupted);
            _ctx.BodyForm = default;
            _ctx.BodyHandle = default;
            _ctx.BodyIsBlend = false;
        }
    }
}
