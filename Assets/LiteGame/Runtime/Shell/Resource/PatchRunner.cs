using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 内容激活器（§8 安全窗口）。把 <see cref="IContentService"/> 的重建能力接到编排端口。
    ///
    /// **本实现的能力边界（如实标注）**：
    /// - <see cref="RebuildConfirmedAsync"/> 走 <c>ShutdownAsync</c> + <c>InitializeAsync</c>——
    ///   即"释放旧运行时后重建"（§8"首版不具备版本并存证明时，释放旧运行时后使用候选重建"）。
    /// - <see cref="ActivateAsync"/> 本身**不重建**：代次推进由 <see cref="IGenerationSink"/> 在
    ///   健康确认后统一执行（见 <c>PatchCoordinator</c>）。此处只留安全窗口钩子——
    ///   未来的 Match/Scene/UI Scope 退出与在途取消接在这里（§8"停止接受受影响的新操作，
    ///   退出相关 Scope，取消在途请求，解除旧 Lua 回调和资源引用"）。
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
            // 释放旧运行时 → 重新初始化（§8"从允许的 Confirmed 版本重建，不使用半成品"）
            await _content.ShutdownAsync(ct);
            await _content.InitializeAsync(ct);
        }
    }

    /// <summary>
    /// 补丁运行的编排器（§4 流程图）。把"取候选 → 验描述 → 交给 <see cref="PatchCoordinator"/>"串起来。
    ///
    /// **信任顺序不可颠倒**（§6"先验证描述的结构/预算与签名，再依据可信描述计划下载"）：
    /// 候选**必须先过 <see cref="ReleaseManifestValidator"/>**，未通过则根本不进入编排——
    /// 编排的输入契约正是"已通过校验的清单"。
    /// </summary>
    public sealed class PatchRunner
    {
        private readonly ICandidateProvider _provider;
        private readonly PlayerCapabilities _player;
        private readonly ActivationTransactionStore _store;
        private readonly PatchCoordinator _coordinator;
        private readonly ReleaseBudget _budget;
        private readonly ISignatureVerifier _verifier;
        private readonly Func<ReleaseManifest, SpaceCheckRequest> _spaceRequestFactory;

        public PatchRunner(
            ICandidateProvider provider,
            PlayerCapabilities player,
            ActivationTransactionStore store,
            PatchCoordinator coordinator,
            ISignatureVerifier verifier,
            ReleaseBudget budget = null,
            Func<ReleaseManifest, SpaceCheckRequest> spaceRequestFactory = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _verifier = verifier;
            _budget = budget ?? new ReleaseBudget();
            _spaceRequestFactory = spaceRequestFactory ?? DefaultSpaceRequest;
        }

        /// <summary>最近一次描述校验的拒绝原因（诊断；接受时为 None）。</summary>
        public ReleaseRejectReason LastRejectReason { get; private set; }

        public async UniTask<PatchRunResult> RunAsync(CancellationToken ct = default)
        {
            CandidateOffer offer = await _provider.TryGetCandidateAsync(ct);
            if (offer.IsEmpty)
            {
                // 无候选：仍走一次编排以执行**启动恢复**（§8 中断/失败阶段表）——
                // 上次激活中断的记录必须在本次启动被处理，不能因"这次没有候选"而跳过。
                return await _coordinator.RunAsync(null, null, null, ct);
            }

            // §6 信任门：描述未过校验的候选**不得**进入编排。
            //
            // 反回退基线取**已确认的发布修订**（非代次）——`ConfirmedRevision` 与 `ConfirmedVersion`
            // 语义不同，前者才是 §6 说的"已确认修订"。该字段未并入完整性摘要（见 ActivationRecord 注释），
            // 故它只提高拒绝门槛、不是安全边界；重放防护由签名与发布修订单调性共同承担。
            ReleaseVerdict verdict = ReleaseManifestValidator.Validate(
                offer.Manifest, offer.SignedBytes, offer.Signature, _verifier, _player,
                confirmedRevision: _store.Current.ConfirmedRevision,
                budget: _budget);

            LastRejectReason = verdict.Accepted ? ReleaseRejectReason.None : verdict.Reason;
            if (!verdict.Accepted)
            {
                _store.RecordFailure("候选描述被拒：" + verdict);
                return PatchRunResult.Fail(PatchPhase.Recovering,
                    new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: verdict.Reason.ToString()), null);
            }

            ReleaseVerdict windowVerdict = ReleaseManifestValidator.ValidateEffectWindow(offer.Manifest);
            if (!windowVerdict.Accepted)
            {
                LastRejectReason = windowVerdict.Reason;
                _store.RecordFailure("生效窗口不支持：" + windowVerdict);
                return PatchRunResult.Fail(PatchPhase.Recovering,
                    new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: windowVerdict.Reason.ToString()), null);
            }

            var plan = new DownloadPlan(offer.Manifest,
                new[] { new DownloadSource("candidate-root", "file://candidate") });

            return await _coordinator.RunAsync(
                offer.Manifest, plan, _spaceRequestFactory(offer.Manifest), ct);
        }

        /// <summary>默认空间请求：候选总字节 + 清单声明的解压峰值（未声明为 0）+ 安全余量 0。</summary>
        private static SpaceCheckRequest DefaultSpaceRequest(ReleaseManifest manifest)
        {
            long total = 0;
            foreach (ReleaseFileEntry f in manifest.Files) total += f.Length;
            return new SpaceCheckRequest { CandidateBytes = total };
        }
    }
}
