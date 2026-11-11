using LiteFramework;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Locomotion;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.State;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Aim
{
    /// <summary>
    /// 开火·移动（"firewalk 也一样"——同窗规则）：AimMoveBlend 四向**循环**即其保持 clip
    /// （与站定开火同族——不播射击片段，反馈由枪口特效承担）；停步 → 迁 FireIdle 持站姿不重播。
    /// </summary>
    internal sealed class FireWalkStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal FireWalkStage(SlotAnimContext ctx) { _ctx = ctx; }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => AimWalkForm.Submit(_ctx);                        // 含同形态续播（AimWalk 开火 → 就地续播不重提交）
        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;
            if (!_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.FireIdle);            // 停步：持枪站姿（窗保持）
                return;
            }
            AimWalkForm.Maintain(_ctx);
        }
        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
    }
}
