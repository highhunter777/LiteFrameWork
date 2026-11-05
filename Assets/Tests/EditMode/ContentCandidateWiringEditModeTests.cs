using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>H4-3/H4-4：候选 Lua 全量来源、LuaPreloader 候选优先，以及 Bridge 能力探针。</summary>
    public sealed class ContentCandidateWiringEditModeTests
    {
        private static TestFileStore LuaFiles()
        {
            var files = new TestFileStore();
            files.Put(ReleaseLayout.CandidateRootRelative + "/lua/main.lua", new byte[] { 1, 2, 3 });
            files.Put(ReleaseLayout.CandidateRootRelative + "/lua/UI/Page_Login.lua", new byte[] { 4, 5, 6 });
            return files;
        }

        [Test]
        public void 候选Lua源_全量脚本_按require键映射()
        {
            var source = new FileSystemLuaCandidateSource(LuaFiles(), () => new ContentGeneration("rel-h4", 1));

            Assert.IsTrue(source.IsActive);
            Assert.IsTrue(source.TryLoadAll(out var scripts, out string problem), problem);
            Assert.AreEqual(2, scripts.Count);
            Assert.IsTrue(scripts.ContainsKey("main"));
            Assert.IsTrue(scripts.ContainsKey("UI/Page_Login"));
        }

        [Test]
        public void 候选Lua源_内置代次_不激活()
        {
            var source = new FileSystemLuaCandidateSource(LuaFiles(), () => ContentGeneration.Default);
            Assert.IsFalse(source.IsActive);
        }

        [Test]
        public void LuaPreloader_候选优先_不读tag清单()
        {
            var source = new FileSystemLuaCandidateSource(LuaFiles(), () => new ContentGeneration("rel-h4", 1));
            var preloader = new LuaPreloader(
                (path, ct) => UniTask.FromException<byte[]>(new InvalidOperationException("不应回落到内容租约：" + path)),
                () => throw new InvalidOperationException("不应读 tag 清单"),
                "Assets/LiteGame/Lua",
                source);

            preloader.PreloadAllAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(2, preloader.Count);
            Assert.IsTrue(preloader.Scripts.ContainsKey("UI/Page_Login"));
        }

        [Test]
        public void LuaPreloader_候选空目录_失败()
        {
            var source = new FileSystemLuaCandidateSource(new TestFileStore(), () => new ContentGeneration("rel-h4", 1));
            var preloader = new LuaPreloader(
                (path, ct) => UniTask.FromResult(Array.Empty<byte>()),
                () => Array.Empty<string>(),
                "Assets/LiteGame/Lua",
                source);

            Assert.Throws<InvalidOperationException>(
                () => preloader.PreloadAllAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Bridge能力探针_空要求通过_缺能力拒绝_具备通过()
        {
            var set = new BridgeCapabilitySet(1, new[] { "bridge.battle" });
            var probe = new BridgeCapabilityHealthProbe(set, manifest => manifest.RequiredCapabilities);

            Assert.IsNull(probe.Check(new ReleaseManifest()));

            var missing = new ReleaseManifest();
            missing.RequiredCapabilities.Add("bridge.room");
            StringAssert.Contains("bridge.room", probe.Check(missing));

            var covered = new ReleaseManifest();
            covered.RequiredCapabilities.Add("bridge.battle");
            Assert.IsNull(probe.Check(covered));
        }

        private sealed class TestFileStore : IFileStore
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public void Put(string rel, byte[] bytes) => _files[rel] = bytes;

            public bool DirectoryExists(string relDir)
                => relDir == ReleaseLayout.CandidateRootRelative + "/lua";

            public byte[] ReadAllBytes(string relPath)
                => _files.TryGetValue(relPath, out byte[] bytes) ? bytes : null;

            public string[] GetFilesRecursive(string relDir)
            {
                var result = new List<string>();
                foreach (string path in _files.Keys)
                    if (path.StartsWith(relDir + "/", StringComparison.Ordinal))
                        result.Add(path);
                return result.ToArray();
            }

            public string PathOf(string first, string second) => first + "/" + second;
            public string PathOf(params string[] parts) => string.Join("/", parts);
            public bool Exists(string relPath) => _files.ContainsKey(relPath);
            public string ReadAllText(string relPath) => throw new NotSupportedException();
            public void WriteAllText(string relPath, string content) => throw new NotSupportedException();
            public void WriteAllBytes(string relPath, byte[] bytes) => throw new NotSupportedException();
            public void AppendAllBytes(string relPath, byte[] bytes) => throw new NotSupportedException();
            public void Delete(string relPath) => throw new NotSupportedException();
            public long GetFileLength(string relPath) => _files.TryGetValue(relPath, out byte[] b) ? b.Length : -1;
            public string[] GetFiles(string relDir) => Array.Empty<string>();
            public void DeleteDirectory(string relDir) => throw new NotSupportedException();
            public void WriteJson<T>(string relPath, T value) => throw new NotSupportedException();
            public T ReadJson<T>(string relPath) => throw new NotSupportedException();
            public bool TryReadJson<T>(string relPath, out T value, out string detail) => throw new NotSupportedException();
            public System.Threading.Tasks.Task WriteAllTextAsync(string relPath, string content, CancellationToken ct = default) => throw new NotSupportedException();
            public System.Threading.Tasks.Task<string> ReadAllTextAsync(string relPath, CancellationToken ct = default) => throw new NotSupportedException();
            public System.Threading.Tasks.Task WriteJsonAsync<T>(string relPath, T value, CancellationToken ct = default) => throw new NotSupportedException();
            public System.Threading.Tasks.Task<T> ReadJsonAsync<T>(string relPath, CancellationToken ct = default) => throw new NotSupportedException();
            public long GetAvailableBytes(string relDir) => -1;
            public long WriteProbe(string relDir, int blockBytes = 4 * 1024 * 1024, long capBytes = 512L * 1024 * 1024) => -1;
        }
    }
}
