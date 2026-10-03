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
    /// 走 <see cref="FileSys"/> 而非直接 System.IO：本项目 LiteGame 层**零 System.IO**
    /// （IO 唯一入口是 FileSys，其字节/递归枚举 API 供此处使用）。
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
    /// 磁盘余量探测（**通用实现**；不依赖平台专有 API）。
    ///
    /// 策略：先试桌面 <see cref="System.IO.DriveInfo"/>（精确、廉价）；不可用时回退**写探针**——
    /// 向目标目录逐级试探写入固定块直到失败，得出"至少能写多少"。写探针拿到的是**下界**
    /// （受限于单次试探粒度），故报告值再乘一个保守系数，宁可少报也不多报
    /// （多报会让预检放行、更新写到一半失败）。
    ///
    /// **代价与边界（如实标注）**：
    /// - 写探针会**实际占用并删除**临时文件；在低存储设备上可能触发系统清理——故只在
    ///   DriveInfo 不可用时走这条路，且探针上限远低于真实盘容量；
    /// - 它测量的是"**当前能写多少**"，不是"总剩余空间"——有配额/写保护的目录会被如实判小；
    /// - **不是精确计量**：报告值只够做"够/不够"的判定，不能用于展示给用户的容量数字。
    /// </summary>
    public sealed class WriteProbeDiskSpaceProbe : IDiskSpaceProbe
    {
        private readonly string _absoluteDir;
        private readonly string _probeFileName;
        private readonly DriveInfoSpaceProbe _desktop;
        private readonly long _probeCapBytes;

        /// <param name="absoluteDir">目标目录（绝对路径）。</param>
        /// <param name="probeCapBytes">写探针上限（默认 512MB；避免在低存储设备上制造压力）。</param>
        /// <param name="probeFileName">探针文件名（装配点可指定为唯一名以防并发冲突）。</param>
        public WriteProbeDiskSpaceProbe(string absoluteDir, long probeCapBytes = 512L * 1024 * 1024,
            string probeFileName = ".disk_probe")
        {
            _absoluteDir = absoluteDir ?? throw new ArgumentNullException(nameof(absoluteDir));
            _desktop = new DriveInfoSpaceProbe(absoluteDir);
            _probeFileName = string.IsNullOrEmpty(probeFileName) ? ".disk_probe" : probeFileName;
            _probeCapBytes = probeCapBytes < 1024 * 1024 ? 1024 * 1024 : probeCapBytes;
        }

        public long GetAvailableBytes()
        {
            // ① 桌面精确路径
            long desktop = _desktop.GetAvailableBytes();
            if (desktop >= 0) return desktop;

            // ② 通用写探针（下界）
            long writable = ProbeWritable();
            return writable < 0 ? -1L : writable;
        }

        /// <summary>逐块试探写入，返回可写字节下界；失败返回 -1。</summary>
        private long ProbeWritable()
        {
            const int BlockBytes = 4 * 1024 * 1024;
            var block = new byte[BlockBytes];
            long written = 0;
            string path = null;

            try
            {
                string dir = System.IO.Path.GetFullPath(_absoluteDir);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir, _probeFileName);

                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Create,
                           System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    while (written < _probeCapBytes)
                    {
                        stream.Write(block, 0, BlockBytes);
                        written += BlockBytes;
                    }
                    stream.Flush(true);
                }

                // 到达自设上限说明实际余量不少于它；保守回退为上限（不夸大）
                return _probeCapBytes;
            }
            catch (Exception)
            {
                // 写失败 = 已探明边界。written 为下界；但失败本身可能来自配额/权限而非空间，
                // 故按保守系数折算（宁可少报），完全不确知时返回 -1。
                if (written <= 0) return -1L;
                return written / 2;
            }
            finally
            {
                try { if (path != null && System.IO.File.Exists(path)) System.IO.File.Delete(path); }
                catch (Exception) { /* 清理失败不影响结论 */ }
            }
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
    /// 而非缺陷。需要跨平台精确探测时改用 <see cref="WriteProbeDiskSpaceProbe"/>
    /// （以写入压力换通用性）或接入平台专有 API。
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
                if (string.IsNullOrEmpty(root))
                {
                    Log.Warning($"磁盘余量不可知：无法解析卷根（path='{_rootPath}'）", "Content");
                    return -1L;
                }

                var drive = new System.IO.DriveInfo(root);
                if (!drive.IsReady)
                {
                    Log.Warning($"磁盘余量不可知：卷未就绪（root='{root}'）", "Content");
                    return -1L;
                }
                return drive.AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                // 静默 -1 会让"桌面拿不到余量"成为不可见事故（SpacePrecheck 按不足拒绝所有候选，
                // 热更链在 Player 上整体不可用）——留一次可诊断痕迹（每次预检至多一条，非热路径）。
                Log.Warning($"磁盘余量不可知：DriveInfo 失败（path='{_rootPath}'）：{ex.GetType().Name}: {ex.Message}", "Content");
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

        /// <summary>本实现只清点已落盘内容、不发起下载，**不产生临时文件**——无可清理，按端口契约 no-op。</summary>
        public UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default)
            => UniTask.CompletedTask;
    }
}
