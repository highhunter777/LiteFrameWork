using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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

        /// <summary>最近一次 <see cref="ManifestWith"/> 的候选内容（落盘用）。</summary>
        private readonly List<(string path, string content)> _candidateContents = new List<(string, string)>();

        private void WriteCandidate(string relPath, string content)
            => FileSys.WriteAllBytes(Root + "/" + relPath, Bytes(content));

        /// <summary>构造清单并把内容登记到 <see cref="_candidateContents"/>（长度/摘要与内容同源）。</summary>
        private ReleaseManifest ManifestWith(params (string path, string content)[] files)
        {
            _candidateContents.Clear();
            var m = new ReleaseManifest { ReleaseId = "rel-test", Revision = 1 };
            foreach ((string path, string content) in files)
            {
                m.Files.Add(new ReleaseFileEntry
                {
                    Path = path,
                    Length = Bytes(content).LongLength,
                    Sha256 = ContentHash.Sha256Hex(Bytes(content)),
                });
                _candidateContents.Add((path, content));
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
            CollectionAssert.Contains((System.Collections.ICollection)all, "a.bin");
            CollectionAssert.Contains((System.Collections.ICollection)all, "nested/deep/b.bin");
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
            var source = new FileSysCandidateFileSource(Root);

            ICandidateFile f = source.Open("a.bin");
            Assert.IsNotNull(f);
            Assert.AreEqual(3L, f.Length);
            Assert.AreEqual(Bytes("AAA"), f.ReadAll());
        }

        [Test]
        public void 候选文件端口_缺失返回null()
        {
            var src = new FileSysCandidateFileSource(Root);
            Assert.IsNull(src.Open("missing.bin"));
            Assert.IsNull(src.Open(null));
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

        // ---- 空间探测：真实实现 ----

        [Test]
        public void 空间探测_不可知_预检按不足拒绝()
        {
            var probe = new UnavailableDiskSpaceProbe();
            Assert.AreEqual(-1L, probe.GetAvailableBytes());

            SpaceCheckResult r = SpacePrecheck.Evaluate(
                new SpaceCheckRequest { CandidateBytes = 1 }, probe.GetAvailableBytes());

            Assert.IsFalse(r.Passed);                             // 不可知不得被当作"空间充足"
        }

        [Test]
        public void 空间探测_DriveInfo_桌面返回真实余量()
        {
            // 桌面/Editor 下应能拿到真实可用空间；移动端/不支持时返回 -1（fail-closed）
            var probe = new DriveInfoSpaceProbe(UnityEngine.Application.persistentDataPath);
            long available = probe.GetAvailableBytes();

            Assert.IsTrue(available == -1L || available > 0,
                $"应为 -1（不可知）或正字节数，实得 {available}");
        }

        [Test]
        public void 空间探测_不存在路径_按不可知处理()
        {
            var probe = new DriveInfoSpaceProbe(@"Z:\definitely\not\here\nor\there");
            Assert.AreEqual(-1L, probe.GetAvailableBytes());
        }

        [Test]
        public void 空间探测_空参数拒绝()
        {
            Assert.Throws<System.ArgumentNullException>(() => new DriveInfoSpaceProbe(null));
        }

        // ---- 签名信封解析（§6；纯解析，不代判信任） ----

        [Test]
        public void 签名信封_畸形JSON_返回空提案()
        {
            Assert.IsTrue(SignedManifestEnvelope.Parse("{ not json").IsEmpty);
            Assert.IsTrue(SignedManifestEnvelope.Parse("").IsEmpty);
            Assert.IsTrue(SignedManifestEnvelope.Parse(null).IsEmpty);
        }

        [Test]
        public void 签名信封_缺字段_返回空提案()
        {
            Assert.IsTrue(SignedManifestEnvelope.Parse("{\"manifest\":{\"ReleaseId\":\"r\"}}").IsEmpty);
            Assert.IsTrue(SignedManifestEnvelope.Parse("{\"signature\":\"AAAA\"}").IsEmpty);
        }

        [Test]
        public void 签名信封_合法信封_取出清单与签名()
        {
            const string json = "{\"manifest\":{\"ReleaseId\":\"rel-9\",\"SchemaVersion\":1," +
                                "\"Revision\":7,\"Files\":[{\"Path\":\"a.bin\",\"Length\":3," +
                                "\"Sha256\":\"" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
                                "\"}]},\"signature\":\"AQID\"}";

            CandidateOffer offer = SignedManifestEnvelope.Parse(json);

            Assert.IsFalse(offer.IsEmpty);
            Assert.AreEqual("rel-9", offer.Manifest.ReleaseId);
            Assert.AreEqual(0, offer.Manifest.Revision == 7 ? 0 : 1);   // Revision 保真
            Assert.AreEqual(1, offer.Manifest.Files.Count);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, offer.Signature);   // base64 "AQID"
            // 签名覆盖的字节为 manifest 段的原文——非空且与重新序列化无关
            Assert.IsTrue(offer.SignedBytes.Length > 0);
        }

        // ---- 候选来源：文件不存在 = 无候选（不是异常） ----

        [Test]
        public void 文件候选来源_无信封文件_返回无候选()
        {
            var provider = new FileSystemCandidateProvider("test_no_such_candidate.json");
            CandidateOffer offer = provider.TryGetCandidateAsync().GetAwaiter().GetResult();
            Assert.IsTrue(offer.IsEmpty);
        }

        // ---- 编排端到端（§8：信任门 → 编排 → 健康 → 确认/回退） ----

        private sealed class MemoryActivationIO : IActivationRecordIO
        {
            private ActivationRecord _stored;
            public ActivationRecord TryLoad() => _stored;
            public void Save(ActivationRecord r)
            {
                _stored = new ActivationRecord
                {
                    SchemaVersion = r.SchemaVersion,
                    ConfirmedReleaseId = r.ConfirmedReleaseId,
                    ConfirmedVersion = r.ConfirmedVersion,
                    PendingReleaseId = r.PendingReleaseId,
                    PendingState = r.PendingState,
                    RecoveryAttempts = r.RecoveryAttempts,
                    LastFailure = r.LastFailure,
                    RecordSequence = r.RecordSequence,
                    TransactionId = r.TransactionId,
                    Integrity = r.Integrity,
                };
            }
        }

        /// <summary>无验签器 = 候选一律被拒（当前装配形态；受信公钥随密钥分发接线）。</summary>
        private sealed class AlwaysOkVerifier : ISignatureVerifier
        {
            public bool Verify(byte[] data, byte[] signature) => true;
        }

        private sealed class AlwaysHealthy : ICandidateHealthCheck
        {
            public UniTask<string> CheckAsync(ReleaseManifest candidate, System.Threading.CancellationToken ct = default)
                => UniTask.FromResult<string>(null);
        }

        private sealed class NoOpActivator : IContentActivator
        {
            public UniTask ActivateAsync(ReleaseManifest candidate, System.Threading.CancellationToken ct = default)
                => UniTask.CompletedTask;
            public UniTask RebuildConfirmedAsync(ContentGeneration confirmed, System.Threading.CancellationToken ct = default)
                => UniTask.CompletedTask;
        }

        private PatchRunner BuildRunner(ReleaseManifest manifest, byte[] signedBytes, bool healthy)
        {
            var io = new MemoryActivationIO();
            var store = new ActivationTransactionStore(io, () => 1);

            // 落盘候选内容（ManifestWith 已按同一内容算好摘要与长度）
            foreach ((string path, string content) in _candidateContents)
                FileSys.WriteAllBytes(Root + "/" + path, Bytes(content));

            var coord = new PatchCoordinator(
                store,
                new FileSysCandidateFileSource(Root),
                new DriveInfoSpaceProbe(UnityEngine.Application.persistentDataPath),
                new LocalDirectoryCandidateFetcher(Root),
                healthy ? (ICandidateHealthCheck)new AlwaysHealthy() : new CompositeHealthCheck(),   // 空聚合 = 不健康
                new NoOpActivator(),
                generationSink: null);

            var offer = new CandidateOffer(manifest, signedBytes, new byte[] { 1 });
            return new PatchRunner(new FixedProvider(offer), Player(), store, coord,
                keyId => (ISignatureVerifier)new AlwaysOkVerifier(),   // 端到端装配批：verifier → 按 KeyId 解析
                spaceRequestFactory: m => new SpaceCheckRequest
                {
                    CandidateBytes = TotalOf(m),
                    SafetyMarginBytes = 0,
                });
        }

        private static long TotalOf(ReleaseManifest m)
        {
            long t = 0;
            foreach (ReleaseFileEntry f in m.Files) t += f.Length;
            return t;
        }

        private static PlayerCapabilities Player() => new PlayerCapabilities
        {
            Platform = UnityEngine.Application.platform.ToString(),
        };

        private sealed class FixedProvider : ICandidateProvider
        {
            private readonly CandidateOffer _offer;
            public FixedProvider(CandidateOffer offer) { _offer = offer; }
            public UniTask<CandidateOffer> TryGetCandidateAsync(System.Threading.CancellationToken ct = default)
                => UniTask.FromResult(_offer);
        }

        [Test]
        public void 编排端到端_健康通过_推进到Confirmed()
        {
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"), ("sub/b.bin", "BB"));

            PatchRunner runner = BuildRunner(m, Bytes("signed"), healthy: true);
            PatchRunResult r = runner.RunAsync().GetAwaiter().GetResult();

            Assert.IsTrue(r.Succeeded, r.ToString());
            Assert.AreEqual(PatchPhase.Confirmed, r.FinalPhase);
        }

        [Test]
        public void 编排端到端_健康不通过_拒绝且不推进()
        {
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            // 空 CompositeHealthCheck = 无探针 = 判为不健康（§8：不得在未覆盖任何项时声称健康）
            PatchRunner runner = BuildRunner(m, Bytes("signed"), healthy: false);
            PatchRunResult r = runner.RunAsync().GetAwaiter().GetResult();

            Assert.IsFalse(r.Succeeded);
            Assert.IsFalse(r.NoWork);
            Assert.AreEqual(PatchPhase.HealthChecking, r.FinalPhase);
        }

        [Test]
        public void 编排端到端_描述被拒_不进入编排()
        {
            // 清单为空 → ReleaseManifestValidator 第③步拒绝（文件清单为空）
            var m = new ReleaseManifest { ReleaseId = "rel-empty", Revision = 1 };
            PatchRunner runner = BuildRunner(m, Bytes("signed"), healthy: true);

            PatchRunResult r = runner.RunAsync().GetAwaiter().GetResult();

            Assert.IsFalse(r.Succeeded);
            Assert.IsFalse(r.NoWork);
            Assert.AreEqual(ReleaseRejectReason.InvalidEntry, runner.LastRejectReason);
        }

        // ---- 真签名候选的接受路径（《框架先行》样例①"验签/下载段"）------------------
        //
        // 上面三条 `编排端到端_*` 用的是 `AlwaysOkVerifier`——**信任关被短路**，
        // 故它们证明的是"编排能跑通"，证明不了"真签名能被接受"。
        // 下面这条把验签器换成**内置锚点**，并让候选来自**真信封**（发布私钥签出、已入库），
        // 从而覆盖「真信封 → 信任关 → 下载 → 校验 → 健康 → 激活」的完整贯通。
        //
        // 不依赖私钥：验签只需公钥，而公钥在内置锚点 ContentTrustAnchors 里。

        /// <summary>真信封夹具的声明内容——必须与签名时的字节**完全一致**（摘要会核对）。</summary>
        private static readonly (string path, string content)[] FixtureFiles =
        {
            ("config/tbcombatnum.json", "{\"id\":1,\"hp\":100}"),
            ("lua/ui/UIMain.lua", "return { OnShow = function() end }\n"),
        };

        [Test]
        public void 编排端到端_真签名候选_经内置锚点接受并推进到Confirmed()
        {
            const string fixture = "Assets/Tests/EditMode/Fixtures/signed-candidate.json";
            Assert.IsTrue(System.IO.File.Exists(fixture), $"真信封夹具缺失：{fixture}");

            CandidateOffer offer = SignedManifestEnvelope.Parse(System.IO.File.ReadAllText(fixture));
            Assert.IsFalse(offer.IsEmpty, "真信封必须可解析");

            // 把夹具声明的内容落到候选根（摘要与之一致 → 通过逐文件校验）
            foreach ((string path, string content) in FixtureFiles)
                WriteCandidate(path, content);

            var store = new ActivationTransactionStore(new MemoryActivationIO(), () => 1);
            var coord = new PatchCoordinator(
                store,
                new FileSysCandidateFileSource(Root),
                new DriveInfoSpaceProbe(UnityEngine.Application.persistentDataPath),
                new LocalDirectoryCandidateFetcher(Root),
                new AlwaysHealthy(),
                new NoOpActivator(),
                generationSink: null);

            // **关键差异**：验签器来自内置锚点（真实信任链），不是 AlwaysOkVerifier
            var trustedKeys = new TrustedKeyStore();
            int anchors = ContentTrustAnchors.ApplyTo(trustedKeys);
            Assert.GreaterOrEqual(anchors, 1, "内置锚点应至少一条");

            var runner = new PatchRunner(new FixedProvider(offer), Player(), store, coord,
                trustedKeys.AsResolver(),
                spaceRequestFactory: m => new SpaceCheckRequest
                {
                    CandidateBytes = TotalOf(m),
                    SafetyMarginBytes = 0,
                });

            PatchRunResult r = runner.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ReleaseRejectReason.None, runner.LastRejectReason,
                "信任关必须放行——被拒说明签发端与运行时的字节契约漂移");
            Assert.IsTrue(r.Succeeded, r.ToString());
            Assert.AreEqual(PatchPhase.Confirmed, r.FinalPhase);
            Assert.AreEqual(offer.Manifest.ReleaseId, store.Current.ConfirmedReleaseId);
        }

        [Test]
        public void 编排端到端_签名被篡改_信任关拒绝且不下载()
        {
            const string fixture = "Assets/Tests/EditMode/Fixtures/signed-candidate.json";
            Assert.IsTrue(System.IO.File.Exists(fixture), $"真信封夹具缺失：{fixture}");

            string json = System.IO.File.ReadAllText(fixture);
            // 改清单里的 ReleaseId —— 被签名字节随之改变，而签名不动 → 必须被拒。
            // 这条钉的是"信任关看的是**被签名字节**，不是反序列化后的对象"。
            string tampered = json.Replace("\"ReleaseId\":\"rel-fixture-001\"",
                                           "\"ReleaseId\":\"rel-evil-999\"");
            Assert.AreNotEqual(json, tampered, "替换未生效——夹具的 ReleaseId 变了？");

            CandidateOffer offer = SignedManifestEnvelope.Parse(tampered);
            Assert.IsFalse(offer.IsEmpty);

            foreach ((string path, string content) in FixtureFiles)
                WriteCandidate(path, content);

            var store = new ActivationTransactionStore(new MemoryActivationIO(), () => 1);
            var coord = new PatchCoordinator(
                store,
                new FileSysCandidateFileSource(Root),
                new DriveInfoSpaceProbe(UnityEngine.Application.persistentDataPath),
                new LocalDirectoryCandidateFetcher(Root),
                new AlwaysHealthy(),
                new NoOpActivator(),
                generationSink: null);

            var trustedKeys = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(trustedKeys);

            var runner = new PatchRunner(new FixedProvider(offer), Player(), store, coord,
                trustedKeys.AsResolver());

            // 已被确认的版本必须在被拒前后**不变**。
            // 注意 `ActivationRecord.ConfirmedReleaseId` 的**初值是 "builtin"**（不是 null）——
            // 断言 null 会失败；正确的性质是"未被改写"。
            string confirmedBefore = store.Current.ConfirmedReleaseId;
            PatchRunResult r = runner.RunAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ReleaseRejectReason.BadSignature, runner.LastRejectReason);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(confirmedBefore, store.Current.ConfirmedReleaseId,
                "被拒的候选不得改写已确认版本");
        }
    }
}
