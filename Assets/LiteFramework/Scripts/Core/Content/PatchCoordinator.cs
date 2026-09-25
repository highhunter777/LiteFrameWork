using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>补丁编排的阶段（§4 mermaid 流程图逐节点；诊断与"中断在哪一步"的定位依据）。</summary>
    public enum PatchPhase
    {
        /// <summary>未开始。</summary>
        Idle = 0,
        /// <summary>读激活事务记录并做启动恢复决策（§8 中断/失败阶段表）。</summary>
        Recovering,
        /// <summary>空间预检（§7）。</summary>
        PrecheckingSpace,
        /// <summary>获取候选文件（§7）。</summary>
        Fetching,
        /// <summary>候选校验：逐文件摘要 + 双向差异（§7）。</summary>
        Verifying,
        /// <summary>已持久化待激活、运行时尚未健康（§8 表行 2 的恢复决策点）。</summary>
        PendingActivation,
        /// <summary>安全窗口内激活候选（§8）。</summary>
        Activating,
        /// <summary>健康确认（§8）。</summary>
        HealthChecking,
        /// <summary>已确认（Confirmed 已提交）。</summary>
        Confirmed,
        /// <summary>失败并已回退到允许版本（保留可用版本，§8/§12）。</summary>
        Failed,
    }

    /// <summary>一次编排的最终结论。</summary>
    public readonly struct PatchRunResult
    {
        /// <summary>是否推进到 Confirmed（true）/ 失败并回退（false）/ 无候选可做（<see cref="NoWork"/>）。</summary>
        public readonly bool Succeeded;

        /// <summary>无候选存在——不是失败，调用方按"以已确认版本继续启动"处理。</summary>
        public readonly bool NoWork;

        /// <summary>
        /// 本次运行的终局阶段：**成功 = <see cref="PatchPhase.Confirmed"/>**；
        /// **失败 = 失败发生的那个阶段**（如 <see cref="PatchPhase.Verifying"/>）——这是定位依据。
        /// 若要判断"整条编排是否已进入失败态"，看 <see cref="PatchCoordinator.Phase"/>（= Failed）。
        /// </summary>
        public readonly PatchPhase FinalPhase;

        public readonly DownloadFailureInfo Failure;
        public readonly string Detail;

        private PatchRunResult(bool succeeded, bool noWork, PatchPhase finalPhase,
            DownloadFailureInfo failure, string detail)
        {
            Succeeded = succeeded;
            NoWork = noWork;
            FinalPhase = finalPhase;
            Failure = failure;
            Detail = detail;
        }

        internal static PatchRunResult Ok(PatchPhase phase, string detail)
            => new PatchRunResult(true, false, phase, default, detail);

        internal static PatchRunResult Nothing() => new PatchRunResult(false, true, PatchPhase.Idle, default, null);

        /// <summary>
        /// 失败结果。**public 供装配层构造**（`PatchRunner` 在候选描述被拒时需产出失败结果——
        /// 该路径不进入 <see cref="PatchCoordinator"/>）。
        /// </summary>
        public static PatchRunResult Fail(PatchPhase phase, DownloadFailureInfo failure, string detail)
            => new PatchRunResult(false, false, phase, failure, detail);

        public override string ToString()
            => NoWork ? "无候选（以已确认版本继续）"
               : Succeeded ? $"完成（{FinalPhase}）：{Detail}"
               : $"失败（{FinalPhase}）：{Failure} {Detail}";
    }

    /// <summary>
    /// 补丁编排（《热更与内容发布专项设计》§8 激活、健康确认与中断恢复；§4 流程图）。
    ///
    /// **本类是热更链的第一个生产消费者**：把此前零散且无调用方的
    /// <see cref="ReleaseManifestValidator"/>（描述层）、<see cref="DownloadPlan"/>（获取决策）、
    /// <see cref="CandidateContentVerifier"/>（内容执行侧校验）、
    /// 与 <see cref="ActivationTransactionStore"/>（持久事务）**串成一条可测的编排**。
    ///
    /// 顺序（与 §8 及 §4 mermaid 一致，**每一步都持久化**以便中断后可恢复）：
    /// 启动恢复 → 空间预检 → 获取 → 校验 → 标记待激活 → 激活 → 健康 → 确认。
    /// 候选事务在**获取开始前**落盘（Candidate 覆盖下载/校验区间，§8 表行 1）；
    /// 在途候选的临时文件在**启动恢复**与**失败收尾**两处经端口回收。
    ///
    /// **失败一律保留允许版本**（§8"失败从保留版本重建"）：任何一步失败都
    /// ① <see cref="ActivationTransactionStore.RecordFailure"/> 留档、
    /// ② 重建已确认代次、③ 返回 <see cref="PatchRunResult.Failed"/>——
    /// 不部分发布、不无限重启（恢复尝试由 Store 计数封顶）。
    ///
    /// 本类纯逻辑 + 端口，零 Unity/零 IO 实体，L1 全覆盖。
    /// </summary>
    public sealed class PatchCoordinator
    {
        private readonly ActivationTransactionStore _store;
        private readonly ICandidateFileSource _fileSource;
        private readonly IDiskSpaceProbe _spaceProbe;
        private readonly ICandidateFetcher _fetcher;
        private readonly ICandidateHealthCheck _health;
        private readonly IContentActivator _activator;
        private readonly IGenerationSink _generationSink;
        private readonly ReleaseBudget _budget;

        /// <summary>本次编排已开始候选事务的发布身份（BeginCandidate 之后非空）——
        /// 失败收尾按它回收该候选的临时文件；预检/依赖阶段失败时为 null（尚无临时归属）。</summary>
        private string _activeReleaseId;

        /// <summary>当前进行到的阶段（诊断；失败后保留失败阶段供定位）。</summary>
        public PatchPhase Phase { get; private set; } = PatchPhase.Idle;

        /// <summary>最近一次失败的详情（成功时为 default）。</summary>
        public DownloadFailureInfo LastFailure { get; private set; }

        public PatchCoordinator(
            ActivationTransactionStore store,
            ICandidateFileSource fileSource,
            IDiskSpaceProbe spaceProbe,
            ICandidateFetcher fetcher,
            ICandidateHealthCheck health,
            IContentActivator activator,
            IGenerationSink generationSink = null,
            ReleaseBudget budget = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
            _spaceProbe = spaceProbe ?? throw new ArgumentNullException(nameof(spaceProbe));
            _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _health = health ?? throw new ArgumentNullException(nameof(health));
            _activator = activator ?? throw new ArgumentNullException(nameof(activator));
            _generationSink = generationSink;                 // 可选：无内容服务时留空
            _budget = budget ?? new ReleaseBudget();
        }

        /// <summary>
        /// 编排一次补丁。全部步骤只走端口——**没有真实网络/文件系统依赖**。
        ///
        /// <paramref name="candidate"/> 为 null 表示本次无可应用候选（已是最新）：
        /// 不进入激活链路，直接 <see cref="PatchRunResult.NoWork"/>。
        ///
        /// <paramref name="resolveRelease"/> 把依赖的 ReleaseId 解析为清单：设计要求
        /// "本发布生效前必须已确认"的依赖（<see cref="ReleaseManifest.Dependencies"/>）必须就已确认 ——
        /// 未确认则拒绝本次候选（依赖未就位的包不得激活）。
        /// </summary>
        public async UniTask<PatchRunResult> RunAsync(
            ReleaseManifest candidate,
            DownloadPlan plan,
            SpaceCheckRequest spaceRequest,
            CancellationToken ct = default)
        {
            // ── ① 启动恢复：在途事务按 §8 表回退（持久化；尝试有上限）──
            SetPhase(PatchPhase.Recovering);
            string pendingBeforeRecovery = _store.Current.PendingReleaseId;
            ActivationRecord recovered = _store.RecoverOnStartup();

            // §8 表行 1/行 2：上次在途候选（下载/校验/激活窗口中断）——按记录回收其临时归属。
            // 清理是恢复的次要目标（端口契约不抛），不得阻断"以 Confirmed 继续"的主流程。
            if (pendingBeforeRecovery != null)
                await _fetcher.CleanupTempAsync(pendingBeforeRecovery, ct);

            if (candidate == null)
            {
                SetPhase(PatchPhase.Idle);
                return PatchRunResult.Nothing();
            }

            // ── ①.5 依赖必须先于本发布被确认（§6"本发布生效前必须已确认的版本"）──
            DownloadFailureInfo unmet = FindUnmetDependency(candidate, recovered);
            if (unmet.Kind != DownloadFailureKind.None)
                return await FailAsync(PatchPhase.Recovering, recovered, unmet, ct);

            // ── ② 空间预检（§7）：不足即拒绝，保留可用版本 ──
            SetPhase(PatchPhase.PrecheckingSpace);
            SpaceCheckResult space = SpacePrecheck.Evaluate(spaceRequest, _spaceProbe.GetAvailableBytes());
            if (!space.Passed)
                return await FailAsync(PatchPhase.PrecheckingSpace, recovered,
                    new DownloadFailureInfo(DownloadFailureKind.InsufficientSpace, detail: space.Detail), ct);

            // ── ③ 获取候选（§7）──
            // 候选事务先于下载落盘（§8 表行 1：下载/校验中中断 → 下次启动按记录清理该候选
            // 的临时文件）。不先记录，则该阶段的进程中断既无恢复依据、临时文件也无归属可查。
            _store.BeginCandidate(candidate.ReleaseId);
            _activeReleaseId = candidate.ReleaseId;
            SetPhase(PatchPhase.Fetching);
            CandidateFetchResult fetched = await _fetcher.FetchAsync(candidate, plan, ct);
            if (!fetched.Succeeded)
                return await FailAsync(PatchPhase.Fetching, recovered, fetched.Failure, ct);

            // ── ④ 候选校验：逐文件摘要 + 双向差异（§7）──
            SetPhase(PatchPhase.Verifying);
            CandidateVerifyResult verified =
                CandidateContentVerifier.Verify(candidate, _fileSource, fetched.Paths, ct);
            if (!verified.Passed)
                return await FailAsync(PatchPhase.Verifying, recovered, verified.Failure, ct);

            // ── ⑤ 持久化"待激活"（§8 表行 2 的恢复决策点）──
            // 顺序不可颠倒：必须在健康确认之前落盘，否则进程在激活后中断将无从恢复。
            // （Candidate 事务已在 ③ 下载前开始，此处推进为 PendingActivation。）
            _store.MarkPendingActivation();
            SetPhase(PatchPhase.PendingActivation);

            // ── ⑥ 安全窗口内激活（§8）──
            SetPhase(PatchPhase.Activating);
            await _activator.ActivateAsync(candidate, ct);

            // ── ⑦ 健康确认（§8）──
            SetPhase(PatchPhase.HealthChecking);
            string unhealthy = await _health.CheckAsync(candidate, ct);
            if (!string.IsNullOrEmpty(unhealthy))
                return await FailAsync(PatchPhase.HealthChecking, recovered,
                    new DownloadFailureInfo(DownloadFailureKind.ReadError, detail: unhealthy), ct);

            // ── ⑧ 确认提交（§8；写盘失败由 IO 实现方抛出，本类不吞）──
            _store.Confirm(candidate.ReleaseId, recovered.ConfirmedVersion + 1, candidate.Revision);
            _generationSink?.Advise(_store.ActiveGeneration);
            SetPhase(PatchPhase.Confirmed);
            return PatchRunResult.Ok(PatchPhase.Confirmed, candidate.ReleaseId);
        }

        /// <summary>
        /// 依赖检查（§6"依赖的其他 ReleaseId（本发布生效前必须已确认的版本）"）。
        ///
        /// 已确认版本满足；**在途候选版本也满足**——否则 A→B 两个包在同一批下载后会互相等待死锁
        /// （B 是本次候选、A 已在上一步确认的场景仍是串行的，故此处只需判"已确认或正在本次在途"）。
        /// </summary>
        private static DownloadFailureInfo FindUnmetDependency(ReleaseManifest candidate, ActivationRecord record)
        {
            if (candidate.Dependencies == null || candidate.Dependencies.Count == 0)
                return default;

            string confirmed = record.ConfirmedReleaseId;
            string pending = record.PendingReleaseId;

            foreach (string dep in candidate.Dependencies)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                bool satisfied = string.Equals(dep, confirmed, StringComparison.Ordinal)
                                 || string.Equals(dep, pending, StringComparison.Ordinal);
                if (!satisfied)
                {
                    return new DownloadFailureInfo(DownloadFailureKind.FileMissing, dep,
                        detail: $"依赖未确认（已确认={confirmed}，在途={pending ?? "无"}）");
                }
            }
            return default;
        }

        /// <summary>
        /// 失败收尾（§8"从允许的 Confirmed 版本重建，不使用半成品"）：
        /// 留档失败 → 重建已确认代次 → 代次提示回已确认 → 返回失败。
        ///
        /// **留档先于重建**：即便重建抛错，失败原因也已持久化，下次启动可见。
        /// </summary>
        private async UniTask<PatchRunResult> FailAsync(
            PatchPhase phase, ActivationRecord recovered, DownloadFailureInfo failure, CancellationToken ct)
        {
            LastFailure = failure;
            _store.RecordFailure($"{phase}：{failure}");

            // 该候选的获取/校验已终止——立即回收其临时文件，不等下次启动（§8 表行 1 的清理义务；
            // 幂等：获取成功后的失败此处已无可清理 = no-op）。
            if (_activeReleaseId != null)
                await _fetcher.CleanupTempAsync(_activeReleaseId, ct);

            ContentGeneration confirmed = new ContentGeneration(recovered.ConfirmedReleaseId, recovered.ConfirmedVersion);
            _generationSink?.Advise(confirmed);

            // 已激活过候选才需要重建；未进入激活链路时已确认内容本就在运行，重建是多余的清缓存。
            if (phase == PatchPhase.HealthChecking)
            {
                try
                {
                    await _activator.RebuildConfirmedAsync(confirmed, ct);
                }
                catch (Exception ex)
                {
                    // 重建失败不再向上抛：失败原因已留档，向上抛会掩盖真正的失败阶段。
                    _store.RecordFailure($"{phase}：回退重建失败 {ex.GetType().Name}：{ex.Message}");
                }
            }

            SetPhase(PatchPhase.Failed);
            return PatchRunResult.Fail(phase, failure, null);
        }

        private void SetPhase(PatchPhase phase) => Phase = phase;
    }
}
