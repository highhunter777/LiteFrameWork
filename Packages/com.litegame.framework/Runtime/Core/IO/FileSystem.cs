using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 本地持久化实现（<see cref="IFileStore"/> 的生产实现）。构造注入路径根与 JSON 序列化器；
    /// 契约：
    /// ① relPath 禁 .. 段、盘符、绝对开头——release 也校验（字符级零分配）；
    /// ② 原子写：tmp 写完再替换，进程被杀目标仍是旧档；残留 .tmp = 上次半截写，下次写自然覆盖；
    ///    同一文件并发写 = 违例（存档走主线程流程；异步逃生口按单飞使用）；
    /// ③ JSON 二分口径：不存在 → default 静默（首启是正常状态）；损坏 → default + Log.Error；
    ///    需要区分两者（版本迁移）用 TryReadJson；
    /// ④ 同步是主路径，异步是逃生口；元数据操作无异步；
    /// ⑤ 异步签名用 BCL Task（Core 零第三方），业务侧 .AsUniTask() 转签名。
    /// 主线程同步方法 + 异步逃生口；同文件的异步写必须由调用方 await 串行。
    /// </summary>
    public sealed class FileSystem : IFileStore
    {
        private readonly IPathProvider _path;
        private readonly IJsonSerializer _json;

        /// <summary>同一文件的 in-flight 登记表：并发异步写同一文件 = .tmp 冲突 = 静默损坏。</summary>
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.Ordinal);

        public FileSystem(IPathProvider provider, IJsonSerializer serializer)
        {
            _path = provider ?? throw new ArgumentNullException(nameof(provider));
            _json = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        // ---- 路径 ----

        public string PathOf(string first, string second)
        {
            ValidateRelPath(first);
            ValidateRelPath(second);
            return Path.Combine(Path.Combine(_path.RootPath, first), second);
        }

        public string PathOf(params string[] parts)
        {
            string p = _path.RootPath;
            for (int i = 0; i < parts.Length; i++)
            {
                ValidateRelPath(parts[i]);
                p = Path.Combine(p, parts[i]);
            }
            return p;
        }

        // ---- 文本/字节 ----

        public void WriteAllText(string relPath, string content)
        {
            string path = Combine(relPath);
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            CommitAtomic(path, tmp);
        }

        public string ReadAllText(string relPath)
        {
            string path = Combine(relPath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public void WriteAllBytes(string relPath, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string path = Combine(relPath);
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            CommitAtomic(path, tmp);
        }

        public byte[] ReadAllBytes(string relPath)
        {
            string path = Combine(relPath);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public long GetFileLength(string relPath)
        {
            string path = Combine(relPath);
            return File.Exists(path) ? new FileInfo(path).Length : -1L;
        }

        public void AppendAllBytes(string relPath, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            string path = Combine(relPath);
            EnsureDirFor(path);
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                stream.Write(bytes, 0, bytes.Length);
        }

        public string[] GetFilesRecursive(string relDir)
        {
            string dir = Combine(relDir);
            if (!Directory.Exists(dir)) return Array.Empty<string>();

            string[] full = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var result = new string[full.Length];
            int prefixLen = _path.RootPath.Length + 1;
            for (int i = 0; i < full.Length; i++)
                result[i] = full[i].Substring(prefixLen).Replace('\\', '/');
            return result;
        }

        public void DeleteDirectory(string relDir)
        {
            if (string.IsNullOrEmpty(relDir)) throw new ArgumentException("禁删除根目录", nameof(relDir));
            string dir = Combine(relDir);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        public bool DirectoryExists(string relDir)
            => !string.IsNullOrEmpty(relDir) && Directory.Exists(Combine(relDir));

        public long GetAvailableBytes(string relDir)
        {
            try
            {
                string full = Path.GetFullPath(Combine(relDir));
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return -1L;

                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : -1L;
            }
            catch (Exception)
            {
                return -1L;      // 平台不支持/权限不足/路径异常——"不可知"是合法答案
            }
        }

        public long WriteProbe(string relDir, int blockBytes = 4 * 1024 * 1024,
            long capBytes = 512L * 1024 * 1024)
        {
            if (blockBytes <= 0) throw new ArgumentOutOfRangeException(nameof(blockBytes));
            if (capBytes <= 0) return -1L;

            string relProbe = CombineSlash(relDir, ".disk_probe." + Guid.NewGuid().ToString("N"));
            string path = Combine(relProbe);
            var block = new byte[blockBytes];
            long written = 0;

            try
            {
                EnsureDirFor(path);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    while (written < capBytes)
                    {
                        stream.Write(block, 0, blockBytes);
                        written += blockBytes;
                    }
                    stream.Flush(true);
                }
                return capBytes;
            }
            catch (Exception)
            {
                return written <= 0 ? -1L : written / 2;
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (Exception) { /* 清理失败不影响已得结论 */ }
            }
        }

        // ---- JSON ----

        public void WriteJson<T>(string relPath, T value)
            => WriteAllText(relPath, _json.Serialize(value));

        public T ReadJson<T>(string relPath)
        {
            bool ok = TryReadJsonCore<T>(relPath, out var v, out var detail);
            if (!ok && detail != "not_found")
                Log.Error($"JSON 损坏 {relPath}({detail})——按 default 继续;版本迁移请用 TryReadJson", LogTag.FileSystem);
            return v;
        }

        public bool TryReadJson<T>(string relPath, out T value, out string detail)
            => TryReadJsonCore(relPath, out value, out detail);

        private bool TryReadJsonCore<T>(string relPath, out T value, out string detail)
        {
            value = default;
            detail = null;
            string json = ReadAllText(relPath);
            if (json == null) { detail = "not_found"; return false; }
            try { value = _json.Deserialize<T>(json); return true; }
            catch (Exception ex) { detail = "parse: " + ex.Message; return false; }
        }

        // ---- 元数据 ----

        public bool Exists(string relPath) => File.Exists(Combine(relPath));

        public void Delete(string relPath)
        {
            string path = Combine(relPath);
            if (File.Exists(path)) File.Delete(path);
        }

        public string[] GetFiles(string relDir)
        {
            string dir = Combine(relDir);
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            string[] full = Directory.GetFiles(dir);
            var result = new string[full.Length];
            int prefixLen = _path.RootPath.Length + 1;
            for (int i = 0; i < full.Length; i++)
                result[i] = full[i].Substring(prefixLen).Replace('\\', '/');
            return result;
        }

        // ---- 异步逃生口 ----

        public async Task WriteAllTextAsync(string relPath, string content, CancellationToken ct = default)
        {
            string path = Combine(relPath);
            ct.ThrowIfCancellationRequested();
            EnsureDirFor(path);
            string tmp = path + ".tmp";
            lock (_inFlight)
            {
                if (!_inFlight.Add(path))
                    Log.Error($"同一文件并发异步写 \"{relPath}\"——await 串行同一文件,否则 .tmp 冲突静默损坏", LogTag.FileSystem);
            }
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                using (var sw = new StreamWriter(fs))
                {
                    await sw.WriteAsync(content);
                    await sw.FlushAsync();
                }
                CommitAtomic(path, tmp);
            }
            finally { lock (_inFlight) _inFlight.Remove(path); }
        }

        public async Task<string> ReadAllTextAsync(string relPath, CancellationToken ct = default)
        {
            string path = Combine(relPath);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var sr = new StreamReader(fs))
            {
                string result = await sr.ReadToEndAsync();
                ct.ThrowIfCancellationRequested();
                return result;
            }
        }

        public async Task WriteJsonAsync<T>(string relPath, T value, CancellationToken ct = default)
        {
            ValidateRelPath(relPath);
            string json = await Task.Run(() => _json.Serialize(value), ct);
            await WriteAllTextAsync(relPath, json, ct);
        }

        public async Task<T> ReadJsonAsync<T>(string relPath, CancellationToken ct = default)
        {
            string json = await ReadAllTextAsync(relPath, ct);
            if (json == null) return default;
            try { return await Task.Run(() => _json.Deserialize<T>(json), ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Error($"JSON 损坏 {relPath}({ex.Message})——按 default 继续", LogTag.FileSystem);
                return default;
            }
        }

        // ---- 内部 ----

        private string Combine(string relPath)
        {
            ValidateRelPath(relPath);
            return Path.Combine(_path.RootPath, relPath);
        }

        private static string CombineSlash(string relDir, string name)
        {
            ValidateRelPath(relDir);
            return relDir + "/" + name;
        }

        private static void ValidateRelPath(string p)
        {
            if (string.IsNullOrEmpty(p)) throw new ArgumentException("路径段为空");
            if (SafeRelativePath.IsRooted(p)) throw new ArgumentException($"禁绝对路径:{p}");
            if (SafeRelativePath.HasDriveLetter(p)) throw new ArgumentException($"禁盘符:{p}");
            if (SafeRelativePath.HasTraversalSegment(p)) throw new ArgumentException($"禁 .. 越界:{p}");
        }

        private static void EnsureDirFor(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void CommitAtomic(string path, string tmp)
        {
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (NotSupportedException)
            {
                // 部分平台/文件系统（如 Android Mono）不支持 File.Replace：退化为删除+移动。
                // 该路径不再保证崩溃原子性，但保证写入可用；IOException（磁盘满/权限）仍上抛。
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
