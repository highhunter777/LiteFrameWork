using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// SharedLoadCoordinator 用例（C1-⑧：《商业级通用客户端框架总设计》§8.2 SharedLoad
    /// + 《热更与内容发布专项设计》§7 取消语义）：并发合并、失败穿透、单人取消不取消共享任务、
    /// 全员取消弃置与迟到结果不复活、引用计数末位卸载、Dispose 释放面。
    /// 全部用 UniTaskCompletionSource 定向续延（无真实时钟/线程调度依赖）。
    /// </summary>
    public sealed class SharedLoadCoordinatorTests
    {
        private sealed class Asset { public readonly string Name; public Asset(string name) => Name = name; }

        private static T Pump<T>(UniTask<T> task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            return awaiter.GetResult();
        }

        [Fact]
        public void 并发合并_同键多次获取_loader只调一次_共享同一资产()
        {
            int loads = 0;
            var pending = new UniTaskCompletionSource<Asset>();
            var shared = new Asset("shared");
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loads++; return pending.Task; },
                (key, a) => { });

            var t1 = coord.AcquireAsync("ui/a");
            var t2 = coord.AcquireAsync("ui/a");
            var t3 = coord.AcquireAsync("ui/a");
            Assert.Equal(1, loads);                            // 三次获取共享一次底层加载

            pending.TrySetResult(shared);
            var l1 = Pump(t1);
            var l2 = Pump(t2);
            var l3 = Pump(t3);

            Assert.Same(shared, l1.Asset);
            Assert.Same(shared, l2.Asset);
            Assert.Same(shared, l3.Asset);
            Assert.Equal(1, coord.LiveEntryCount);             // 三份租约、一个存活条目（合并不产生重复键）
        }

        [Fact]
        public void 完成后新获取_同步复用_引用归零后移除_再取重新加载()
        {
            int loads = 0;
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loads++; return UniTask.FromResult(new Asset("n" + loads)); },
                (key, a) => { });

            var l1 = Pump(coord.AcquireAsync("k"));            // 立即完成路径
            Assert.Equal(1, loads);
            Assert.Equal(1, coord.LiveEntryCount);
            var l2 = Pump(coord.AcquireAsync("k"));            // 完成后复用（不再加载）
            Assert.Equal(1, loads);
            Assert.Same(l1.Asset, l2.Asset);                   // 同一份资产
            Assert.Equal(1, coord.LiveEntryCount);             // 复用不增加条目

            l1.Dispose();
            l2.Dispose();                                      // 引用归零：条目移除（无保留缓存）
            Assert.Equal(0, coord.LiveEntryCount);

            var l3 = Pump(coord.AcquireAsync("k"));            // 再取 = 重新加载
            Assert.Equal(2, loads);
            Assert.NotSame(l1.Asset, l3.Asset);
            l3.Dispose();
        }

        [Fact]
        public void 等待者取消_只退出本人_底层继续_其余等待者成功()
        {
            var pending = new UniTaskCompletionSource<Asset>();
            CancellationToken loadCt = default;
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loadCt = ct; return pending.Task; },
                (key, a) => { });
            var cts1 = new CancellationTokenSource();

            var t1 = coord.AcquireAsync("k", cts1.Token);      // 第一个等待者（将取消）
            var t2 = coord.AcquireAsync("k");                  // 第二个等待者（仍需要）

            cts1.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(t1));   // 本人退出
            Assert.False(loadCt.IsCancellationRequested);      // 共享任务未被单人取消牵连（热更 §7）
            Assert.Equal(1, coord.LiveEntryCount);             // 键仍存活（t2 在等待）

            pending.TrySetResult(new Asset("ok"));
            var l2 = Pump(t2);
            Assert.Equal("ok", l2.Asset.Name);
            l2.Dispose();
        }

        [Fact]
        public void 全员取消_键弃置_底层收到尽力取消()
        {
            var pending = new UniTaskCompletionSource<Asset>();
            CancellationToken loadCt = default;
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loadCt = ct; return pending.Task; },
                (key, a) => { });
            var cts1 = new CancellationTokenSource();
            var cts2 = new CancellationTokenSource();

            var t1 = coord.AcquireAsync("k", cts1.Token);
            var t2 = coord.AcquireAsync("k", cts2.Token);

            cts1.Cancel();
            cts2.Cancel();                                     // 全员退出

            Assert.True(loadCt.IsCancellationRequested);       // 尽力取消通知底层（可取消实现方生效）
            Assert.Equal(0, coord.LiveEntryCount);             // 键已弃置（可重试——新获取 = 新加载）
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(t1));
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(t2));
        }

        [Fact]
        public void 迟到结果不复活_全员弃置后完成_结果就地卸载_不发出租约()
        {
            var pending = new UniTaskCompletionSource<Asset>();
            var unloaded = new List<Asset>();
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => pending.Task,
                (key, a) => unloaded.Add(a));
            var cts = new CancellationTokenSource();

            var t1 = coord.AcquireAsync("k", cts.Token);
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Pump(t1));

            var late = new Asset("late");
            pending.TrySetResult(late);                        // 底层迟到完成（如 YooAsset 不可中途取消）

            Assert.Same(late, unloaded[0]);                     // 迟到结果被卸载（不复活、不入缓存）
            Assert.Equal(0, coord.LiveEntryCount);
        }

        [Fact]
        public void 失败穿透_全部等待者同一异常_键移除_重试是新加载()
        {
            int loads = 0;
            var boom = new InvalidOperationException("资产损坏");
            var pending = new UniTaskCompletionSource<Asset>();
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) =>
                {
                    loads++;
                    return loads == 1 ? pending.Task : UniTask.FromResult(new Asset("retry-ok"));
                },
                (key, a) => { });

            var t1 = coord.AcquireAsync("k");
            var t2 = coord.AcquireAsync("k");

            pending.TrySetException(boom);                      // 一次失败，全部等待者收到
            var e1 = Assert.Throws<InvalidOperationException>(() => Pump(t1));
            var e2 = Assert.Throws<InvalidOperationException>(() => Pump(t2));
            Assert.Same(boom, e1);
            Assert.Same(boom, e2);
            Assert.Equal(0, coord.LiveEntryCount);              // 失败键移除

            var l3 = Pump(coord.AcquireAsync("k"));             // 重试 = 新的一次加载
            Assert.Equal(2, loads);
            Assert.Equal("retry-ok", l3.Asset.Name);
            l3.Dispose();
        }

        [Fact]
        public void 引用计数_多持有者释放_末位才卸载且恰好一次()
        {
            int loads = 0, unloads = 0;
            var asset = new Asset("multi");
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loads++; return UniTask.FromResult(asset); },
                (key, a) => unloads++);

            var l1 = Pump(coord.AcquireAsync("k"));
            var l2 = Pump(coord.AcquireAsync("k"));

            l1.Dispose();
            Assert.Equal(0, unloads);                          // 还有持有者：不卸载
            l2.Dispose();
            Assert.Equal(1, unloads);                          // 末位持有者触发恰好一次卸载

            l1.Dispose();                                      // 重复释放幂等（租约自身保证）——不追加卸载
            Assert.Equal(1, unloads);
        }

        [Fact]
        public void Dispose释放面_已加载就地卸载_在途尽力取消_迟到结果丢弃()
        {
            var inFlight = new UniTaskCompletionSource<Asset>();
            CancellationToken inFlightCt = default;
            var unloaded = new List<Asset>();
            var loaded = new Asset("loaded");
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) =>
                {
                    if (key == "done") return UniTask.FromResult(loaded);
                    inFlightCt = ct;
                    return inFlight.Task;
                },
                (key, a) => unloaded.Add(a));

            var heldLease = Pump(coord.AcquireAsync("done"));   // 已加载 + 持有
            var waiting = coord.AcquireAsync("pending");        // 在途等待

            coord.Dispose();                                    // 关闭释放面

            Assert.Single(unloaded, loaded);                    // 已加载条目就地卸载（持有租约被关闭收走）
            Assert.True(inFlightCt.IsCancellationRequested);    // 在途尽力取消
            Assert.Equal(0, coord.LiveEntryCount);

            var late = new Asset("late");
            inFlight.TrySetResult(late);                        // 在途迟到完成
            Assert.Equal(2, unloaded.Count);                    // 迟到结果丢弃 = 就地卸载
            Assert.ThrowsAny<Exception>(() => Pump(waiting));   // 等待者不拿到租约（ObjectDisposedException）
        }

        [Fact]
        public void 不同键_不合并_各自加载()
        {
            var loads = new List<string>();
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => { loads.Add(key); return UniTask.FromResult(new Asset(key)); },
                (key, a) => { });

            var la = Pump(coord.AcquireAsync("a"));
            var lb = Pump(coord.AcquireAsync("b"));

            Assert.Equal(new[] { "a", "b" }, loads.ToArray());
            Assert.Equal("a", la.Asset.Name);
            Assert.Equal("b", lb.Asset.Name);
            la.Dispose();
            lb.Dispose();
        }

        [Fact]
        public void 已弃用协调器_再获取显性失败()
        {
            var coord = new SharedLoadCoordinator<string, Asset>(
                (key, ct) => UniTask.FromResult(new Asset(key)),
                (key, a) => { });
            coord.Dispose();

            Assert.Throws<ObjectDisposedException>(() => coord.AcquireAsync("k"));
        }
    }
}
