using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程（U1-④：真实主页面接入——《UI框架总设计》U1 退出条件"实际资源包 Player 可打开/关闭"的验收锚点）：
    /// 进入 Main 即打开 UIMain（真 prefab + 真 Lua + 内容租约链路端到端），失败进确定错误态。
    /// 后续流程树（主菜单 → 进场景 → 战斗 → 结算，M5+/C2）在此扩展；
    /// Match/Battle/Result 预留见 <see cref="ProcedureId"/>。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>主页面 formId（tbuiform id=1 = UIMain——真配置投影，与 UI-U0 真 Lua 用例同一行）。</summary>
        private const int MainFormId = 1;

        /// <summary>Player 冒烟标记（scripts/player-smoke.ps1 标记集——真资源包页面打开的健康证据）。</summary>
        private const string SmokeMarker = "[UI] main open";

        private readonly UIService _ui;

        public ProcedureMain(UIService ui, CancellationToken rootToken = default) : base(rootToken)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                await _ui.ShowAsync(MainFormId, ct: ct);      // 真 prefab + 真 Lua + 内容租约（U1 全链）
                UnityEngine.Debug.Log($"{SmokeMarker} form={MainFormId} lease-held");
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
