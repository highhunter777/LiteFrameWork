using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 候选文件访问的 FileSys 适配（《热更与内容发布专项设计》§7）。
    ///
    /// **路径契约**：<see cref="ICandidateFileSource.Open"/> 收到的是**相对候选根的路径**
    /// （即 <see cref="ReleaseFileEntry.Path"/> 原样）——本类内部拼上候选根解析。
    /// 这样 <see cref="CandidateContentVerifier"/> 保持"零根路径概念"的纯规则，
    /// 根路径只在装配层出现一次。
    ///
    /// 走 <see cref="FileSys"/> 而非直接 <see cref="System.IO"/>：IO 唯一入口是 FileSys
    /// （本程序集内不出现直接 System.IO 调用——磁盘余量探测同此纪律，见 DiskSpaceProbes）。
    /// </summary>
    public sealed class FileSysCandidateFileSource : ICandidateFileSource
    {
        private readonly string _rootDir;

        /// <param name="rootDir">候选根（FileSys 相对路径）。</param>
        public FileSysCandidateFileSource(string rootDir)
        {
            _rootDir = string.IsNullOrEmpty(rootDir)
                ? throw new ArgumentException("候选根不能为空", nameof(rootDir))
                : rootDir.TrimEnd('/');
        }

        public ICandidateFile Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string full = _rootDir + "/" + path;
            long len = FileSys.GetFileLength(full);
            if (len < 0) return null;                     // 不存在 = null（调用方按缺失处理）
            return new FileSysCandidateFile(full, path, len);
        }

        /// <summary>候选根下全部文件的**相对路径**（双向差异的"多"一侧）。</summary>
        public static IReadOnlyList<string> ListAll(string rootDir)
        {
            string prefix = rootDir.TrimEnd('/') + "/";
            string[] full = FileSys.GetFilesRecursive(rootDir);
            var result = new List<string>(full.Length);
            foreach (string f in full)
                result.Add(f.StartsWith(prefix, StringComparison.Ordinal) ? f.Substring(prefix.Length) : f);
            return result;
        }

        private sealed class FileSysCandidateFile : ICandidateFile
        {
            /// <summary>清单路径（相对候选根）——校验诊断用。</summary>
            public string Path { get; }

            private readonly string _fullPath;

            public long Length { get; }

            public FileSysCandidateFile(string fullPath, string declaredPath, long length)
            {
                _fullPath = fullPath;
                Path = declaredPath;
                Length = length;
            }

            public byte[] ReadAll() => FileSys.ReadAllBytes(_fullPath);
        }
    }

    /// <summary>
    /// 从 FileSys 读候选信封（§6）。只读**一个**约定路径：存在即返回提案，不存在即无候选。
    /// 真实发布通道（CDN/发布服务）属 H3-b，本实现**不发起任何网络请求**。
    /// </summary>
    public sealed class FileSystemCandidateProvider : ICandidateProvider
    {
        /// <summary>约定候选信封路径（FileSys 相对路径）。</summary>
        public const string DefaultOfferPath = "content/candidate.json";

        private readonly string _offerPath;

        public FileSystemCandidateProvider(string offerPath = DefaultOfferPath)
        {
            _offerPath = string.IsNullOrEmpty(offerPath) ? DefaultOfferPath : offerPath;
        }

        public UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromResult(default(CandidateOffer));
            if (!FileSys.Exists(_offerPath)) return UniTask.FromResult(default(CandidateOffer));

            string json = FileSys.ReadAllText(_offerPath);
            return UniTask.FromResult(SignedManifestEnvelope.Parse(json));
        }
    }

    /// <summary>
    /// 本地目录候选获取（§7 的最小实现）。
    ///
    /// **定位**：把"已落盘的候选文件"按清单清点出来交给编排——覆盖
    /// **本地/内置候选**与**已由其它通道下载完成的候选**两种场景。
    /// **真实 CDN 下载**（HTTP/Host 模式、断点续传、多源）属 H3-b，不在本实现内，
    /// 也不伪造：本实现不发起任何网络请求。
    /// </summary>
    public sealed class LocalDirectoryCandidateFetcher : ICandidateFetcher
    {
        private readonly string _candidateRoot;

        /// <param name="candidateRoot">候选根（FileSys 相对路径）。</param>
        public LocalDirectoryCandidateFetcher(string candidateRoot)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
        }

        public UniTask<CandidateFetchResult> FetchAsync(
            ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return UniTask.FromResult(CandidateFetchResult.Fail(DownloadFailureKind.Canceled));

            if (!FileSys.DirectoryExists(_candidateRoot))
                return UniTask.FromResult(CandidateFetchResult.Fail(
                    DownloadFailureKind.SourceUnavailable, detail: $"候选根不存在：{_candidateRoot}"));

            // 逐清单项核对落盘存在性；**清单外文件一并返回**，交由 CandidateContentVerifier
            // 做双向差异——本实现不替校验器下结论。
            // 返回的是**相对候选根的路径**，与校验器的清单口径一致（见 FileSysCandidateFileSource 路径契约）。
            var paths = new List<string>();
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (ct.IsCancellationRequested)
                    return UniTask.FromResult(CandidateFetchResult.Fail(DownloadFailureKind.Canceled));

                if (FileSys.GetFileLength(_candidateRoot + "/" + f.Path) < 0)
                    return UniTask.FromResult(CandidateFetchResult.Fail(
                        DownloadFailureKind.FileMissing, f.Path,
                        detail: $"候选根下不存在：{_candidateRoot}/{f.Path}"));
                paths.Add(f.Path);
            }

            // 追加清单外文件（路径同样相对候选根）——校验器按清单比对时会判 UnexpectedFile。
            // 比对口径 **Ordinal**：清单路径由发布方生成、大小写受控；忽略大小写会在
            // 大小写敏感的目标文件系统上把"同名不同大小写"判为已声明而漏报差异。
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReleaseFileEntry f in manifest.Files) declared.Add(f.Path);

            foreach (string extra in FileSysCandidateFileSource.ListAll(_candidateRoot))
                if (!declared.Contains(extra)) paths.Add(extra);

            return UniTask.FromResult(CandidateFetchResult.Ok(paths));
        }

        /// <summary>本实现只清点已落盘内容、不发起下载，**不产生临时文件**——无可清理，按端口契约 no-op。</summary>
        public UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default)
            => UniTask.CompletedTask;
    }
}
