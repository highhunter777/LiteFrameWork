using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

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
    ///
    /// 失败 = RecordFailure + Error 流程（确定错误态）；编排内部已保证"失败保留允许版本"（§8），
    /// 故此处不再回退——回退由 `PatchCoordinator` 在健康失败时执行。
    /// </summary>
    public sealed class ProcedurePatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IContentService _content;
        private readonly ActivationTransactionStore _activations;
        private readonly PatchRunner _patchRunner;

        /// <param name="patchRunner">内容事务编排（可空——为空时退化为"仅启动恢复 + 初始化"，
        /// 用于无候选来源的装配形态；**不得**据此宣称具备热更能力）。</param>
        public ProcedurePatch(IContentService content, ActivationTransactionStore activations,
            PatchRunner patchRunner = null, CancellationToken rootToken = default)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _activations = activations ?? throw new ArgumentNullException(nameof(activations));
            _patchRunner = patchRunner;
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

                m.Request(ProcedureId.Preload);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                _activations.RecordFailure($"Patch 阶段失败：{ex.Message}");   // 留档（Confirmed 不动——不影响下次以已确认版本启动）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
