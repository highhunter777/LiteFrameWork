using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

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

    /// <summary>
    /// 从 FileSystem 读候选信封（§6）。只读**一个**约定路径：存在即返回提案，不存在即无候选。
    /// 真实发布通道（CDN/发布服务）属 H3-b，本实现**不发起任何网络请求**。
    /// </summary>
    public sealed class FileSystemCandidateProvider : ICandidateProvider
    {
        /// <summary>约定候选信封路径（FileSystem 相对路径）。</summary>
        public const string DefaultOfferPath = "content/candidate.json";

        private readonly string _offerPath;
        private readonly IFileStore _files;

        public FileSystemCandidateProvider(string offerPath = DefaultOfferPath, IFileStore files = null)
        {
            _offerPath = string.IsNullOrEmpty(offerPath) ? DefaultOfferPath : offerPath;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromResult(default(CandidateOffer));
            if (!_files.Exists(_offerPath)) return UniTask.FromResult(default(CandidateOffer));

            string json = _files.ReadAllText(_offerPath);
            return UniTask.FromResult(SignedManifestEnvelope.Parse(json));
        }
    }

    /// <summary>
    /// 本地目录候选获取（§7 的最小实现）。
    /// </summary>
    public sealed class LocalDirectoryCandidateFetcher : ICandidateFetcher
    {
        private readonly string _candidateRoot;
        private readonly IFileStore _files;

        /// <param name="candidateRoot">候选根（IFileStore 相对路径）。</param>
        public LocalDirectoryCandidateFetcher(string candidateRoot, IFileStore files = null)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public UniTask<CandidateFetchResult> FetchAsync(
            ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromResult(CandidateFetchResult.Fail(DownloadFailureKind.Canceled));

            if (!_files.DirectoryExists(_candidateRoot))
                return UniTask.FromResult(CandidateFetchResult.Fail(
                    DownloadFailureKind.SourceUnavailable, detail: $"候选根不存在：{_candidateRoot}"));

            // 逐清单项核对落盘存在性；**清单外文件一并返回**，交由 CandidateContentVerifier 做双向差异——本实现不替校验器下结论。
            var paths = new List<string>();
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (ct.IsCancellationRequested)
                    return UniTask.FromResult(CandidateFetchResult.Fail(DownloadFailureKind.Canceled));

                if (_files.GetFileLength(_candidateRoot + "/" + f.Path) < 0)
                    return UniTask.FromResult(CandidateFetchResult.Fail(
                        DownloadFailureKind.FileMissing, f.Path,
                        detail: $"候选根下不存在：{_candidateRoot}/{f.Path}"));
                paths.Add(f.Path);
            }

            // 追加清单外文件（路径同样相对候选根）——校验器按清单比对时会判 UnexpectedFile。
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReleaseFileEntry f in manifest.Files) declared.Add(f.Path);

            foreach (string extra in FileSystemCandidateFileSource.ListAll(_files, _candidateRoot))
                if (!declared.Contains(extra)) paths.Add(extra);

            return UniTask.FromResult(CandidateFetchResult.Ok(paths));
        }

        /// <summary>本实现只清点已落盘内容、不发起下载，**不产生临时文件**——无可清理，按端口契约 no-op。</summary>
        public UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default)
            => UniTask.CompletedTask;
    }
}
