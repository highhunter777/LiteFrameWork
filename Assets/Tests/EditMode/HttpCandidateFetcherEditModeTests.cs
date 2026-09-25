using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
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
    /// （实测：`GetAwaiter().GetResult()` 对 `SendWebRequest` 会抛 "Not yet completed"）。
    ///
    /// **形态**：异步用例用 <c>[UnityTest]</c> + <c>IEnumerator</c>——
    /// EditMode 下没有 PlayerLoop，`SendWebRequest` 的续延不在 MoveNext 点执行，
    /// 同步忙等会挂起（实测），必须由 Test Framework 在帧间推进。
    /// 纯同步用例（路径换算、构造校验）仍用 <c>[Test]</c>。
    /// </summary>
    public sealed class HttpCandidateFetcherEditModeTests : UnityTestBase
    {
        private const string Root = "test_http_candidate";

        private HttpListener _listener;
        private string _baseUrl;
        private readonly Dictionary<string, (int status, byte[] body)> _routes =
            new Dictionary<string, (int, byte[])>();

        [SetUp]
        protected void Init()
        {
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);

            int port = FreePort();
            _baseUrl = "http://127.0.0.1:" + port;
            _listener = new HttpListener();
            _listener.Prefixes.Add(_baseUrl + "/");
            _listener.Start();
            _listener.BeginGetContext(OnRequest, null);
        }

        [TearDown]
        protected void Clean()
        {
            try { _listener?.Stop(); _listener?.Close(); } catch (Exception) { }
            _listener = null;
            if (FileSys.DirectoryExists(Root)) FileSys.DeleteDirectory(Root);
        }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private void OnRequest(IAsyncResult ar)
        {
            HttpListener listener = _listener;
            if (listener == null || !listener.IsListening) return;
            try
            {
                HttpListenerContext ctx = listener.EndGetContext(ar);
                listener.BeginGetContext(OnRequest, null);

                string path = ctx.Request.Url.AbsolutePath;
                if (_routes.TryGetValue(path, out var route))
                {
                    ctx.Response.StatusCode = route.status;
                    if (route.body != null) ctx.Response.OutputStream.Write(route.body, 0, route.body.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            }
            catch (Exception) { /* 停止时的竞态 */ }
        }

        private void Route(string path, string content, int status = 200)
            => _routes[path] = (status, Encoding.UTF8.GetBytes(content));

        private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

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

        private static DownloadPlan PlanFor(ReleaseManifest m)
            => new DownloadPlan(m, new[] { new DownloadSource("http", "http://x") });

        private HttpCandidateFetcher Fetcher() => new HttpCandidateFetcher(Root, _baseUrl);

        // ---- 正常路径 ----

        [UnityTest]
        public IEnumerator 真实HTTP_下载并落盘_内容与摘要一致()
        {
            Route("/rel-http/a.bin", "AAA");
            Route("/rel-http/sub/b.bin", "BB");
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
            Route("/rel-http/a.bin", "A");
            Route("/rel-http/b.bin", "B");
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
        }

        [UnityTest]
        public IEnumerator HTTP500_判暂态失败_可重试()
        {
            Route("/rel-http/a.bin", "boom", status: 500);
            ReleaseManifest m = ManifestWith(("a.bin", "AAA"));

            CandidateFetchResult r = default;
            yield return Fetcher().FetchAsync(m, PlanFor(m)).ToCoroutine(x => r = x);

            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(DownloadFailureKind.TransientNetwork, r.Failure.Kind);
            Assert.IsTrue(r.Failure.IsTransient);
        }

        [UnityTest]
        public IEnumerator 长度不符_写盘前拒绝_不留半截()
        {
            Route("/rel-http/a.bin", "TOOLONG");               // 清单声称 3
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
            Route("/rel-http/a.bin", "AAA");
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
            var f = new HttpCandidateFetcher(Root, _baseUrl);

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
            Route("/rel-http/a.bin", "AAA");
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
            var f = new HttpCandidateFetcher(Root, _baseUrl);

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
        public void 构造参数_空值拒绝()
        {
            Assert.Throws<ArgumentException>(() => new HttpCandidateFetcher("", "http://x"));
            Assert.Throws<ArgumentException>(() => new HttpCandidateFetcher(Root, ""));
        }
    }
}
