using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using LiteNet.Transport;
using LiteSim;
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
        }

        /// <summary>记录首个释放调用（幂等：容器收尾的双释放是既有权责重叠，第二次不再记）。
        /// 实现 IDisposable 同真实 FileSettlementOutbox——宿主经 <c>as IDisposable</c> 接管释放。</summary>
        private sealed class RecordingOutbox : ISettlementOutbox, IDisposable
        {
            private readonly Action _onDisposed;
            private bool _disposed;

            public RecordingOutbox(Action onDisposed) { _onDisposed = onDisposed; }

            public SettlementOutboxResult Enqueue(MatchResultSummary summary) => SettlementOutboxResult.Appended;
            public bool TryMarkCompleted(string matchId) => true;
            public int Count => 0;
            public IReadOnlyList<MatchResultSummary> ListPending() => Array.Empty<MatchResultSummary>();
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
