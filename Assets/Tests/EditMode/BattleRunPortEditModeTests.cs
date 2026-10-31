using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Google.Protobuf;
using LiteFramework;
using LiteNet;
using LiteNet.Proto;
using LiteNet.Protocol;
using LiteNet.Transport;
using LiteSim;
using LiteTesting;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 对局运行端口（<see cref="IBattleRun"/>）直接验收：StartGame 早到/Ended 早到、终局原因映射、
    /// Leave 幂等、Dispose 不触发 Ended、视图挂载/拆除。用真 <see cref="BattleSession"/> +
    /// 假传输（零网络），不依赖 PlayMode。
    /// </summary>
    public sealed class BattleRunPortEditModeTests
    {
        private const string Host = "127.0.0.1";
        private const int Port = 17777;
        private const string RoomId = "Room-Run";
        private const string Token = "c2-dev-token";

        private sealed class FakeClientTransport : IClientTransport
        {
            public bool Connected { get; private set; }
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port)
            {
                Connected = true;
                OnConnected?.Invoke();
            }

            public void Disconnect()
            {
                if (!Connected) return;
                Connected = false;
                OnDisconnected?.Invoke();
            }

            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Send(ArraySegment<byte> data, bool reliable) { }
            public void Dispose() => Disconnect();

            public void Deliver(IMessage message, PacketType type)
                => OnData?.Invoke(new ArraySegment<byte>(PacketCodec.Encode(type, message)), true);
        }

        private sealed class FakeViewGroup : IBattleViewGroup
        {
            public bool Attached;
            public bool Disposed;
            public UniTask LoadAsync(CancellationToken ct) => UniTask.CompletedTask;
            public void Attach(BattleContext context, Action openBattleMenu = null) => Attached = true;
            public void Tick(float elapseSeconds) { }
            public void TickPreSample() { }
            public void Dispose() => Disposed = true;
        }

        private sealed class RunFixture : IDisposable
        {
            public readonly ClientScope Account;
            public readonly FakeClientTransport Transport;
            public readonly BattleClient Battle;
            public readonly BattleContext Context;
            public readonly BattleSession Run;

            public RunFixture()
            {
                Account = new ClientScope("Account");
                Transport = new FakeClientTransport();
                Battle = new BattleClient(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, Transport);
                Account.Register(Battle);        // 传输/客户端随 Account Scope 释放（测试不靠 GC）
                Context = new BattleContext(Battle, Account, map: GreyboxMap());
                var services = new ServiceContainer();
                services.Seal();
                var events = new EventBus(new ReferencePoolService());
                var match = new MatchSession(services, Context, events);
                Run = new BattleSession(match);
            }

            public void DeliverJoinAck()
                => Transport.Deliver(new JoinAck { PlayerId = 1, ReconnectToken = "ticket-1" }, PacketType.JoinAck);

            public void DeliverStartGame()
                => Transport.Deliver(new StartGame
                {
                    Seed = 1,
                    ConfigHash = CombatConfigDigest.WithTestRules(0u, false, false),
                    Frame = 0,
                }, PacketType.StartGame);

            public void Dispose()
            {
                Run.Dispose();
                Context.Dispose();
                Account.Dispose();
            }
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void StartGame早到_等待端口立即返回true()
        {
            using var f = new RunFixture();
            f.DeliverJoinAck();
            f.DeliverStartGame();

            Assert.IsTrue(f.Run.HasStarted);
            Assert.IsTrue(f.Run.WaitStartedOrEndedAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsFalse(f.Run.Outcome.HasValue);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 等待挂起后StartGame到达_唤醒返回true()
        {
            using var f = new RunFixture();
            f.DeliverJoinAck();

            var waiting = f.Run.WaitStartedOrEndedAsync(CancellationToken.None);
            f.DeliverStartGame();              // 订阅已挂起后再到：必须唤醒而非只在早到路径成立

            Assert.IsTrue(waiting.GetAwaiter().GetResult());
            Assert.IsTrue(f.Run.HasStarted);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 等待挂起后Ended到达_唤醒返回false()
        {
            using var f = new RunFixture();

            var waiting = f.Run.WaitStartedOrEndedAsync(CancellationToken.None);
            f.Run.Leave();

            Assert.IsFalse(waiting.GetAwaiter().GetResult());
            Assert.AreEqual(BattleRunOutcome.Leave, f.Run.Outcome);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 等待取消_抛取消异常()
        {
            using var f = new RunFixture();
            using var cts = new CancellationTokenSource();

            var waiting = f.Run.WaitStartedOrEndedAsync(cts.Token);
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Ended早到_等待端口返回false且映射Leave()
        {
            using var f = new RunFixture();
            int ended = 0;
            f.Run.Ended += _ => ended++;

            f.Run.Leave();
            f.Run.Leave();                       // 幂等：终局只发生一次

            Assert.AreEqual(BattleRunOutcome.Leave, f.Run.Outcome);
            Assert.AreEqual(1, ended, "Ended 恰好一次");
            Assert.IsFalse(f.Run.WaitStartedOrEndedAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void Dispose不触发Ended()
        {
            var f = new RunFixture();
            int ended = 0;
            f.Run.Ended += _ => ended++;
            f.Run.Dispose();

            Assert.AreEqual(0, ended, "清理不是结束——Dispose 不得触发 Ended");
            f.Context.Dispose();
            f.Account.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 视图挂载与拆除_归运行端口()
        {
            using var f = new RunFixture();
            var view = new FakeViewGroup();

            f.Run.AttachView(view);
            f.Run.ActivateView();
            Assert.IsTrue(view.Attached, "ActivateView 把已挂视图接到上下文");

            f.Run.DetachView();
            Assert.IsTrue(view.Disposed, "DetachView 逆序拆视图");
            Assert.IsNull(f.Run.View);
        }

        private static SimMapData GreyboxMap()
        {
            var obstacles = new SimObstacle[5];
            obstacles[0] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, -69f), HalfX = 70f, HalfZ = 1f, Height = 20f };
            obstacles[1] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 69f), HalfX = 70f, HalfZ = 1f, Height = 20f };
            obstacles[2] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(-69f, 0f, 0f), HalfX = 1f, HalfZ = 70f, Height = 20f };
            obstacles[3] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(69f, 0f, 0f), HalfX = 1f, HalfZ = 70f, Height = 20f };
            obstacles[4] = new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(5f, 0f, 0f), HalfX = 0.5f, HalfZ = 0.5f, Height = 1f };
            var spawns = new SimVector3[SimMapData.MaxSpawnPoints];
            for (int i = 0; i < spawns.Length; i++)
                spawns[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);
            return SimMapBuilder.Build(0f, 100f, 100f, obstacles, obstacles.Length, spawns, spawns.Length);
        }
    }
}
