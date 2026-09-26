using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>
    /// 启动流程：**唯一受信装配点**（设计方案 §3.3）。依赖在 GameEntry.Awake 装配点构造注入存为本类字段——
    /// 流程依赖不从 payload 取（那是局部服务定位器，与"容器不静态暴露"同罪）。
    /// 职责：注册业务服务 → Seal 封注册面 → 移交 Preload。**Start 由 GameEntry.Start() 触发**
    /// （晚于全部组件 Awake 的 RegisterInstance——流程顺序契约，避免密封后注册违例）。
    /// </summary>
    public sealed class ProcedureLaunch : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        private readonly ServiceContainer _container;
        private readonly ConfigService _config;
        private readonly UIService _ui;
        private readonly RedDotRegistry _redDot;
        private readonly ILogicScheduler _logicScheduler;
        private readonly IUIScheduler _uiScheduler;
        private readonly GameTimelineRunner _timelineRunner;
        private readonly EntityService _entities;
        private readonly AudioService _audio;
        private readonly VfxService _vfx;
        private readonly UINavigationController _nav;

        public ProcedureLaunch(ServiceContainer container, ConfigService config,
            UIService uiService, RedDotRegistry redDotRegistry,
            ILogicScheduler logicScheduler, IUIScheduler uiScheduler, GameTimelineRunner timelineRunner,
            EntityService entityService, AudioService audioService, VfxService vfxService,
            UINavigationController nav, CancellationToken rootToken = default)
            : base(rootToken)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _ui = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _redDot = redDotRegistry ?? throw new ArgumentNullException(nameof(redDotRegistry));
            _logicScheduler = logicScheduler ?? throw new ArgumentNullException(nameof(logicScheduler));
            _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
            _timelineRunner = timelineRunner ?? throw new ArgumentNullException(nameof(timelineRunner));
            _entities = entityService ?? throw new ArgumentNullException(nameof(entityService));
            _audio = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _vfx = vfxService ?? throw new ArgumentNullException(nameof(vfxService));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void（M0 指导 §6）

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                _container.RegisterInstance<ConfigService>(_config);
                _container.RegisterInstance<IConfigService>(_config);
                _container.RegisterInstance<UIService>(_ui);    // UI 壳（M4 §2.1：薄壳 = DI 注册的普通服务）
                _container.RegisterInstance<RedDotRegistry>(_redDot);   // 红点规则口（M4 §2.5：完整树 = M4c）
                _container.RegisterInstance<ILogicScheduler>(_logicScheduler);   // 时序双轨（M4 §2.7：逻辑轨受时停）
                _container.RegisterInstance<IUIScheduler>(_uiScheduler);         // UI 轨不受时停
                _container.RegisterInstance<ITimelineRunner>(_timelineRunner);   // 时间轴执行器（剧情/技能）
                _container.RegisterInstance<EntityService>(_entities);   // 实体壳（M4 §2.8：池化+竞态表）
                _container.RegisterInstance<AudioService>(_audio);       // 声音壳（M4 §2.9：组+代理）
                _container.RegisterInstance<IVFXService>(_vfx);          // VFX 服务（M11：表现层，注册即发现 ITickable → 自动驱动到期回收）
                var dialogs = new DialogService(_ui);    // U2-⑥b：弹窗服务（ITickable 随注册自动驱动；§4.3 ShowDialogAsync 口径）
                _container.RegisterInstance<DialogService>(dialogs);
                var feedback = new FeedbackService(_ui, dialogs, _nav);   // U2-⑥c：Loading/Error/Toast 统一入口（System 层 form；Back 链首位拦截）
                _container.RegisterInstance<FeedbackService>(feedback);
                // Toast 多条计时：接 UIClock 步进（未打开时短路）；显式持有实例，不做全局单例查找
                _container.RegisterInstance<ToastTicker>(new ToastTicker(() => feedback.ToastHost));
                _container.Seal();                       // 注册面冻结；此后 Resolve 不受限


                m.Request(ProcedureId.Patch);            // C1-⑩：先过内容事务（启动恢复 + 资源包初始化）再预载
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
