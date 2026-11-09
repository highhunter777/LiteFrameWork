using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>In：入场（Push）/ 切换（Replace，两组并发在 Runner 绑定的 Play 里；**不设第四态**）。</summary>
    internal sealed class InTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }

        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req)
        {
            var c = req.Ctx;
            if (c == null || c.GateClosed) return;
            c.GateClosed = true;
            TransitionStageOps.CloseGate(c.Outgoing);   // Replace：两组都关
            TransitionStageOps.CloseGate(c.Incoming);
            TransitionStageOps.StartPlay(c);
        }

        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }
}
