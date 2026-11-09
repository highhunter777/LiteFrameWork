using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Config;
using LiteGame.Modules.UI;

namespace LiteGame
{
    /// <summary>
    /// 预载流程：配置加载 → 启动走廊开场 → 模态登记 → 放行 Main。
    /// （页面 C# 化后无脚本预载/注册表填充。）
    /// </summary>
    public sealed class ProcedurePreload : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly IConfigService _config;
        private readonly UIService _ui;
        private readonly FeedbackService _feedback;        // 启动走廊装载面（窗口③）

        public ProcedurePreload(IConfigService config, UIService ui,
            CancellationToken rootToken = default, FeedbackService feedback = null)
            : base(rootToken)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _feedback = feedback;                          // 可空 = 测试装配（LaunchLoading.Begin 空引用跳过）
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).ForgetLogged("ProcedurePreload");

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // 资源包初始化在 Patch 流程完成（§7.1 Patch 先于 Preload）。
                await _config.LoadAsync(ct);

                // ⓪ 启动走廊开场（窗口③——不定态，Preload 开 / Main 报进度 / 首屏收，§通用加载页 §1）。
                LaunchLoading.Begin(_feedback, "正在预载资源…");

                // ---- 模态登记（配置已装载；页 id 来自生成的 PageIds，未登记在编译期即失败）。
                _ui.RegisterModal(PageIds.Page_Settings);

                m.Request(ProcedureId.Main);                     // 放行（启动走廊跨流程存续——Main 报进度、首屏收）
            }
            catch (OperationCanceledException) { LaunchLoading.End(); /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                LaunchLoading.End();                             // 失败收口（错误页不能被遮罩挡住）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }
    }
}
