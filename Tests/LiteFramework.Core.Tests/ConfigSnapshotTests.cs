using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// ConfigSnapshot 用例（《商业级通用客户端框架总设计》§9 配置/版本 + §4 原则 6/10：
    /// 先验证后原子提交；失败保留上一可用版本）。
    /// </summary>
    public sealed class ConfigSnapshotTests
    {
        private sealed class Snapshot { public string Tag; public int Payload; }

        [Fact]
        public void 发布_原子替换_版本单调递增()
        {
            var svc = new ConfigSnapshotService<Snapshot>(s => null);

            var v1 = svc.Publish(new Snapshot { Tag = "v1", Payload = 10 });
            Assert.Equal(1UL, v1);
            Assert.Equal("v1", svc.Current.Tag);
            Assert.Equal(10, svc.Current.Payload);

            var v2 = svc.Publish(new Snapshot { Tag = "v2", Payload = 20 });
            Assert.Equal(2UL, v2);
            Assert.Equal("v2", svc.Current.Tag);               // 原子替换：消费方读到完整新视图
        }

        [Fact]
        public void 发布_校验失败_保留已发布版本_拒绝原因上抛()
        {
            var svc = new ConfigSnapshotService<Snapshot>(s => s.Payload < 0 ? "payload 不可为负" : null);

            svc.Publish(new Snapshot { Payload = 5 });
            var versionBefore = svc.Version;

            var ex = Assert.Throws<InvalidOperationException>(() => svc.Publish(new Snapshot { Payload = -1 }));
            Assert.Contains("payload 不可为负", ex.Message);   // 拒绝原因显性上抛（不静默）
            Assert.Equal(1UL, svc.Version);                    // 版本不递增
            Assert.Equal(5, svc.Current.Payload);              // 旧版本保留（失败不降级为空）
        }

        [Fact]
        public void 发布_null候选_拒绝()
        {
            var svc = new ConfigSnapshotService<Snapshot>(s => null);
            Assert.Throws<ArgumentNullException>(() => svc.Publish(null));
        }

        [Fact]
        public void 异步发布_候选构造取消_OCE穿透且版本不变()
        {
            var svc = new ConfigSnapshotService<Snapshot>(s => null);
            var cts = new CancellationTokenSource();

            Assert.ThrowsAny<OperationCanceledException>(() =>
                ConfigSnapshotPublishExtensions.PublishAsync(svc,
                    ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return UniTask.FromResult<Snapshot>(null); },
                    cts.Token).AsTask().GetAwaiter().GetResult());

            Assert.Equal(0UL, svc.Version);                    // 发布未发生
        }

        [Fact]
        public void 异步发布_构造完成即原子发布()
        {
            var svc = new ConfigSnapshotService<Snapshot>(s => null);
            var snapshot = new Snapshot { Tag = "async", Payload = 42 };

            ulong version = ConfigSnapshotPublishExtensions.PublishAsync(svc,
                ct => UniTask.FromResult(snapshot)).AsTask().GetAwaiter().GetResult();

            Assert.Equal(1UL, version);
            Assert.Same(snapshot, svc.Current);
        }
    }
}
