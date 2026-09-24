using System.Collections.Generic;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 内容运行时适配（《热更与内容发布专项设计》§7"下载、候选校验与容量"）：
    /// <see cref="FileSysCandidateFileSource"/>（候选文件读取）、
    /// <see cref="LocalDirectoryCandidateFetcher"/>（本地候选清点）、
    /// <see cref="UnavailableDiskSpaceProbe"/>（空间不可知的 fail-closed）。
    ///
    /// 同时覆盖本批为适配新增的 <see cref="FileSys"/> 字节/递归 API ——
    /// LiteGame 层零 System.IO，候选文件（含二进制）必须走 FileSys 通道。
    /// </summary>
    public sealed class ContentRuntimeAdaptersEditModeTests : UnityTestBase
    {
        private const string Root = "test_candidate_root";

        [SetUp]
        protected void Init()
        {
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);
        }

        [TearDown]
        protected void Clean()
        {
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);
        }

        private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        private void WriteCandidate(string relPath, string content)
            => FileSys.WriteAllBytes(Root + "/" + relPath, Bytes(content));

        private static ReleaseManifest ManifestWith(params (string path, string content)[] files)
        {
            var m = new ReleaseManifest { ReleaseId = "rel-test", Revision = 1 };
            foreach ((string path, string content) in files)
            {
                m.Files.Add(new ReleaseFileEntry
                {
                    Path = path,
                    Length = Bytes(content).LongLength,
                    Sha256 = ContentHash.Sha256Hex(Bytes(content)),
                });
            }
            return m;
        }

        private static DownloadPlan PlanFor(ReleaseManifest m)
            => new DownloadPlan(m, new[] { new DownloadSource("local", "file://local") });

        // ---- FileSys 新增 API ----

        [Test]
        public void FileSys_字节往返_保留二进制()
        {
            // 非 UTF-8 序列字节——文本通道会损坏
            var raw = new byte[] { 0x00, 0xFF, 0x80, 0x7F, 0x0A, 0x0D };
            FileSys.WriteAllBytes(Root + "/bin.dat", raw);

            byte[] read = FileSys.ReadAllBytes(Root + "/bin.dat");
            Assert.AreEqual(raw, read);
            Assert.AreEqual(raw.LongLength, FileSys.GetFileLength(Root + "/bin.dat"));
        }

        [Test]
        public void FileSys_不存在的文件_长度负一_读取为空()
        {
            Assert.AreEqual(-1L, FileSys.GetFileLength(Root + "/nope.dat"));
            Assert.IsNull(FileSys.ReadAllBytes(Root + "/nope.dat"));
        }

        [Test]
        public void FileSys_递归枚举_看到全部层级()
        {
            WriteCandidate("a.bin", "A");
            FileSys.WriteAllBytes(Root + "/nested/deep/b.bin", Bytes("B"));

            IReadOnlyList<string> all = FileSysCandidateFileSource.ListAll(Root);
            CollectionAssert.Contains((System.Collections.ICollection)all, Root + "/a.bin");
            CollectionAssert.Contains((System.Collections.ICollection)all, Root + "/nested/deep/b.bin");
        }

        [Test]
        public void FileSys_递归枚举_目录不存在_返回空()
        {
            Assert.IsEmpty(FileSys.GetFilesRecursive(Root + "/missing"));
        }

        [Test]
        public void FileSys_删除目录_幂等()
        {
            WriteCandidate("x.bin", "X");
            Assert.IsTrue(FileSys.DirectoryExists(Root));
            FileSys.DeleteDirectory(Root);
            Assert.IsFalse(FileSys.DirectoryExists(Root));
            FileSys.DeleteDirectory(Root);                       // 再次删除不抛（幂等）
        }

        [Test]
        public void FileSys_拒绝删除根目录()
        {
            // 越界保护：空/根路径不得被当作可删目录
            Assert.Throws<System.ArgumentException>(() => FileSys.DeleteDirectory(""));
            Assert.Throws<System.ArgumentException>(() => FileSys.DeleteDirectory(null));
            Assert.Throws<System.ArgumentException>(() => FileSys.DeleteDirectory("../escape"));
        }

        // ---- 候选文件端口 ----

        [Test]
        public void 候选文件端口_读句柄报长度与字节()
        {
            WriteCandidate("a.bin", "AAA");
            var source = new FileSysCandidateFileSource();

            ICandidateFile f = source.Open(Root + "/a.bin");
            Assert.IsNotNull(f);
            Assert.AreEqual(3L, f.Length);
            Assert.AreEqual(Bytes("AAA"), f.ReadAll());
        }

        [Test]
        public void 候选文件端口_缺失返回null()
        {
            Assert.IsNull(new FileSysCandidateFileSource().Open(Root + "/missing.bin"));
            Assert.IsNull(new FileSysCandidateFileSource().Open(null));
        }

        // ---- 本地候选获取 ----

        [Test]
        public void 本地获取_清单齐备_返回落盘路径()
        {
            WriteCandidate("a.bin", "AAA");
            WriteCandidate("sub/b.bin", "BB");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"), ("sub/b.bin", "BB"));

            CandidateFetchResult r = new LocalDirectoryCandidateFetcher(Root)
                .FetchAsync(m, PlanFor(m)).GetAwaiter().GetResult();

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual(2, r.Paths.Count);
        }

        [Test]
        public void 本地获取_缺文件_按缺失失败()
        {
            WriteCandidate("a.bin", "AAA");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"), ("missing.bin", "X"));

            CandidateFetchResult r = new LocalDirectoryCandidateFetcher(Root)
                .FetchAsync(m, PlanFor(m)).GetAwaiter().GetResult();

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.FileMissing, r.Failure.Kind);
            Assert.AreEqual("missing.bin", r.Failure.Path);
        }

        [Test]
        public void 本地获取_候选根不存在_按源不可用失败()
        {
            ReleaseManifest m = ManifestWith(("a.bin", "A"));

            CandidateFetchResult r = new LocalDirectoryCandidateFetcher(Root + "/nope")
                .FetchAsync(m, PlanFor(m)).GetAwaiter().GetResult();

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.SourceUnavailable, r.Failure.Kind);
        }

        [Test]
        public void 本地获取_清单外文件_一并返回由校验器判定()
        {
            WriteCandidate("a.bin", "AAA");
            WriteCandidate("sneaked.bin", "S");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = new LocalDirectoryCandidateFetcher(Root)
                .FetchAsync(m, PlanFor(m)).GetAwaiter().GetResult();

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual(2, r.Paths.Count);                    // 获取不替校验器下结论
        }

        // ---- 空间探测：不可知 = fail-closed ----

        [Test]
        public void 空间探测_不可知_预检按不足拒绝()
        {
            var probe = new UnavailableDiskSpaceProbe();
            Assert.AreEqual(-1L, probe.GetAvailableBytes());

            SpaceCheckResult r = SpacePrecheck.Evaluate(
                new SpaceCheckRequest { CandidateBytes = 1 }, probe.GetAvailableBytes());

            Assert.IsFalse(r.Passed);                             // 不可知不得被当作"空间充足"
        }
    }
}
