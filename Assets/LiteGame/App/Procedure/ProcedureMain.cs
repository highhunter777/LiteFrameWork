using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;

namespace LiteGame
{
    /// <summary>
    /// 主流程：**启动落点决策**（登录页/大厅页/不开页，见 <see cref="ResolveLanding"/>）＋ 开发直入（训练场/工作台）。
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
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // 开发直入（DebugTuner 开关，优先级：直入训练场 > 直入工作台）：**直入不加载登录页**——
            // 训练场的进入请求在本阶段（Main 已就绪）发出一次，由下面的请求桥消费后进 Match(testRoom)；
            // 直入工作台的加性场景加载由 DebugTuner 自身在 Update 首帧完成（走到这里说明已生效）。
            if (DebugTuner.DirectToTrainingEnabled && !_directEntryRequested)
            {
                _directEntryRequested = true;
                DebugTuner.ApplySnapshotOrDefaults();
                TestModeRuntime.RequestEnter();
                UnityEngine.Debug.Log("[Main] 直入训练场：请求测试模式对局（DebugTuner.DirectToTraining）");
            }
            if (TestModeRuntime.TakeDirectRequest())            // 开发页「直连本机服」：原 F9 链（非测试房）
            {
                _pages?.CloseOpenPagesForBattle();
                UnityEngine.Debug.Log("[Main] 直连对局：请求 Match（非测试房——真 KCP/本机服 Room-A）");
                m.Request(ProcedureId.Match);
            }
            if (TestModeRuntime.TakeEnterRequest())             // 开发页/GM 面板/直入 同一条链（读即清）
            {
                TestModeRuntime.Active = true;
                _pages?.CloseOpenPagesForBattle();               // dev 入口直入对局：在开页面会盖住 HUD，先收
                m.Request(ProcedureId.Match, new ProcedureArgs(testRoom: true));
            }
            if (TestModeRuntime.TakeExitRequest())              // 主菜单里点退出：只关模式（对局内由 Battle 消费=离场）
            {
                TestModeRuntime.Active = false;
            }
            // 工作台落点收尾（dev）：菜单退出后关工作台场景——回大厅页（UI 服务与页面在 DDOL 根上，不受影响）
            if (DebugTuner.TakeWorkbenchExitRequest()) DebugTuner.CloseWorkbenchScene();
#endif
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // 启动落点（《UI工作台设计》§5 入口矩阵，**一个决策点**）：
                //   直入形态（直入训练场/直入工作台）→ 不开页（场景/面板接管）；
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

        /// <summary>启动落点决策（**单点**）：直入形态 &gt; 本地服务器→大厅 &gt; Meta→登录页 &gt; 不开页。</summary>
        private PageFlow.Landing ResolveLanding()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (DebugTuner.DirectToTrainingEnabled || DebugTuner.WorkbenchModeEnabled) return PageFlow.Landing.None;
            if (DebugTuner.UseLocalServerEnabled) return PageFlow.Landing.Lobby;
#endif
            return _meta != null && _meta.IsConfigured ? PageFlow.Landing.Login : PageFlow.Landing.None;
        }
    }
}
