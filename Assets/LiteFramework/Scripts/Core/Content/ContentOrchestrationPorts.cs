using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 候选内容获取结果（《热更与内容发布专项设计》§7）。
    ///
    /// <see cref="Paths"/> 是**实际落盘的文件路径集合**——交由
    /// <see cref="CandidateContentVerifier"/> 做双向差异核对（清单外杂散文件检出）
    /// 与逐文件摘要复算。获取成功**不等于**校验通过：本结果只表示字节已就位。
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
    /// 候选内容获取端口（§7"只下载固定 Release 的不可变文件"）。
    ///
    /// **为什么是独立端口而非复用 <see cref="ICandidateFileSource"/>**：
    /// 获取**有网络 IO 特性**（HTTP/Host 模式），而这属于装配层——`RoomServer/Runtime` 与
    /// `LiteFramework.Core` 的纯化纪律禁止核心逻辑直接引用 HTTP/传输。
    /// 端口分离让编排（本批）保持纯逻辑可 L1 覆盖，真实下载适配（H3-b）留在装配层。
    ///
    /// 实现方职责：按 <see cref="DownloadPlan"/> 的选定来源取字节、落到候选根；
    /// 返回实际落盘路径集合供上层核对；**不得自行省略清单内文件**（缺失由上层按 FileMissing 处理）。
    /// </summary>
    public interface ICandidateFetcher
    {
        UniTask<CandidateFetchResult> FetchAsync(
            ReleaseManifest manifest, DownloadPlan plan, CancellationToken ct = default);

        /// <summary>
        /// 清理属于指定发布的临时文件（§8 表行 1"清理/恢复属于该候选的临时文件"）。
        ///
        /// 调用场景：① 启动恢复发现上次在途候选——按记录里的发布身份回收其临时归属；
        /// ② 编排失败收尾——获取/校验已终止的候选不留半截文件。
        ///
        /// **幂等且不抛**：无可清理时 no-op；清理失败由实现方吞掉——清理是恢复的次要目标，
        /// 不得阻断"以 Confirmed 继续"的主流程（残留垃圾在按发布隔离的目录内，
        /// 下次同发布清理幂等重试）。
        /// </summary>
        UniTask CleanupTempAsync(string releaseId, CancellationToken ct = default);
    }

    /// <summary>
    /// 候选内容健康确认端口（§8"健康确认至少覆盖候选 ConfigSnapshot、Lua/main、
    /// 全部必需注册表、关键 UI/入口及其资源"）。
    ///
    /// 是否健康由装配方按上表逐项判定；本端口只承载结论——编排不重复实现探针
    /// （那些探针依赖真资源/Lua env，属 H3-e）。
    /// </summary>
    public interface ICandidateHealthCheck
    {
        /// <summary>对**已校验通过**的候选做健康确认；返回失败说明（null/空 = 健康）。</summary>
        UniTask<string> CheckAsync(ReleaseManifest candidate, CancellationToken ct = default);
    }

    /// <summary>
    /// 内容激活端口（§8 安全窗口："停止接受受影响的新操作，退出相关 Match/Scene/UI Scope，
    /// 取消在途请求，解除旧 Lua 回调和资源引用"）。
    ///
    /// 编排在 <see cref="PatchCoordinator"/> 的健康确认**之前**调用 <see cref="ActivateAsync"/>：
    /// 顺序错误会让"健康检查失败后回退"无法复原已经切换过的运行时。
    /// </summary>
    public interface IContentActivator
    {
        /// <summary>在安全窗口内切换到候选内容。</summary>
        UniTask ActivateAsync(ReleaseManifest candidate, CancellationToken ct = default);

        /// <summary>健康检查失败时重建到已确认版本（§8"从允许的 Confirmed 版本重建，不使用半成品"）。</summary>
        UniTask RebuildConfirmedAsync(ContentGeneration confirmed, CancellationToken ct = default);
    }

    /// <summary>
    /// 内容代次推进端口（§9"ContentGeneration 持有固定资源目录、脚本集合、配置快照及依赖 ID"）。
    ///
    /// **为什么需要独立端口**：<c>YooAssetContentService._generation</c> 当前只在
    /// <c>InitializeAsync</c> 里被设为 Default 且**无 setter**——代次维度已冻结为加载键的一部分
    /// 但从未切换过。健康的候选确认后必须把当前代次推进到它，否则新内容永远不会被加载。
    /// </summary>
    public interface IGenerationSink
    {
        void Advise(ContentGeneration generation);
    }

    /// <summary>
    /// 一份候选提案（§6"先验证描述的结构/预算与签名，再依据可信描述计划下载"）。
    ///
    /// <see cref="SignedBytes"/> 是**描述文件的原始字节**（签名对象，不做换行归一化——§5）；
    /// <see cref="Signature"/> 是解码后的签名字节。三者一起交给校验器，
    /// **不得由提供方代判是否可信**（信任判定只在 <see cref="ReleaseManifestValidator"/>）。
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
    /// 候选来源端口（§6/§7）。取回"有没有新发布、它是什么"——**不负责判断可不可信**。
    ///
    /// 实现方可读本地目录、可查询发布服务；无论哪种，返回的都是**未经信任的**提案，
    /// 由 <see cref="ReleaseManifestValidator"/> 校验后才可依据其内容行动。
    /// 真实 CDN 通道属 H3-b，不在本端口的最小实现内。
    /// </summary>
    public interface ICandidateProvider
    {
        /// <summary>取候选；无候选返回 <see cref="CandidateOffer.IsEmpty"/> 为 true 的结果（不是异常）。</summary>
        UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default);
    }
}
