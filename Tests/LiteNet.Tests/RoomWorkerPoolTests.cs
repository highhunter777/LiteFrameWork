using System;
using System.Collections.Concurrent;
using System.Threading;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    public sealed class RoomWorkerPoolTests
    {
        [Fact]
        public void 同一房间始终路由到同一worker_且按FNV取模()
        {
            using var pool = new RoomWorkerPool(5, 8);

            var a = pool.GetWorkerIndex("room-alpha");
            var b = pool.GetWorkerIndex("room-alpha");
            var c = pool.GetWorkerIndex("room-beta");

            Assert.InRange(a, 0, 4);
            Assert.Equal(a, b);
            Assert.InRange(c, 0, 4);
        }

        [Fact]
        public void Mailbox有界_满载时拒绝新工作()
        {
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            using var pool = new RoomWorkerPool(1, 1);
            pool.Start();

            Assert.True(pool.TryEnqueue("room", () =>
            {
                entered.Set();
                release.Wait();
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(pool.TryEnqueue("room", () => { }));
            Assert.False(pool.TryEnqueue("room", () => { }));

            release.Set();
            pool.Stop(drain: true, timeout: TimeSpan.FromSeconds(2));
            Assert.Equal(0, pool.PendingCount);
        }

        [Fact]
        public void 排空停止_执行已接受工作且停止后拒绝()
        {
            var completed = new ConcurrentQueue<int>();
            using var pool = new RoomWorkerPool(2, 16);
            pool.Start();
            for (var i = 0; i < 10; i++)
            {
                var value = i;
                Assert.True(pool.TryEnqueue("room-" + (i % 2), () => completed.Enqueue(value)));
            }

            pool.Stop(drain: true, timeout: TimeSpan.FromSeconds(2));

            Assert.Equal(10, completed.Count);
            Assert.False(pool.TryEnqueue("room-0", () => { }));
            Assert.True(pool.IsStopped);
        }

        [Fact]
        public void 丢弃停止_不执行尚未开始的工作()
        {
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var ran = 0;
            using var pool = new RoomWorkerPool(1, 4);
            pool.Start();
            Assert.True(pool.TryEnqueue("room", () =>
            {
                entered.Set();
                release.Wait();
                Interlocked.Increment(ref ran);
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(pool.TryEnqueue("room", () => Interlocked.Increment(ref ran)));
            Assert.True(pool.TryEnqueue("room", () => Interlocked.Increment(ref ran)));

            var stop = new Thread(() => pool.Stop(drain: false, timeout: TimeSpan.FromSeconds(2)));
            stop.Start();
            Thread.Sleep(20);
            release.Set();
            Assert.True(stop.Join(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, ran);
        }

        [Fact]
        public void 停止后不能启动_且启动状态保持正确()
        {
            using var pool = new RoomWorkerPool();

            pool.Stop();

            Assert.True(pool.IsStopped);
            Assert.False(pool.IsStarted);
            Assert.Throws<InvalidOperationException>(() => pool.Start());
            Assert.False(pool.IsStarted);
            Assert.False(pool.TryEnqueue("room", () => { }));
        }

        [Fact]
        public void 启动和停止并发_不会尝试等待未启动线程()
        {
            for (var i = 0; i < 32; i++)
            {
                using var go = new ManualResetEventSlim(false);
                using var pool = new RoomWorkerPool(2, 2);
                Exception startFailure = null;
                Exception stopFailure = null;
                var starter = new Thread(() =>
                {
                    go.Wait();
                    try { pool.Start(); }
                    catch (Exception ex) { startFailure = ex; }
                });
                var stopper = new Thread(() =>
                {
                    go.Wait();
                    try { pool.Stop(timeout: TimeSpan.FromSeconds(2)); }
                    catch (Exception ex) { stopFailure = ex; }
                });

                starter.Start();
                stopper.Start();
                go.Set();
                Assert.True(starter.Join(TimeSpan.FromSeconds(2)));
                Assert.True(stopper.Join(TimeSpan.FromSeconds(2)));
                Assert.True(startFailure == null || startFailure is InvalidOperationException);
                Assert.Null(stopFailure);
                Assert.True(pool.IsStopped);
                pool.Stop(timeout: TimeSpan.FromSeconds(2));
            }
        }

        [Fact]
        public void 停止超时后再次停止_仍会等待运行中的工作()
        {
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            using var retryReturned = new ManualResetEventSlim(false);
            using var pool = new RoomWorkerPool(1, 2);
            pool.Start();
            Assert.True(pool.TryEnqueue("room", () =>
            {
                entered.Set();
                release.Wait();
            }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

            Thread retry = null;
            try
            {
                Assert.Throws<TimeoutException>(() => pool.Stop(timeout: TimeSpan.FromMilliseconds(20)));
                retry = new Thread(() =>
                {
                    pool.Stop(timeout: TimeSpan.FromSeconds(2));
                    retryReturned.Set();
                });
                retry.Start();
                Assert.False(retryReturned.Wait(TimeSpan.FromMilliseconds(50)));
            }
            finally
            {
                release.Set();
                if (retry != null) Assert.True(retry.Join(TimeSpan.FromSeconds(2)));
            }
            Assert.True(retryReturned.IsSet);
        }

        [Fact]
        public void 工作回调内请求停止_不会等待自身()
        {
            using var completed = new ManualResetEventSlim(false);
            using var pool = new RoomWorkerPool();
            pool.Start();

            Assert.True(pool.TryEnqueue("room", () =>
            {
                pool.Stop(timeout: TimeSpan.FromSeconds(2));
                completed.Set();
            }));

            Assert.True(completed.Wait(TimeSpan.FromSeconds(2)));
            pool.Stop(timeout: TimeSpan.FromSeconds(2));
            Assert.True(pool.IsStopped);
        }

        [Fact]
        public void 失败监控回调抛异常_后续工作仍能执行()
        {
            var reported = 0;
            var completed = 0;
            using var pool = new RoomWorkerPool(1, 2, _ =>
            {
                Interlocked.Increment(ref reported);
                throw new InvalidOperationException("monitor failed");
            });
            pool.Start();
            Assert.True(pool.TryEnqueue("room", () => throw new InvalidOperationException("work failed")));
            Assert.True(pool.TryEnqueue("room", () => Interlocked.Increment(ref completed)));

            pool.Stop(timeout: TimeSpan.FromSeconds(2));

            Assert.Equal(1, reported);
            Assert.Equal(1, completed);
        }
    }
}
