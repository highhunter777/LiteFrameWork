using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

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

    /// <summary>
    /// 候选内容健康确认端口：健康确认至少覆盖候选 ConfigSnapshot、Lua/main、 全部必需注册表、关键 UI/入口及其资源。
    /// </summary>
    public interface ICandidateHealthCheck
    {
        /// <summary>对**已校验通过**的候选做健康确认；返回失败说明（null/空 = 健康）。</summary>
        UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default);
    }

    /// <summary>
    /// 内容激活端口：安全窗口内"停止接受受影响的新操作，退出相关 Match/Scene/UI Scope， 取消在途请求，解除旧 Lua 回调和资源引用"。
    /// </summary>
    public interface IContentActivator
    {
        /// <summary>在安全窗口内切换到候选内容。</summary>
        UniTask ActivateAsync(ReleaseManifest candidate, CancellationToken ct = default);

        /// <summary>健康检查失败时重建到已确认版本（从允许的 Confirmed 版本重建，不使用半成品）。</summary>
        UniTask RebuildConfirmedAsync(ContentGeneration confirmed, CancellationToken ct = default);
    }

    /// <summary>
    /// 内容代次推进端口：ContentGeneration 持有固定资源目录、脚本集合、配置快照及依赖 ID。
    /// </summary>
    public interface IGenerationSink
    {
        void Advise(ContentGeneration generation);
    }

    /// <summary>
    /// 一份候选提案：先验证描述的结构/预算与签名，再依据可信描述计划下载。
    /// </summary>
    public readonly struct CandidateOffer
    {
        public readonly ReleaseManifest Manifest;
        public readonly byte[] SignedBytes;
        public readonly byte[] Signature;

        public CandidateOffer(ReleaseManifest manifest, byte[] signedBytes, byte[] signature)
        {
            Manifest = manifest;
            SignedBytes = signedBytes;
            Signature = signature;
        }

        /// <summary>无候选（已是最新 / 无发布通道）。</summary>
        public bool IsEmpty => Manifest == null;
    }

    /// <summary>
    /// 候选来源端口。
    /// </summary>
    public interface ICandidateProvider
    {
        /// <summary>取候选；无候选返回 <see cref="CandidateOffer.IsEmpty"/> 为 true 的结果（不是异常）。</summary>
        UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default);
    }
}
