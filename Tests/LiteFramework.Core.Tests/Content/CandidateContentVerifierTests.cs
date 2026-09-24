using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 候选内容校验（《热更与内容发布专项设计》§7 逐文件校验 / §5 原始字节摘要）。
    ///
    /// **本类覆盖的是信任链的执行侧**：设计 §7 要求"文件通过完整性校验后才进入资源/Lua 解析与执行"，
    /// 而 <see cref="ReleaseManifestValidator"/> 只校验描述的字段形态、从不读字节。
    /// </summary>
    public sealed class CandidateContentVerifierTests
    {
        private static ReleaseManifest ManifestWith(params ReleaseFileEntry[] files)
        {
            return new ReleaseManifest
            {
                ReleaseId = "rel-1",
                Files = new List<ReleaseFileEntry>(files),
            };
        }

        private static ReleaseFileEntry Entry(string path) => new ReleaseFileEntry { Path = path, Length = 0 };

        [Fact]
        public void 正确内容_校验通过()
        {
            var e1 = Entry("a.bin");
            var e2 = Entry("dir/b.bin");
            var source = new FakeCandidateFileSource()
                .AddMatching(e1, new byte[] { 1, 2, 3 })
                .AddMatching(e2, new byte[] { 4, 5, 6, 7 });

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e1, e2), source);

            Assert.True(r.Passed, r.Failure.ToString());
            Assert.Equal(2, r.VerifiedFiles);
            Assert.Equal(7, r.VerifiedBytes);
        }

        [Fact]
        public void 文件缺失_被判FileMissing()
        {
            var e1 = Entry("a.bin");
            var e2 = Entry("missing.bin");
            var source = new FakeCandidateFileSource().AddMatching(e1, new byte[] { 1 });
            // missing.bin 根本不写入

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e1, e2), source);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.FileMissing, r.Failure.Kind);
            Assert.Equal("missing.bin", r.Failure.Path);
            Assert.False(r.Failure.IsTransient);   // 缺失是确定性失败，重试无效
        }

        [Fact]
        public void 长度不符_被判LengthMismatch_且不白算摘要()
        {
            var e = Entry("a.bin");
            var source = new FakeCandidateFileSource().AddMatching(e, new byte[] { 1, 2, 3 });
            e.Length = 99;                          // 清单声称 99，实际 3

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e), source);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.LengthMismatch, r.Failure.Kind);
            Assert.Equal("99", r.Failure.Expected);
            Assert.Equal("3", r.Failure.Actual);
        }

        [Fact]
        public void 内容被篡改_摘要复算检出()
        {
            var e = Entry("a.bin");
            var source = new FakeCandidateFileSource().AddMatching(e, new byte[] { 1, 2, 3 });

            // 篡改落盘内容，但保持长度不变——只靠长度检查发现不了
            source.Add("a.bin", new byte[] { 1, 2, 4 });

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e), source);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.DigestMismatch, r.Failure.Kind);
            Assert.NotEqual(r.Failure.Expected, r.Failure.Actual);
        }

        [Fact]
        public void 读取失败_被判ReadError()
        {
            var e = Entry("a.bin");
            var source = new FakeCandidateFileSource().AddMatching(e, new byte[] { 1, 2, 3 });
            source.FailReads = true;

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e), source);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.ReadError, r.Failure.Kind);
        }

        [Fact]
        public void 清单外文件_被判UnexpectedFile()
        {
            var e = Entry("a.bin");
            var source = new FakeCandidateFileSource().AddMatching(e, new byte[] { 1 });
            source.DeclaredPaths = new[] { "a.bin", "sneaked.bin" };   // 多出一个未声明文件

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e), source, source.DeclaredPaths);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.UnexpectedFile, r.Failure.Kind);
            Assert.Equal("sneaked.bin", r.Failure.Path);
        }

        [Fact]
        public void 不支持目录枚举_仍执行摘要复算()
        {
            var e = Entry("a.bin");
            var source = new FakeCandidateFileSource().AddMatching(e, new byte[] { 1, 2 });
            source.Add("not-declared.bin", new byte[] { 9 });

            // declaredPaths = null：调用方不支持枚举——杂散检查跳过，但摘要仍必须复算
            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e), source, null);

            Assert.True(r.Passed, r.Failure.ToString());
            Assert.Single(source.Opened);           // 只打开了清单内的文件
        }

        [Fact]
        public void 取消_在循环边界生效()
        {
            var e1 = Entry("a.bin");
            var e2 = Entry("b.bin");
            var source = new FakeCandidateFileSource()
                .AddMatching(e1, new byte[] { 1 })
                .AddMatching(e2, new byte[] { 2 });

            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            CandidateVerifyResult r = CandidateContentVerifier.Verify(ManifestWith(e1, e2), source, null, cts.Token);

            Assert.False(r.Passed);
            Assert.Equal(DownloadFailureKind.Canceled, r.Failure.Kind);
        }

        [Fact]
        public void 空清单或空端口_确定性拒绝()
        {
            Assert.False(CandidateContentVerifier.Verify(null, new FakeCandidateFileSource()).Passed);
            Assert.False(CandidateContentVerifier.Verify(ManifestWith(), null).Passed);
        }

        [Fact]
        public void 路径大小写歧义_按平台归一化检出重复()
        {
            // Windows 文件系统不区分大小写：清单同时声明 a/b 与 A/B 会让一份候选覆盖另一份
            var manifest = ManifestWith(Entry("dir/x.bin"), Entry("DIR/X.bin"));

            Assert.True(CandidateContentVerifier.TryFindDuplicatePath(manifest, out string dup));
            Assert.NotNull(dup);
        }

        [Fact]
        public void 无重复路径_返回false()
        {
            var manifest = ManifestWith(Entry("a.bin"), Entry("dir/b.bin"));
            Assert.False(CandidateContentVerifier.TryFindDuplicatePath(manifest, out _));
        }

        [Fact]
        public void 反斜杠路径_与正斜杠同口径比较()
        {
            var manifest = ManifestWith(Entry("dir/x.bin"), Entry("dir\\X.bin"));
            Assert.True(CandidateContentVerifier.TryFindDuplicatePath(manifest, out _));
        }
    }

    /// <summary>
    /// 空间预检（《热更与内容发布专项设计》§7"空间预检：计入候选、临时/解压峰值、保留版本及余量"）。
    /// </summary>
    public sealed class SpacePrecheckTests
    {
        private static SpaceCheckRequest Req(long candidate, long peak = 0, long retained = 0, long margin = 0)
            => new SpaceCheckRequest
            {
                CandidateBytes = candidate,
                DecompressPeakBytes = peak,
                RetainedVersionBytes = retained,
                SafetyMarginBytes = margin,
            };

        [Fact]
        public void 空间充足_通过且回报所需总量()
        {
            SpaceCheckResult r = SpacePrecheck.Evaluate(Req(100, 50, 200, 10), availableBytes: 1000);

            Assert.True(r.Passed);
            Assert.Equal(360, r.RequiredBytes);      // 100+50+200+10，四项都要计入
        }

        [Fact]
        public void 正好相等_通过()
        {
            Assert.True(SpacePrecheck.Evaluate(Req(100), 100).Passed);
        }

        [Fact]
        public void 差一字节_拒绝并报缺口()
        {
            SpaceCheckResult r = SpacePrecheck.Evaluate(Req(100), availableBytes: 99);

            Assert.False(r.Passed);
            Assert.Contains("缺口 1", r.Detail);
        }

        [Fact]
        public void 可用空间不可知_按不足拒绝()
        {
            // -1 = 平台不提供配额。不可预检不能被当作"空间充足"——那会让更新在写入中途失败。
            SpaceCheckResult r = SpacePrecheck.Evaluate(Req(100), availableBytes: -1);

            Assert.False(r.Passed);
            Assert.Contains("不可知", r.Detail);
        }

        [Fact]
        public void 分项累加溢出_按不足拒绝而非回绕()
        {
            // 回绕会让"空间不够"变成"空间充足"——安全缺陷，必须显式判定
            SpaceCheckResult r = SpacePrecheck.Evaluate(
                Req(long.MaxValue, long.MaxValue), availableBytes: long.MaxValue);

            Assert.False(r.Passed);
        }

        [Fact]
        public void 负分项_按不足拒绝()
        {
            Assert.False(SpacePrecheck.Evaluate(Req(-5), long.MaxValue).Passed);
        }

        [Fact]
        public void 空请求_拒绝()
        {
            Assert.False(SpacePrecheck.Evaluate(null, long.MaxValue).Passed);
        }
    }
}
