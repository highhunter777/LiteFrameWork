using LiteFramework;

namespace LiteFramework.UI
{
    /// <summary>Out：离场（Pop）。<c>OnEnter</c> = 关交互门 + 启动 PlayClose。
    /// 轮询与收尾刻意留空——那两件事要读 payload 而 <c>OnUpdate/OnLeave</c> 拿不到（Runner 在帧末做）。</summary>
    internal sealed class OutTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }

        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req)
        {
            var c = req.Ctx;
            if (c == null || c.GateClosed) return;
            c.GateClosed = true;
            TransitionStageOps.CloseGate(c.Outgoing);
            TransitionStageOps.CloseGate(c.Incoming);
            TransitionStageOps.StartPlay(c);
        }

        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }
}
