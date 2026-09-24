using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 候选文件访问的 FileSys 适配（《热更与内容发布专项设计》§7）。
    ///
    /// 走 <see cref="FileSys"/> 而非直接 System.IO：本项目 LiteGame 层**零 System.IO**
    /// （IO 唯一入口是 FileSys，见其类注释与纪律扫描）。新增的字节/递归枚举 API 即为此处补足。
    /// </summary>
    public sealed class FileSysCandidateFileSource : ICandidateFileSource
    {
        public ICandidateFile Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            long len = FileSys.GetFileLength(path);
            if (len < 0) return null;                     // 不存在 = null（调用方按缺失处理）
            return new FileSysCandidateFile(path, len);
        }

        /// <summary>候选根下全部文件的相对路径（双向差异的"多"一侧）。</summary>
        public static IReadOnlyList<string> ListAll(string rootDir)
            => FileSys.GetFilesRecursive(rootDir);

        private sealed class FileSysCandidateFile : ICandidateFile
        {
            public string Path { get; }
            public long Length { get; }

            public FileSysCandidateFile(string path, long length)
            {
                Path = path;
                Length = length;
            }

            public byte[] ReadAll() => FileSys.ReadAllBytes(Path);
        }
    }

    /// <summary>
    /// 候选磁盘余量探测（§7"空间预检"）。
    ///
    /// **诚实实现**：.NET Standard 没有跨平台的"目录可用空间" API
    /// （`DriveInfo.AvailableFreeSpace` 只在桌面 .NET 可用，Unity 的 IL2CPP/移动端不保证）。
    /// 本项目当前不引入平台插件，故返回 **-1 = 不可知**——
    /// 而 <see cref="SpacePrecheck"/> 对"不可知"按**不足**处理（不会让更新在写入中途失败）。
    ///
    /// 这意味着**当前形态下空间预检会拒绝所有候选**：这是刻意的 fail-closed，
    /// 而非缺陷。接通真实平台探测（Android/iOS 原生命令）后本实现替换，属 H3-b。
    /// </summary>
    public sealed class UnavailableDiskSpaceProbe : IDiskSpaceProbe
    {
        public long GetAvailableBytes() => -1L;
    }

    /// <summary>
    /// 本地目录候选获取（§7 的最小实现）。
    ///
    /// **定位**：把"已落盘的候选文件"按清单清点出来交给编排——覆盖
    /// **本地/内置候选**与 **已由其它通道下载完成的候选**两种场景。
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
            var paths = new List<string>();
            foreach (ReleaseFileEntry f in manifest.Files)
            {
                if (ct.IsCancellationRequested)
                    return UniTask.FromResult(CandidateFetchResult.Fail(DownloadFailureKind.Canceled));

                string full = _candidateRoot + "/" + f.Path;
                if (FileSys.GetFileLength(full) < 0)
                    return UniTask.FromResult(CandidateFetchResult.Fail(
                        DownloadFailureKind.FileMissing, f.Path,
                        detail: $"候选根下不存在：{full}"));
                paths.Add(full);
            }

            // 追加清单外文件（若候选根有）——路径以候选根为前缀，校验器按清单比对时会判 UnexpectedFile
            foreach (string extra in FileSysCandidateFileSource.ListAll(_candidateRoot))
            {
                // 已知项已按 full 形式加入；此处找出不在清单内的
                bool declared = false;
                foreach (ReleaseFileEntry f in manifest.Files)
                {
                    if (string.Equals(_candidateRoot + "/" + f.Path, extra, StringComparison.OrdinalIgnoreCase))
                    {
                        declared = true;
                        break;
                    }
                }
                if (!declared) paths.Add(extra);
            }

            return UniTask.FromResult(CandidateFetchResult.Ok(paths));
        }
    }
}
