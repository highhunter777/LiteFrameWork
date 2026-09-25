using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程（U1-④：真实主页面接入——《UI框架总设计》U1 退出条件"实际资源包 Player 可打开/关闭"的验收锚点）：
    /// 进入 Main 即打开 UIMain（真 prefab + 真 Lua + 内容租约链路端到端），失败进确定错误态。
    /// 导航走 <see cref="UINavigationController"/>（U2 首版单写者导航的真实消费者——产品入口必须走导航，
    /// 不直调 ShowAsync）。后续流程树（主菜单 → 进场景 → 战斗 → 结算，M5+/C2）在此扩展；
    /// Match/Battle/Result 预留见 <see cref="ProcedureId"/>。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>主页面 formId（tbuiform id=1 = UIMain——真配置投影，与 UI-U0 真 Lua 用例同一行）。</summary>
        private const int MainFormId = 1;

        /// <summary>Player 冒烟标记（scripts/player-smoke.ps1 标记集——真资源包页面打开的健康证据）。</summary>
        private const string SmokeMarker = "[UI] main open";

        private readonly UINavigationController _nav;

        public ProcedureMain(UIService ui, UINavigationController nav, CancellationToken rootToken = default) : base(rootToken)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            if (ui == null) throw new ArgumentNullException(nameof(ui));   // 依赖存在性校验（装配契约），字段暂不持用
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // C2 测试入口（批①）：F9 = 进入测试对局（隔离测试发行者身份，正式登录页归 G3）。
            // 主菜单 → 进对局的正式入口（页面按钮/Matchmaking）归 U2/G3——此处只留热键，不进正式 UI。
            // 门禁与 SimSandbox 同口径：开发/编辑器可用，正式包无该入口（不塞进 Release 玩家）。
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
                m.Request(ProcedureId.Match);
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                await _nav.GoAsync(MainFormId, ct: ct);        // 单写者导航（真 prefab + 真 Lua + 内容租约——U1 全链）
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
