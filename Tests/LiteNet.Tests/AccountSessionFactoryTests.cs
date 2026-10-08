using System;
using System.Collections.Generic;
using LiteClient;
using LiteFramework;
using LiteGame;
using LiteNet;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Account 会话工厂 L1 直测（《客户端依赖注入专项设计》§6/DI-3）：
    /// <see cref="AccountSessionFactory"/> 与 <see cref="BattleClient"/> 以**源链接**进本工程
    /// （零引擎依赖——测真工厂非复制品）；假 IClientTransport + 真 ServiceContainer/ClientScope。
    /// 验收口径 = DI-3 的 .NET 半边："进房、离场（取消/失败）后再进房，均无旧局对象残留"。
    /// Match 半边（BattleContext，依赖 SimView）归 Unity 面/DI-4。
    /// </summary>
    public sealed class AccountSessionFactoryTests
    {
        private const string Host = "127.0.0.1";
        private const int Port = 17777;
        private const string RoomId = "Room-A";
        private const string Token = "c2-dev-token";

        /// <summary>假传输（全替身、零网络）：连接/断线手工驱动；Dispose 计数即"释放恰好一次"的观测点。</summary>
        private sealed class FakeClientTransport : IClientTransport
        {
            public bool Connected { get; private set; }
            public int ConnectCalls;
            public int DisposeCalls;
            public event Action OnConnected;
            public event Action<ArraySegment<byte>, bool> OnData;
            public event Action OnDisconnected;

            public void Connect(string address, int port)
            {
                ConnectCalls++;
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

        /// <summary>根域标记服务（跨会话常驻的观测点：会话生灭后根域仍可解析它）。</summary>
        private sealed class RootMarker { }

        private static ServiceContainer NewRoot(out ClientScope rootScope)
        {
            rootScope = new ClientScope("Root");
            var root = new ServiceContainer();
            root.Register<RootMarker, RootMarker>();
            root.Seal();                                  // 根域先密封：会话子域只消费根常驻服务
            return root;
        }

        private static AccountSessionFactory NewFactory(ClientScope rootScope, ServiceContainer rootServices,
            Func<bool, IClientTransport> transportFactory = null)
            => new AccountSessionFactory(rootScope, rootServices, transportFactory ?? (_ => new FakeClientTransport()));

        [Fact]
        public void 创建即入域_连接发Join_BattleClient解析同引用()
        {
            ServiceContainer root = NewRoot(out ClientScope rootScope);
            var transport = new FakeClientTransport();
            AccountSessionFactory factory = NewFactory(rootScope, root, _ => transport);

            AccountSession session = factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false);

            Assert.Equal(1, transport.ConnectCalls);                    // 连接已发起（BattleClient 构造即 Connect）
            Assert.True(session.Battle.Connected);
            Assert.Same(session.Battle, session.Services.Resolve<BattleClient>());   // 服务域在册同一实例
            Assert.True(session.Services.IsSealed);                     // 子域交付即密封
            Assert.False(session.Scope.IsDisposed);
            Assert.False(rootScope.IsDisposed);
            Assert.False(root.IsDisposed);                              // 根域不随会话创建受染

            session.Dispose();
        }

        [Fact]
        public void 会话Dispose_传输与会话域恰好释放一次_幂等复释放()
        {
            ServiceContainer root = NewRoot(out ClientScope rootScope);
            var transport = new FakeClientTransport();
            AccountSessionFactory factory = NewFactory(rootScope, root, _ => transport);
            AccountSession session = factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false);

            session.Dispose();
            session.Dispose();                                          // 幂等：第二次为 no-op

            Assert.Equal(1, transport.DisposeCalls);                    // 恰好一次（BattleClient.Dispose → 传输释放）
            Assert.True(session.Scope.IsDisposed);
            Assert.True(session.Services.IsDisposed);                    // 服务域随 Scope 登记面释放
            Assert.False(rootScope.IsDisposed);                          // 父作用域不随子释放
            Assert.False(root.IsDisposed);
        }

        [Fact]
        public void 工厂失败就地回滚_作用域与服务域无残留()
        {
            ServiceContainer root = NewRoot(out ClientScope rootScope);
            AccountSessionFactory factory = NewFactory(rootScope, root,
                _ => throw new InvalidOperationException("transport unavailable"));

            AccountSession session = null;
            try
            {
                session = factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false);
                Assert.True(false, "传输工厂抛出时 Create 必须失败");
            }
            catch (InvalidOperationException)
            {
            }

            Assert.Null(session);                                       // 失败不交付半成品会话
            Assert.Equal(0, rootScope.OwnedCount);                      // 回滚干净：Account 子作用域资源归零
            Assert.False(rootScope.IsDisposed);                          // 根域仍在（可重试进房）
            Assert.False(root.IsDisposed);
        }

        [Fact]
        public void 离场再进房_二次会话同构_旧实例已释放新实例独立()
        {
            ServiceContainer root = NewRoot(out ClientScope rootScope);
            var firstTransport = new FakeClientTransport();
            var secondTransport = new FakeClientTransport();
            IClientTransport current = firstTransport;
            AccountSessionFactory factory = NewFactory(rootScope, root, _ =>
            {
                IClientTransport t = current;
                current = secondTransport;
                return t;
            });

            AccountSession first = factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false);
            BattleClient firstBattle = first.Battle;
            ClientScope firstScope = first.Scope;
            first.Dispose();

            AccountSession second = factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false);

            Assert.Equal(1, firstTransport.DisposeCalls);               // 旧局传输已释放（无旧局对象残留）
            Assert.NotSame(firstBattle, second.Battle);                  // 新会话新客户端
            Assert.Equal(1, secondTransport.ConnectCalls);               // 新传输连接恰一次
            Assert.True(second.Battle.Connected);
            Assert.True(firstScope.IsDisposed);                          // 旧作用域已死
            Assert.False(second.Scope.IsDisposed);
            Assert.False(rootScope.IsDisposed);                          // 根域跨会话存活
            Assert.NotNull(root.Resolve<RootMarker>());                  // 根域仍可解析（未受染）

            second.Dispose();
        }

        [Fact]
        public void 传输工厂被调用时收到测试房意图_产品装配根裁决()
        {
            ServiceContainer root = NewRoot(out ClientScope rootScope);
            var seen = new List<bool>();
            AccountSessionFactory factory = NewFactory(rootScope, root, testRoom =>
            {
                seen.Add(testRoom);
                return new FakeClientTransport();
            });

            factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: false).Dispose();
            factory.Create(Host, Port, RoomId, Token, LiteNet.BuildHash.Value, testRoom: true).Dispose();

            Assert.Equal(new[] { false, true }, seen);                   // 委托逐次透传——工厂不吞测试开关语义
        }
    }
}
