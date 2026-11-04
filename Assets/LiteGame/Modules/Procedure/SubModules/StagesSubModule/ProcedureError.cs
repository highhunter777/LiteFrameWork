using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 错误流程：任何流程 Fail 后的落点。显示失败原因（复用 AOT 侧
    /// <see cref="BootstrapErrorPresenter"/> 的最小错误页，代码构建、不经资源包），
    /// **消费 <see cref="ErrorKind"/>** 决定提示语，并经 <see cref="ErrorRecoveryService"/> 执行终止动作
    /// （Security：导出诊断后阻断；Fatal：导出诊断并安全退出——编辑器内不退出）。
    /// 失败原因经 **payload** 传来（<c>ProcedureArgs.Error</c>）。
    /// </summary>
    public sealed class ProcedureError : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly ErrorRecoveryService _recovery;

        public ProcedureError(CancellationToken rootToken = default, ErrorRecoveryService recovery = null)
            : base(rootToken)
        {
            _recovery = recovery;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.Error, ct).ForgetLogged("ProcedureError");

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, Exception error, CancellationToken ct)
        {
            LaunchLoading.End();                     // 兜底：任何流程泄漏的启动走廊在此收口（错误 UI 不能被遮罩挡住）
            ErrorKind kind = ErrorRecoveryService.Classify(error);
            string message = error?.Message ?? "(payload 无 Error)";
            try
            {
                Log.Error($"启动失败[{kind}]，停机待查:{message}", LogTag.Procedure);
                BootstrapErrorPresenter.Show(message, kind);
                Log.Error($"{BootstrapErrorPresenter.SmokeMarker} minimal error UI shown - kind={kind} - see Player.log", LogTag.Procedure);

                if (_recovery == null) return;
                RecoveryOutcome outcome = await _recovery.HandleAsync(error, "bootstrap.error", null, ct);
                Log.Error($"{BootstrapErrorPresenter.SmokeMarker} recovery result={outcome.Result} action={outcome.Action}", LogTag.Procedure);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception recoveryEx)
            {
                Log.Error($"{BootstrapErrorPresenter.SmokeMarker} 恢复动作失败:{recoveryEx.Message}", LogTag.Procedure);
            }
        }

        /// <summary>
        /// 引导期（流程机就绪前）的启动失败落点：转 AOT 侧最小错误页 + 固定冒烟标记
        /// （无恢复执行器——宿主未就绪）。
        /// </summary>
        public static void ShowBootstrapError(string message)
            => BootstrapErrorPresenter.Show(message);

        /// <summary>同上，按异常分类展示（未标记 = Fatal）。</summary>
        public static void ShowBootstrapError(Exception error)
            => BootstrapErrorPresenter.Show(error);
    }
}
