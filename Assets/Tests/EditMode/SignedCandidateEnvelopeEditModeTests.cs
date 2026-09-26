using System.IO;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 候选信封的**接受路径**（《热更与内容发布专项设计》§6；《框架先行》样例①"验签/下载段"）。
    ///
    /// **为什么需要本组**：编排层测试（`PatchCoordinatorTests`）全部运行在信任关**之下**——
    /// 直接喂 <see cref="ReleaseManifest"/> 对象，绕过了签名；签名链此前只在
    /// `ReleaseManifestTests` 里用**内存生成的密钥**测过。于是
    /// 「用**发布私钥**签出的信封，能不能被**内置锚点**接受」这条真实链路**没有任何测试载体**。
    ///
    /// **夹具来源**：`Fixtures/signed-candidate.json` 由 `scripts/gen-candidate.ps1` 用
    /// `release-key-2026-09-26` 私钥生成（私钥在签名机仓库外，**绝不入库**）。
    /// 夹具只含**公钥可验**的数据，入库无碍——故本组在任何机器上都能跑，不依赖私钥。
    ///
    /// **防的是**：签发端与运行时的**字节契约漂移**。若哪天有人改了清单字段序、字段名或
    /// 换掉 `Formatting.None`，本组会红——而只测内存密钥的旧用例仍会绿。
    /// </summary>
    public sealed class SignedCandidateEnvelopeEditModeTests : UnityTestBase
    {
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
            CandidateOffer offer = SignedManifestEnvelope.Parse(ReadFixture());
            Assert.IsFalse(offer.IsEmpty, "真私钥签出的信封必须可解析（解析失败 = 信封格式或字段名漂移）");
            return offer;
        }

        private static TrustedKeyStore StoreWithAnchors()
        {
            var store = new TrustedKeyStore();
            ContentTrustAnchors.ApplyTo(store);
            return store;
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
            // 判据用**排斥法**：签名关（⑦）在平台关（⑨）之前，故只要不是签名类拒绝，
            // 就说明签名已过。这样断言不依赖夹具的具体 releaseId/revision（重生成夹具不会假红）。
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
