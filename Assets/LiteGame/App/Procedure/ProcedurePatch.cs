using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// Patch 流程（《商业级通用客户端框架总设计》§7.1 标准流程 Launch → Bootstrap → **Patch** → Preload；
    /// 《热更与内容发布专项设计》§4 流程图 / §8 中断恢复）。
    ///
    /// ① 内容事务编排（<see cref="PatchRunner"/> → `PatchCoordinator`）：
    ///    启动恢复 → 取候选 → 信任门（描述校验/签名）→ 空间预检 → 获取 → 逐文件校验 →
    ///    标记待激活 → 激活 → 健康确认 → 确认提交。**无候选时仍执行启动恢复**。
    /// ② 资源包初始化（当前 Confirmed 代——`ActiveGeneration`）。
    /// ③ 入口资源检查（可选；<see cref="AssetsHealthProbe"/>）——**须在②之后**：探针走运行时同一条
    ///    租约通道加载入口资源，而在②之前资源包尚未就绪（Patch 早于初始化的时序不可能执行它）。
    ///    判据：属于当前代次清单的入口必须可加载（失败 = Error 流程，与②同轨不静默）；不属于的跳过
    ///    （内置代次不含候选专属入口——内置启动不因此失败）。通过记证据行（含 checked/skipped）供冒烟核对。
    ///
    /// 失败 = RecordFailure + Error 流程（确定错误态）；编排内部已保证"失败保留允许版本"（§8），
    /// 故此处不做回退——回退由 `PatchCoordinator` 在健康失败时执行。
    /// </summary>
    public sealed class ProcedurePatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IContentService _content;
        private readonly ActivationTransactionStore _activations;
        private readonly PatchRunner _patchRunner;
        private readonly AssetsHealthProbe _entryAssetsProbe;

        /// <param name="patchRunner">内容事务编排（可空——为空时退化为"仅启动恢复 + 初始化"，
        /// 用于无候选来源的装配形态；**不得**据此宣称具备热更能力）。</param>
        /// <param name="entryAssetsProbe">入口资源可加载性探针（可空——为空时不检查；时序说明见类注释）。
        /// 不属于当前代次清单的入口会被探针跳过（内置启动不因候选专属入口而失败）。</param>
        public ProcedurePatch(IContentService content, ActivationTransactionStore activations,
            PatchRunner patchRunner = null, CancellationToken rootToken = default,
            AssetsHealthProbe entryAssetsProbe = null)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _activations = activations ?? throw new ArgumentNullException(nameof(activations));
            _patchRunner = patchRunner;
            _entryAssetsProbe = entryAssetsProbe;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                if (_patchRunner != null)
                {
                    // ① 完整内容事务编排（含启动恢复；失败已在编排内留档并回退）
                    PatchRunResult result = await _patchRunner.RunAsync(ct);
                    Log.Info($"内容事务：phase={result.FinalPhase} {result}", "Patch");

                    if (!result.Succeeded && !result.NoWork)
                    {
                        // 编排已 RecordFailure + 重建已确认版本；此处只负责进确定错误态
                        var patchError = new InvalidOperationException(
                            $"内容更新失败（{result.FinalPhase}）：{result.Failure}");
                        Fail(m, patchError, nameof(RunAsyncCore));
                        m.Request(ProcedureId.Error, new ProcedureArgs(patchError));
                        return;
                    }
                }
                else
                {
                    // 退化形态：仅启动恢复（无候选来源时的最小路径）
                    var record = _activations.RecoverOnStartup();
                    Log.Info(
                        $"内容事务状态（无候选来源）：confirmed={record.ConfirmedReleaseId}#{record.ConfirmedVersion}" +
                        (record.LastFailure != null ? $" lastFailure={record.LastFailure}" : ""), "Patch");
                }

                // ② 资源包初始化（当前 Confirmed 代——ActiveGeneration；失败含根因上抛）
                await _content.InitializeAsync(ct);

                // ③ 入口资源检查（须在②之后——见类注释）。候选资产包根是否真正可用（能加载出入口资源）
                //    在此给出确定证据；失败进确定错误态（与②同轨）。
                if (_entryAssetsProbe != null)
                {
                    string problem = await _entryAssetsProbe.CheckAsync(null, ct);
                    if (problem != null)
                    {
                        var probeError = new InvalidOperationException(
                            $"入口资源检查失败（{_entryAssetsProbe.Name}）：{problem}");
                        Fail(m, probeError, nameof(RunAsyncCore));
                        m.Request(ProcedureId.Error, new ProcedureArgs(probeError));
                        return;
                    }
                    UnityEngine.Debug.Log($"[Content] entry-assets ok probe={_entryAssetsProbe.Name} checked={_entryAssetsProbe.LastChecked} skipped={_entryAssetsProbe.LastSkipped}");   // 证据行（冒烟核对）
                    Log.Info($"入口资源可加载（{_entryAssetsProbe.Name}）：checked={_entryAssetsProbe.LastChecked} skipped={_entryAssetsProbe.LastSkipped}", "Content");
                }

                m.Request(ProcedureId.Preload);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                try
                {
                    _activations.RecordFailure($"Patch 阶段失败：{ex.Message}");   // 留档（Confirmed 不动——不影响下次以已确认版本启动）
                }
                catch (Exception persistEx)
                {
                    // 留档自身写盘失败（确认点写盘被拒/磁盘满等）不得吞掉原始失败并阻断错误流程——
                    // 确定错误态仍须可达（§8：失败 = 留档 + Error 流程；写盘失败时"留档"只剩日志这一条腿）。
                    Log.Error($"内容失败留档未落盘：{persistEx.GetType().Name}: {persistEx.Message}", "Patch");
                }
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
