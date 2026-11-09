using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.Networking;

namespace LiteFramework
{
    /// <summary>
    /// 真实候选下载器（《热更与内容发布专项设计》§7"只下载固定 Release 的不可变文件"）。
    /// </summary>
    public sealed class HttpCandidateFetcher : ICandidateFetcher
    {
        private readonly string _candidateRoot;      // 落盘根（IFileStore 相对路径）
        private readonly int _timeoutSeconds;
        private readonly IFileStore _files;

        /// <summary>下载进度回调（已完成文件数、总文件数、当前文件名）；null = 不回调。</summary>
        public Action<int, int, string> OnProgress;

        public HttpCandidateFetcher(string candidateRoot, int timeoutSeconds = 30,
            IFileStore files = null)
        {
            _candidateRoot = string.IsNullOrEmpty(candidateRoot)
                ? throw new ArgumentException("候选根不能为空", nameof(candidateRoot))
                : candidateRoot.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds < 1 ? 30 : timeoutSeconds;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
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

                DownloadFailureInfo failure = await FetchOneAsync(manifest.ReleaseId, entry, plan, ct);
                if (failure.Kind != DownloadFailureKind.None)
                    return CandidateFetchResult.Fail(failure.Kind, failure.Path, failure.Detail);

                done++;
                OnProgress?.Invoke(done, files.Count, entry.Path);
            }

            // 落盘文件清点（清单外文件一并返回，交由校验器判 UnexpectedFile）。
            var paths = new List<string>(files.Count);
            foreach (ReleaseFileEntry f in files) paths.Add(f.Path);
            foreach (string extra in FileSystemCandidateFileSource.ListAll(_files, _candidateRoot))
            {
                bool declared = false;
                foreach (ReleaseFileEntry f in files)
                    if (string.Equals(f.Path, extra, StringComparison.OrdinalIgnoreCase)) { declared = true; break; }
                if (!declared) paths.Add(extra);
            }

            return CandidateFetchResult.Ok(paths);
        }

        /// <summary>
        /// 回收该发布的临时目录（<see cref="TempPath"/> 的 Release 隔离根： <c>&lt;候选根&gt;/.tmp/&lt;releaseId&gt。
        /// </summary>
        public UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(releaseId)) return UniTask.CompletedTask;
            try
            {
                _files.DeleteDirectory(_candidateRoot + "/.tmp/" + releaseId);
            }
            catch (Exception)
            {
                // 端口契约：不抛。清理失败只影响磁盘整洁度，不影响恢复决策与允许版本。
            }
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 取单个文件：已完整落盘则跳过（幂等重跑不重复下载）；否则按计划逐源尝试。
        /// </summary>
        private async UniTask<DownloadFailureInfo> FetchOneAsync(
            string releaseId, ReleaseFileEntry entry, DownloadPlan plan, CancellationToken ct)
        {
            // 已完整落盘（长度一致）则跳过——幂等重跑不重复下载。
            string target = _candidateRoot + "/" + entry.Path;
            if (_files.GetFileLength(target) == entry.Length)
                return default;

            // 临时文件归属到 Release（§7：换 Release 不误用旧半截）。
            string temp = TempPath(releaseId, entry.Path);
            long have = _files.GetFileLength(temp);
            if (have > entry.Length)
                _files.Delete(temp);                 // 超出清单长度 = 无归属价值，弃置重下

            while (true)
            {
                // 选源（含预算判定）：暂态失败轮转换源，确定性失败不在此路径（下方即止）。
                if (!plan.TrySelect(entry.Path, out DownloadPlanEntry selected, out DownloadFailureInfo selectFailure))
                    return selectFailure;             // 达尝试上限/不在清单 → SourceUnavailable

                string url = selected.Source.BaseLocation + "/" + releaseId + "/" + entry.Path;
                DownloadFailureInfo failure = await FetchFromUrl(url, entry, temp, target, ct);
                if (failure.Kind == DownloadFailureKind.None)
                    return default;                   // 该文件完成（摘要复算归上层校验器）

                if (!failure.IsTransient) return failure;               // 确定性：重试与换源无效
                if (!plan.RecordAttempt(entry.Path, failure)) return failure;   // 预算耗尽：末次失败如实上报

                int backoffMs = plan.BackoffMs(selected.Attempt);
                if (backoffMs > 0)
                {
                    try
                    {
                        await UniTask.Delay(backoffMs, cancellationToken: ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return new DownloadFailureInfo(DownloadFailureKind.Canceled, entry.Path);
                    }
                }
            }
        }

        /// <summary>对单个 URL 做一次完整获取：传输 → 长度核对 → 原子提交（临时件随即移除）。</summary>
        private async UniTask<DownloadFailureInfo> FetchFromUrl(
            string url, ReleaseFileEntry entry, string temp, string target, CancellationToken ct)
        {
            using (var request = UnityWebRequest.Get(url))
            using (OperationScope scope = OperationScope.Start(
                       "content.fetch", entry.Path, TimeSpan.FromSeconds(_timeoutSeconds), ct))
            {
                try
                {
                    await request.SendWebRequest().WithCancellation(scope.Token);
                }
                catch (OperationCanceledException)
                {
                    // 超时与调用方取消都表现为取消；按作用域原因区分（超时是暂态，可重试/换源）。
                    return scope.IsTimedOut
                        ? new DownloadFailureInfo(DownloadFailureKind.TransientNetwork, entry.Path,
                            detail: $"超时 {_timeoutSeconds}s")
                        : new DownloadFailureInfo(DownloadFailureKind.Canceled, entry.Path);
                }
                catch (UnityWebRequestException ex)
                {
                    // UniTask 的 WithCancellation 在非 2xx 时**自己抛**（不是返回失败结果），
                    // 故分类必须在这里做——await 之后的 request.result 检查对错误路径不可达。
                    return Classify(ex.ResponseCode, ex.Message, entry.Path);
                }

                if (request.result != UnityWebRequest.Result.Success)
                    return Classify(request.responseCode, request.error, entry.Path);

                byte[] body = request.downloadHandler?.data;
                if (body == null)
                    return new DownloadFailureInfo(DownloadFailureKind.ReadError, entry.Path, detail: "响应为空");

                // 长度核对在写盘前：不符即拒，不落半截。
                if (body.LongLength != entry.Length)
                    return new DownloadFailureInfo(DownloadFailureKind.LengthMismatch, entry.Path,
                        expected: entry.Length.ToString(), actual: body.LongLength.ToString());

                // 内容摘要由上层 CandidateContentVerifier 统一复算——本类不重复实现摘要。
                _files.WriteAllBytes(temp, body);

                // 原子提交到目标路径（临时件随即移除）。
                byte[] committed = _files.ReadAllBytes(temp);
                _files.WriteAllBytes(target, committed);
                _files.Delete(temp);
                return default;
            }
        }

        /// <summary>
        /// 传输失败分类（§7"区分暂态网络错误与签名/兼容错误"）。
        /// </summary>
        internal static DownloadFailureInfo Classify(long responseCode, string error, string path)
        {
            bool deterministic = responseCode >= 400 && responseCode < 500;
            return new DownloadFailureInfo(
                deterministic ? DownloadFailureKind.FileMissing : DownloadFailureKind.TransientNetwork,
                path, detail: $"HTTP {responseCode}: {error}");
        }

        /// <summary>
        /// 临时文件路径（**相对 FileSystem 根**）：`&lt;候选根&gt;/.tmp/&lt;releaseId&gt;/&lt;相对路径&gt;`。
        /// </summary>
        public string TempPath(string releaseId, string relativePath)
            => _candidateRoot + "/.tmp/" + releaseId + "/" + relativePath;
    }
}
