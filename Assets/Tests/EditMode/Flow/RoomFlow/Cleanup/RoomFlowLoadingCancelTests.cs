using System.Collections;
using System.Threading;
using LiteGame.Modules.UI;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiteGame.Modules.Meta.Abstractions.Session;

namespace LiteGame.Tests.EditMode
{
    /// <summary>进房等待取消：加载遮罩令牌取消 → 回 Main、会话恰一次释放、观察者放行重进、不落 Error。</summary>
    public sealed class RoomFlowLoadingCancelTests : LiteTesting.Unity.UnityTestBase
    {
        [UnityTest]
        public IEnumerator 等待JoinAck时取消加载遮罩_回Main_会话恰释放一次_观察者放行可重进()
        {
            var harness = new RoomFlowFeedbackHarness(Scope);
            var rig = new RoomFlowRig(feedback: harness.Feedback);
            try
            {
                rig.Login();
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();
                ControlledMatchSession account = rig.Accounts.Last;
                Assert.AreEqual(ProcedureId.Match, rig.Machine.Current, "Match 已入场（前置）");
                Assert.AreEqual(1, rig.Accounts.CreateCount, "已建连等待 JoinAck（前置）");
                Assert.IsTrue(harness.Feedback.IsLoading, "进房等待场遮罩在场（前置）");
                Assert.IsFalse(rig.Machine.HasPending, "JoinAck 未到，本阶段仍在等待（前置）");

                // 取消来源 = 加载遮罩令牌（Match 等待时绑定的同一 cts；Back 拦截与之同源）。
                CancellationToken loadingToken = harness.Feedback.LoadingToken;
                bool loadingCanceled = false;
                loadingToken.Register(() => loadingCanceled = true);
                Assert.IsTrue(harness.Feedback.TryCancelLoading(), "加载面 Back 消费本次取消（前置）");
                Assert.IsTrue(loadingCanceled, "取消确实落在阶段等待的加载遮罩令牌上");
                for (int i = 0; i < 10 && !rig.Machine.HasPending; i++)
                {
                    harness.Pump();
                    yield return null;
                }
                Assert.IsTrue(rig.Machine.HasPending, "取消加载面后应请求流转");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.PendingId, "本阶段回 Main");
                rig.Machine.Advance();

                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "本阶段确实回 Main");
                Assert.AreEqual(0, rig.Error.Enters, "不得把整个流程钉在 ProcedureError");
                Assert.AreEqual(1, account.DisposeCount, "已建会话恰好释放一次（不泄漏、不重复）");

                // 完成信号已发出，但观察者要等阶段真正回 Main 才释放：此刻占用仍拒新入场。
                Assert.IsTrue(rig.Lifetime.IsBusy, "清理完成前占用仍被拒绝");
                int tickets = rig.Client.TicketCalls;
                MetaResult<string> busy = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsFalse(busy.Ok, "清理完成前再次入场被拒");
                Assert.AreEqual("room.flow-busy", busy.Reason, "拒绝原因是稳定忙结果");
                Assert.AreEqual(tickets, rig.Client.TicketCalls, "被拒的入场不得取票");

                for (int i = 0; i < 30 && rig.Lifetime.IsBusy; i++)
                {
                    harness.Pump();
                    yield return null;
                }
                Assert.IsFalse(rig.Lifetime.IsBusy, "观察者最终释放操作占用");
                Assert.AreEqual(1, account.DisposeCount, "会话不得被重复释放");

                MetaResult<string> again = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsTrue(again.Ok, "占用释放后放行重新入场");
                Assert.AreEqual(tickets + 1, rig.Client.TicketCalls, "放行后确实重新取票");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId, "重新入场排队 Match");
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
                harness.Dispose();
            }
        }
    }
}
