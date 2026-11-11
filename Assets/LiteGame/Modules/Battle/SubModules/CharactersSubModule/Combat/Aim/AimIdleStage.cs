using LiteFramework;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Locomotion;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Profiles;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Aim
{
    /// <summary>瞄准·静止：AimIdle 单片段循环（Override 覆盖；窗无涉——活跃判据只 IsAiming）。</summary>
    internal sealed class AimIdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal AimIdleStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.AimWalk);            // 迟滞进入（锁存置位）
                return;
            }
            _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle); // 在播续播/死了自愈（幂等）
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }
}
