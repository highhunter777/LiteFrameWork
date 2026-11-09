using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 候选内容获取端口：只下载固定 Release 的不可变文件。
    /// </summary>
    public interface ICandidateFetcher
    {
        UniTask<CandidateFetchResult> FetchAsync(
            ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default);

        /// <summary>
        /// 清理属于指定发布的临时文件。
        /// </summary>
        UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default);
    }
}
