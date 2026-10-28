using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 主流程：**启动落点决策**（登录页/大厅页/不开页，见 <see cref="ResolveLanding"/>）＋ 开发直入训练场。
    /// **不再加载对局场景组**——场景组的加载与卸载都归 `ProcedureBattle`
    /// （`StartGame` 后加载、离场收尾卸载；《地图数据专项设计》加载链修订；Main 阶段只有启动场景 Boot 与 UI）。
    /// 后续流程树（主菜单 → 选模式 → 进场景 → 战斗 → 结算）在此扩展；Match/Battle 预留见 <see cref="ProcedureId"/>。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>五页页面流（批C-④；可空 = 测试装配不接 UI）。</summary>
        private readonly PageFlow _pages;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        /// <summary>直入训练场只发一次请求（Main 可能重进）。</summary>
        private bool _directEntryRequested;
#endif

        /// <summary>联机入口编排件（判"是否配置了 Meta 基址"——入口页只在配置形态开；可空 = 离线装配）。</summary>
        private readonly MetaEntryService _meta;

        public ProcedureMain(CancellationToken rootToken = default,
            PageFlow pages = null, MetaEntryService meta = null)
            : base(rootToken)
        {
            _pages = pages;
            _meta = meta;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).ForgetLogged("ProcedureMain");          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 开发直入（DevStartupConfig 开关）：**直入不加载登录页**——训练场的进入请求在本阶段
            // （Main 已就绪）发出一次，由下面的请求桥消费后进 Match(testRoom)。
            if (DevStartupConfig.DirectToTrainingEnabled && !_directEntryRequested)
            {
                _directEntryRequested = true;
                TestModeRuntime.RequestEnter();
                Log.Info("[Main] 直入训练场：请求本地测试模式对局（DevStartupConfig.DirectToTraining）", LogTag.Procedure);
            }
            if (TestModeRuntime.TakeEnterRequest())             // 开发控制台/GM 面板/直入 同一消费点（读即清）
            {
                DevStartupConfig.ApplySnapshotOrDefaults();     // 进房初值在此单点落值，任何入口语义一致
                TestModeRuntime.Active = true;
                _pages?.CloseOpenPagesForBattle();               // dev 入口直入对局：在开页面会盖住 HUD，先收
                m.Request(ProcedureId.Match, new ProcedureArgs(testRoom: true));
            }
            if (TestModeRuntime.TakeExitRequest())              // 主菜单里点退出：只关模式（对局内由 Battle 消费=离场）
            {
                TestModeRuntime.Active = false;
            }
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // 启动落点（**一个决策点**）：
                //   直入训练场 → 不开页（对局接管）；
                //   本地服务器（开发）→ 大厅页（跳过登录）；Meta 已配置（正式）→ 登录页；否则不开页。
                // **只开一次**——对局结束/离场返回 Main 不重开入口（页面流接管：结算页或大厅在场）。
                if (_pages != null) _pages.OpenLanding(ResolveLanding());

                // 启动走廊收口（窗口⑥ 首屏接线点——登录页/大厅就绪即收，通用加载页 §1）。
                LaunchLoading.End();
            }
            catch (OperationCanceledException) { LaunchLoading.End(); /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                LaunchLoading.End();                         // 失败收口（错误页不能被遮罩挡住）
                Fail(m, ex, nameof(RunAsyncCore));
                m.Request(ProcedureId.Error, new ProcedureArgs(ex));
            }
        }

        /// <summary>启动落点决策（**单点**）：直入本地测试模式 &gt; Meta→登录页 &gt; 不开页。</summary>
        private PageFlow.Landing ResolveLanding()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (DevStartupConfig.DirectToTrainingEnabled) return PageFlow.Landing.None;
#endif
            return _meta != null && _meta.IsConfigured ? PageFlow.Landing.Login : PageFlow.Landing.None;
        }
    }
}
