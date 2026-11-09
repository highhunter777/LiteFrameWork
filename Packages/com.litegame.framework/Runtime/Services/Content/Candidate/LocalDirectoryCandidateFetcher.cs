using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
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
