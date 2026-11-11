using LiteFramework;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Locomotion;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.State;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Aim
{
    /// <summary>瞄准·移动：AimMoveBlend 四向混合 + 步频倍率（限速走路档 ⇒ 倍率恒≈1）。</summary>
    internal sealed class AimWalkStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal AimWalkStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => AimWalkForm.Submit(_ctx);
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.AimIdle);            // 迟滞退出（锁存清零）
                return;
            }
            AimWalkForm.Maintain(_ctx);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }
}
