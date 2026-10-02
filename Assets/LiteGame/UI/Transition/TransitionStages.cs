using System;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>阶段共用动作（Out/In 两个阶段完全同构，收口在一处）。</summary>
    internal static class TransitionStageOps
    {
        /// <summary>关输入锁（§6.2/§6.3：接受即锁——阶段 OnEnter 时机，不等策略开始）。
        /// 职责分离：锁的是 <b>interactable</b>（本页可交互）；<b>blocksRaycasts 不动</b>——
        /// 打开的界面继续遮挡下层射线（"禁用页面交互 ≠ 停止阻挡下层射线"，§6.2）。幂等。</summary>
        internal static void CloseGate(UIForm form)
        {
            if (form != null && form.CanvasGroup != null) form.CanvasGroup.interactable = false;
        }

        /// <summary>启动表现（fire-and-forget）。结局只写回 ctx——**不抛穿**（动效不携带判定，动效方案原则 1）。</summary>
        internal static void StartPlay(TransitionContext c)
        {
            if (c.Play == null)
            {
                c.Done = true;
                c.Completed = true;
                return;
            }
            RunAsync(c).Forget();
        }

        private static async UniTaskVoid RunAsync(TransitionContext c)
        {
            try
            {
                await c.Play();
                c.Completed = true;
            }
            catch (OperationCanceledException)
            {
                c.Completed = false;                    // 取消（超时/权威）：不算异常完成
            }
            catch (Exception ex)
            {
                Log.Error($"转场表现失败(mode={c.Mode}):{ex.Message}", "UI");
                c.Completed = false;
                c.Failed = true;                        // U1-③：Kind=Failed 的判据
            }
            finally
            {
                c.Done = true;
            }
        }
    }

    /// <summary>Idle：无事务。收尾（恢复交互门 / 完成 TCS / 取队列）由 <see cref="UITransitionRunner"/> 在本阶段做。</summary>
    internal sealed class IdleTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }
        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req) { }
        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }

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
