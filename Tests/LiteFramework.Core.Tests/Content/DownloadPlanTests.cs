using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 下载计划与失败分类（《热更与内容发布专项设计》§7"多源切换/并发重试"、
    /// §12 错误类别表"暂态网络/CDN：有界重试/切源"）。
    /// </summary>
    public sealed class DownloadPlanTests
    {
        private static ReleaseFileEntry File(string path, long len = 10)
            => new ReleaseFileEntry { Path = path, Length = len, Sha256 = new string('a', 64) };

        private static ReleaseManifest Manifest(int fileCount = 2)
        {
            var m = new ReleaseManifest { ReleaseId = "rel-1", Files = new List<ReleaseFileEntry>() };
            for (int i = 0; i < fileCount; i++) m.Files.Add(File($"f{i}.bin", 10));
            return m;
        }

        private static DownloadSource[] TwoSources() => new[]
        {
            new DownloadSource("cdn-a", "https://a.example/", priority: 0),
            new DownloadSource("cdn-b", "https://b.example/", priority: 1),
        };

        [Fact]
        public void 首试_选到最高优先级源()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources());

            Assert.True(plan.TrySelect("f0.bin", out DownloadPlanEntry e, out _));
            Assert.Equal("cdn-a", e.Source.Id);
            Assert.Equal(1, e.Attempt);
        }

        [Fact]
        public void 暂态失败_可换源重试()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources(), new DownloadBudget { MaxAttemptsPerFile = 3 });

            Assert.True(plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.TransientNetwork)));
            Assert.True(plan.TrySelect("f0.bin", out DownloadPlanEntry e, out _));
            Assert.Equal("cdn-b", e.Source.Id);          // 换到备用源
        }

        [Fact]
        public void 多源轮转_每个源都被轮到()
        {
            var plan = new DownloadPlan(Manifest(), new[]
            {
                new DownloadSource("s1", "https://1/", priority: 0),
                new DownloadSource("s2", "https://2/", priority: 1),
                new DownloadSource("s3", "https://3/", priority: 2),
            }, new DownloadBudget { MaxAttemptsPerFile = 3 });

            var used = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Assert.True(plan.TrySelect("f0.bin", out DownloadPlanEntry e, out _));
                used.Add(e.Source.Id);
                plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.TransientNetwork));
            }

            Assert.Equal(new[] { "s1", "s2", "s3" }, used);
            Assert.False(plan.TrySelect("f0.bin", out _, out _));   // 源轮完 + 达上限
        }

        [Fact]
        public void 确定性失败_不再给条目()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources());

            // 摘要不符是确定性失败——重试无效（§12"不通过反复重试或忽略验证绕过"）
            Assert.False(plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.DigestMismatch)));
        }

        [Fact]
        public void 达尝试上限_拒绝()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources(),
                new DownloadBudget { MaxAttemptsPerFile = 2 });

            plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.TransientNetwork));
            plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.TransientNetwork));

            Assert.False(plan.TrySelect("f0.bin", out _, out DownloadFailureInfo f));
            Assert.Contains("尝试上限", f.Detail);
        }

        [Fact]
        public void 清单外路径_拒绝()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources());

            Assert.False(plan.TrySelect("not-in-manifest.bin", out _, out DownloadFailureInfo f));
            Assert.Equal(DownloadFailureKind.FileMissing, f.Kind);
        }

        [Fact]
        public void 路径大小写不敏感_匹配同一文件()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources());

            Assert.True(plan.TrySelect("F0.BIN", out DownloadPlanEntry e, out _));
            Assert.Equal("f0.bin", e.File.Path);
        }

        [Fact]
        public void 无可用来源_构造即拒绝()
        {
            Assert.Throws<ArgumentException>(() => new DownloadPlan(Manifest(), Array.Empty<DownloadSource>()));
            Assert.Throws<ArgumentException>(() => new DownloadPlan(Manifest(), new DownloadSource[] { null }));
        }

        [Fact]
        public void 空清单_构造即拒绝()
        {
            var empty = new ReleaseManifest { ReleaseId = "r", Files = new List<ReleaseFileEntry>() };
            Assert.Throws<ArgumentException>(() => new DownloadPlan(empty, TwoSources()));
        }

        [Fact]
        public void 暂时失败后成功_AllowedToDownload()
        {
            var plan = new DownloadPlan(Manifest(), TwoSources());
            plan.RecordAttempt("f0.bin", new DownloadFailureInfo(DownloadFailureKind.TransientNetwork));

            Assert.False(plan.AllDownloaded(new List<string>()));
            Assert.False(plan.AllDownloaded(new List<string> { "f0.bin" }));
            Assert.True(plan.AllDownloaded(new List<string> { "f0.bin", "f1.bin" }));
        }

        [Fact]
        public void 总量与退避_可计算()
        {
            var plan = new DownloadPlan(Manifest(3), TwoSources(), new DownloadBudget { BackoffBaseMs = 100, BackoffCapMs = 250 });

            Assert.Equal(30, plan.TotalBytes);
            Assert.Equal(0, plan.BackoffMs(0));
            Assert.Equal(100, plan.BackoffMs(1));
            Assert.Equal(200, plan.BackoffMs(2));
            Assert.Equal(250, plan.BackoffMs(3));       // 受上限约束
        }

        [Fact]
        public void 来源按优先级稳定排序()
        {
            var plan = new DownloadPlan(Manifest(), new[]
            {
                new DownloadSource("low", "https://low/", priority: 5),
                new DownloadSource("high", "https://high/", priority: 1),
            });

            Assert.True(plan.TrySelect("f0.bin", out DownloadPlanEntry e, out _));
            Assert.Equal("high", e.Source.Id);
        }

        [Fact]
        public void 失败分类_暂态判定()
        {
            Assert.True(new DownloadFailureInfo(DownloadFailureKind.TransientNetwork).IsTransient);
            Assert.True(new DownloadFailureInfo(DownloadFailureKind.SourceUnavailable).IsTransient);

            Assert.False(new DownloadFailureInfo(DownloadFailureKind.DigestMismatch).IsTransient);
            Assert.False(new DownloadFailureInfo(DownloadFailureKind.LengthMismatch).IsTransient);
            Assert.False(new DownloadFailureInfo(DownloadFailureKind.FileMissing).IsTransient);
            Assert.False(new DownloadFailureInfo(DownloadFailureKind.InsufficientSpace).IsTransient);
            Assert.False(new DownloadFailureInfo(DownloadFailureKind.UnexpectedFile).IsTransient);
            Assert.False(new DownloadFailureInfo(DownloadFailureKind.ReadError).IsTransient);
        }
    }
}
