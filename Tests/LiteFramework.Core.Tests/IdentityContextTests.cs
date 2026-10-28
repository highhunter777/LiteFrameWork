using System;
using System.Text;
using LiteFramework;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 统一身份上下文（路线图 §4.9）：Build/Content/Schema 单入口、握手/诊断投影、
    /// 能力快照的 Off 形态与内容绑定语义。
    /// </summary>
    public sealed class IdentityContextTests
    {
        private static IdentityContext Context() => new IdentityContext(
            new BuildIdentity("8cc75e264b5f1ccd", "0.1.0", "WindowsPlayer", string.Empty, 5),
            new SchemaIdentity(0, 0, 0, ActivationRecord.CurrentSchemaVersion));

        [Fact]
        public void 能力快照_Off且填充真实身份串()
        {
            PlayerCapabilities caps = Context().CreatePlayerCapabilities();

            Assert.Equal(PlayerCapabilities.CapabilityMode.Off, caps.CompatibilityMode);
            Assert.Equal("8cc75e264b5f1ccd", Context().Build.BuildHash);
            Assert.Equal("0.1.0", caps.AppVersion);
            Assert.Equal("WindowsPlayer", caps.Platform);
            Assert.Equal(string.Empty, caps.Channel);
            Assert.Equal(5, caps.BridgeApiVersion);
            Assert.Equal(0, caps.ProtocolVersion);
            Assert.Equal(0, caps.SimVersion);
            Assert.Equal(0, caps.ConfigSchemaVersion);
            Assert.Equal(ActivationRecord.CurrentSchemaVersion, caps.SaveSchemaVersion);
        }

        [Fact]
        public void 握手与诊断投影同源()
        {
            IdentityContext context = Context();

            Assert.Equal(context.Build.BuildHash, context.Handshake.BuildHash);
            Assert.Equal(context.Build.BuildHash, context.Diagnostics.BuildHash);
            Assert.Equal(context.Build.AppVersion, context.Diagnostics.AppVersion);
            Assert.Equal(context.Build.Platform, context.Diagnostics.Platform);
        }

        [Fact]
        public void 内容身份_未绑定为空_绑定后读取_重复绑定抛()
        {
            IdentityContext context = Context();
            Assert.True(context.Content.IsBuiltin);
            Assert.Equal(0uL, context.Content.Generation);

            var content = new ContentIdentity("rel-1", 3, 7, 0x1234u, "root-digest");
            context.BindContent(() => content);

            Assert.Equal("rel-1", context.Content.ReleaseId);
            Assert.Equal(3uL, context.Content.Generation);
            Assert.Equal(7, context.Content.Revision);
            Assert.Equal(0x1234u, context.Content.ConfigDigestBase);
            Assert.Throws<InvalidOperationException>(() => context.BindContent(() => content));
        }

        [Fact]
        public void 能力Off与Enforce_零下限清单判定一致()
        {
            IdentityContext context = Context();
            var manifest = new ReleaseManifest
            {
                ReleaseId = "rel-test",
                Revision = 1,
                Files =
                {
                    new ReleaseFileEntry { Path = "lua/main.lua", Length = 1, Sha256 = new string('a', 64) },
                },
                KeyId = "release-key-1",
            };
            manifest.Compatibility = new ReleaseCompatibility();

            byte[] bytes = Encoding.UTF8.GetBytes("manifest");
            var verifier = new AlwaysVerifier();

            PlayerCapabilities off = context.CreatePlayerCapabilities();
            PlayerCapabilities enforce = context.CreatePlayerCapabilities();
            enforce.CompatibilityMode = PlayerCapabilities.CapabilityMode.Enforce;

            var a = ReleaseManifestValidator.Validate(manifest, bytes, new byte[] { 1 }, verifier, off, 0);
            var b = ReleaseManifestValidator.Validate(manifest, bytes, new byte[] { 1 }, verifier, enforce, 0);
            Assert.True(a.Accepted, a.ToString());
            Assert.True(b.Accepted, b.ToString());
        }

        private sealed class AlwaysVerifier : ISignatureVerifier
        {
            public bool Verify(byte[] data, byte[] signature) => true;
        }
    }
}
