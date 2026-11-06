using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 候选内容获取结果。
    /// </summary>
    public readonly struct CandidateFetchResult
    {
        public readonly bool Succeeded;
        public readonly DownloadFailureInfo Failure;

        /// <summary>落盘文件路径集合（相对候选根，正斜杠）。成功时非空。</summary>
        public readonly IReadOnlyList<string> Paths;

        public CandidateFetchResult(bool succeeded, DownloadFailureInfo failure, IReadOnlyList<string> paths)
        {
            Succeeded = succeeded;
            Failure = failure;
            Paths = paths;
        }

        public static CandidateFetchResult Ok(IReadOnlyList<string> paths)
            => new CandidateFetchResult(true, default, paths);

        public static CandidateFetchResult Fail(DownloadFailureKind kind, string path = null, string detail = null)
            => new CandidateFetchResult(false, new DownloadFailureInfo(kind, path, detail: detail), null);
    }
}
