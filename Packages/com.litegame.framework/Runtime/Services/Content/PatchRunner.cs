using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 内容激活器（§8 安全窗口）。
    /// </summary>
    public sealed class ContentActivator : IContentActivator
    {
        private readonly IContentService _content;
        private readonly Action _enterSafeWindow;

        /// <param name="content">内容服务（重建用）。</param>
        /// <param name="enterSafeWindow">进入安全窗口的钩子（可空；Scope 退出/在途取消的接线点）。</param>
        public ContentActivator(IContentService content, Action enterSafeWindow = null)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _enterSafeWindow = enterSafeWindow;
        }

        public UniTask ActivateAsync(ReleaseManifest candidate, CancellationToken ct = default)
        {
            _enterSafeWindow?.Invoke();       // §8：安全窗口在此进入（当前无 Match 运行，为空实现）
            return UniTask.CompletedTask;
        }

        public async UniTask RebuildConfirmedAsync(ContentGeneration confirmed, CancellationToken ct = default)
        {
            // 释放旧运行时 → 重新初始化（§8"从允许的 Confirmed 版本重建，不使用半成品"）。
            await _content.ShutdownAsync(ct);
            await _content.InitializeAsync(ct);
        }
    }

    /// <summary>
    /// 补丁运行的编排器（§4 流程图）。
    /// </summary>
    public sealed class PatchRunner
    {
        private readonly ICandidateProvider _provider;
        private readonly PlayerCapabilities _player;
        private readonly ActivationTransactionStore _store;
        private readonly PatchCoordinator _coordinator;
        private readonly ReleaseBudget _budget;
        private readonly Func<string, ISignatureVerifier> _verifierResolver;
        private readonly Func<ReleaseManifest, SpaceCheckRequest> _spaceRequestFactory;
        private readonly Func<ReleaseManifest, DownloadPlan> _planFactory;

        /// <summary>
        /// 清单 KeyId → 验签器（受信公钥库的解析接缝，§6"对应 TrustedKeyRing"）。
        /// </summary>
        /// <param name="verifierResolver">
        /// </param>
        /// <param name="planFactory">
        /// </param>
        public PatchRunner(
            ICandidateProvider provider,
            PlayerCapabilities player,
            ActivationTransactionStore store,
            PatchCoordinator coordinator,
            Func<string, ISignatureVerifier> verifierResolver,
            ReleaseBudget budget = null,
            Func<ReleaseManifest, SpaceCheckRequest> spaceRequestFactory = null,
            Func<ReleaseManifest, DownloadPlan> planFactory = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _verifierResolver = verifierResolver ?? throw new ArgumentNullException(nameof(verifierResolver));
            _budget = budget ?? new ReleaseBudget();
            _spaceRequestFactory = spaceRequestFactory ?? DefaultSpaceRequest;
            _planFactory = planFactory ?? DefaultPlan;
        }

        /// <summary>最近一次描述校验的拒绝原因（诊断；接受时为 None）。</summary>
        public ReleaseRejectReason LastRejectReason { get; private set; }

        /// <summary>跑一次补丁编排：整条内容事务共享一个操作作用域（owner=content.patch，取消级联调用方）。</summary>
        public async UniTask<PatchRunResult> RunAsync(CancellationToken ct = default)
        {
            using OperationScope scope = OperationScope.Start("content.patch", "patch", parent: ct);
            scope.MarkRunning();
            CancellationToken token = scope.Token;
            try
            {
                CandidateOffer offer = await _provider.TryGetCandidateAsync(token);
                if (offer.IsEmpty)
                {
                    // 无候选：仍走一次编排以执行**启动恢复**（§8 中断/失败阶段表）——
                    // 上次激活中断的记录必须在本次启动被处理，不能因"这次没有候选"而跳过。
                    PatchRunResult recovery = await _coordinator.RunAsync(null, null, null, token);
                    MarkResult(scope, recovery);
                    return recovery;
                }

                // §6 信任门：描述未过校验的候选**不得**进入编排。
                ReleaseVerdict verdict = ReleaseManifestValidator.Validate(
                    offer.Manifest, offer.SignedBytes, offer.Signature,
                    _verifierResolver(offer.Manifest.KeyId), _player,
                    confirmedRevision: _store.Current.ConfirmedRevision,
                    budget: _budget);

                LastRejectReason = verdict.Accepted ? ReleaseRejectReason.None : verdict.Reason;
                if (!verdict.Accepted)
                {
                    _store.RecordFailure("候选描述被拒：" + verdict);
                    scope.MarkFailed();
                    return PatchRunResult.Fail(PatchPhase.Recovering,
                        new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: verdict.Reason.ToString()), null);
                }

                ReleaseVerdict windowVerdict = ReleaseManifestValidator.ValidateEffectWindow(offer.Manifest);
                if (!windowVerdict.Accepted)
                {
                    LastRejectReason = windowVerdict.Reason;
                    _store.RecordFailure("生效窗口不支持：" + windowVerdict);
                    scope.MarkFailed();
                    return PatchRunResult.Fail(PatchPhase.Recovering,
                        new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: windowVerdict.Reason.ToString()), null);
                }

                DownloadPlan plan = _planFactory(offer.Manifest);

                PatchRunResult result = await _coordinator.RunAsync(
                    offer.Manifest, plan, _spaceRequestFactory(offer.Manifest), token);
                MarkResult(scope, result);
                return result;
            }
            catch (OperationCanceledException)
            {
                scope.Cancel();
                throw;
            }
            catch
            {
                scope.MarkFailed();
                throw;
            }
        }

        private static void MarkResult(OperationScope scope, PatchRunResult result)
        {
            if (result.Succeeded || result.NoWork) scope.MarkSucceeded();
            else scope.MarkFailed();
        }

        /// <summary>本地通道默认计划：来源仅作诊断标识——<see cref="LocalDirectoryCandidateFetcher"/>
        /// 清点已落盘文件、不消费来源位置。</summary>
        private static DownloadPlan DefaultPlan(ReleaseManifest manifest)
            => new DownloadPlan(manifest, new[] { new DownloadSource("candidate-root", "file://candidate") });

        /// <summary>默认空间请求：候选总字节 + 清单声明的解压峰值（未声明为 0）+ 安全余量 0。</summary>
        private static SpaceCheckRequest DefaultSpaceRequest(ReleaseManifest manifest)
        {
            long total = 0;
            foreach (ReleaseFileEntry f in manifest.Files) total += f.Length;
            return new SpaceCheckRequest { CandidateBytes = total };
        }
    }
}
