using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 补丁编排测试夹具（L1）：内存激活记录 IO + 各编排端口替身，
    /// 让 <see cref="PatchCoordinator"/> 全链路走**定向续延**、无真实时钟/线程/网络。
    /// </summary>
    internal sealed class FakeActivationIO : IActivationRecordIO
    {
        private ActivationRecord _stored;

        /// <summary>已写盘次数（断言"每一步都持久化"）。</summary>
        public int SaveCount { get; private set; }

        /// <summary>模拟确认写盘失败（§8 表行 4"确认记录写入失败 → 不声称已确认"）。</summary>
        public bool FailNextSave;

        public ActivationRecord TryLoad() => _stored;

        public void Save(ActivationRecord record)
        {
            if (FailNextSave) { FailNextSave = false; throw new System.IO.IOException("模拟写盘失败"); }
            // 深拷贝语义：模拟"重新读回来的是另一份对象"
            _stored = new ActivationRecord
            {
                SchemaVersion = record.SchemaVersion,
                ConfirmedReleaseId = record.ConfirmedReleaseId,
                ConfirmedVersion = record.ConfirmedVersion,
                PendingReleaseId = record.PendingReleaseId,
                PendingState = record.PendingState,
                RecoveryAttempts = record.RecoveryAttempts,
                LastFailure = record.LastFailure,
                RecordSequence = record.RecordSequence,
                TransactionId = record.TransactionId,
                Integrity = record.Integrity,
            };
            SaveCount++;
        }

        /// <summary>把记录直接塞进存储（模拟"上次运行的遗留"）。</summary>
        public void Seed(ActivationRecord record) => _stored = record;
    }

    internal sealed class FakeFetcher : ICandidateFetcher
    {
        public bool Succeed = true;
        public DownloadFailureKind FailKind = DownloadFailureKind.TransientNetwork;
        public int Calls;
        public List<string> PathsToReturn = new List<string>();

        /// <summary>FetchAsync 进入时回调（测试用来观察"下载开始前"的事务状态）。</summary>
        public Action BeforeFetch;

        /// <summary>CleanupTempAsync 收到的发布身份（按序）。</summary>
        public readonly List<string> Cleaned = new List<string>();

        public UniTask<CandidateFetchResult> FetchAsync(ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default)
        {
            Calls++;
            BeforeFetch?.Invoke();
            if (!Succeed) return UniTask.FromResult(CandidateFetchResult.Fail(FailKind, detail: "夹具"));
            var paths = PathsToReturn.Count > 0
                ? PathsToReturn
                : ManifestPaths(manifest);
            return UniTask.FromResult(CandidateFetchResult.Ok(paths));
        }

        public UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default)
        {
            Cleaned.Add(releaseId);
            return UniTask.CompletedTask;
        }

        private static List<string> ManifestPaths(ReleaseManifest m)
        {
            var list = new List<string>();
            foreach (ReleaseFileEntry f in m.Files) list.Add(f.Path);
            return list;
        }
    }

    internal sealed class FakeHealthCheck : ICandidateHealthCheck
    {
        public string UnhealthyReason;          // null = 健康
        public int Calls;
        public Exception Throw;

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            Calls++;
            if (Throw != null) throw Throw;
            return UniTask.FromResult(UnhealthyReason);
        }
    }

    internal sealed class FakeActivator : IContentActivator
    {
        public int ActivateCalls;
        public int RebuildCalls;
        public string LastActivatedReleaseId;
        public ContentGeneration LastRebuiltGeneration;
        public Exception ActivateThrow;
        public Exception RebuildThrow;

        public UniTask ActivateAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            ActivateCalls++;
            if (ActivateThrow != null) throw ActivateThrow;
            LastActivatedReleaseId = candidate.ReleaseId;
            return UniTask.CompletedTask;
        }

        public UniTask RebuildConfirmedAsync(ContentGeneration confirmed, CancellationToken ct = default)
        {
            RebuildCalls++;
            if (RebuildThrow != null) throw RebuildThrow;
            LastRebuiltGeneration = confirmed;
            return UniTask.CompletedTask;
        }
    }

    internal sealed class FakeGenerationSink : IGenerationSink
    {
        public readonly List<ContentGeneration> Advised = new List<ContentGeneration>();
        public void Advise(ContentGeneration generation) => Advised.Add(generation);
    }

    /// <summary>单项健康探针替身。</summary>
    internal sealed class FakeHealthProbe : IHealthProbe
    {
        public string ProbeName;
        public string Reason;                   // null = 健康
        public Exception Throw;
        public int Calls;

        public FakeHealthProbe(string name, string reason = null)
        {
            ProbeName = name;
            Reason = reason;
        }

        public string Name => ProbeName;

        public UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            Calls++;
            if (Throw != null) throw Throw;
            return UniTask.FromResult(Reason);
        }
    }

    internal static class PatchTestFixtures
    {
        /// <summary>同步驱动 UniTask（既有 Core 测试同款：定向续延、无真实调度）。</summary>
        public static T Pump<T>(UniTask<T> task)
        {
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) { }
            return awaiter.GetResult();
        }

        /// <summary>构造一个含真实摘要与内容的候选（文件源同步写入字节）。</summary>
        public static ReleaseManifest ManifestWithFiles(
            FakeCandidateFileSource source, string releaseId, params (string path, string content)[] files)
        {
            var manifest = new ReleaseManifest { ReleaseId = releaseId, Revision = 1 };
            foreach ((string path, string content) in files)
            {
                var entry = new ReleaseFileEntry { Path = path };
                source.AddMatching(entry, System.Text.Encoding.UTF8.GetBytes(content));
                manifest.Files.Add(entry);
            }
            return manifest;
        }

        public static DownloadPlan PlanFor(ReleaseManifest manifest)
            => new DownloadPlan(manifest, new[] { new DownloadSource("test-src", "mem://test") });

        public static SpaceCheckRequest SpaceFor(ReleaseManifest manifest)
        {
            long total = 0;
            foreach (ReleaseFileEntry f in manifest.Files) total += f.Length;
            return new SpaceCheckRequest { CandidateBytes = total };
        }
    }
}
