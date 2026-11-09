using System;
using System.Collections.Generic;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 内存候选文件端口（L1 假件——Core 不依赖 System.IO）。
    /// </summary>
    public sealed class FakeCandidateFileSource : ICandidateFileSource
    {
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>被访问过的路径（断言"未触碰未声明文件"用）。</summary>
        public readonly List<string> Opened = new List<string>();

        /// <summary>模拟读取失败（ReadAll 返回 null）。</summary>
        public bool FailReads;

        public FakeCandidateFileSource Add(string path, byte[] bytes)
        {
            _files[path.Replace('\\', '/')] = bytes ?? Array.Empty<byte>();
            return this;
        }

        public FakeCandidateFileSource AddText(string path, string text)
            => Add(path, System.Text.Encoding.UTF8.GetBytes(text));

        /// <summary>以清单为准写入正确内容（用 ContentHash 计算真实摘要）。</summary>
        public FakeCandidateFileSource AddMatching(ReleaseFileEntry entry, byte[] bytes)
        {
            entry.Length = bytes.LongLength;
            entry.Sha256 = ContentHash.Sha256Hex(bytes);
            return Add(entry.Path, bytes);
        }

        /// <summary>目录枚举结果（null = 不支持枚举）。</summary>
        public IReadOnlyCollection<string> DeclaredPaths { get; set; }

        public ICandidateFile Open(string path)
        {
            Opened.Add(path);
            string key = path?.Replace('\\', '/');
            if (key == null || !_files.TryGetValue(key, out byte[] bytes)) return null;
            return new FakeCandidateFile(key, bytes, FailReads);
        }

        private sealed class FakeCandidateFile : ICandidateFile
        {
            private readonly byte[] _bytes;
            private readonly bool _failRead;

            public string Path { get; }
            public long Length => _bytes.LongLength;

            public FakeCandidateFile(string path, byte[] bytes, bool failRead)
            {
                Path = path;
                _bytes = bytes;
                _failRead = failRead;
            }

            public byte[] ReadAll() => _failRead ? null : _bytes;
        }
    }

    /// <summary>内存磁盘余量端口（L1 假件）。</summary>
    public sealed class FakeDiskSpaceProbe : IDiskSpaceProbe
    {
        public long Available;
        public FakeDiskSpaceProbe(long available) { Available = available; }
        public long GetAvailableBytes() => Available;
    }
}
