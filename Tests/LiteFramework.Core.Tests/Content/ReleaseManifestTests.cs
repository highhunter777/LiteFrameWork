using System;
using System.Security.Cryptography;
using System.Text;
using LiteFramework;
using Xunit;

namespace LiteFramework.Core.Tests.Content
{
    /// <summary>
    /// 内容签名与发布描述校验验收（《热更与内容发布专项设计》§6 发布描述与信任边界；
    /// 热更 §13 L1 行"版本策略、摘要、事务恢复表、取消/代次、纯快照验证"）。
    ///
    /// **用真实 RSA 而非替身**：算法选型是本批的裁决结论（ECDSA 在本运行时不可用），
    /// 必须证明"用真实密钥能签能验、篡改能被拒"——替身只能证明调用顺序。
    /// </summary>
    public sealed class ReleaseManifestTests
    {
        /// <summary>造一对密钥；返回 (验签器, 签名函数)。与 Unity 侧同一对 API（RSA + PKCS#1 + SHA-256）。</summary>
        private static (RsaSignatureVerifier verifier, Func<byte[], byte[]> sign) NewKeyPair(string keyId = "release-key-1")
        {
            var rsa = RSA.Create(2048);
            var pub = rsa.ExportParameters(false);

            var verifier = new RsaSignatureVerifier(keyId, pub.Modulus, pub.Exponent);
            byte[] Sign(byte[] data) => rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return (verifier, Sign);
        }

        private static ReleaseManifest NewManifest(string releaseId = "rel-2026-09-25", long revision = 5)
        {
            return new ReleaseManifest
            {
                SchemaVersion = ReleaseManifest.CurrentSchemaVersion,
                ReleaseId = releaseId,
                Revision = revision,
                Platform = "StandaloneWindows64",
                Channel = "dev",
                Compatibility = new ReleaseCompatibility
                {
                    ProtocolVersion = 1,
                    SimVersion = 1,
                    BridgeApiVersion = 1,
                    ConfigSchemaVersion = 1,
                    SaveSchemaVersion = 1,
                },
                Files =
                {
                    new ReleaseFileEntry { Path = "lua/main.lua", Length = 10, Sha256 = new string('a', 64) },
                    new ReleaseFileEntry { Path = "config/tbuiform.bytes", Length = 20, Sha256 = new string('b', 64) },
                },
                KeyId = "release-key-1",
            };
        }

        private static PlayerCapabilities Player() => new PlayerCapabilities
        {
            AppVersion = "0.1.0",
            Platform = "StandaloneWindows64",
            Channel = "dev",
            BridgeApiVersion = 1,
            ProtocolVersion = 1,
            SimVersion = 1,
            ConfigSchemaVersion = 1,
            SaveSchemaVersion = 1,
        };

        /// <summary>正常路径：签名覆盖描述字节，校验接受。</summary>
        private static ReleaseVerdict Validate(ReleaseManifest m, Func<byte[], byte[]> sign,
            RsaSignatureVerifier verifier, long confirmedRevision, ReleaseBudget budget = null, long now = 0)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("manifest-canonical-bytes");
            byte[] sig = sign(bytes);
            return ReleaseManifestValidator.Validate(m, bytes, sig, verifier, Player(), confirmedRevision, budget, now);
        }

        // ---- 摘要工具 ----

        [Fact]
        public void 摘要_SHA256十六进制_与已知向量一致()
        {
            // "abc" 的 SHA-256 是公开测试向量——钉死实现不是自造的
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                ContentHash.Sha256Hex("abc"));
        }

        [Fact]
        public void 摘要_不做换行归一化_CRLF与LF摘要不同()
        {
            // 热更 §5"二进制文件不做 CRLF 替换"——文件完整性必须对原始字节
            string lf = "a\nb";
            string crlf = "a\r\nb";
            Assert.NotEqual(ContentHash.Sha256Hex(lf), ContentHash.Sha256Hex(crlf));
        }

        [Fact]
        public void 摘要_Hex往返_与非法输入拒绝()
        {
            var bytes = new byte[] { 0x00, 0x0f, 0xff, 0xab };
            string hex = ContentHash.ToLowerHex(bytes);
            Assert.Equal("000fffab", hex);
            Assert.True(ContentHash.TryParseHex(hex, out var back));
            Assert.Equal(bytes, back);

            Assert.False(ContentHash.TryParseHex("abc", out _), "奇数长度拒绝");
            Assert.False(ContentHash.TryParseHex("zz", out _), "非 hex 字符拒绝");
            Assert.False(ContentHash.TryParseHex("", out _));
        }

        [Fact]
        public void 摘要_可复用会话_与静态路径同结果()
        {
            // 批量校验用 Hasher 复用算法对象（不每文件 SHA256.Create()）；
            // 结果必须与静态路径逐字符一致——否则两条路径会给出不同完整性判定。
            var data = Encoding.UTF8.GetBytes("batch-file-content");
            string expected = ContentHash.Sha256Hex(data);

            using var hasher = new ContentHash.Hasher();
            Assert.Equal(expected, hasher.ComputeHex(data));
            Assert.Equal(expected, hasher.ComputeHex(data));      // 可连续调用（自动重置）
            Assert.Equal(ContentHash.Sha256Hex("other"), hasher.ComputeHex(Encoding.UTF8.GetBytes("other")));
        }

        [Fact]
        public void 摘要_恒定时间比较()
        {
            string a = new string('a', 64);
            Assert.True(ContentHash.HexEquals(a, a));
            Assert.True(ContentHash.HexEquals("ABCDEF", "abcdef"), "大小写不敏感");
            Assert.False(ContentHash.HexEquals(a, new string('b', 64)));
            Assert.False(ContentHash.HexEquals(a, a.Substring(1)), "长度不等");
        }

        // ---- 真实 RSA 签名 ----

        [Fact]
        public void 签名_真实RSA_有效签名接受_篡改被拒()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();

            Assert.True(Validate(manifest, sign, verifier, confirmedRevision: 1).Accepted, "有效签名应接受");

            // 篡改描述字节 → 签名不再匹配
            byte[] bytes = Encoding.UTF8.GetBytes("manifest-canonical-bytes-TAMPERED");
            byte[] sig = sign(Encoding.UTF8.GetBytes("manifest-canonical-bytes"));
            var verdict = ReleaseManifestValidator.Validate(manifest, bytes, sig, verifier, Player(), 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.BadSignature, verdict.Reason);
        }

        [Fact]
        public void 签名_篡改签名字节被拒()
        {
            var (verifier, sign) = NewKeyPair();
            byte[] bytes = Encoding.UTF8.GetBytes("m");
            byte[] sig = sign(bytes);
            sig[0] ^= 0xFF;                                  // 翻转一位

            var verdict = ReleaseManifestValidator.Validate(NewManifest(), bytes, sig, verifier, Player(), 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.BadSignature, verdict.Reason);
        }

        [Fact]
        public void 签名_他人密钥签的拒绝()
        {
            var (verifier, _) = NewKeyPair("release-key-1");
            var (_, otherSign) = NewKeyPair("attacker-key");

            byte[] bytes = Encoding.UTF8.GetBytes("m");
            var verdict = ReleaseManifestValidator.Validate(NewManifest(), bytes, otherSign(bytes), verifier, Player(), 1);

            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.BadSignature, verdict.Reason);
        }

        [Fact]
        public void 签名_缺签名或验签器_拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            byte[] bytes = Encoding.UTF8.GetBytes("m");

            var noSig = ReleaseManifestValidator.Validate(NewManifest(), bytes, null, verifier, Player(), 1);
            Assert.False(noSig.Accepted);
            Assert.Equal(ReleaseRejectReason.BadSignature, noSig.Reason);

            var noVerifier = ReleaseManifestValidator.Validate(NewManifest(), bytes, sign(bytes), null, Player(), 1);
            Assert.False(noVerifier.Accepted);
            Assert.Equal(ReleaseRejectReason.UnknownOrRevokedKey, noVerifier.Reason);
        }

        // ---- 密钥轮换/撤销 ----

        [Fact]
        public void 密钥_撤销后拒绝_撤销优先于存在()
        {
            var ring = new TrustedKeyRing();
            var rsa = RSA.Create(2048);
            var pub = rsa.ExportParameters(false);
            ring.Add("k1", pub.Modulus, pub.Exponent);

            Assert.NotNull(RsaSignatureVerifier.FromKeyRing(ring, "k1"));   // 撤销前可用

            Assert.True(ring.Revoke("k1"));
            Assert.Null(RsaSignatureVerifier.FromKeyRing(ring, "k1"));      // 撤销后不可用（即使公钥仍在）
            Assert.False(ring.TryGet("k1", out _));
        }

        [Fact]
        public void 密钥_未登记keyId拒绝_非法登记显性抛()
        {
            var ring = new TrustedKeyRing();
            Assert.False(ring.TryGet("nope", out _));
            Assert.Null(RsaSignatureVerifier.FromKeyRing(ring, "nope"));

            Assert.Throws<ArgumentException>(() => ring.Add("", new byte[] { 1 }, new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => ring.Add("k", null, new byte[] { 1 }));
            Assert.Throws<ArgumentException>(() => ring.Add("k", new byte[] { 1 }, null));
        }

        [Fact]
        public void 密钥_轮换_新旧并存期各自可验_撤销旧后只剩新()
        {
            var ring = new TrustedKeyRing();
            var oldRsa = RSA.Create(2048);
            var newRsa = RSA.Create(2048);
            var oldPub = oldRsa.ExportParameters(false);
            var newPub = newRsa.ExportParameters(false);
            ring.Add("k-old", oldPub.Modulus, oldPub.Exponent);
            ring.Add("k-new", newPub.Modulus, newPub.Exponent);

            byte[] bytes = Encoding.UTF8.GetBytes("m");
            var oldSig = oldRsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var newSig = newRsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            Assert.True(ReleaseManifestValidator.Validate(NewManifest(), bytes, oldSig,
                RsaSignatureVerifier.FromKeyRing(ring, "k-old"), Player(), 1).Accepted);
            Assert.True(ReleaseManifestValidator.Validate(NewManifest(), bytes, newSig,
                RsaSignatureVerifier.FromKeyRing(ring, "k-new"), Player(), 1).Accepted);

            ring.Revoke("k-old");
            Assert.Null(RsaSignatureVerifier.FromKeyRing(ring, "k-old"));
            Assert.NotNull(RsaSignatureVerifier.FromKeyRing(ring, "k-new"));   // 轮换不牵连新密钥
        }

        // ---- 路径与预算（§6）----

        [Theory]
        [InlineData("../escape.txt")]           // 越界
        [InlineData("a/../../b.txt")]           // 越界（中段）
        [InlineData("/abs.txt")]                // 绝对路径
        [InlineData("C:/win.txt")]              // 盘符
        [InlineData("a\\b.txt")]                // 反斜杠（归一化歧义）
        [InlineData("a//b.txt")]                // 空段
        [InlineData("./a.txt")]                 // 当前目录段
        public void 路径_非法形态一律拒绝(string badPath)
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.Files[0].Path = badPath;

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted, $"应拒绝路径:{badPath}");
            Assert.Contains(verdict.Reason, new[] { ReleaseRejectReason.InvalidPath, ReleaseRejectReason.BudgetExceeded });
        }

        [Fact]
        public void 路径_重复条目拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.Files.Add(new ReleaseFileEntry { Path = "lua/main.lua", Length = 1, Sha256 = new string('c', 64) });

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.InvalidPath, verdict.Reason);
        }

        [Fact]
        public void 预算_超文件数拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            for (int i = 0; i < 10; i++)
                manifest.Files.Add(new ReleaseFileEntry { Path = $"f{i}.bin", Length = 1, Sha256 = new string('a', 64) });

            var verdict = Validate(manifest, sign, verifier, 1, new ReleaseBudget { MaxFileCount = 3 });
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.BudgetExceeded, verdict.Reason);
        }

        [Fact]
        public void 预算_超单文件与总字节拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var m1 = NewManifest();
            Assert.Equal(ReleaseRejectReason.BudgetExceeded,
                Validate(m1, sign, verifier, 1, new ReleaseBudget { MaxFileBytes = 5 }).Reason);

            var m2 = NewManifest();
            Assert.Equal(ReleaseRejectReason.BudgetExceeded,
                Validate(m2, sign, verifier, 1, new ReleaseBudget { MaxTotalBytes = 15 }).Reason);
        }

        [Fact]
        public void 条目_非法长度与非法摘要拒绝()
        {
            var (verifier, sign) = NewKeyPair();

            var neg = NewManifest();
            neg.Files[0].Length = -1;
            Assert.Equal(ReleaseRejectReason.InvalidEntry, Validate(neg, sign, verifier, 1).Reason);

            var shortHash = NewManifest();
            shortHash.Files[0].Sha256 = "abc";
            Assert.Equal(ReleaseRejectReason.InvalidEntry, Validate(shortHash, sign, verifier, 1).Reason);

            var upperHash = NewManifest();
            upperHash.Files[0].Sha256 = new string('A', 64);          // 只接受小写
            Assert.Equal(ReleaseRejectReason.InvalidEntry, Validate(upperHash, sign, verifier, 1).Reason);
        }

        [Fact]
        public void 描述_空文件清单拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.Files.Clear();

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.InvalidEntry, verdict.Reason);
        }

        // ---- 结构/releaseId/过期/撤销/反回退/兼容 ----

        [Fact]
        public void 结构_不支持的schemaVersion拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.SchemaVersion = 99;

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.UnsupportedSchema, verdict.Reason);
        }

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("has/slash")]
        [InlineData("has\\backslash")]
        [InlineData("..")]
        public void releaseId_非法形态拒绝(string badId)
        {
            var (verifier, sign) = NewKeyPair();
            var verdict = Validate(NewManifest(badId), sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.InvalidReleaseId, verdict.Reason);
        }

        [Fact]
        public void 过期_超期描述拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.ExpiresAtUnix = 1000;

            var verdict = Validate(manifest, sign, verifier, 1, null, now: 2000);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.Expired, verdict.Reason);

            // 未到期（或未声明 now）则放行
            Assert.True(Validate(manifest, sign, verifier, 1, null, now: 500).Accepted);
        }

        [Fact]
        public void 撤销_已撤销发布拒绝()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.Revoked = true;

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.Revoked, verdict.Reason);
        }

        [Fact]
        public void 反回退_修订低于已确认拒绝_等于放行()
        {
            var (verifier, sign) = NewKeyPair();

            var older = Validate(NewManifest(revision: 3), sign, verifier, confirmedRevision: 5);
            Assert.False(older.Accepted);
            Assert.Equal(ReleaseRejectReason.RevisionRollback, older.Reason);

            Assert.True(Validate(NewManifest(revision: 5), sign, verifier, confirmedRevision: 5).Accepted, "等值放行");
            Assert.True(Validate(NewManifest(revision: 6), sign, verifier, confirmedRevision: 5).Accepted, "更新放行");
        }

        [Fact]
        public void 平台与渠道_不符拒绝()
        {
            var (verifier, sign) = NewKeyPair();

            var wrongPlatform = NewManifest();
            wrongPlatform.Platform = "Android";
            Assert.Equal(ReleaseRejectReason.PlatformMismatch, Validate(wrongPlatform, sign, verifier, 1).Reason);

            var wrongChannel = NewManifest();
            wrongChannel.Channel = "release";
            Assert.Equal(ReleaseRejectReason.PlatformMismatch, Validate(wrongChannel, sign, verifier, 1).Reason);

            var anyChannel = NewManifest();
            anyChannel.Channel = "";                                   // 空 = 全渠道
            Assert.True(Validate(anyChannel, sign, verifier, 1).Accepted);
        }

        [Fact]
        public void 兼容_Player能力低于候选下限拒绝_不静默降级()
        {
            var (verifier, sign) = NewKeyPair();
            var manifest = NewManifest();
            manifest.Compatibility.ProtocolVersion = 99;

            var verdict = Validate(manifest, sign, verifier, 1);
            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.IncompatiblePlayer, verdict.Reason);
            Assert.Contains("协议版本不足", verdict.Detail);
        }

        [Theory]
        [InlineData("sim")]
        [InlineData("bridge")]
        [InlineData("config")]
        [InlineData("save")]
        [InlineData("app")]
        public void 兼容_各维度逐项生效(string dimension)
        {
            var (verifier, sign) = NewKeyPair();
            var m = NewManifest();
            switch (dimension)
            {
                case "sim": m.Compatibility.SimVersion = 9; break;
                case "bridge": m.Compatibility.BridgeApiVersion = 9; break;
                case "config": m.Compatibility.ConfigSchemaVersion = 9; break;
                case "save": m.Compatibility.SaveSchemaVersion = 9; break;
                case "app": m.Compatibility.AppVersion = "9.9.9"; break;   // 精确匹配：本机 0.1.0
            }

            var verdict = Validate(m, sign, verifier, 1);
            Assert.False(verdict.Accepted, $"维度 {dimension} 未生效");
            Assert.Equal(ReleaseRejectReason.IncompatiblePlayer, verdict.Reason);
        }

        [Fact]
        public void 兼容_AppVersion精确匹配_相等即放行()
        {
            // AppVersion 声明即要求相等（不做 semver 范围匹配——见校验器注释）。
            var (verifier, sign) = NewKeyPair();
            var match = NewManifest();
            match.Compatibility.AppVersion = "0.1.0";
            Assert.True(Validate(match, sign, verifier, 1).Accepted, "与本机一致应放行");

            var mismatch = NewManifest();
            mismatch.Compatibility.AppVersion = "0.1.1";
            Assert.Equal(ReleaseRejectReason.IncompatiblePlayer, Validate(mismatch, sign, verifier, 1).Reason);
        }

        [Fact]
        public void 兼容_候选不声明兼容维度_视为不限()
        {
            var (verifier, sign) = NewKeyPair();
            var noCompat = NewManifest();
            noCompat.Compatibility = null;
            Assert.True(Validate(noCompat, sign, verifier, 1).Accepted);
        }

        [Fact]
        public void 生效窗口_首版只支持NextLaunch()
        {
            var nextLaunch = NewManifest();
            Assert.True(ReleaseManifestValidator.ValidateEffectWindow(nextLaunch).Accepted);

            foreach (var window in new[] { ReleaseEffectWindow.SafeWindow, ReleaseEffectWindow.NextMatch })
            {
                var m = NewManifest();
                m.EffectWindow = window;
                var verdict = ReleaseManifestValidator.ValidateEffectWindow(m);
                Assert.False(verdict.Accepted);
                Assert.Equal(ReleaseRejectReason.UnsupportedEffectWindow, verdict.Reason);
            }
        }

        [Fact]
        public void 顺序_签名在路径校验之后_畸形路径不消耗验签算力()
        {
            // §6"先验证描述的结构/预算与签名"——畸形输入在验签之前就被拒，
            // 用"会抛异常"的验签器证明它根本没被调用。
            var manifest = NewManifest();
            manifest.Files[0].Path = "../escape";

            var verdict = ReleaseManifestValidator.Validate(
                manifest, new byte[] { 1 }, new byte[] { 1 }, new ThrowingVerifier(), Player(), 1);

            Assert.False(verdict.Accepted);
            Assert.Equal(ReleaseRejectReason.InvalidPath, verdict.Reason);
        }

        private sealed class ThrowingVerifier : ISignatureVerifier
        {
            public bool Verify(byte[] data, byte[] signature) => throw new InvalidOperationException("验签器不应被调用");
        }
    }
}
