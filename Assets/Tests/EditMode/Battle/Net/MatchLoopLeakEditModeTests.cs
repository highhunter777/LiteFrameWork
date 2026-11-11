using System;
using System.Collections.Generic;
using System.Reflection;
using LiteFramework;
using LiteNet;
using LiteNet.Transport;
using LiteSim;
using NUnit.Framework;
using LiteGame.Modules.Battle.Hosting.Client;
using LiteGame.Modules.Battle.Shared;
using LiteGame.Modules.Map;
using LiteGame.Modules.Battle.SubModules.SessionSubModule;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 100 次 Match 循环无泄漏（《客户端架构重构演进路线图》§5 M2 退出条件）： 每次循环走真装配件——<see cref="MatchSessionFactory"/> 建 Account 域 + 真 <s。
    /// </summary>
    public sealed class MatchLoopLeakEditModeTests
    {
        private const string Host = "127.0.0.1";
        private const int Port = 17777;
        private const string RoomId = "Room-Loop";
        private const string Token = "c2-dev-token";
        private const int Loops = 100;

        /// <summary>假传输（零网络）：Connect 即回调、Send 丢弃；Dispose 计数即"恰好一次"观测点。</summary>
        private sealed class FakeClientTransport : IClientTransport
        {
            public bool Connected { get; private set; }
            public int DisposeCalls;
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

            public void Dispose()
            {
                DisposeCalls++;
                Disconnect();
            }
        }

        /// <summary>地图提供者替身（单图灰盒——与 BattleSessionEditModeTests 同值就地构建）。</summary>
        private sealed class FakeMapProvider : IMapProvider
        {
            private readonly SimMapData _map = GreyboxMap();
            private const string ScenePathValue = "Assets/Scenes/Greybox.unity";

            public SimMapData GetMap(int mapId = 0) => _map;
            public string ScenePath(int mapId) => ScenePathValue;
            public IReadOnlyList<string> ScenePaths() => new[] { ScenePathValue };
            public IReadOnlyList<MapOption> Options => Array.Empty<MapOption>();
        }

        [Test]
        [Category("Contract")]
        public void 百次进房离场_作用域与传输无残留()
        {
            var rootScope = new ClientScope("Root");
            var rootServices = new ServiceContainer();
            rootServices.Seal();

            var transports = new List<FakeClientTransport>(Loops);
            var maps = new FakeMapProvider();                    // 判定图提供者替身（经会话端口委托进对局）
            var accounts = new MatchSessionFactory(rootScope, _ =>
            {
                var transport = new FakeClientTransport();
                transports.Add(transport);
                return new MatchTransport(transport, null);
            }, new HandshakeIdentity(LiteNet.BuildHash.Value),
                new BattleClientFactory(),                       // 客户端只经端口工厂建（A1：会话域不 new）
                maps.GetMap, BattleScenes.ResolveMapId);         // 地图委托：装配根从 Map 域投影进会话端口
            var matches = new BattleDomainFactory(input: null, gameplayDigestBase: () => 0u,
                diagnostics: new LiteFramework.Diagnostics.DiagnosticIdentity(
                    LiteNet.BuildHash.Value, "test", "Editor"));

            var diagnostics = new DevBattleDiagnostics();

            Assert.False(rootServices.TryResolve<BattleContext>(out _),
                "Battle 域只在 Match 子域可解析（父域解析失败——不得跨局复用）");

            for (int i = 0; i < Loops; i++)
            {
                MatchSession account = accounts.Create(Host, Port, RoomId, Token, testRoom: false);
                BattleDomain match = matches.Create(account, new MatchTestRules());
                var session = new BattleSession(match, diagnostics: diagnostics);

                Assert.NotNull(BattleDiagnostics.Current, "对局期诊断读口应指向本局（会话持有登记）");
                Assert.AreSame(match.Context, BattleDiagnostics.Current);

                // AC4：树形无重名——Root→Match→Battle（旧形态 Root→Account→Match→Match）。
                Assert.AreEqual("Match", account.Scope.Name, $"第 {i + 1} 次：per-room 作用域名必须是 Match");
                ClientScope battleScope = BattleScopeOf(match.Context);
                Assert.IsNotNull(battleScope, $"第 {i + 1} 次：BattleContext 缺 Battle 子作用域");
                Assert.AreEqual("Battle", battleScope.Name,
                    $"第 {i + 1} 次：Battle 子作用域必须叫 Battle（Match→Match 重名会掩盖所有权）");
                Assert.AreSame(account.Scope, battleScope.Parent, $"第 {i + 1} 次：Battle 子域必须挂在 Match 域下");
                Assert.AreNotEqual("Account", account.Scope.Name, $"第 {i + 1} 次：per-room 语义旧名不得回流");

                session.Dispose();
                account.Dispose();

                Assert.IsTrue(match.Services.IsDisposed, $"第 {i + 1} 次：Match 服务域未释放");
                Assert.IsTrue(match.Events.IsDisposed, $"第 {i + 1} 次：Match 事件总线未随域释放");
                Assert.IsTrue(account.Scope.IsDisposed, $"第 {i + 1} 次：Match 作用域未释放");
                Assert.IsTrue(battleScope.IsDisposed, $"第 {i + 1} 次：Battle 子作用域未随上下文释放");
                Assert.AreEqual(0, rootScope.OwnedCount, $"第 {i + 1} 次：根作用域残留 {rootScope.OwnedCount} 项");
                Assert.AreEqual(1, transports[i].DisposeCalls, $"第 {i + 1} 次：传输未恰好释放一次");
                Assert.IsNull(BattleDiagnostics.Current, $"第 {i + 1} 次：诊断读口未清（全局当前局残留）");
            }

            Assert.AreEqual(Loops, transports.Count);
            rootScope.Dispose();
        }

        /// <summary>BattleContext 的 Battle 子作用域（私有字段读口：作用域名是树形守卫的观测面）。</summary>
        private static ClientScope BattleScopeOf(BattleContext context)
        {
            FieldInfo field = typeof(BattleContext).GetField("_matchScope",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "BattleContext 缺 _matchScope 字段——树形守卫需同步更新");
            return (ClientScope)field.GetValue(context);
        }

        /// <summary>灰盒地图夹具（与 BattleSessionEditModeTests.GreyboxMap 同值：±100 边界 / 围墙 / 测试方块 / 16 网格出生点）。</summary>
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
