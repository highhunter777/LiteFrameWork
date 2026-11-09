using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>Idle：无事务。收尾（恢复交互门 / 完成 TCS / 取队列）由 <see cref="UITransitionRunner"/> 在本阶段做。</summary>
    internal sealed class IdleTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }
        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req) { }
        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }
}
