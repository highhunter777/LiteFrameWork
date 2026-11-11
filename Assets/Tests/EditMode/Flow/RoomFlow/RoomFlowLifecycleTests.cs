using System;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Shared;
using LiteGame.Modules.UI;
using NUnit.Framework;
using UnityEngine;
using LiteGame.Modules.Battle.SubModules.SettlementSubModule;

namespace LiteGame.Tests.EditMode
{
    /// <summary>离房行为：JoinAck→Battle 排队被覆盖的会话回收、必要卸载未完成的入场阻断、账号重入登录落点。</summary>
    public sealed class RoomFlowLifecycleTests : LiteTesting.Unity.UnityTestBase
    {
        [Test]
        public void JoinAck后Battle排队被Main覆盖_未接管会话恰好回收一次()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            ControlledMatchSession account = null;
            try
            {
                rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                rig.Machine.Advance();
                Assert.AreEqual(1, rig.Accounts.CreateCount, "Match 已创建账号会话（前置）");
                account = rig.Accounts.Last;

                account.EmitJoined();
                Assert.IsTrue(rig.Machine.HasPending, "JoinAck 后已排队 Battle（前置）");
                Assert.AreEqual(ProcedureId.Battle, rig.Machine.PendingId);

                rig.Session.LeaveMatch();
                rig.Machine.Advance();

                Assert.AreEqual(0, rig.Runs.CreateCount, "被 Main 覆盖的 Battle 不得接管运行");
                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "离房回 Main");
                Assert.AreEqual(1, account.DisposeCount, "未接管的账号会话必须恰好回收一次");
            }
            finally
            {
                if (account != null && account.DisposeCount == 0) account.Dispose();
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 必要卸载未结束_再次入场零取票_完成后放行()
        {
            var rig = new RoomFlowRig();
            rig.Login();
            ControlledMatchSession account = null;
            try
            {
                rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                rig.Machine.Advance();
                account = rig.Accounts.Last;
                account.EmitJoined();
                rig.Machine.Advance();
                Assert.AreEqual(ProcedureId.Battle, rig.Machine.Current, "已进入 Battle（前置）");
                Assert.AreEqual(1, rig.Runs.CreateCount);

                Assert.GreaterOrEqual(rig.Scenes.LoadCalls, 1, "本局确实经 ISceneService 加载了场景");
                Assert.IsTrue(rig.Scenes.IsLoaded(ControlledMapProvider.MapLocation), "本次地图场景已加载并由本局持有");

                rig.Session.LeaveMatch();
                rig.Machine.Advance();

                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "Battle 离场后回 Main");
                Assert.GreaterOrEqual(rig.Scenes.UnloadCalls, 1, "离场已发起本次持有场景的卸载");
                Assert.AreEqual(0, rig.Scenes.IgnoredUnloadCalls, "不得卸载本次未加载持有的场景");
                Assert.IsFalse(rig.Scenes.UnloadCompleted, "必要卸载仍在途（前置）");

                int before = rig.Client.TicketCalls;
                rig.Session.QuickEnterAsync();

                Assert.AreEqual(before, rig.Client.TicketCalls, "必要卸载未完成前不得再次取票/入场");

                rig.Scenes.CompleteUnload();
                rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.AreEqual(before + 1, rig.Client.TicketCalls, "卸载完成后放行重新取票");
            }
            finally
            {
                rig.Scenes.CompleteUnload();
                rig.Client.CompleteAll("Room-Idle");
                if (account != null && account.DisposeCount == 0) account.Dispose();
                rig.LeaveToMain();
            }
        }

        [Test]
        public void 账号重入后旧房间清理完成_不覆盖登录页()
        {
            var probe = new ProbeLogic();
            PageDefinition login = UIPageCatalog.Instance.Get(PageIds.Page_Login);
            PageDefinition lobby = UIPageCatalog.Instance.Get(PageIds.Page_Lobby);
            Assert.IsNotNull(login, "生成页面目录缺登录页（前置）");
            Assert.IsNotNull(lobby, "生成页面目录缺大厅页（前置）");

            var ui = new UIService(
                new StaticCatalog(new[] { login, lobby }),
                logicResolver: _ => probe,
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(
                    UIPrefabLeases.Unowned(FakePrefab())),
                transitionStrategy: new InstantTransition());
            var nav = new UINavigationController(ui);
            var flow = new PageFlow(nav, ui, new SettlementSummary());
            try
            {
                flow.OpenLanding(PageFlow.Landing.Lobby);
                Pump(ui);
                Assert.IsTrue(ui.IsOpen(PageIds.Page_Lobby), "大厅已开（前置）");

                flow.ShowLogin("meta.401");
                Pump(ui);
                Assert.IsTrue(ui.IsOpen(PageIds.Page_Login), "账号重入已回登录页（前置）");

                flow.OnMatchEnded(completed: false, result: null, localPlayerId: 1);
                Pump(ui);

                Assert.IsTrue(ui.IsOpen(PageIds.Page_Login), "账号重入后旧房间清理完成不得覆盖登录页");
                Assert.IsFalse(ui.IsOpen(PageIds.Page_Lobby), "旧完成不得重新展示大厅");
            }
            finally
            {
                var shutdown = ui.ShutdownAsync();
                for (int i = 0; i < 200 && shutdown.Status == UniTaskStatus.Pending; i++) ui.Tick(0.05f);
            }
        }

        private GameObject FakePrefab()
            => Scope.CreateGameObject("roomflow_probe", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        private static void Pump(UIService ui, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks; i++) ui.Tick(0.05f);
        }

        private sealed class ProbeLogic : IUIFormLogic
        {
            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) { }
            public void OnUpdate(float deltaTime) { }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
            public void Release() { }
        }

        private sealed class StaticCatalog : IPageCatalog
        {
            private readonly PageDefinition[] _rows;
            public StaticCatalog(PageDefinition[] rows) => _rows = rows;

            public PageDefinition Get(int id)
            {
                foreach (PageDefinition row in _rows) if (row.Id == id) return row;
                return null;
            }
        }
    }
}
