using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// Patch 流程（C1-⑩：《商业级通用客户端框架总设计》§7.1 标准流程 Launch → Bootstrap → **Patch** → Preload 的
    /// 最小实装；《热更与内容发布专项设计》§4 启动恢复 + §8 中断恢复决策）：
    /// ① 启动恢复——读激活事务记录，在途（Candidate/PendingActivation）回退 Confirmed（持久化、计数有上限）；
    /// ② 资源包初始化（当前 Confirmed 代；EditorSimulate/Offline 共用主链，原 Preload 内初始化移入本阶段）。
    /// builtin 健康检查 = 初始化成功（[Asset] ready 标记）；远程候选的完整健康检查（配置/Lua/关键入口）
    /// 与 Host 下载/验签随热更批——本阶段失败 = RecordFailure + Error 流程（确定错误态，§C1 退出条件）。
    /// </summary>
    public sealed class ProcedurePatch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IContentService _content;
        private readonly ActivationTransactionStore _activations;

        public ProcedurePatch(IContentService content, ActivationTransactionStore activations,
            CancellationToken rootToken = default)
            : base(rootToken)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // ① 启动恢复：在途事务回退 Confirmed（持久化；超限仍以 Confirmed 继续——确定态，不无限重启）
                var record = _activations.RecoverOnStartup();
                Log.Info(
                    $"内容事务状态：confirmed={record.ConfirmedReleaseId}#{record.ConfirmedVersion}" +
                    (record.LastFailure != null ? $" lastFailure={record.LastFailure}" : "") +
                    (record.PendingReleaseId != null ? $" pending={record.PendingReleaseId}" : ""), "Patch");

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
