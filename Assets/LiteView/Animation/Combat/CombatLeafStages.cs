using LiteFramework;

namespace LiteView.Animation
{
    /// <summary>
    /// 开火·静止（"窗内保持 clip"）：进态即持 AimIdle 循环——**开火不播专用射击片段**
    /// （包内 `AimIdle_Shoot` 后坐节奏与射速表节拍对不上；开火反馈归枪口特效）。
    /// Fire 事件只由驱动器刷新射击窗，叶无合并动作——站定连发/点按全程同句柄续播
    /// （零重提交、零终态）。起跑 → 迁 FireWalk（窗不清——限速语境保持）。
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
        // 离场不无条件 Stop：窗尽退根/降级由根收口（同形态续播/异形态提交替换）；起跑由次态提交替换
    }

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
