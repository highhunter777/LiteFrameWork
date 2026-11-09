using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// H4-1/H4-2 候选 config 段用例：全量表装载、缺表 fail-closed、无候选段回退、
    /// 以及激活前语义探针（建表 + ConfigTableValidator）。
    /// </summary>
    public sealed class ConfigCandidateConfigEditModeTests
    {
        private static Dictionary<string, byte[]> ReadTableBytes()
        {
            string dir = Path.Combine(ProjectRoot(), ConfigService.DataDir);
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length, StringComparer.Ordinal);
            foreach (string file in ConfigService.TableDataFiles)
            {
                string path = Path.Combine(dir, file + ".bytes");
                Assert.IsTrue(File.Exists(path), $"缺表数据：{path}（跑 Luban/gen.bat）");
                cache[TableLayout.TableName(file)] = File.ReadAllBytes(path);
            }
            return cache;
        }

        private static UniTask<byte[]> FailProvider(string location, CancellationToken ct)
            => UniTask.FromException<byte[]>(new InvalidOperationException("候选路径不应回落到租约通道：" + location));

        [Test]
        public void 候选全量表_ConfigService_走候选源且发布()
        {
            var cache = ReadTableBytes();
            var source = new FakeCandidateSource(cache);

            var svc = new ConfigService(FailProvider, candidateConfig: source);
            svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(svc.Loaded);
            Assert.AreEqual(1UL, svc.Version);
            Assert.AreEqual(1, source.ReadCount, "候选源应被调用一次");
            Assert.IsNotNull(svc.Tables.Tbcombatnum.Get(1));
        }

        [Test]
        public void 候选缺表_ConfigService_fail_closed且不发布()
        {
            var cache = ReadTableBytes();
            cache.Remove("tbuiform");
            var source = new FakeCandidateSource(cache);

            var svc = new ConfigService(FailProvider, candidateConfig: source);
            var ex = Assert.Throws<InvalidOperationException>(
                () => svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

            StringAssert.Contains("tbuiform", ex.Message);
            Assert.IsFalse(svc.Loaded);
            Assert.AreEqual(0UL, svc.Version);
        }

        [Test]
        public void 无候选段_回退租约通道()
        {
            var cache = ReadTableBytes();
            var source = new FakeCandidateSource(cache) { HasConfigSegment = false };

            var svc = new ConfigService((loc, ct) => FromCache(cache, loc, ct), candidateConfig: source);
            svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(svc.Loaded);
            Assert.AreEqual(0, source.ReadCount, "无候选段时不应读候选源");
        }

        [Test]
        public void 健康探针_全量合法_通过()
        {
            var probe = new ConfigCandidateHealthProbe(TableFiles(ReadTableBytes()));

            string problem = probe.CheckAsync(new ReleaseManifest()).GetAwaiter().GetResult();
            Assert.IsNull(problem);
        }

        [Test]
        public void 健康探针_缺表_失败并带表名()
        {
            var cache = ReadTableBytes();
            cache.Remove("tbweapon");
            var probe = new ConfigCandidateHealthProbe(TableFiles(cache));

            string problem = probe.CheckAsync(new ReleaseManifest()).GetAwaiter().GetResult();
            StringAssert.Contains("tbweapon", problem);
        }

        [Test]
        public void 健康探针_坏表_失败()
        {
            var cache = ReadTableBytes();
            cache["tbcombatnum"] = new byte[] { 0x01, 0x02, 0x03 };
            var probe = new ConfigCandidateHealthProbe(TableFiles(cache));

            string problem = probe.CheckAsync(new ReleaseManifest()).GetAwaiter().GetResult();
            Assert.IsNotNull(problem);
            StringAssert.Contains("候选配置建表失败", problem);
        }

        private static StubFileStore TableFiles(Dictionary<string, byte[]> cache)
        {
            var files = new StubFileStore();
            foreach (string file in ConfigService.TableDataFiles)
            {
                string name = TableLayout.TableName(file);
                if (!cache.TryGetValue(name, out byte[] bytes)) continue;
                string rel = ReleaseLayout.CandidateRootRelative + "/" + ReleaseLayout.ConfigPrefix + file + ".bytes";
                files.Put(rel, bytes);
            }
            return files;
        }

        [Test]
        public void CandidateConfigSource_路径映射_全量表()
        {
            var cache = ReadTableBytes();
            var files = new StubFileStore();
            foreach (string file in ConfigService.TableDataFiles)
            {
                string rel = ReleaseLayout.CandidateRootRelative + "/" + ReleaseLayout.ConfigPrefix + file + ".bytes";
                files.Put(rel, cache[TableLayout.TableName(file)]);
            }

            var source = new CandidateConfigSource(files, () => new ContentGeneration("rel-h4", 1));
            Assert.IsTrue(source.HasConfigSegment);
            Assert.IsTrue(source.TryReadAll(out var tables, out string problem), problem);
            Assert.AreEqual(ConfigService.TableDataFiles.Length, tables.Count);
            Assert.IsTrue(tables.ContainsKey("tbuiform"));
        }

        [Test]
        public void CandidateConfigSource_内置代次_不激活()
        {
            var files = new StubFileStore();
            files.Put(ReleaseLayout.CandidateRootRelative + "/config/Strategy/tbbattlestrategy.bytes", new byte[] { 1 });

            var source = new CandidateConfigSource(files, () => ContentGeneration.Default);
            Assert.IsFalse(source.HasConfigSegment);
        }

        private static UniTask<byte[]> FromCache(Dictionary<string, byte[]> cache, string location, CancellationToken ct)
        {
            string file = location.Substring(ConfigService.DataDir.Length,
                location.Length - ConfigService.DataDir.Length - ".bytes".Length);
            return cache.TryGetValue(TableLayout.TableName(file), out byte[] bytes)
                ? UniTask.FromResult(bytes)
                : UniTask.FromException<byte[]>(new FileNotFoundException(location));
        }

        private static string ProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(UnityEngine.Application.dataPath) ?? UnityEngine.Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            throw new DirectoryNotFoundException("找不到项目根");
        }

        private sealed class FakeCandidateSource : ICandidateConfigSource
        {
            private readonly Dictionary<string, byte[]> _tables;

            public FakeCandidateSource(Dictionary<string, byte[]> tables)
                => _tables = new Dictionary<string, byte[]>(tables, StringComparer.Ordinal);

            public bool HasConfigSegment { get; set; } = true;
            public int ReadCount { get; private set; }

            public bool TryReadAll(out IReadOnlyDictionary<string, byte[]> tables, out string problem)
            {
                ReadCount++;
                if (!HasConfigSegment)
                {
                    tables = null;
                    problem = "无候选 config 段";
                    return false;
                }

                foreach (string file in ConfigService.TableDataFiles)
                {
                    string name = TableLayout.TableName(file);
                    if (!_tables.TryGetValue(name, out byte[] bytes) || bytes == null || bytes.Length == 0)
                    {
                        tables = null;
                        problem = "候选 config 缺表：" + file;
                        return false;
                    }
                }

                tables = _tables;
                problem = null;
                return true;
            }
        }

        private sealed class StubFileStore : IFileStore
        {
            private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public void Put(string rel, byte[] bytes) => _files[rel] = bytes;

            public bool DirectoryExists(string relDir)
                => relDir == ReleaseLayout.CandidateRootRelative + "/config";

            public byte[] ReadAllBytes(string relPath)
                => _files.TryGetValue(relPath, out byte[] bytes) ? bytes : null;

            public string PathOf(string first, string second) => first + "/" + second;
            public string PathOf(params string[] parts) => string.Join("/", parts);
            public bool Exists(string relPath) => _files.ContainsKey(relPath);
            public string ReadAllText(string relPath) => throw new NotSupportedException();
            public void WriteAllText(string relPath, string content) => throw new NotSupportedException();
            public void WriteAllBytes(string relPath, byte[] bytes) => throw new NotSupportedException();
            public void AppendAllBytes(string relPath, byte[] bytes) => throw new NotSupportedException();
            public void Delete(string relPath) => throw new NotSupportedException();
            public long GetFileLength(string relPath) => _files.TryGetValue(relPath, out byte[] bytes) ? bytes.Length : -1;
            public string[] GetFiles(string relDir) => Array.Empty<string>();
            public string[] GetFilesRecursive(string relDir) => Array.Empty<string>();
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
