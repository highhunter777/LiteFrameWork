using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using LiteNet.Transport;
using LiteSim;
using LiteSim.Tests;
using RoomServer;
using RoomServer.Application;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 宿主装配收敛批1（《服务端宿主装配收敛专项设计》）：组合根解析全图（含 Worker 执行形态装配）
    /// ＋释放序等价（host 接管释放内部序 transport → outbox；容器按创建序逆序收尾，双释放不抛）。
    /// 装配等价——宿主运行语义（准入/排空/多房间/过载）由既有宿主链用例把守，本组不重复。
    /// 批3 直迁件：五件协作对象（Ops/RateLimiter/SessionManager/ReconnectService/JoinAdmissionGate）
    /// 经容器注册——解析所得＝宿主持有同一单例；公共构造缺省自建与显式注入端口各自可验。
    /// </summary>
    public sealed class HostAssemblyTests
    {
        private const string OneRoomConfig = @"{
            ""port"": 0, ""max_rooms"": 1, ""worker_count"": 1, ""mailbox_capacity"": 64,
            ""audience"": """", ""default_template"": ""one"",
            ""rooms"": { ""one"": { ""expected_players"": 2 } }
        }";

        [Fact]
        public void ResolvesFullGraph_AndDisposeOrderMatchesLegacyUsings()
        {
            var services = new ServiceCollection();
            HostAssembly.Register(services, new HostAssembly.Inputs
            {
                Config = RoomServerConfig.Parse(OneRoomConfig),
                Audience = "",
                CombatValues = CombatValues.Default,
                Weapons = WeaponTable.Default,
                Map = TestMaps.Greybox(),
            });
            var order = new List<string>();
            var transport = new FakeRoomTransport { OnDisposed = () => order.Add("transport") };
            var outbox = new RecordingOutbox(() => order.Add("outbox"));
            services.AddSingleton<IRoomTransport>(transport);        // last-wins 覆写：端口替身/记录型 Outbox
            services.AddSingleton<ISettlementOutbox>(outbox);

            using var provider = HostAssembly.Build(services);

            // 与 Program 同序解析（专项 §3 解析序=创建序）：outbox → transport → host
            Assert.Same(outbox, provider.GetRequiredService<ISettlementOutbox>());
            Assert.Same(transport, provider.GetRequiredService<IRoomTransport>());
            var host = provider.GetRequiredService<ServerHost>();
            host.Ops.PrintEnabled = false;
            var loop = provider.GetRequiredService<ServerLoop>();

            Assert.NotNull(host);
            Assert.NotNull(loop);
            Assert.True(host.MailboxRoutingEnabled);                 // 生产装配形态：路由 + 延迟排空 + Worker 执行
            Assert.True(host.WorkerExecutionEnabled);

            provider.Dispose();

            // 释放序等价：容器按创建序逆序先释放 host——host 内部接管释放（workerPool → transport → outbox）；
            // 容器随后对两替身的收尾释放是幂等的（各只记首个释放调用，双释放不抛）。
            Assert.True(host.WorkerPoolStopped, "宿主先于替身完成释放（逆序：后创建先释放）");
            Assert.Equal(new[] { "transport", "outbox" }, order);
        }

        [Fact]
        public void Register_RejectsMissingInputs()
        {
            Assert.Throws<ArgumentNullException>(() => HostAssembly.Register(new ServiceCollection(), null));
            Assert.Throws<ArgumentException>(() => HostAssembly.Register(new ServiceCollection(), new HostAssembly.Inputs()));
            // 数值/武器表必填（技术债 #1 的"丢弃装载产物跑默认值"结构性防线）：缺一即拒
            Assert.Throws<ArgumentException>(() => HostAssembly.Register(new ServiceCollection(),
                new HostAssembly.Inputs { Config = RoomServerConfig.Parse(OneRoomConfig) }));
            Assert.Throws<ArgumentException>(() => HostAssembly.Register(new ServiceCollection(),
                new HostAssembly.Inputs
                {
                    Config = RoomServerConfig.Parse(OneRoomConfig),
                    CombatValues = CombatValues.Default,
                }));
            // 地图必填（D22："回退地图"结构性防线）：数值/武器齐而缺图同样拒装配
            Assert.Throws<ArgumentException>(() => HostAssembly.Register(new ServiceCollection(),
                new HostAssembly.Inputs
                {
                    Config = RoomServerConfig.Parse(OneRoomConfig),
                    CombatValues = CombatValues.Default,
                    Weapons = WeaponTable.Default,
                }));
        }

        [Fact]
        public void ResolvesCollaborationSingletons_HostHoldsSameInstances()
        {
            // 批3 直迁件：五件协作对象经容器注册——宿主工厂必须经容器解析注入**同一单例**，
            // 不得自建第二份（"生产路径零手建散点"的对账面）。
            var services = new ServiceCollection();
            HostAssembly.Register(services, new HostAssembly.Inputs
            {
                Config = RoomServerConfig.Parse(OneRoomConfig),
                Audience = "",
                CombatValues = CombatValues.Default,
                Weapons = WeaponTable.Default,
                Map = TestMaps.Greybox(),
            });
            var transport = new FakeRoomTransport();
            var outbox = new RecordingOutbox(() => { });
            services.AddSingleton<IRoomTransport>(transport);        // last-wins 覆写：端口替身/记录型 Outbox
            services.AddSingleton<ISettlementOutbox>(outbox);

            using var provider = HostAssembly.Build(services);
            Assert.Same(outbox, provider.GetRequiredService<ISettlementOutbox>());
            Assert.Same(transport, provider.GetRequiredService<IRoomTransport>());

            var host = provider.GetRequiredService<ServerHost>();
            host.Ops.PrintEnabled = false;

            Assert.Same(provider.GetRequiredService<Ops>(), host.Ops);
            Assert.Same(provider.GetRequiredService<RateLimiter>(), host.RateLimit);
            Assert.Same(provider.GetRequiredService<JoinAdmissionGate>(), host.Admission);
            Assert.Same(provider.GetRequiredService<SessionManager>(), host.Sessions);
            Assert.Same(provider.GetRequiredService<ReconnectService>(), host.Reconnects);

            // 会话容量公式单源（§9.2）：配置形态 = 4 × 房间上限 × 最大房间人数
            var config = provider.GetRequiredService<RoomServerConfig>();
            Assert.Equal(4 * config.MaxRooms * config.MaxExpectedPlayers, host.Sessions.Capacity);

            provider.Dispose();
        }

        [Fact]
        public void DirectNew_BuildsOwnDefaults_StayCompatible()
        {
            // 嵌入式/测试通道（批3 口径）：不传协作对象端口——宿主按单源构造公式自建，
            // 容量公式与迁移前一致；直接 new 的既有契约不变。
            var config = RoomServerConfig.Parse(OneRoomConfig);
            using var host = new ServerHost(new FakeRoomTransport(), roomServerConfig: config, mailboxRouting: true, map: TestMaps.Greybox());
            host.Ops.PrintEnabled = false;

            Assert.NotNull(host.Ops);
            Assert.NotNull(host.Admission);
            Assert.NotNull(host.Reconnects);
            Assert.Equal(4 * config.MaxRooms * config.MaxExpectedPlayers, host.Sessions.Capacity);
        }

        [Fact]
        public void DirectNew_NoMap_Rejected()
        {
            // D22：宿主参数与 RoomConfig 两处皆缺地图 → 构造即拒（无回退地图——不静默兜底）
            var config = RoomServerConfig.Parse(OneRoomConfig);
            Assert.Throws<ArgumentException>(() =>
                new ServerHost(new FakeRoomTransport(), roomServerConfig: config, mailboxRouting: true));
        }

        [Fact]
        public void InjectedCollaborators_AreAdoptedByHost()
        {
            // 装配口（批3）：显式传入的协作对象被宿主原样采用（不重建第二份）——
            // 缺省通道与注入通道同选一档，由空合并到单源构造公式决定。
            var config = RoomServerConfig.Parse(OneRoomConfig);
            var transport = new FakeRoomTransport();
            var ops = new Ops();
            var rateLimiter = ServerHost.CreateRateLimiter(config);
            var admission = ServerHost.CreateAdmissionGate(null, rateLimiter, transport, "", config);
            var sessions = new SessionManager(7);                    // 非缺省容量——证明未被重建
            var reconnects = new ReconnectService();

            using var host = new ServerHost(transport, roomServerConfig: config, mailboxRouting: true,
                ops: ops, rateLimiter: rateLimiter, admission: admission,
                sessions: sessions, reconnects: reconnects, map: TestMaps.Greybox());
            host.Ops.PrintEnabled = false;

            Assert.Same(ops, host.Ops);
            Assert.Same(rateLimiter, host.RateLimit);
            Assert.Same(admission, host.Admission);
            Assert.Same(sessions, host.Sessions);
            Assert.Same(reconnects, host.Reconnects);
            Assert.Equal(7, host.Sessions.Capacity);
        }

        /// <summary>记录首个释放调用（幂等：容器收尾的双释放是既有权责重叠，第二次不再记）。
        /// 实现 IDisposable 同真实 FileSettlementOutbox——宿主经 <c>as IDisposable</c> 接管释放。</summary>
        private sealed class RecordingOutbox : ISettlementOutbox, IDisposable
        {
            private readonly Action _onDisposed;
            private bool _disposed;

            public RecordingOutbox(Action onDisposed) { _onDisposed = onDisposed; }

            public SettlementOutboxResult Enqueue(MatchResultSummary summary, string[] seatAccountIds = null) => SettlementOutboxResult.Appended;
            public bool TryMarkCompleted(string matchId) => true;
            public bool TryAbandon(string matchId, string reason) => false;
            public int Count => 0;
            public IReadOnlyList<PendingSettlement> ListPending() => Array.Empty<PendingSettlement>();
            public void Flush() { }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDisposed();
            }
        }
    }
}
