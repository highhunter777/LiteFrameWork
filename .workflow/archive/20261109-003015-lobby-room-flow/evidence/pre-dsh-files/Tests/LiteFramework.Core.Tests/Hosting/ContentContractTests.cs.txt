using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// IContentService/AssetLease 契约用例（《商业级通用客户端框架总设计》§8.1/§8.2 + C1-⑤ 接缝批）：
    /// 租约对称释放、引用计数、失败注入、Shutdown 语义——fake 实现与未来 YooAsset 适配层共用同一契约。
    /// </summary>
    public sealed class ContentContractTests
    {
        private sealed class FakeAsset { public string Name; }

        private static FakeContentService Service()
        {
            var svc = new FakeContentService();
            svc.InitializeAsync().GetAwaiter().GetResult();
            return svc;
        }

        [Fact]
        public void 租约_Acquire成功_对称释放_重复释放幂等()
        {
            var svc = Service();
            svc.Register("ui/icon", new FakeAsset { Name = "icon-1" });

            var lease = svc.AcquireAsync<FakeAsset>("ui/icon").GetAwaiter().GetResult();
            Assert.Equal("ui/icon", lease.Key);
            Assert.Equal("icon-1", lease.Asset.Name);
            Assert.False(lease.IsReleased);
            Assert.Equal(1, svc.LiveLeaseCount);

            lease.Dispose();                                     // 释放
            Assert.True(lease.IsReleased);
            Assert.Equal(0, svc.LiveLeaseCount);

            lease.Dispose();                                     // 重复释放：幂等不抛
            Assert.Equal(0, svc.LiveLeaseCount);
        }

        [Fact]
        public void 租约_共享引用计数_最后使用者释放后归零()
        {
            var svc = Service();
            svc.Register("prefab/weapon", new FakeAsset { Name = "weapon-prefab" });

            var lease1 = svc.AcquireAsync<FakeAsset>("prefab/weapon").GetAwaiter().GetResult();
            var lease2 = svc.AcquireAsync<FakeAsset>("prefab/weapon").GetAwaiter().GetResult();
            Assert.Equal(2, svc.LiveLeaseCount);                 // 两个使用者各持一份租约

            lease1.Dispose();
            Assert.Equal(1, svc.LiveLeaseCount);                 // 释放一个使用者：还有一个引用存活

            lease2.Dispose();
            Assert.Equal(0, svc.LiveLeaseCount);                 // 最后一个使用者释放：引用归零（服务方可卸载）
        }

        [Fact]
        public void 失败注入_命中location抛注入异常_未命中正常返回()
        {
            var svc = Service();
            svc.Register("ui/ok", new FakeAsset());
            svc.InjectFailure("ui/broken", new InvalidOperationException("资产损坏"));

            Assert.Throws<InvalidOperationException>(
                () => svc.AcquireAsync<FakeAsset>("ui/broken").GetAwaiter().GetResult());
            Assert.Equal(0, svc.LiveLeaseCount);                 // 失败不产生租约

            var ok = svc.AcquireAsync<FakeAsset>("ui/ok").GetAwaiter().GetResult();   // 未命中路径正常
            Assert.Equal("ui/ok", ok.Key);
        }

        [Fact]
        public void 未初始化与未登记location_显性失败()
        {
            var svc = new FakeContentService();                  // 未 InitializeAsync
            Assert.Throws<InvalidOperationException>(
                () => svc.AcquireAsync<FakeAsset>("any").GetAwaiter().GetResult());

            var ready = Service();
            Assert.Throws<KeyNotFoundException>(
                () => ready.AcquireAsync<FakeAsset>("未登记").GetAwaiter().GetResult());
        }

        [Fact]
        public void Shutdown_释放全部剩余租约_幂等()
        {
            var svc = Service();
            svc.Register("a", new FakeAsset());
            svc.Register("b", new FakeAsset());
            svc.Register("c", new FakeAsset());

            var l1 = svc.AcquireAsync<FakeAsset>("a").GetAwaiter().GetResult();
            var l2 = svc.AcquireAsync<FakeAsset>("b").GetAwaiter().GetResult();
            var l3 = svc.AcquireAsync<FakeAsset>("c").GetAwaiter().GetResult();
            Assert.Equal(3, svc.LiveLeaseCount);

            Pump(svc.ShutdownAsync());
            Assert.True(svc.ShutdownCompleted);
            Assert.Equal(0, svc.LiveLeaseCount);                 // 全部剩余租约被 Shutdown 释放

            l1.Dispose(); l2.Dispose(); l3.Dispose();            // 释放后消费方再 Dispose：幂等
            Assert.Equal(0, svc.LiveLeaseCount);
        }

        [Fact]
        public void AcquireAsync_失败注入取消令牌_以OCE穿透()
        {
            var svc = Service();
            svc.Register("slow", new FakeAsset());
            svc.InjectFailure("slow", new OperationCanceledException("加载被取消"));

            var cts = new CancellationTokenSource();
            Assert.ThrowsAny<OperationCanceledException>(
                () => svc.AcquireAsync<FakeAsset>("slow", default, cts.Token).AsTask().GetAwaiter().GetResult());
        }

        [Fact]
        public void 代次隔离_同location不同代_不共享登记与引用计数()
        {
            var svc = Service();
            var genA = ContentGeneration.Default;                       // builtin#0（默认代）
            var genB = new ContentGeneration("release-2", 3UL);        // 候选/新代
            var assetA = new FakeAsset { Name = "builtin-icon" };
            var assetB = new FakeAsset { Name = "new-icon" };
            svc.Register("ui/icon", assetA);                            // 默认代登记
            svc.Register("ui/icon", assetB, genB);                      // 新代登记（同 location 不同资源）

            var la = svc.AcquireAsync<FakeAsset>("ui/icon").GetAwaiter().GetResult();
            var lb = svc.AcquireAsync<FakeAsset>("ui/icon", genB).GetAwaiter().GetResult();

            Assert.Same(assetA, la.Asset);                              // 各取各代（§8.2 加载键含代次）
            Assert.Same(assetB, lb.Asset);
            Assert.Equal(2, svc.LiveLeaseCount);                        // 两代各自计数

            la.Dispose();
            Assert.Equal(1, svc.LiveLeaseCount);                        // 释放默认代不影响新代
            lb.Dispose();
            Assert.Equal(0, svc.LiveLeaseCount);
        }

        [Fact]
        public void 代次身份_默认代与builtin零代一致_不同值不等()
        {
            Assert.Equal(new ContentGeneration("builtin", 0UL), ContentGeneration.Default);
            Assert.NotEqual(new ContentGeneration("builtin", 1UL), ContentGeneration.Default);
            Assert.NotEqual(new ContentGeneration("other", 0UL), ContentGeneration.Default);
        }

        [Fact]
        public void 代次隔离_失败注入按代命中_不影响他代同location()
        {
            var svc = Service();
            var genB = new ContentGeneration("release-2", 1UL);
            svc.Register("ui/icon", new FakeAsset { Name = "builtin-icon" });
            svc.InjectFailure("ui/icon", new InvalidOperationException("新代资产损坏"), genB);

            Assert.Throws<InvalidOperationException>(
                () => svc.AcquireAsync<FakeAsset>("ui/icon", genB).GetAwaiter().GetResult());   // 新代失败

            var ok = svc.AcquireAsync<FakeAsset>("ui/icon").GetAwaiter().GetResult();            // 默认代不受牵连
            Assert.Equal("builtin-icon", ok.Asset.Name);
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }
    }
}
