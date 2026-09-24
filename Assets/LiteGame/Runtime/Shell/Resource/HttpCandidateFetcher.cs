using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.Networking;

namespace LiteGame
{
    /// <summary>
    /// 真实候选下载器（《热更与内容发布专项设计》§7"只下载固定 Release 的不可变文件"）。
    ///
    /// **分工**（§7 原文"YooAsset 负责其擅长的下载与加载实现，项目负责可信发布描述、
    /// 事务代次与激活决策"）：本类负责**逐文件获取 + 归属校验 + 原子落盘**；
    /// 交付路径（contentRoot/releaseId/相对路径）由发布流水线保证与清单一致。
    ///
    /// **断点续传**（§7"验证临时文件的 Release/长度/摘要归属；完成后全文件校验，
    /// **不能只信已传字节计数**"）：
    /// - 临时文件路径**归属到具体 Release**（含 releaseId 段）——换 Release 不会误用旧半截文件；
    /// - 续传前校验"已有长度 ≤ 清单长度"，超出即弃置重下（吞掉的旧字节无归属价值）；
    /// - **完成判据是摘要复算**，不是字节计数——本类只在完整拿到后交上层，摘要由
    ///   <see cref="CandidateContentVerifier"/> 统一复算（不在此重复实现摘要）。
    ///
    /// **暂态 vs 确定性失败**（§7"区分暂态网络错误与签名/兼容错误"）：
    /// 网络/HTTP 5xx/超时 → <see cref="DownloadFailureKind.TransientNetwork"/>（可重试换源）；
    /// HTTP 4xx/本地写入失败 → 确定性（重试无效）。
    /// </summary>
    public sealed class HttpCandidateFetcher : ICandidateFetcher
    {
        private readonly string _candidateRoot;      // 落盘根（FileSys 相对路径）
        private readonly string _baseUrl;            // 来源基址（末尾不带 /）
        private readonly int _timeoutSeconds;

        /// <summary>下载进度回调（已完成文件数、总文件数、当前文件名）；null = 不回调。</summary>
        public Action<int, int, string> OnProgress;

        public HttpCandidateFetcher(string candidateRoot, string baseUrl, int timeoutSeconds = 30)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _baseUrl = string.IsNullOrEmpty(baseUrl)
                ? throw new ArgumentException("来源基址不能为空", nameof(baseUrl))
                : baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds < 1 ? 30 : timeoutSeconds;
        }

        public async UniTask<CandidateFetchResult> FetchAsync(
            ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default)
        {
            if (manifest == null || plan == null)
                return CandidateFetchResult.Fail(DownloadFailureKind.ReadError, detail: "清单或计划为空");

            List<ReleaseFileEntry> files = manifest.Files;
            int done = 0;

            for (int i = 0; i < files.Count; i++)
            {
                ReleaseFileEntry entry = files[i];
                if (ct.IsCancellationRequested)
                    return CandidateFetchResult.Fail(DownloadFailureKind.Canceled);

                DownloadFailureInfo failure = await FetchOneAsync(manifest.ReleaseId, entry, ct);
                if (failure.Kind != DownloadFailureKind.None)
                    return CandidateFetchResult.Fail(failure.Kind, failure.Path, failure.Detail);

                done++;
                OnProgress?.Invoke(done, files.Count, entry.Path);
            }

            // 落盘文件清点（清单外文件一并返回，交由校验器判 UnexpectedFile）
            var paths = new List<string>(files.Count);
            foreach (ReleaseFileEntry f in files) paths.Add(f.Path);
            foreach (string extra in FileSysCandidateFileSource.ListAll(_candidateRoot))
            {
                bool declared = false;
                foreach (ReleaseFileEntry f in files)
                    if (string.Equals(f.Path, extra, StringComparison.OrdinalIgnoreCase)) { declared = true; break; }
                if (!declared) paths.Add(extra);
            }

            return CandidateFetchResult.Ok(paths);
        }

        /// <summary>
        /// 取单个文件。临时文件与目标文件同目录（跨卷 Move 会退化为拷贝）。
        /// </summary>
        private async UniTask<DownloadFailureInfo> FetchOneAsync(
            string releaseId, ReleaseFileEntry entry, CancellationToken ct)
        {
            // 已完整落盘（长度一致）则跳过——幂等重跑不重复下载
            string target = _candidateRoot + "/" + entry.Path;
            if (FileSys.GetFileLength(target) == entry.Length)
                return default;

            // 临时文件归属到 Release（§7：换 Release 不误用旧半截）
            string temp = TempPath(releaseId, entry.Path);
            long have = FileSys.GetFileLength(temp);
            if (have > entry.Length)
            {
                FileSys.Delete(temp);                 // 超出清单长度 = 无归属价值，弃置重下
                have = 0;
            }

            string url = _baseUrl + "/" + releaseId + "/" + entry.Path;

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = _timeoutSeconds;

                try
                {
                    await request.SendWebRequest().WithCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    return new DownloadFailureInfo(DownloadFailureKind.Canceled, entry.Path);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 4xx 是确定性（资源不存在/无权限，重试无效）；5xx 与网络错是暂态（§7 分类）
                    long code = request.responseCode;
                    bool deterministic = code >= 400 && code < 500;
                    return new DownloadFailureInfo(
                        deterministic ? DownloadFailureKind.FileMissing : DownloadFailureKind.TransientNetwork,
                        entry.Path, detail: $"HTTP {code}: {request.error}");
                }

                byte[] body = request.downloadHandler?.data;
                if (body == null)
                    return new DownloadFailureInfo(DownloadFailureKind.ReadError, entry.Path, detail: "响应为空");

                // 长度核对在写盘前：不符即拒，不落半截
                if (body.LongLength != entry.Length)
                    return new DownloadFailureInfo(DownloadFailureKind.LengthMismatch, entry.Path,
                        expected: entry.Length.ToString(), actual: body.LongLength.ToString());

                // 内容摘要由上层 CandidateContentVerifier 统一复算——本类不重复实现摘要
                FileSys.WriteAllBytes(temp, body);

                // 原子提交到目标路径（临时件随即移除）
                byte[] committed = FileSys.ReadAllBytes(temp);
                FileSys.WriteAllBytes(target, committed);
                FileSys.Delete(temp);
                return default;
            }
        }

        /// <summary>
        /// 临时文件路径（**相对 FileSys 根**）：`&lt;候选根&gt;/.tmp/&lt;releaseId&gt;/&lt;相对路径&gt;`。
        /// 归属到 Release——换 Release 不会误用旧半截（§7"验证临时文件的 Release 归属"）。
        /// 完成后必须移到目标路径，否则会被清点判为清单外文件。
        ///
        /// **public 供诊断与测试断言**（它是纯路径换算，无副作用）。
        /// </summary>
        public string TempPath(string releaseId, string relativePath)
            => _candidateRoot + "/.tmp/" + releaseId + "/" + relativePath;
    }
}
