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
        private readonly Func<string, ISignatureVerifier> _verifierResolver;
        private readonly Func<ReleaseManifest, SpaceCheckRequest> _spaceRequestFactory;
        private readonly Func<ReleaseManifest, DownloadPlan> _planFactory;

        /// <param name="verifierResolver">
        /// 清单 KeyId → 验签器（受信公钥库的解析接缝，§6"对应 TrustedKeyRing"）。
        /// 返回 null = keyId 未登记/已撤销 → 校验器按 <see cref="ReleaseRejectReason.UnknownOrRevokedKey"/>
        /// 拒绝（fail-closed——语义即"未登记/已撤销"，与单验签器形态的 null 语义一致）。
        /// </param>
        /// <param name="planFactory">
        /// 下载计划工厂（清单 → 计划；**来源拓扑的唯一装配口**）：CDN 通道传"真实部署基址源"
        /// （与 <c>HttpCandidateFetcher</c> 同一套 <see cref="DownloadSource"/>）。null = 本地通道
        /// 默认计划（<c>LocalDirectoryCandidateFetcher</c> 只清点落盘、不消费来源）。
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
            // 验签器按清单 KeyId 从受信公钥库解析（§6"签名公钥标识（对应 TrustedKeyRing）"）：
            // 未登记/已撤销 → 解析为 null → 校验器按 UnknownOrRevokedKey 拒（fail-closed）。
            // 反回退基线取**已确认的发布修订**（非代次）——`ConfirmedRevision` 与 `ConfirmedVersion`
            // 语义不同，前者才是 §6 说的"已确认修订"。该字段未并入完整性摘要（见 ActivationRecord 注释），
            // 故它只提高拒绝门槛、不是安全边界；重放防护由签名与发布修订单调性共同承担。
            ReleaseVerdict verdict = ReleaseManifestValidator.Validate(
                offer.Manifest, offer.SignedBytes, offer.Signature,
                _verifierResolver(offer.Manifest.KeyId), _player,
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

            DownloadPlan plan = _planFactory(offer.Manifest);

            return await _coordinator.RunAsync(
                offer.Manifest, plan, _spaceRequestFactory(offer.Manifest), ct);
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
