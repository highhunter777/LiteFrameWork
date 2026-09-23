using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
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
        private readonly SceneService _scenes;
        private readonly UiLuaRegistry _uiRegistry;
        private readonly ContentLuaRegistry _contentRegistry;
        private readonly StrategyLuaRegistry _strategyRegistry;
        private readonly UIService _ui;
        private readonly RedDotRegistry _redDot;
        private readonly ILogicScheduler _logicScheduler;
        private readonly IUIScheduler _uiScheduler;
        private readonly GameTimelineRunner _timelineRunner;
        private readonly EntityService _entities;
        private readonly AudioService _audio;
        private readonly VfxService _vfx;
        private readonly LuaRegistryRefillService _refill;

        public ProcedureLaunch(ServiceContainer container, ConfigService config, SceneService scenes,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
            UIService uiService, RedDotRegistry redDotRegistry,
            ILogicScheduler logicScheduler, IUIScheduler uiScheduler, GameTimelineRunner timelineRunner,
            EntityService entityService, AudioService audioService, VfxService vfxService,
            LuaRegistryRefillService refillService, CancellationToken rootToken = default)
            : base(rootToken)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            _uiRegistry = uiRegistry ?? throw new ArgumentNullException(nameof(uiRegistry));
            _contentRegistry = contentRegistry ?? throw new ArgumentNullException(nameof(contentRegistry));
            _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
            _ui = uiService ?? throw new ArgumentNullException(nameof(uiService));
            _redDot = redDotRegistry ?? throw new ArgumentNullException(nameof(redDotRegistry));
            _logicScheduler = logicScheduler ?? throw new ArgumentNullException(nameof(logicScheduler));
            _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
            _timelineRunner = timelineRunner ?? throw new ArgumentNullException(nameof(timelineRunner));
            _entities = entityService ?? throw new ArgumentNullException(nameof(entityService));
            _audio = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _vfx = vfxService ?? throw new ArgumentNullException(nameof(vfxService));
            _refill = refillService ?? throw new ArgumentNullException(nameof(refillService));
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, ct).Forget();          // 一行转发，仅此而已——禁止 async void（M0 指导 §6）

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, CancellationToken ct)
        {
            try
            {
                _container.RegisterInstance<ConfigService>(_config);
                _container.RegisterInstance<IConfigService>(_config);
                _container.RegisterInstance<SceneService>(_scenes);
                // 三注册表（M3 §2.4：注册表实例装配在唯一受信装配点；元素同为 LuaTable，标记接口区分）
                _container.RegisterInstance<IUILuaRegistry>(_uiRegistry);
                _container.RegisterInstance<IContentLuaRegistry>(_contentRegistry);
                _container.RegisterInstance<IStrategyLuaRegistry>(_strategyRegistry);
                _container.RegisterInstance<UIService>(_ui);    // UI 壳（M4 §2.1：薄壳 = DI 注册的普通服务）
                _container.RegisterInstance<RedDotRegistry>(_redDot);   // 红点规则口（M4 §2.5：完整树 = M4c）
                _container.RegisterInstance<ILogicScheduler>(_logicScheduler);   // 时序双轨（M4 §2.7：逻辑轨受时停）
                _container.RegisterInstance<IUIScheduler>(_uiScheduler);         // UI 轨不受时停
                _container.RegisterInstance<ITimelineRunner>(_timelineRunner);   // 时间轴执行器（剧情/技能）
                _container.RegisterInstance<EntityService>(_entities);   // 实体壳（M4 §2.8：池化+竞态表）
                _container.RegisterInstance<AudioService>(_audio);       // 声音壳（M4 §2.9：组+代理）
                _container.RegisterInstance<IVFXService>(_vfx);          // VFX 服务（M11：表现层，注册即发现 ITickable → 自动驱动到期回收）
                _container.RegisterInstance<LuaRegistryRefillService>(_refill);   // 运行期增量重填（§2.3；触发点 M11 + 调试菜单）
                _container.Seal();                       // 注册面冻结；此后 Resolve 不受限

                Bridge.Bind(() => _config.Tables);       // 服务桥装配期绑定（M2 C# 骨架，M3 绑成 Lua 全局表）
                Bridge.BindRegistries(_uiRegistry, _contentRegistry);   // ui/content 骨架门面查询底座（§2.5）
                Bridge.BindUIService(_ui);               // 真实门面 Show/Close/IsOpen 后端（M4 §2.3）

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
