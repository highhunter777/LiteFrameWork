using LiteFramework;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Locomotion;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Machine;
using LiteGame.Modules.Battle.SubModules.CharactersSubModule.Profiles;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Combat.Aim
{
    /// <summary>
    /// 开火·静止（"窗内保持 clip"）：进态即持 AimIdle 循环——**开火不播专用射击片段** （包内 `AimIdle_Shoot` 后坐节奏与射速表节拍对不上）。
    /// </summary>
    internal sealed class FireIdleStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;

        internal FireIdleStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
        }

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }

        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req)
            => _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);   // 同形态续播保相位（AimIdle 进态开火零闪动）

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            if (StageGate.Pending(m)) return;                  // 根已挂退根/降级——叶放弃层内请求

            if (_ctx.IsMoving)
            {
                m.Request(CharacterAnimId.FireWalk);           // 起跑：clip 由次态提交替换、窗不清
                return;
            }

            _ctx.PlayBodySingle(CharacterAnimationIds.AimIdle);   // 在播续播/死了自愈（幂等）
        }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        // 离场不无条件 Stop：窗尽退根/降级由根收口（同形态续播/异形态提交替换）；起跑由次态提交替换。
    }
}
