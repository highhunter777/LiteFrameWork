using System.Collections;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.UI;
using MetaWire;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiteGame.Modules.Meta.Abstractions.Room;
using LiteGame.Modules.Meta.Abstractions.Session;

namespace LiteGame.Tests.EditMode
{
    /// <summary>Match 域（AC3/AC4）：进房创建、交付即 Seal、失败/取消/离场释放恰一次、未登录拒绝、开发直入。</summary>
    public sealed class RoomFlowMatchDomainTests : LiteTesting.Unity.UnityTestBase
    {
        [Test]
        public void 进房_Match域创建一次_交付即Seal_作用域名Match()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();

                Assert.AreEqual(1, rig.Accounts.CreateCount, "进房创建 Match 域恰一次");
                ControlledMatchSession session = rig.Accounts.Last;
                Assert.IsNotNull(session, "Match 会话已交付");
                Assert.IsNotNull(session.Scope, "Match 域必须有资源域");
                Assert.AreEqual("Match", session.Scope.Name, "per-room 作用域名 = Match（无 Account→Match→Match 重名）");
                Assert.IsFalse(session.Scope.IsDisposed, "交付期不得释放");
                Assert.IsTrue(session.Services.IsSealed, "Match 服务域交付即 Seal（不新增未 Seal 窗口）");
                Assert.IsTrue(rig.Domain.IsOpen, "产品进房要求账户域已开（前置）");
            }
            finally
            {
                rig.Session.LeaveMatch();
                rig.AdvanceUntilNoPending();
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 取票失败_零建域()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            rig.Client.TicketResponse = MetaCall<JoinTicketInfo>.Failed(
                MetaFailure.Rejected(503, "room.rejected", "模拟拒绝"));
            try
            {
                MetaCall<JoinTicketInfo> rejected = rig.Flow.EnterRoomAsync("Room-Quick").GetAwaiter().GetResult();

                Assert.IsFalse(rejected.Ok);
                Assert.AreEqual(0, rig.Accounts.CreateAttempts, "取票被拒不得创建 Match 域");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "流程停留 Main");
            }
            finally
            {
                rig.Client.TicketResponse = null;
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator Join未完成即离场_Battle前失败直接释放恰一次()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();
                ControlledMatchSession session = rig.Accounts.Last;
                Assert.IsNotNull(session, "Match 域已建（前置，JoinAck 未到）");

                rig.Session.LeaveMatch();
                rig.Machine.Advance();
                for (int i = 0; i < 120 && session.DisposeCount == 0; i++) yield return null;

                Assert.AreEqual(1, session.DisposeCount, "Battle 前失败/离场：Match 域恰好释放一次");
                Assert.IsTrue(session.Scope.IsDisposed, "Match 资源域已释放");
                Assert.AreEqual(0, session.Scope.OwnedCount, "Match 资源域无残留登记");
                Assert.IsTrue(session.Services.IsDisposed, "Match 服务域随域释放");
                Assert.AreEqual(0, rig.Runs.CreateCount, "未接管的 Battle 不得接管运行");
                Assert.AreEqual(1, rig.RootScope.OwnedCount, "根作用域只剩账户域（无 Match 子域残留）");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 离场_Match域释放恰一次_根无残留()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            try
            {
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();
                ControlledMatchSession session = rig.Accounts.Last;
                Assert.IsNotNull(session, "Match 域已建（前置）");
                session.EmitJoined();
                rig.Machine.Advance();
                Assert.AreEqual(ProcedureId.Battle, rig.Machine.Current, "已进入 Battle（前置）");
                Assert.AreEqual(1, rig.Runs.CreateCount, "Battle 已接管运行（前置）");

                rig.Session.LeaveMatch();
                rig.Machine.Advance();
                rig.Scenes.CompleteUnload();
                for (int i = 0; i < 120 && session.DisposeCount == 0; i++) yield return null;

                Assert.AreEqual(1, session.DisposeCount, "离场：Match 域恰好释放一次");
                Assert.IsTrue(session.Scope.IsDisposed, "Match 资源域已释放");
                Assert.AreEqual(0, session.Scope.OwnedCount, "Match 资源域无残留登记");
                Assert.IsTrue(session.Services.IsDisposed, "Match 服务域随域释放");
                Assert.AreEqual(1, rig.RootScope.OwnedCount, "根作用域只剩账户域（无 Match→Match 子域残留）");
                Assert.AreEqual(1, rig.Runs.Last.DisposeCount, "对局运行端口恰好释放一次");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 未登录_产品进房被拒_NotLoggedIn零取票零建域()
        {
            var rig = new RoomFlowRig();
            try
            {
                Assert.IsFalse(rig.Domain.IsOpen, "未登录（前置）");

                MetaResult<string> enter = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();

                Assert.IsFalse(enter.Ok, "未登录产品进房必须被拒");
                Assert.AreEqual("entry.not-logged-in", enter.Reason, "失败码单源：NotLoggedIn");
                Assert.AreEqual(0, rig.Client.TicketCalls, "未登录不得取票（零连接）");
                Assert.AreEqual(0, rig.Accounts.CreateAttempts, "未登录不得创建 Match 域");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 开发直入_不要求登录_可进Match()
        {
            var rig = new RoomFlowRig();
            try
            {
                Assert.IsFalse(rig.Domain.IsOpen, "未登录（前置）");

                rig.Machine.Request(ProcedureId.Match, new ProcedureArgs(testRoom: true));
                rig.Machine.Advance();
                for (int i = 0; i < 120 && rig.Accounts.CreateCount == 0; i++) yield return null;

                Assert.AreEqual(1, rig.Accounts.CreateCount, "开发直入不经登录也能建 Match 域");
                Assert.AreEqual(ProcedureMatch.TestRoomId, rig.Accounts.LastRoomId, "直入用测试房号");
                Assert.AreEqual(0, rig.Client.TicketCalls, "直入不经 Meta 票据");

                rig.Flow.LeaveMatch();
                rig.Machine.Advance();
                ControlledMatchSession session = rig.Accounts.Last;
                for (int i = 0; i < 120 && session != null && session.DisposeCount == 0; i++) yield return null;

                Assert.IsNotNull(session, "直入已建会话（前置）");
                Assert.AreEqual(1, session.DisposeCount, "直入离场/失败释放恰一次");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }
    }
}
