using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 候选文件访问的 FileSystem 适配（《热更与内容发布专项设计》§7）。
    /// </summary>
    public sealed class FileSystemCandidateFileSource : ICandidateFileSource
    {
        private readonly string _rootDir;
        private readonly IFileStore _files;

        /// <param name="rootDir">候选根（IFileStore 相对路径）。</param>
        public FileSystemCandidateFileSource(string rootDir, IFileStore files = null)
        {
            _rootDir = string.IsNullOrEmpty(rootDir)
                ? throw new ArgumentException("候选根不能为空", nameof(rootDir))
                : rootDir.TrimEnd('/');
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public ICandidateFile Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string full = _rootDir + "/" + path;
            long len = _files.GetFileLength(full);
            if (len < 0) return null;                     // 不存在 = null（调用方按缺失处理）
            return new FileSystemCandidateFile(_files, full, path, len);
        }

        /// <summary>候选根下全部文件的**相对路径**（双向差异的"多"一侧；注入端口版本）。</summary>
        public static IReadOnlyList<string> ListAll(IFileStore files, string rootDir)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            string prefix = rootDir.TrimEnd('/') + "/";
            string[] full = files.GetFilesRecursive(rootDir);
            var result = new List<string>(full.Length);
            foreach (string f in full)
                result.Add(f.StartsWith(prefix, StringComparison.Ordinal) ? f.Substring(prefix.Length) : f);
            return result;
        }

        private sealed class FileSystemCandidateFile : ICandidateFile
        {
            /// <summary>清单路径（相对候选根）——校验诊断用。</summary>
            public string Path { get; }

            private readonly string _fullPath;
            private readonly IFileStore _files;

            public long Length { get; }

            public FileSystemCandidateFile(IFileStore files, string fullPath, string declaredPath, long length)
            {
                _files = files;
                _fullPath = fullPath;
                Path = declaredPath;
                Length = length;
            }

            public byte[] ReadAll() => _files.ReadAllBytes(_fullPath);
        }
    }
}
