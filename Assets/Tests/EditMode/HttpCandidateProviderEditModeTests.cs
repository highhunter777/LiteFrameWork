using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// CDN 信封提供者（《热更与内容发布专项设计》§7 部署配置契约）。
    ///
    /// **真实传输**：本地 <see cref="LocalHttpTestServer"/> 走 UnityWebRequest 全链路（非替身）。
    /// 结果语义对齐 <see cref="FileSystemCandidateProvider"/>：404 = 无候选；畸形信封 = 无候选；
    /// **网络错/5xx = 抛出**（暂态）——"查不到"不得伪装成"没有"。
    ///
    /// **带签接受链**：夹具信封（真私钥签出，`scripts/gen-candidate.ps1` 生成）经 HTTP 取回 →
    /// 解析 → <see cref="ReleaseManifestValidator"/> 全量校验（签名/预算/兼容/修订）——
    /// 即"信封走 CDN"装配形态下的真实接受路径。
    /// </summary>
    public sealed class HttpCandidateProviderEditModeTests : UnityTestBase
    {
        private const string FixturePath = "Assets/Tests/EditMode/Fixtures/signed-candidate.json";

        private LocalHttpTestServer _server;

        [SetUp]
        protected void Init() => _server = new LocalHttpTestServer();

        [TearDown]
        protected void Clean() => _server?.Dispose();

        /// <summary>读夹具。EditMode 下工作目录 = 工程根，故相对路径可用。</summary>
        private static string ReadFixture()
        {
            Assert.IsTrue(File.Exists(FixturePath),
                $"夹具缺失：{FixturePath}（应由 scripts/gen-candidate.ps1 生成并入库）");
            return File.ReadAllText(FixturePath);
        }

        private HttpCandidateProvider Provider() => new HttpCandidateProvider(_server.BaseUrl, "candidate.json");

        // ---- 结果语义 ----

        [UnityTest]
        public IEnumerator HTTP200_信封经HTTP取回并解析成提案()
        {
            _server.Route("/candidate.json", ReadFixture());

            CandidateOffer offer = default;
            yield return Provider().TryGetCandidateAsync().ToCoroutine(x => offer = x);

            Assert.IsFalse(offer.IsEmpty, "200 + 合法信封必须是提案，而非无候选");
            Assert.AreEqual("rel-fixture-001", offer.Manifest.ReleaseId);
            Assert.AreEqual("release-key-2026-09-26", offer.Manifest.KeyId);
            Assert.IsTrue(offer.SignedBytes.Length > 0);
            Assert.IsTrue(offer.Signature.Length > 0);
        }

        [UnityTest]
        public IEnumerator HTTP404_无发布_空提案_不抛()
        {
            CandidateOffer offer = default;
            yield return Provider().TryGetCandidateAsync().ToCoroutine(x => offer = x);

            Assert.IsTrue(offer.IsEmpty, "404 = 发布点无候选（同本地信封文件不存在语义）");
        }

        [UnityTest]
        public IEnumerator HTTP200_畸形信封_空提案_不抛()
        {
            _server.Route("/candidate.json", "not-json{{");

            CandidateOffer offer = default;
            yield return Provider().TryGetCandidateAsync().ToCoroutine(x => offer = x);

            Assert.IsTrue(offer.IsEmpty, "畸形信封 = 无候选（Parse 既有契约，不抛、不代判）");
        }

        [UnityTest]
        public IEnumerator HTTP500_暂态失败_抛出不伪装成无候选()
        {
            _server.Route("/candidate.json", "boom", status: 500);

            Exception error = null;
            yield return Provider().TryGetCandidateAsync().ToCoroutine(x => { }, e => error = e);

            Assert.IsNotNull(error, "网络/服务端故障必须抛出——静默返回无候选会跳过更新");
            Assert.IsInstanceOf<InvalidOperationException>(error);
            StringAssert.Contains(_server.BaseUrl, error.Message, "异常应带基址定位信息");
        }

        // ---- 带签接受链（CDN 装配形态的真实接受路径） ----

        [UnityTest]
        public IEnumerator CDN通道_信封经HTTP取回_内置锚点全量校验通过()
        {
            _server.Route("/candidate.json", ReadFixture());

            CandidateOffer offer = default;
            yield return Provider().TryGetCandidateAsync().ToCoroutine(x => offer = x);
            Assert.IsFalse(offer.IsEmpty);

            // 与 ContentModule 生产装配同一套信任面：内置锚表 → keyId 解析验签器（fail-closed）
            var trustedKeys = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(trustedKeys);
            ISignatureVerifier verifier = trustedKeys.Resolve(offer.Manifest.KeyId);
            Assert.IsNotNull(verifier, "内置锚点必须能解析夹具的 KeyId");

            ReleaseVerdict verdict = ReleaseManifestValidator.Validate(
                offer.Manifest, offer.SignedBytes, offer.Signature, verifier,
                MinimalPlayerCapabilities(), confirmedRevision: 0, budget: null);

            Assert.IsTrue(verdict.Accepted, $"带签信封必须被接受（拒因：{verdict.Reason}）");
        }

        /// <summary>最小 Player 能力声明（与本形态 <c>ContentModule</c> 装配同口径：全 0/空 = 不限）。</summary>
        private static PlayerCapabilities MinimalPlayerCapabilities() => new PlayerCapabilities
        {
            AppVersion = "",
            Platform = Application.platform.ToString(),
            Channel = "",
            BridgeApiVersion = 0,
            ProtocolVersion = 0,
            SimVersion = 0,
            ConfigSchemaVersion = 0,
            SaveSchemaVersion = 0,
        };
    }
}
