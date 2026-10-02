using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 真实下载端到端（《热更与内容发布专项设计》§7）。
    ///
    /// **本地起 HTTP 监听做真实传输**——不需要外部 CDN，但也不是替身：
    /// 走的是 <c>UnityWebRequest</c> 全链路（TCP→HTTP→响应），
    /// 能抓到替身测试断言不了的传输层与 PlayerLoop 依赖问题
    /// （`GetAwaiter().GetResult()` 对 `SendWebRequest` 会抛 "Not yet completed"）。
    ///
    /// **来源语义**：下载基址来自 <see cref="DownloadPlan"/> 选定源（端口契约"按计划取字节"）——
    /// 故每用例的计划的源就是本地监听基址；重试/换源语义用 <see cref="LocalHttpTestServer.RouteFlaky"/>
    /// 的故障注入 + <see cref="LocalHttpTestServer.HitCount"/> 断言实际尝试次数。
    ///
    /// **形态**：异步用例用 <c>[UnityTest]</c> + <c>IEnumerator</c>——
    /// EditMode 下没有 PlayerLoop，`SendWebRequest` 的续延不在 MoveNext 点执行，
    /// 同步忙等会挂起，必须由 Test Framework 在帧间推进。
    /// 纯同步用例（路径换算、构造校验）仍用 <c>[Test]</c>。
    /// </summary>
    public sealed class HttpCandidateFetcherEditModeTests : UnityTestBase
    {
        private const string Root = "test_http_candidate";

        private LocalHttpTestServer _server;

        [SetUp]
        protected void Init()
        {
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);
            _server = new LocalHttpTestServer();
        }

        [TearDown]
        protected void Clean()
        {
            _server?.Dispose();
            _server = null;
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);
        }

        private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        /// <summary>零退避预算：重试语义照常计数，但不真等（退避数值归 DownloadPlan 的 L1 用例）。</summary>
        private static DownloadBudget ZeroBackoff() => new DownloadBudget { BackoffBaseMs = 0, BackoffCapMs = 0 };

        private static ReleaseManifest ManifestWith(params (string path, string content)[] files)
        {
            var m = new ReleaseManifest { ReleaseId = "rel-http", Revision = 1 };
            foreach ((string path, string content) in files)
            {
                m.Files.Add(new ReleaseFileEntry
                {
                    Path = path,
                    Length = B(content).LongLength,
                    Sha256 = ContentHash.Sha256Hex(B(content)),
                });
            }
            return m;
        }

        /// <summary>单源计划（源 = 本地监听基址）。</summary>
        private DownloadPlan PlanFor(ReleaseManifest m)
            => new DownloadPlan(m, new[] { new DownloadSource("http", _server.BaseUrl) }, ZeroBackoff());

        private HttpCandidateFetcher Fetcher() => new HttpCandidateFetcher(Root);

        // ---- 正常路径 ----

        [UnityTest]
        public IEnumerator 真实HTTP_下载并落盘_内容与摘要一致()
        {
            _server.Route("/rel-http/a.bin", "AAA");
            _server.Route("/rel-http/sub/b.bin", "BB");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"), ("sub/b.bin", "BB"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual("AAA", Encoding.UTF8.GetString(FileSys.ReadAllBytes(Root + "/a.bin")));
            Assert.AreEqual("BB", Encoding.UTF8.GetString(FileSys.ReadAllBytes(Root + "/sub/b.bin")));

            // 落盘后过校验器（真实摘要复算）
            CandidateVerifyResult verify =
                CandidateContentVerifier.Verify(m, new FileSysCandidateFileSource(Root), r.Paths);
            Assert.IsTrue(verify.Passed, verify.Failure.ToString());
        }

        [UnityTest]
        public IEnumerator 真实HTTP_进度回调_逐文件推进()
        {
            _server.Route("/rel-http/a.bin", "A");
            _server.Route("/rel-http/b.bin", "B");
            ReleaseManifest m = ManifestWith(("a.bin", "A"), ("b.bin", "B"));

            var progress = new List<(int done, int total)>();
            HttpCandidateFetcher fetcher = Fetcher();
            fetcher.OnProgress = (d, t, _) => progress.Add((d, t));

            CandidateFetchResult r = default;
            yield return fetcher.FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual(2, progress.Count);
            Assert.AreEqual((1, 2), progress[0]);
            Assert.AreEqual((2, 2), progress[1]);
        }

        [UnityTest]
        public IEnumerator 已在本地且长度一致_跳过下载()
        {
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));
            // 不注册任何路由——若真去下载会 404，证明走了跳过路径
            FileSys.WriteAllBytes(Root + "/a.bin", B("AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual(0, _server.HitCount("/rel-http/a.bin"));
        }

        // ---- 失败分类 ----

        [UnityTest]
        public IEnumerator HTTP404_判确定性失败_不重试()
        {
            ReleaseManifest m = ManifestWith(("missing.bin", "X"));   // 未注册路由 → 404

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.FileMissing, r.Failure.Kind);
            Assert.IsFalse(r.Failure.IsTransient);            // 4xx = 确定性
            Assert.AreEqual(1, _server.HitCount("/rel-http/missing.bin"), "确定性失败不得重试");
        }

        [UnityTest]
        public IEnumerator HTTP500_判暂态失败_按预算重试至耗尽()
        {
            _server.Route("/rel-http/a.bin", "boom", status: 500);    // 恒 500
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.TransientNetwork, r.Failure.Kind);
            Assert.IsTrue(r.Failure.IsTransient);
            // 默认预算 MaxAttemptsPerFile=3：三次尝试全部落在同一源后如实上报末次暂态失败
            Assert.AreEqual(3, _server.HitCount("/rel-http/a.bin"));
        }

        [UnityTest]
        public IEnumerator 暂态失败_重试后成功()
        {
            _server.RouteFlaky("/rel-http/a.bin", "AAA", failTimes: 1);   // 首次 500，之后 200
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual(2, _server.HitCount("/rel-http/a.bin"));
            Assert.AreEqual("AAA", Encoding.UTF8.GetString(FileSys.ReadAllBytes(Root + "/a.bin")));
        }

        [UnityTest]
        public IEnumerator 多源_首选源暂态故障_换源成功()
        {
            // 同一监听的两个路径前缀模拟两个源：srcA 恒 500，srcB 正常
            _server.Route("/srcA/rel-http/a.bin", "boom", status: 500);
            _server.Route("/srcB/rel-http/a.bin", "AAA");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));
            var plan = new DownloadPlan(m, new[]
            {
                new DownloadSource("srcA", _server.BaseUrl + "/srcA", priority: 0),
                new DownloadSource("srcB", _server.BaseUrl + "/srcB", priority: 1),
            }, ZeroBackoff());

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, plan).ToCoroutine(x => r = x);

            Assert.IsTrue(r.Succeeded, r.Failure.ToString());
            Assert.AreEqual(1, _server.HitCount("/srcA/rel-http/a.bin"), "首选源一次暂态后轮转");
            Assert.AreEqual(1, _server.HitCount("/srcB/rel-http/a.bin"));
            Assert.AreEqual("AAA", Encoding.UTF8.GetString(FileSys.ReadAllBytes(Root + "/a.bin")));
        }

        [UnityTest]
        public IEnumerator 多源_预算耗尽_如实上报末次失败()
        {
            _server.Route("/srcA/rel-http/a.bin", "boom", status: 500);
            _server.Route("/srcB/rel-http/a.bin", "boom", status: 500);
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));
            var plan = new DownloadPlan(m, new[]
            {
                new DownloadSource("srcA", _server.BaseUrl + "/srcA", priority: 0),
                new DownloadSource("srcB", _server.BaseUrl + "/srcB", priority: 1),
            }, new DownloadBudget { BackoffBaseMs = 0, BackoffCapMs = 0, MaxAttemptsPerFile = 2 });

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, plan).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.TransientNetwork, r.Failure.Kind);
            Assert.AreEqual(1, _server.HitCount("/srcA/rel-http/a.bin"));
            Assert.AreEqual(1, _server.HitCount("/srcB/rel-http/a.bin"));
        }

        [UnityTest]
        public IEnumerator 长度不符_写盘前拒绝_不留半截()
        {
            _server.Route("/rel-http/a.bin", "TOOLONG");               // 清单声称 3
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.LengthMismatch, r.Failure.Kind);
            Assert.AreEqual(-1L, FileSys.GetFileLength(Root + "/a.bin"));   // 目标路径未被写入
        }

        [UnityTest]
        public IEnumerator 取消_在循环边界生效()
        {
            _server.Route("/rel-http/a.bin", "AAA");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m), cts.Token).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.Canceled, r.Failure.Kind);
        }

        // ---- 临时文件归属（§7"验证临时文件的 Release 归属"） ----

        [Test]
        public void 临时路径_归属到Release()
        {
            var f = new HttpCandidateFetcher(Root);

            string p1 = f.TempPath("rel-A", "a.bin");
            string p2 = f.TempPath("rel-B", "a.bin");

            StringAssert.Contains("rel-A", p1);
            StringAssert.Contains("rel-B", p2);
            Assert.AreNotEqual(p1, p2);       // 换 Release 不会误用旧半截
            StringAssert.StartsWith(Root + "/.tmp/", p1);
        }

        [UnityTest]
        public IEnumerator 下载完成后_临时件不残留()
        {
            _server.Route("/rel-http/a.bin", "AAA");
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);
            Assert.IsTrue(r.Succeeded, r.Failure.ToString());

            // .tmp 下不应留下本次的临时件（否则清点会判 UnexpectedFile）
            foreach (string f in FileSys.GetFilesRecursive(Root + "/.tmp"))
                StringAssert.DoesNotContain("a.bin", f);
        }

        [Test]
        public void 临时目录回收_按发布隔离_幂等不抛()
        {
            var f = new HttpCandidateFetcher(Root);

            // 两个 Release 各留一个临时件——只回收 rel-A，rel-B 不受牵连（§7 Release 归属）
            FileSys.WriteAllBytes(f.TempPath("rel-A", "a.bin"), B("AAA"));
            FileSys.WriteAllBytes(f.TempPath("rel-B", "a.bin"), B("BBB"));

            f.CleanupTempAsync("rel-A").GetAwaiter().GetResult();   // 全程同步完成（无网络）

            Assert.AreEqual(-1L, FileSys.GetFileLength(f.TempPath("rel-A", "a.bin")));  // 已回收
            Assert.AreEqual(3L, FileSys.GetFileLength(f.TempPath("rel-B", "a.bin")));   // 不误伤

            f.CleanupTempAsync("rel-A").GetAwaiter().GetResult();   // 幂等：目录已不存在 = no-op
            f.CleanupTempAsync(null).GetAwaiter().GetResult();     // 空发布身份按 no-op，不抛
        }

        // ---- 构造校验 ----

        [Test]
        public void 构造参数_空候选根拒绝()
        {
            Assert.Throws<ArgumentException>(() => new HttpCandidateFetcher(""));
        }
    }
}
