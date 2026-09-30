using System;
using System.Threading;
using LiteNet.Protocol;
using LiteNet.Transport;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// R2 第一批宿主接线：只验 Worker Pool 装配与停止顺序。
    /// Transport 回调仍是同步直投，真正的 Control/Input/Outbound 路由归后续批次。
    /// </summary>
    public sealed class ServerHostWorkerPoolTests
    {
        [Fact]
        public void 显式注入WorkerPool_构造后启动_同步Transport行为保持不变()
        {
            var pool = new RoomWorkerPool(1, 4);
            var transport = new FakeRoomTransport();
            using (var host = new ServerHost(transport,
                new RoomConfig { Port = 40101, RoomId = "WorkerRoom", ExpectedPlayers = 2 },
                workerPool: pool))
            {
                Assert.Same(pool, host.WorkerPool);
                Assert.True(host.WorkerPoolStarted);
                Assert.False(host.WorkerPoolStopped);
                Assert.Equal(0, host.WorkerPoolPending);

                // 第一批只接生命周期：Join 仍在当前 Transport 回调线程同步完成。
                transport.RaiseConnected(1);
                transport.RaiseData(1, PacketCodec.Encode(PacketType.Join,
                    new LiteNet.Proto.JoinRequest
                    {
                        RoomId = "WorkerRoom",
                        Token = "token",
                        BuildHash = ServerHost.ServerBuildHash,
                    }));
                Assert.NotNull(transport.LastJoinAck(1));
            }

            Assert.True(pool.IsStopped);
            Assert.True(transport.Disposed);
        }

        [Fact]
        public void 宿主Dispose_先停止Worker再释放Transport()
        {
            var pool = new RoomWorkerPool(1, 4);
            var transport = new DisposeOrderTransport(() => pool.IsStopped);
            var host = new ServerHost(transport,
                new RoomConfig { Port = 40102, RoomId = "WorkerRoom", ExpectedPlayers = 2 },
                workerPool: pool);

            host.Dispose();

            Assert.True(pool.IsStopped);
            Assert.True(transport.DisposeSawWorkerStopped);
            Assert.True(transport.Disposed);
        }

        [Fact]
        public void 宿主Dispose_等待已接受工作排空后才释放Transport()
        {
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var completed = 0;
            var pool = new RoomWorkerPool(1, 4);
            var transport = new DisposeOrderTransport(() => Volatile.Read(ref completed) == 2);
            var host = new ServerHost(transport,
                new RoomConfig { Port = 40105, RoomId = "WorkerRoom", ExpectedPlayers = 2 },
                workerPool: pool);

            Assert.True(pool.TryEnqueue("WorkerRoom", () =>
            {
                entered.Set();
                release.Wait();
                Interlocked.Increment(ref completed);
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(pool.TryEnqueue("WorkerRoom", () => Interlocked.Increment(ref completed)));

            var disposer = new Thread(host.Dispose);
            disposer.Start();
            try
            {
                Assert.False(disposer.Join(TimeSpan.FromMilliseconds(50)));
                Assert.False(transport.Disposed);
            }
            finally
            {
                release.Set();
                Assert.True(disposer.Join(TimeSpan.FromSeconds(2)));
            }

            Assert.Equal(2, completed);
            Assert.True(transport.DisposeSawWorkerStopped);
            Assert.True(transport.Disposed);
        }

        [Fact]
        public void 配置驱动宿主_按Worker配置自动装配并启动固定池()
        {
            const string json = @"{
                ""port"": 40104, ""max_rooms"": 2, ""worker_count"": 2, ""mailbox_capacity"": 3,
                ""audience"": """", ""default_template"": ""two"",
                ""rooms"": { ""two"": { ""expected_players"": 2 } }
            }";
            var config = RoomServerConfig.Parse(json);
            var transport = new FakeRoomTransport();

            using (var host = new ServerHost(transport, null, null, null, config))
            {
                Assert.NotNull(host.WorkerPool);
                Assert.Equal(2, host.WorkerPool.WorkerCount);
                Assert.Equal(3, host.WorkerPool.MailboxCapacity);
                Assert.True(host.WorkerPoolStarted);
            }

            Assert.True(transport.Disposed);
        }

        [Fact]
        public void Transport启动失败_构造回收已启动Worker和Transport()
        {
            var pool = new RoomWorkerPool(1, 4);
            var transport = new DisposeOrderTransport(() => pool.IsStopped) { ThrowOnStart = true };

            Assert.Throws<InvalidOperationException>(() => new ServerHost(transport,
                new RoomConfig { Port = 40106, RoomId = "WorkerRoom", ExpectedPlayers = 2 },
                workerPool: pool));

            Assert.True(pool.IsStopped);
            Assert.True(transport.DisposeSawWorkerStopped);
            Assert.True(transport.Disposed);
        }

        [Fact]
        public void 未装配配置且未注入WorkerPool_保持历史单循环形态()
        {
            var transport = new FakeRoomTransport();
            using var host = new ServerHost(transport,
                new RoomConfig { Port = 40103, RoomId = "SingleRoom", ExpectedPlayers = 2 });

            Assert.Null(host.WorkerPool);
            Assert.False(host.WorkerPoolStarted);
            Assert.False(host.WorkerPoolStopped);
        }

        private sealed class DisposeOrderTransport : IRoomTransport
        {
            private readonly Func<bool> _workerStopped;

            public DisposeOrderTransport(Func<bool> workerStopped)
            {
                _workerStopped = workerStopped;
            }

            public bool Disposed { get; private set; }
            public bool DisposeSawWorkerStopped { get; private set; }
            public bool ThrowOnStart { get; set; }

            event Action<int, ArraySegment<byte>, bool> IRoomTransport.OnData { add { } remove { } }
            event Action<int> IRoomTransport.OnConnected { add { } remove { } }
            event Action<int> IRoomTransport.OnDisconnected { add { } remove { } }

            public void Start(int port)
            {
                if (ThrowOnStart) throw new InvalidOperationException("start failed");
            }
            public void TickIncoming() { }
            public void TickOutgoing() { }
            public void Disconnect(int connectionId) { }
            public void Broadcast(ArraySegment<byte> data, bool reliable) { }
            public void SendTo(int connectionId, ArraySegment<byte> data, bool reliable) { }
            public string GetRemoteAddress(int connectionId) => null;

            public void Dispose()
            {
                DisposeSawWorkerStopped = _workerStopped();
                Disposed = true;
            }
        }
    }
}
