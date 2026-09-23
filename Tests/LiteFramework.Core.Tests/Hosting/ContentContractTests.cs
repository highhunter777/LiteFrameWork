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
                () => svc.AcquireAsync<FakeAsset>("slow", cts.Token).AsTask().GetAwaiter().GetResult());
        }

        private static void Pump(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            awaiter.GetResult();
        }
    }
}
