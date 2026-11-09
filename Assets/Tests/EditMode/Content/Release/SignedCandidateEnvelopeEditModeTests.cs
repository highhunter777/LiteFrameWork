using System.IO;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using LiteFramework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 候选信封的**接受路径**（《热更与内容发布专项设计》§6；《框架先行》样例①"验签/下载段"）。
    /// </summary>
    public sealed class SignedCandidateEnvelopeEditModeTests : UnityTestBase
    {
        private static CandidateOffer ParseOffer(string json)
            => SignedManifestEnvelope.Parse(json, new NewtonsoftJsonCodec());

        private const string FixturePath = "Assets/Tests/EditMode/Fixtures/signed-candidate.json";

        /// <summary>读夹具。EditMode 下工作目录 = 工程根，故相对路径可用。
        /// 测试工程用 System.IO 不违反产品层纪律——纪律约束的是 LiteGame 运行时。</summary>
        private static string ReadFixture()
        {
            Assert.IsTrue(File.Exists(FixturePath),
                $"夹具缺失：{FixturePath}（应由 scripts/gen-candidate.ps1 生成并入库）");
            return File.ReadAllText(FixturePath);
        }

        private static CandidateOffer FixtureOffer()
        {
            CandidateOffer offer = ParseOffer(ReadFixture());
            Assert.IsFalse(offer.IsEmpty, "真私钥签出的信封必须可解析（解析失败 = 信封格式或字段名漂移）");
            return offer;
        }

        private static TrustedKeyStore StoreWithAnchors()
        {
            var store = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(store);
            return store;
        }

        [TestCase(@"{""ReleaseId"":""r"",""Revision"":1,""Unknown"":null}", @"{""ReleaseId"":""r"",""Revision"":1,""Unknown"":null}")]
        [TestCase(@"{ ""Revision"": 1e2, ""ReleaseId"": ""中文<&>\n"", ""Extra"": [1.0,true,null] }", @"{""Revision"":100.0,""ReleaseId"":""中文<&>\n"",""Extra"":[1.0,true,null]}")]
        [TestCase(@"{""ReleaseId"":""r"",""CreatedAtUnix"":0,""Tag"":""2026-11-08T12:00:00Z""}", @"{""ReleaseId"":""r"",""CreatedAtUnix"":0,""Tag"":""2026-11-08T12:00:00Z""}")]
        public void 签名信封_字节保持原Newtonsoft紧凑表达(string manifestJson, string compact)
        {
            string envelope = "{\"signature\":\"AQ==\",\"manifest\":" + manifestJson + "}";
            CandidateOffer offer = ParseOffer(envelope);
            Assert.IsFalse(offer.IsEmpty);
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes(compact), offer.SignedBytes);
        }

        [Test]
        public void 签名信封_真签名夹具外层字段顺序_签名字节不变()
        {
            CandidateOffer first = FixtureOffer();
            string reordered = "{\n  \"signature\":\"" + System.Convert.ToBase64String(first.Signature)
                + "\",\n  \"manifest\":" + System.Text.Encoding.UTF8.GetString(first.SignedBytes) + "\n}";
            CandidateOffer second = ParseOffer(reordered);
            Assert.IsFalse(second.IsEmpty);
            CollectionAssert.AreEqual(first.SignedBytes, second.SignedBytes);
            Assert.IsTrue(StoreWithAnchors().Resolve(second.Manifest.KeyId).Verify(second.SignedBytes, second.Signature));
        }

        [Test]
        public void 签名信封_真私钥产出_可被解析且字段保真()
        {
            CandidateOffer offer = FixtureOffer();

            Assert.AreEqual(1, offer.Manifest.SchemaVersion);
            Assert.IsFalse(string.IsNullOrEmpty(offer.Manifest.ReleaseId));
            Assert.AreEqual("release-key-2026-09-26", offer.Manifest.KeyId,
                "KeyId 必须是内置锚点的那一个——否则运行时解析不出验签器");
            Assert.IsNotNull(offer.Manifest.Files);
            Assert.Greater(offer.Manifest.Files.Count, 0, "清单应含受控文件条目");
            Assert.IsTrue(offer.SignedBytes.Length > 0, "被签名字节非空");
            Assert.IsTrue(offer.Signature.Length > 0, "签名非空");
        }

        [Test]
        public void 签名信封_内置锚点验签通过()
        {
            CandidateOffer offer = FixtureOffer();

            var verifier = StoreWithAnchors().Resolve(offer.Manifest.KeyId);
            Assert.IsNotNull(verifier, "内置锚点应能解析出该 KeyId 的验签器（未登记/已撤销 = null）");

            Assert.IsTrue(verifier.Verify(offer.SignedBytes, offer.Signature),
                "内置锚点必须验过发布私钥的签名——失败 = 签发端与运行时的字节契约漂移");
        }

        [Test]
        public void 签名信封_篡改被签名字节_验签拒绝()
        {
            CandidateOffer offer = FixtureOffer();
            var verifier = StoreWithAnchors().Resolve(offer.Manifest.KeyId);

            var tampered = (byte[])offer.SignedBytes.Clone();
            tampered[tampered.Length / 2] ^= 0x01;      // 翻一位

            Assert.IsFalse(verifier.Verify(tampered, offer.Signature), "篡改后的字节不得验过");
        }

        [Test]
        public void 签名信封_未被信任的KeyId_解析为null()
        {
            Assert.IsNull(StoreWithAnchors().Resolve("some-other-key"),
                "未登记的 KeyId 必须解析为 null（调用方据此拒候选）");
        }

        [Test]
        public void 签名信封_完整校验_签名关须通过()
        {
            // 上下文项（平台/兼容/反回退）需要额外输入，本组不构造——只钉「签名关」。
            CandidateOffer offer = FixtureOffer();

            ReleaseVerdict verdict = ReleaseManifestValidator.Validate(
                offer.Manifest, offer.SignedBytes, offer.Signature,
                StoreWithAnchors().Resolve(offer.Manifest.KeyId),
                player: null,
                confirmedRevision: 0,
                budget: null,
                nowUnix: 0);

            Assert.AreNotEqual(ReleaseRejectReason.BadSignature, verdict.Reason,
                $"签名关必须通过（实际：{verdict}）——BadSignature = 字节契约漂移");
            Assert.AreNotEqual(ReleaseRejectReason.UnknownOrRevokedKey, verdict.Reason,
                $"KeyId 必须能在内置锚点里解析（实际：{verdict}）");
        }
    }
}
