using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteGameNext;
using LiteGameNext.Features.Account;
using LiteGameNext.Features.Account.Content;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Matchmaking;
using LiteGameNext.Features.Result;
using LiteGameNext.Platform.Meta;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>Matchmaking 功能：房间列表/建房/快速进入/失败状态；用假 Meta 传输。</summary>
    public sealed class ClientNextMatchmakingTests
    {
        private UiRuntime _ui;
        private AccountFeature _account;
        private FakeTransport _transport;
        private MatchmakingFeature _feature;

        [SetUp]
        public void SetUp()
        {
            _ui = new UiRuntime();
            _ui.Initialize();
            _account = new AccountFeature(_ui);
            _account.SignInAsGuest();
            _transport = new FakeTransport();
            _feature = new MatchmakingFeature(_ui, _account, new MetaClient(_transport));
        }

        [TearDown]
        public void TearDown()
        {
            _feature?.Dispose();
            _ui?.Shutdown();
        }

        [Test]
        public void 刷新房间列表_成功写Rooms()
        {
            _transport.Enqueue(200, "{\"rooms\":[{\"instanceId\":\"i\",\"maxPlayers\":8,\"playerCount\":2}]}");
            bool ok = _feature.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.AreEqual(1, _feature.Rooms.Count);
            StringAssert.Contains("房间数", _feature.Status);
        }

        [Test]
        public void 建房_成功形成MatchInfo()
        {
            _transport.Enqueue(200, "{\"instanceId\":\"i\",\"address\":\"127.0.0.1:7777\",\"roomId\":\"r\",\"ticket\":\"t\",\"expiresAtMs\":9}");
            bool ok = _feature.CreateRoomAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.IsNotNull(_feature.CurrentMatch);
            Assert.AreEqual("r", _feature.CurrentMatch.RoomId);
            Assert.AreEqual("127.0.0.1:7777", _feature.CurrentMatch.Address);
        }

        [Test]
        public void 快速进入_用首个房间换票()
        {
            _transport.Enqueue(200, "{\"rooms\":[{\"instanceId\":\"i\",\"rooms\":[{\"roomId\":\"room-a\"}]}]}");
            _transport.Enqueue(200, "{\"instanceId\":\"i\",\"address\":\"a\",\"roomId\":\"room-a\",\"ticket\":\"t\",\"expiresAtMs\":9}");
            _feature.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            bool ok = _feature.QuickJoinAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(ok);
            Assert.AreEqual("room-a", _feature.CurrentMatch.RoomId);
        }

        [Test]
        public void 失败_保留失败状态不形成Match()
        {
            _transport.Enqueue(500, "{\"code\":\"lobby.down\",\"messageKey\":\"lobby.down\"}");
            bool ok = _feature.CreateRoomAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(ok);
            Assert.IsNull(_feature.CurrentMatch);
            StringAssert.Contains("建房失败", _feature.Status);
        }

        [Test]
        public void 登录成功后_AppFlow打开真实大厅页()
        {
            var ui = new UiRuntime(ClientNextTestPages.PageFactory());
            ui.Initialize();
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"accountId\":\"acc\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\",\"expiresAtMs\":1}");
            transport.Enqueue(200, "{\"rooms\":[]}");
            var account = new AccountFeature(ui, new MetaClient(transport));
            var feature = new MatchmakingFeature(ui, account, new MetaClient(transport));
            using var cts = new CancellationTokenSource();
            UniTask run = new AppFlow(ui, account, feature).RunAsync(cts.Token);
            try
            {
                Assert.IsInstanceOf<LoginPage>(ui.Top.Page);
                Button button = FindButton(ui.Top.Page, "Btn_GuestLogin");
                button.onClick.Invoke();
                Assert.IsInstanceOf<LobbyPage>(ui.Top.Page, "登录后应打开大厅页");
                cts.Cancel();
                Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
                run.GetAwaiter().GetResult();
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 建房后_流程进入房间_结算_回到大厅()
        {
            var ui = new UiRuntime(ClientNextTestPages.PageFactory());
            ui.Initialize();
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"accountId\":\"acc\",\"accessToken\":\"tok\",\"tokenType\":\"Bearer\",\"expiresAtMs\":1}");
            transport.Enqueue(200, "{\"rooms\":[]}");
            transport.Enqueue(200, "{\"instanceId\":\"i\",\"address\":\"127.0.0.1:7777\",\"roomId\":\"r\",\"ticket\":\"t\",\"expiresAtMs\":9}");
            var account = new AccountFeature(ui, new MetaClient(transport));
            var matchmaking = new MatchmakingFeature(ui, account, new MetaClient(transport));
            var battle = new BattleFeature(ui, (match, hud, world, ct) =>
                UniTask.FromResult(BattleResult.Completed("p1")));
            var result = new ResultFeature(ui);
            using var cts = new CancellationTokenSource();
            UniTask run = new AppFlow(ui, account, matchmaking, battle, result).RunAsync(cts.Token);
            try
            {
                FindButton(ui.Top.Page, "Btn_GuestLogin").onClick.Invoke();
                Assert.IsInstanceOf<LobbyPage>(ui.Top.Page);

                matchmaking.CreateRoomAsync(CancellationToken.None).GetAwaiter().GetResult();

                Assert.IsInstanceOf<ResultPage>(ui.Top.Page, "建房后应进入房间→结算页");
                PageHandle resultPage = ui.Top;
                resultPage.CloseAsync().GetAwaiter().GetResult();
                Assert.IsInstanceOf<LobbyPage>(ui.Top.Page, "结算关闭后应回到大厅");

                cts.Cancel();
                Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
                run.GetAwaiter().GetResult();
            }
            finally
            {
                battle.Dispose();
                result.Dispose();
                matchmaking.Dispose();
                ui.Shutdown();
            }
        }

        [Test]
        public void 打开大厅页_真实prefab可装载()
        {
            var ui = new UiRuntime(ClientNextTestPages.PageFactory());
            ui.Initialize();
            var account = new AccountFeature(ui);
            account.SignInAsGuest();
            var transport = new FakeTransport();
            transport.Enqueue(200, "{\"rooms\":[]}");
            var feature = new MatchmakingFeature(ui, account, new MetaClient(transport));
            try
            {
                feature.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
                Assert.IsInstanceOf<LobbyPage>(ui.Top.Page);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        private static Button FindButton(PageBase page, string bindName)
        {
            Button[] buttons = page.GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                BindName bind = buttons[index].GetComponent<BindName>();
                if (bind != null && bind.Name == bindName) return buttons[index];
            }

            throw new InvalidOperationException("找不到按钮：" + bindName);
        }

        private sealed class FakeTransport : IMetaTransport
        {
            private readonly Queue<MetaResponse> _responses = new Queue<MetaResponse>();

            public void Enqueue(long status, string body) => _responses.Enqueue(new MetaResponse(status, body));

            public UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken)
                => UniTask.FromResult(_responses.Count == 0
                    ? new MetaResponse(0, "no response")
                    : _responses.Dequeue());
        }
    }
}
