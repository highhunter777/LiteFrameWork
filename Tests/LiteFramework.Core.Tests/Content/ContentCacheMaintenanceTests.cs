using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-5c 恢复动作「清缓存」（《客户端依赖注入专项设计》§6.2 / 《热更与内容发布专项设计》§8）：
    /// 受控内容缓存清理 = 先关内容运行时 → 删候选根（下载产物）→ 激活记录回退 builtin；
    /// 内置包根不动、幂等、取消不产生副作用。真 <see cref="FileSystem"/> + 临时根，
    /// 内容服务/激活 IO 用替身（不启 YooAsset、不落生产盘）。
    /// </summary>
    public sealed class ContentCacheMaintenanceTests : IDisposable
    {
        private readonly string _root;
        private readonly FileSystem _files;

        public ContentCacheMaintenanceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "litegame-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _files = new FileSystem(new FakePathProvider(_root), new FakeJsonSerializer());
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
            catch (Exception) { /* 测试清理失败不影响结论 */ }
        }

        [Fact]
        public async Task 清缓存_删候选产物_记录回退builtin_内置不动()
        {
            _files.WriteAllText("content/candidate/config/tbconfig.bytes", "cfg");
            _files.WriteAllText("content/candidate/bundle/yoo/version.txt", "v");
            _files.WriteAllText("builtin/yoo/version.txt", "内置包根");     // 内置包根（StreamingAssets 形态）不在删除范围

            var io = new MemoryIO { Preloaded = Confirmed("release-2", 5UL) };
            var store = new ActivationTransactionStore(io);
            var content = new FakeContent();
            var maintenance = new ContentCacheMaintenance(content, _files, store);

            ContentCacheClearResult result = await maintenance.ClearAsync();

            Assert.True(result.Cleared);
            Assert.Equal(2, result.DeletedFiles);
            Assert.Equal(4, result.DeletedBytes);                          // "cfg"(3) + "v"(1)
            Assert.True(result.RolledBackToBuiltin);
            Assert.False(_files.DirectoryExists(ReleaseLayout.CandidateRootRelative));
            Assert.True(_files.Exists("builtin/yoo/version.txt"));         // 内置包根不动
            Assert.Equal(1, content.ShutdownCount);                        // 先关运行时再删
            Assert.Equal(new ContentGeneration("builtin", 0UL), store.ActiveGeneration);
            Assert.Equal(ContentCacheMaintenance.RollbackReason, store.Current.LastFailure);
        }

        [Fact]
        public async Task 清缓存_空目录_幂等_仍回退记录()
        {
            var store = new ActivationTransactionStore(new MemoryIO { Preloaded = Confirmed("release-3", 1UL) });
            var maintenance = new ContentCacheMaintenance(new FakeContent(), _files, store);

            ContentCacheClearResult first = await maintenance.ClearAsync();
            ContentCacheClearResult second = await maintenance.ClearAsync();

            Assert.False(first.Cleared);
            Assert.Equal(0, first.DeletedFiles);
            Assert.True(first.RolledBackToBuiltin);
            Assert.False(second.Cleared);
            Assert.True(second.RolledBackToBuiltin);
            Assert.Equal(new ContentGeneration("builtin", 0UL), store.ActiveGeneration);
        }

        [Fact]
        public async Task 清缓存_取消_不关运行时也不删文件()
        {
            _files.WriteAllText("content/candidate/config/a.bytes", "a");
            var content = new FakeContent();
            var store = new ActivationTransactionStore(new MemoryIO { Preloaded = Confirmed("release-4", 2UL) });
            var maintenance = new ContentCacheMaintenance(content, _files, store);

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await maintenance.ClearAsync(new CancellationToken(canceled: true)));

            Assert.Equal(0, content.ShutdownCount);
            Assert.True(_files.Exists("content/candidate/config/a.bytes"));
            Assert.Equal("release-4", store.Current.ConfirmedReleaseId);
        }

        private static ActivationRecord Confirmed(string releaseId, ulong version)
            => new ActivationRecord { ConfirmedReleaseId = releaseId, ConfirmedVersion = version };

        private sealed class FakeContent : IContentService
        {
            public int ShutdownCount;

            public UniTask InitializeAsync(CancellationToken ct = default) => UniTask.CompletedTask;

            public UniTask<AssetLease<T>> AcquireAsync<T>(string location, ContentGeneration generation = default,
                CancellationToken ct = default) where T : class
                => throw new NotSupportedException("用例不加载资源");

            public IReadOnlyList<string> ListAssetPathsByTag(string tag) => Array.Empty<string>();

            public UniTask ShutdownAsync(CancellationToken ct = default)
            {
                ShutdownCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class MemoryIO : IActivationRecordIO
        {
            public ActivationRecord Preloaded;
            public ActivationRecord TryLoad() => Preloaded;
            public void Save(ActivationRecord record) => Preloaded = record;
        }

        private sealed class FakePathProvider : IPathProvider
        {
            private readonly string _root;
            public FakePathProvider(string root) => _root = root;
            public string RootPath => _root;
        }

        private sealed class FakeJsonSerializer : IJsonSerializer
        {
            public string Serialize<T>(T value) => "{}";
            public T Deserialize<T>(string json) => default;
        }
    }
}
