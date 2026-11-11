using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using LiteFramework;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiteGame.Modules.Meta.Abstractions;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 异步离场（方案 N2 / AC5）：四终态（无操作 / 清理完成+Main·Error / 未启动 / 根取消）、
    /// 卸载失败 false、超时 false（可注入阈值），以及重入顺序 Logout→LeaveMatchAsync→Close→ShowLogin。
    /// </summary>
    public sealed class RoomFlowLeaveMatchAsyncTests : LiteTesting.Unity.UnityTestBase
    {
        private sealed class OrderProbeSurface : ILoginSurface
        {
            public int ShowLoginCount;
            public bool DomainOpenAtShow;

            public bool IsLoginOpen => ShowLoginCount > 0;

            public Func<bool> DomainOpen = () => true;

            public void ShowLogin(string reason)
            {
                DomainOpenAtShow = DomainOpen();
                ShowLoginCount++;
            }
        }

        private static void EnterBattle(RoomFlowRig rig, out ControlledMatchSession session)
        {
            rig.Login();
            Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
            rig.Machine.Advance();
            session = rig.Accounts.Last;
            Assert.IsNotNull(session, "Match 域已建（前置）");
            session.EmitJoined();
            rig.Machine.Advance();
            Assert.AreEqual(ProcedureId.Battle, rig.Machine.Current, "已进入 Battle（前置）");
        }

        [Test]
        public void 无操作_true()
        {
            var rig = new RoomFlowRig();
            try
            {
                UniTask<bool> leave = rig.Flow.LeaveMatchAsync();

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "无在途操作 = 立即终态");
                Assert.IsTrue(leave.GetAwaiter().GetResult(), "无当前操作 → 可安全关账户");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 阶段机未启动_true()
        {
            var rig = new RoomFlowRig();
            try
            {
                rig.Machine.Reset();

                UniTask<bool> leave = rig.Flow.LeaveMatchAsync();

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "未启动阶段机 = 立即终态");
                Assert.IsTrue(leave.GetAwaiter().GetResult(), "!Started → 可安全关账户");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 根取消_true()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            var rig = new RoomFlowRig(rootToken: cts.Token);
            try
            {
                cts.Cancel();

                UniTask<bool> leave = rig.Flow.LeaveMatchAsync();

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "根取消 = 由根关闭兜底，立即终态");
                Assert.IsTrue(leave.GetAwaiter().GetResult(), "根取消 → true");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 清理在途_等到终态才返回true()
        {
            var rig = new RoomFlowRig();
            ControlledMatchSession session = null;
            try
            {
                EnterBattle(rig, out session);
                for (int i = 0; i < 5; i++) yield return null;   // 让 Battle 的 OnEnter 完成会话接管

                UniTask<bool> leave = rig.Flow.LeaveMatchAsync();

                Assert.AreEqual(UniTaskStatus.Pending, leave.Status,
                    "清理未完成前 LeaveMatchAsync 不得完成（终态判据：CleanupTask 完成且回 Main）");

                rig.Machine.Advance();
                for (int i = 0; i < 60 && leave.Status == UniTaskStatus.Pending; i++) yield return null;

                rig.Scenes.CompleteUnload();
                for (int i = 0; i < 180 && leave.Status == UniTaskStatus.Pending; i++) yield return null;

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "清理终态后必须完成");
                Assert.IsTrue(leave.GetAwaiter().GetResult(), "终态 = 可安全关账户");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "清理终态 = 已回 Main");
                Assert.AreEqual(1, session.DisposeCount, "Match 域恰一次释放（前置观察）");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 卸载失败_返回false()
        {
            var rig = new RoomFlowRig(scenes: new ThrowingUnloadSceneService());
            ControlledMatchSession session = null;
            try
            {
                EnterBattle(rig, out session);

                UniTask<bool> leave = rig.Flow.LeaveMatchAsync();
                rig.Machine.Advance();
                for (int i = 0; i < 180 && leave.Status == UniTaskStatus.Pending; i++) yield return null;

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "卸载失败必须收敛为确定结果（不得悬挂）");
                Assert.IsFalse(leave.GetAwaiter().GetResult(),
                    "卸载失败（CleanupDone==false 且 Main）→ false：调用方不得硬关账户、保留 busy");
                Assert.AreEqual(1, session.DisposeCount, "Match 域仍恰一次释放");
            }
            finally
            {
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 超时_可注入阈值_返回false()
        {
            ConstructorInfo ctor = typeof(ProcedureFlow).GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(TimeSpan)
                    || p.ParameterType == typeof(int) || p.ParameterType == typeof(float)));
            Assert.IsNotNull(ctor,
                "T5 需给 ProcedureFlow 可注入的离场等待阈值（构造参数 int 毫秒 / TimeSpan）——否则超时分支不可测");

            var rig = new RoomFlowRig();
            ControlledMatchSession session = null;
            try
            {
                EnterBattle(rig, out session);

                ParameterInfo[] ps = ctor.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    if (ps[i].ParameterType == typeof(TimeSpan)) args[i] = TimeSpan.FromMilliseconds(1);
                    else if (ps[i].ParameterType == typeof(int)) args[i] = 1;
                    else if (ps[i].ParameterType == typeof(float)) args[i] = 0.001f;
                    else if (ps[i].ParameterType == typeof(RoomFlowLifetime)) args[i] = rig.Lifetime;
                    else if (ps[i].ParameterType == typeof(StageMachine<ProcedureId, ProcedureArgs>))
                        args[i] = rig.Machine;
                    else if (ps[i].ParameterType == typeof(IRoomTicketSource)) args[i] = rig.Tickets;
                    else if (ps[i].ParameterType == typeof(System.Threading.CancellationToken))
                        args[i] = System.Threading.CancellationToken.None;
                    else Assert.Fail("ProcedureFlow 超时构造形参未识别：" + ps[i].Name);
                }
                var flow = (ProcedureFlow)ctor.Invoke(args);

                UniTask<bool> leave = flow.LeaveMatchAsync();
                rig.Machine.Advance();
                for (int i = 0; i < 180 && leave.Status == UniTaskStatus.Pending; i++) yield return null;

                Assert.AreNotEqual(UniTaskStatus.Pending, leave.Status, "阈值到期必须收敛（不悬挂）");
                Assert.IsFalse(leave.GetAwaiter().GetResult(), "等待超过注入阈值 → false（保留 busy）");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }

        [UnityTest]
        public IEnumerator 重入_登出_等离场_关域_回登录页()
        {
            var rig = new RoomFlowRig();
            ControlledMatchSession session = null;
            try
            {
                EnterBattle(rig, out session);
                var surface = new OrderProbeSurface { DomainOpen = () => rig.Domain.IsOpen };
                var reentry = new AccountReentry(rig.Flow, rig.Flow, rig.Domain, surface);

                UniTask<bool> returning = reentry.ReturnToLoginAsync("meta.401");
                rig.Machine.Advance();
                rig.Scenes.CompleteUnload();
                for (int i = 0; i < 180 && returning.Status == UniTaskStatus.Pending; i++) yield return null;

                Assert.AreNotEqual(UniTaskStatus.Pending, returning.Status, "重入未收口");
                Assert.IsTrue(returning.GetAwaiter().GetResult(), "账号重入已发生");
                Assert.AreEqual(1, surface.ShowLoginCount, "回登录页恰一次");
                Assert.IsFalse(surface.DomainOpenAtShow, "ShowLogin 时账户域已硬释放（Close 先于导航）");
                Assert.IsFalse(rig.Domain.IsOpen, "重入后账户域已关闭");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "重入后流程回 Main");
                Assert.AreEqual(1, session.DisposeCount, "Match 域恰一次释放（清理先于关域）");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.LeaveToMain();
            }
        }
    }
}
