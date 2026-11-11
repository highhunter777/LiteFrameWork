using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Meta.Abstractions;
using LiteGame.Modules.Dev.FallbacksSubModule;

namespace LiteGame
{
    /// <summary>
    /// 主流程：**启动落点决策**（登录页/大厅页/不开页，见 ResolveLanding）＋ 开发直入训练场。
    /// </summary>
    public sealed class ProcedureMain : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>五页页面流（批C-④；可空 = 测试装配不接 UI）。</summary>
        private readonly PageFlow _pages;

        private bool _directEntryRequested;

        /// <summary>联机入口编排件（判"是否配置了 Meta 基址"——入口页只在配置形态开；可空 = 离线装配）。</summary>
        private readonly IMetaConfigFacts _meta;

        /// <summary>开发会话端口（直入训练场/进退出请求/启动快照的**唯一**读取面；发布 = NullDevSession）。</summary>
        private readonly IDevSession _dev;

        public ProcedureMain(CancellationToken rootToken = default,
            PageFlow pages = null, IMetaConfigFacts meta = null, IDevSession dev = null)
            : base(rootToken)
        {
            _pages = pages;
            _meta = meta;
            _dev = dev ?? NullDevSession.Instance;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).ForgetLogged("ProcedureMain");          // 一行转发，仅此而已——禁止 async void

        public override void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds)
        {
            // 开发直入（启动配置开关，经 IDevSession 读）：**直入不加载登录页**——训练场的进入请求在本阶段
            // （Main 已就绪）发出一次，由下面的请求桥消费后进 Match(testRoom)。发布形态端口全关 = 整段 no-op。
            if (_dev.DirectToTrainingEnabled && !_directEntryRequested)
            {
                _directEntryRequested = true;
                _dev.RequestEnter();
                Log.Info("[Main] 直入训练场：请求本地测试模式对局（DevStartupConfig.DirectToTraining）", LogTag.Procedure);
            }
            if (_dev.TakeEnterRequest())                        // 开发控制台/GM 面板/直入 同一消费点（读即清）
            {
                _dev.ApplyEnterDefaults();                      // 进房初值在此单点落值，任何入口语义一致
                _dev.SetActive(true);
                _pages?.CloseOpenPagesForBattle();               // dev 入口直入对局：在开页面会盖住 HUD，先收
                m.Request(ProcedureId.Match, new ProcedureArgs(testRoom: true));
            }
            if (_dev.TakeExitRequest())                         // 主菜单里点退出：只关模式（对局内由 Battle 消费=离场）
            {
                _dev.SetActive(false);
            }
        }

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                // 启动落点（**一个决策点**）： 直入训练场 → 不开页（对局接管）。
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
            if (_dev.DirectToTrainingEnabled) return PageFlow.Landing.None;
            return _meta != null && _meta.IsConfigured ? PageFlow.Landing.Login : PageFlow.Landing.None;
        }
    }
}
