using System;
using System.Collections;
using LiteGame.Modules.UI;
using MetaWire;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiteGame.Tests.EditMode
{
    /// <summary>T3 前失败恢复：取票故障、畸形票据、排队冻结、建连失败不落到 Error/NRE。</summary>
    public sealed class RoomFlowFailureTests : LiteTesting.Unity.UnityTestBase
    {
        [Test]
        public void 取票拒绝_零排队零连接_占用释放后可重取()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                rig.Client.TicketResponse = MetaCall<JoinTicketInfo>.Failed(
                    MetaFailure.Rejected(503, "room.rejected", "模拟拒绝"));
                MetaCall<JoinTicketInfo> rejected = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsFalse(rejected.Ok, "拒绝必须原样成为失败结果");
                Assert.AreEqual(MetaFailureKind.Rejected, rejected.Failure.Kind);
                Assert.IsFalse(rig.Machine.HasPending, "取票被拒不得排队 Match");
                Assert.AreEqual(0, rig.Accounts.CreateAttempts, "取票被拒不得建连接");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "流程停留 Main");

                rig.Client.TicketResponse = null;
                int tickets = rig.Client.TicketCalls;
                MetaCall<JoinTicketInfo> retry = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsTrue(retry.Ok, "失败后占用须释放，可再次取票");
                Assert.AreEqual(tickets + 1, rig.Client.TicketCalls, "重试确实重新取票");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId, "重试成功重新排队 Match");
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
            }
        }

        [Test]
        public void 取票网络失败_零排队零连接_占用释放后可重取()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                rig.Client.TicketResponse = MetaCall<JoinTicketInfo>.Failed(
                    MetaFailure.Network("模拟网络中断"));
                MetaCall<JoinTicketInfo> failed = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsFalse(failed.Ok);
                Assert.AreEqual(MetaFailureKind.Network, failed.Failure.Kind);
                Assert.IsFalse(rig.Machine.HasPending, "网络失败不得排队 Match");
                Assert.AreEqual(0, rig.Accounts.CreateAttempts, "网络失败不得建连接");

                rig.Client.TicketResponse = null;
                int tickets = rig.Client.TicketCalls;
                MetaCall<JoinTicketInfo> retry = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsTrue(retry.Ok, "网络失败后占用须释放，可再次取票");
                Assert.AreEqual(tickets + 1, rig.Client.TicketCalls);
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId);
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
            }
        }

        [TestCase("null-value")]
        [TestCase("empty-ticket")]
        [TestCase("bad-address")]
        public void 成功响应缺值或非法地址_稳定失败零排队(string mode)
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                rig.Client.TicketResponse = MetaCall<JoinTicketInfo>.OkResult(Payload(mode));
                MetaCall<JoinTicketInfo> call = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsNotNull(call, "畸形成功响应不得以 NRE 逃逸");
                Assert.IsFalse(call.Ok, "缺值/缺票/非法地址不得被当作可入场");
                Assert.AreEqual(MetaFailureKind.InvalidResponse, call.Failure.Kind, "稳定失败（确定性，不重试）");
                Assert.IsFalse(rig.Machine.HasPending, "畸形票据不得排队 Match");
                Assert.AreEqual(0, rig.Accounts.CreateAttempts, "畸形票据不得建连接");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "流程停留 Main");
            }
            finally
            {
                rig.Client.TicketResponse = null;
            }
        }

        [UnityTest]
        public IEnumerator 排队未消费重复进入_只发一票_离房消费后放行()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "首票成功（前置）");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId, "已排队 Match（前置）");
                int tickets = rig.Client.TicketCalls;

                MetaResult<string> duplicate = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsFalse(duplicate.Ok);
                Assert.AreEqual("room.flow-busy", duplicate.Reason, "排队未消费时重复进入 = 稳定忙结果");
                Assert.AreEqual(tickets, rig.Client.TicketCalls, "重复进入不得发第二票");

                rig.Session.LeaveMatch();
                MetaResult<string> whileQueued = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsFalse(whileQueued.Ok, "无效 queue 尚未消费前仍拒新票");
                Assert.AreEqual("room.flow-busy", whileQueued.Reason);
                Assert.AreEqual(tickets, rig.Client.TicketCalls, "无效 queue 未消费前不得取新票");

                rig.AdvanceUntilNoPending();
                for (int i = 0; i < 3; i++) yield return null;
                rig.AdvanceUntilNoPending();

                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "无效 queue 消费后回 Main");
                int retryTickets = rig.Client.TicketCalls;
                MetaResult<string> retry = default;
                for (int i = 0; i < 10; i++)
                {
                    retry = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                    if (retry.Ok) break;
                    yield return null;
                }
                Assert.IsTrue(retry.Ok, "占用已释放，放行新票");
                Assert.AreEqual(retryTickets + 1, rig.Client.TicketCalls, "放行后确实重新取票");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.PendingId);
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
            }
        }

        [UnityTest]
        public IEnumerator 建连工厂抛异常_清理回Main允许新票重进()
        {
            var accounts = new ControlledMatchSessionFactory
            {
                CreateFailure = new InvalidOperationException("模拟建连失败"),
            };
            var rig = new RoomFlowRig(accounts);
            rig.Login();
            try
            {
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();
                for (int i = 0; i < 3; i++) yield return null;
                rig.AdvanceUntilNoPending();
                for (int i = 0; i < 3; i++) yield return null;
                rig.AdvanceUntilNoPending();

                Assert.AreEqual(1, accounts.CreateAttempts, "首轮确已尝试建连（前置）");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current,
                    "建连失败应清理本次资源并回 Main，而非只能重启的 ProcedureError");

                int tickets = rig.Client.TicketCalls;
                MetaResult<string> retry = default;
                for (int i = 0; i < 10; i++)
                {
                    retry = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                    if (retry.Ok) break;
                    yield return null;
                }
                Assert.IsTrue(retry.Ok, "占用已释放，可重新取票");
                Assert.AreEqual(tickets + 1, rig.Client.TicketCalls, "重试确实重新取票");

                rig.Machine.Advance();
                Assert.AreEqual(2, accounts.CreateAttempts, "重试确实再次建连");
                Assert.AreEqual(ProcedureId.Match, rig.Machine.Current, "重试进入真实 Match");
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
            }
        }

        private static JoinTicketInfo Payload(string mode)
        {
            if (mode == "null-value") return null;
            if (mode == "empty-ticket")
                return new JoinTicketInfo
                {
                    InstanceId = "ins-roomflow",
                    Address = "203.0.113.7:17777",
                    RoomId = "Room-Quick",
                    Ticket = string.Empty,
                    ExpiresAtMs = 1,
                };
            return new JoinTicketInfo
            {
                InstanceId = "ins-roomflow",
                Address = "no-port",
                RoomId = "Room-Quick",
                Ticket = "tkt-roomflow",
                ExpiresAtMs = 1,
            };
        }
    }
}
