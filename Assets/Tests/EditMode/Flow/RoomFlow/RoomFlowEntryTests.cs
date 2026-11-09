using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>入场行为：取票前取消、在途取消晚到、重复请求、旧A新B、Main→Match 排队离房、账号重入。</summary>
    public sealed class RoomFlowEntryTests : LiteTesting.Unity.UnityTestBase
    {
        [Test]
        public void 取票前已取消_零网络请求零RequestMatch()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            UniTask task = rig.Session.QuickEnterAsync(cts.Token);

            Assert.AreEqual(0, rig.Client.TicketCalls, "取票前已取消不得发出取票网络请求");
            RoomFlowTestSupport.Drain(task, "取消请求未收口");
            Assert.IsFalse(rig.Machine.HasPending, "取票前取消不得 RequestMatch");
            Assert.AreEqual(0, rig.Accounts.CreateCount, "取票前取消不得创建连接");
        }

        [Test]
        public void 在途取消_晚到票据成功_零流程推进零建连接()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            rig.Client.GateTickets = true;
            using var cts = new CancellationTokenSource();

            UniTask task = rig.Session.QuickEnterAsync(cts.Token);
            Assert.AreEqual(1, rig.Client.TicketCalls, "取票请求已发出（前置）");
            cts.Cancel();
            rig.Client.CompleteTicket(0, "Room-Late");

            Assert.IsFalse(rig.Machine.HasPending, "在途取消后晚到的票据成功不得 RequestMatch");
            RoomFlowTestSupport.Drain(task, "晚到响应未收口");
            rig.AdvanceUntilNoPending();
            Assert.AreEqual(0, rig.Accounts.CreateCount, "取消的入场不得创建连接");
            Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "取消的入场不得推进阶段");
        }

        [Test]
        public void 重复请求_只发一次取票只建一次连接()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            rig.Client.GateTickets = true;
            try
            {
                UniTask first = rig.Session.QuickEnterAsync();
                Assert.AreEqual(1, rig.Client.TicketCalls, "首个请求发出一票（前置）");

                UniTask second = rig.Session.QuickEnterAsync();

                Assert.AreEqual(1, rig.Client.TicketCalls, "重复请求不得发第二次取票网络请求");
                Assert.AreNotEqual(UniTaskStatus.Pending, second.Status, "重复请求须以稳定忙结果立即结束");
                rig.Client.CompleteAll("Room-Dup");
                RoomFlowTestSupport.Drain(first, "首个请求未收口");
                RoomFlowTestSupport.Drain(second, "重复请求未收口");
                rig.AdvanceUntilNoPending();
                Assert.AreEqual(1, rig.Accounts.CreateCount, "一次有效入场只建一次连接");
            }
            finally
            {
                rig.Client.CompleteAll("Room-Dup");
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 旧A取消后新B成功_A晚到不得推进流程()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            rig.Client.GateTickets = true;
            using var ctsA = new CancellationTokenSource();
            try
            {
                UniTask a = rig.Session.QuickEnterAsync(ctsA.Token);
                Assert.AreEqual(1, rig.Client.TicketCalls, "A 的取票已在途（前置）");

                ctsA.Cancel();
                UniTask b = rig.Session.EnterRoomAsync("Room-B");
                Assert.AreEqual(2, rig.Client.TicketCalls, "A 取消后 B 才启动，各一次取票（前置）");

                rig.Client.CompleteTicket(0, "Room-A");

                Assert.IsFalse(rig.Machine.HasPending, "A 取消后晚到成功不得 RequestMatch");
                RoomFlowTestSupport.Drain(a, "A 请求未收口");

                rig.Client.CompleteTicket(1, "Room-B");
                RoomFlowTestSupport.Drain(b, "B 请求未收口");
                rig.AdvanceUntilNoPending();
                Assert.AreEqual(1, rig.Accounts.CreateCount, "只有 B 建立连接");
                Assert.AreEqual("Room-B", rig.Accounts.LastRoomId, "建连使用 B 的票据房间");
            }
            finally
            {
                rig.Client.CompleteAll("Room-B");
                rig.LeaveToMain();
            }
        }

        [Test]
        public void Main到Match排队后离房_零建连接()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsTrue(rig.Machine.HasPending, "票据成功已排队 Match（前置）");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId);

                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();

                Assert.AreEqual(0, rig.Accounts.CreateCount, "Main→Match 排队后离房必须使排队入场失效，零建连接");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "离房后流程停留 Main");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 账号重入_在途取票晚到不得RequestMatch()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            var surface = new RecordingLoginSurface();
            var reentry = new AccountReentry(rig.Flow, surface);
            rig.Client.GateTickets = true;
            try
            {
                UniTask enter = rig.Session.QuickEnterAsync();
                Assert.AreEqual(1, rig.Client.TicketCalls, "重入前取票在途（前置）");

                bool returned = reentry.ReturnToLoginAsync("meta.401").GetAwaiter().GetResult();
                Assert.IsTrue(returned, "账号重入已发生");
                Assert.IsTrue(surface.IsLoginOpen, "登录页已成为落点（前置）");

                rig.Client.CompleteTicket(0, "Room-Old");
                RoomFlowTestSupport.Drain(enter, "旧取票未收口");

                Assert.IsFalse(rig.Machine.HasPending, "账号重入后旧票据晚到不得 RequestMatch");
                Assert.AreEqual(0, rig.Accounts.CreateCount, "账号重入后旧入场不得创建连接");
            }
            finally
            {
                rig.Client.CompleteAll("Room-Old");
            }
        }
    }
}
