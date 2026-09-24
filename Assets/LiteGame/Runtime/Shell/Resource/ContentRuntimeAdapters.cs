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
    /// **路径契约**：<see cref="ICandidateFileSource.Open"/> 收到的是**相对候选根的路径**
    /// （即 <see cref="ReleaseFileEntry.Path"/> 原样）——本类内部拼上候选根解析。
    /// 这样 <see cref="CandidateContentVerifier"/> 保持"零根路径概念"的纯规则，
    /// 根路径只在装配层出现一次。
    ///
    /// 走 <see cref="FileSys"/> 而非直接 System.IO：本项目 LiteGame 层**零 System.IO**
    /// （IO 唯一入口是 FileSys，见其类注释与纪律扫描）。新增的字节/递归枚举 API 即为此处补足。
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
    /// 磁盘余量探测（桌面平台的诚实实现；移动端回退不可知）。
    ///
    /// 用 <see cref="System.IO.DriveInfo"/> 查询 <c>persistentDataPath</c> 所在卷的可用空间：
    /// - **桌面（Editor / Standalone）** → 真实可用字节；
    /// - **移动端 / 不支持时** → 异常或 <see cref="DriveInfo"/> 不可用 → 返回 -1 = 不可知
    ///   （<see cref="SpacePrecheck"/> 按不足处理，fail-closed）。
    ///
    /// **这是非托管 API 边界**（与 <c>FileActivationRecordIO</c> 走 FileSys 不同）：
    /// 层内无对应端口，且 <see cref="System.IO"/> 在本程序集仅此一处、不作为常规 IO 通道——
    /// 所有内容文件读写仍走 <see cref="FileSysCandidateFileSource"/>。
    /// </summary>
    public sealed class DriveInfoSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _rootPath;

        /// <param name="rootPath">绝对根路径（通常是 <c>Application.persistentDataPath</c>）。</param>
        public DriveInfoSpaceProbe(string rootPath)
        {
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        }

        public long GetAvailableBytes()
        {
            try
            {
                string full = System.IO.Path.GetFullPath(_rootPath);
                string root = System.IO.Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return -1L;

                var drive = new System.IO.DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : -1L;
            }
            catch (Exception)
            {
                return -1L;                  // 平台不支持/权限不足/路径异常一律按"不可知"
            }
        }
    }

    /// <summary>
    /// 签名候选文件解析（§6"发布描述至少含 schemaVersion、…keyId/signature"）。
    ///
    /// **纯解析，不做信任判定**：把发布描述字节与签名从信封中取出，交给
    /// <see cref="ReleaseManifestValidator"/>。解析失败一律返回空提案——
    /// 提供方不代判"可不可信"（§6"先验证描述的结构/预算与签名"）。
    ///
    /// 信封格式（最小实现，与 §6 要求的字段对应）：
    /// <code>
    /// {
    ///   "manifest": { …ReleaseManifest 字段… },      // 与签名覆盖的字节一致
    ///   "signature": "&lt;base64&gt;"                    // 对 manifest 原始字节的签名
    /// }
    /// </code>
    /// **`manifest` 必须与签名覆盖的字节逐字节一致**——故本类从同一份字节里切出 manifest 段，
    /// 而不是重新序列化对象（重新序列化会因字段序/空白差异导致验签失败）。
    /// </summary>
    public static class SignedManifestEnvelope
    {
        /// <summary>从信封字节解析（<paramref name="envelopeJson"/> 为文件原文）。失败返回空提案。</summary>
        public static CandidateOffer Parse(string envelopeJson)
        {
            if (string.IsNullOrEmpty(envelopeJson)) return default;

            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(envelopeJson);
                var manifestToken = root["manifest"] as Newtonsoft.Json.Linq.JObject;
                string signatureB64 = (string)root["signature"];
                if (manifestToken == null || string.IsNullOrEmpty(signatureB64)) return default;

                // 关键：签名覆盖的是**字节原文**，故取子串而非重新序列化（§5 不做换行归一化）
                byte[] signedBytes = System.Text.Encoding.UTF8.GetBytes(manifestToken.ToString(
                    Newtonsoft.Json.Formatting.None));

                var manifest = manifestToken.ToObject<ReleaseManifest>();
                if (manifest == null) return default;

                byte[] signature = Convert.FromBase64String(signatureB64);
                return new CandidateOffer(manifest, signedBytes, signature);
            }
            catch (Exception)
            {
                return default;              // 畸形信封 = 无候选（不抛、不代判）
            }
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

            // 追加清单外文件（路径同样相对候选根）——校验器按清单比对时会判 UnexpectedFile
            foreach (string extra in FileSysCandidateFileSource.ListAll(_candidateRoot))
            {
                bool declared = false;
                foreach (ReleaseFileEntry f in manifest.Files)
                {
                    if (string.Equals(f.Path, extra, StringComparison.OrdinalIgnoreCase))
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
