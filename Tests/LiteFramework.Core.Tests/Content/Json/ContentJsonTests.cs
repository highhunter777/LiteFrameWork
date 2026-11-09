using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LiteFramework;
using LiteFramework.Core.Tests.Meta;
using Xunit;

namespace LiteFramework.Tests.Content
{
    public sealed class ContentJsonTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "content-json-" + Guid.NewGuid().ToString("N"));
        private readonly IJsonCodec _json = new StjJsonCodec();
        private readonly IJsonSerializer _serializer = new JsonSerializerAdapter();

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private IFileStore Files() => new FileSystem(new Paths(_root), _serializer);

        [Theory]
        [InlineData("{\"value\":1}", true)]
        [InlineData("[]", true)]
        [InlineData("42", false)]
        [InlineData("null", false)]
        [InlineData("true", false)]
        [InlineData("{ broken", false)]
        [InlineData(" ", false)]
        public async Task 配置健康探针_只接受对象与数组(string data, bool accepted)
        {
            IFileStore files = Files();
            files.WriteAllText("candidate/config/a.json", data);
            var probe = new CandidateConfigHealthProbe("candidate", _ => new[] { "config/a.json" }, _json, files);
            string result = await probe.CheckAsync(new ReleaseManifest());
            Assert.Equal(accepted, result == null);
        }

        [Fact]
        public async Task 配置健康探针_二进制不套用JSON规则_缺失仍拒绝()
        {
            IFileStore files = Files();
            files.WriteAllBytes("candidate/config/a.bytes", new byte[] { 255, 0, 1 });
            var probe = new CandidateConfigHealthProbe("candidate", _ => new[] { "config/a.bytes" }, _json, files);
            Assert.Null(await probe.CheckAsync(new ReleaseManifest()));
            files.Delete("candidate/config/a.bytes");
            Assert.NotNull(await probe.CheckAsync(new ReleaseManifest()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{broken")]
        [InlineData("{\"manifest\":{},\"signature\":\"!\"}")]
        [InlineData("{\"manifest\":[],\"signature\":\"AQ==\"}")]
        [InlineData("{\"manifest\":{}}")]
        public void 畸形信封_返回空提案(string text)
            => Assert.True(SignedManifestEnvelope.Parse(text, _json).IsEmpty);

        [Fact]
        public async Task 独立JSON实现_本地信封读取后可验真签名并保留未知字段()
        {
            using var rsa = RSA.Create(2048);
            const string manifest = "{\"ReleaseId\":\"r\",\"Revision\":7,\"FutureField\":true}";
            byte[] signature = rsa.SignData(Encoding.UTF8.GetBytes(manifest), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string envelope = "{\"manifest\":" + manifest + ",\"signature\":\"" + Convert.ToBase64String(signature) + "\"}";
            IFileStore files = Files();
            files.WriteAllText("candidate.json", envelope);
            var provider = new FileSystemCandidateProvider(_json, "candidate.json", files);
            CandidateOffer offer = await provider.TryGetCandidateAsync();
            Assert.False(offer.IsEmpty);
            Assert.Equal("r", offer.Manifest.ReleaseId);
            Assert.Equal(7, offer.Manifest.Revision);
            Assert.True(rsa.VerifyData(offer.SignedBytes, offer.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        [Fact]
        public void 缺失JSON依赖_按装配错误抛出()
        {
            Assert.Throws<ArgumentNullException>(() => SignedManifestEnvelope.Parse("{}", null));
            Assert.Throws<ArgumentNullException>(() => new FileSystemCandidateProvider(null, files: Files()));
            Assert.Throws<ArgumentNullException>(() => new CandidateConfigHealthProbe("candidate", _ => Array.Empty<string>(), null, Files()));
        }

        [Fact]
        public void 内容服务源码不依赖Newtonsoft()
        {
            string root = Directory.GetCurrentDirectory();
            while (!Directory.Exists(Path.Combine(root, "Packages", "com.litegame.framework"))) root = Directory.GetParent(root).FullName;
            string content = Path.Combine(root, "Packages", "com.litegame.framework", "Runtime", "Services", "Content");
            foreach (string file in Directory.GetFiles(content, "*.cs", SearchOption.AllDirectories))
                Assert.DoesNotContain("Newtonsoft.", File.ReadAllText(file));
        }

        private sealed class Paths : IPathProvider
        {
            public Paths(string root) => RootPath = root;
            public string RootPath { get; }
        }

        private sealed class JsonSerializerAdapter : IJsonSerializer
        {
            private readonly JsonSerializerOptions _options = new JsonSerializerOptions { IncludeFields = true };
            public string Serialize<T>(T value) => JsonSerializer.Serialize(value, _options);
            public T Deserialize<T>(string text) => JsonSerializer.Deserialize<T>(text, _options);
        }
    }
}
