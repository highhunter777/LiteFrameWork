using LiteFramework;
using LiteFramework.Animation;
using LiteSim;

namespace LiteGame.View.Animation
{
    /// <summary>
    /// 死亡叶：死亡期间保持 Death 末帧；复活由根的存活事实裁决退出。
    ///
    /// 帧锁定的机制：
    /// - Death 定义声明 `holdOnFinish`——播放器完成收口按定义**豁免停机**（通道保持活跃），
    ///   非循环资产（Die2 导入 loopTime=0）采样停在末帧——姿态冻结；
    /// - 本叶 OnUpdate 空转（不重发请求——重发会按第 0 帧重播，尸体反复抽搐）；
    /// - 根裁决按 <see cref="SlotAnimContext.IsDead"/> 恒驻本叶（窗充值/退根/降级全被守卫），
    ///   Override 通道权重保持、Base 不回流——Idle 不会盖上来。
    ///
    /// 进入来源 = 驱动器每帧权威状态轮询（本地=预测态经和解、远端=快照渲染态；Hp≤0）；
    /// 客户端 Death 帧事件不再驱动动画（预测事件不可撤销）。
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
            // 只此一次：异 ID 提交替换（覆盖在播的开火/瞄准/移动形态），此后不再发任何请求
            _ctx.PlayBodySingle(CharacterAnimationIds.Death);
        }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds) { }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m)
        {
            // 复活/被更高优先级打断：主动停掉死亡片段并清通道状态。死亡定义是 HoldOnFinish
            // （末帧锁定）；若只依赖下一叶替换，死亡姿态/末帧会多留一段，表现为复活后模型
            // 仍不可见或迟显。这里与根的退根收口同口径（幂等）。
            if (_ctx.BodyHandle.IsValid)
                _ctx.Player.Stop(_ctx.BodyHandle, AnimationStopReason.Interrupted);
            _ctx.BodyForm = default;
            _ctx.BodyHandle = default;
            _ctx.BodyIsBlend = false;
        }
    }
}
