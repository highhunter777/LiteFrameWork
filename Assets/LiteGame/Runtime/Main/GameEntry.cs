using System;
using System.Collections.Generic;
using System.Threading;
using LiteFramework;
using LiteSim.View;
using UnityEngine;

namespace LiteGame
{
    /// <summary>骨架装配点 + 根容器宿主（§12）。容器不静态暴露 Resolve；实例全程持有供驱动枚举 Tickables。</summary>
    [DefaultExecutionOrder(-1000)]   // 必须最先 Awake：RegisterInstance 静态入口只在本件 Awake 后可用
    public sealed class GameEntry : MonoBehaviour
    {
        private static ServiceContainer s_container;
        private static StageMachine<ProcedureId, ProcedureArgs> s_fsm;   // 通用流程状态机（2026-09-17 A 路线）
        private bool _active;

        /// <summary>组件 Awake 的唯一入口：**只许注册，不许解析**；装配密封后抛。</summary>
        public static void RegisterInstance<TInterface>(TInterface instance)
        {
            if (s_container == null || s_container.IsSealed)
                throw new InvalidOperationException("GameEntry 未装配或装配已密封");
            s_container.RegisterInstance(instance);
        }

        /// <summary>把装配权传给起始流程。ProcedureLaunch 是**唯一受信装配点**：
        /// 注册业务服务 → Seal。用 public：ProcedureLaunch 在 LiteGame.Runtime，跨程序集访问。</summary>
        public ServiceContainer TakeContainer()
        {
            if (s_container == null) throw new InvalidOperationException("未装配");
            return s_container;
        }

        /// <summary>只读统计列表（DevHUD 跨程序集拉取用）：**不是解析入口**，不含容器语义。</summary>
        public IReadOnlyList<IModuleStats> Stats => s_container != null
            ? s_container.Stats
            : Array.Empty<IModuleStats>();

        private void Awake()
        {
            // 重复引导守卫（叠加加载场景时的真实隐患）：场景实例若误含 GameEntry，DontDestroyOnLoad + Awake
            // 会二次引导并覆盖静态容器/静态设施——静默销毁重复件，保留首个引导（实测见 M2 指导实施记录）。
            if (s_container != null)
            {
                Log.Warning($"检测到重复 GameEntry（场景 {gameObject.scene.name} 误含引导件）——已销毁，保留首个引导", "GameEntry");
                Destroy(gameObject);
                return;
            }
            _active = true;

            // 1. 基础设施（FileSys 未 Init 一切 IO 抛；Log 未注入静默丢弃）
            FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
            Log.SetHelper(new UnityLogHelper());

            // 2. 设置（先于容器与各壳注册：UI/声音壳注册时就要读玩家偏好）
            var settings = new SettingService();
            settings.Load();

            // 3. 容器
            s_container = new ServiceContainer();

            // 4. 创建骨架件与设置消费面
            var events = new EventCenter();
            var worldClock = new WorldClock();
            var uiClock = new UIClock();
            var wallClock = new SystemWallClock();
            var gameSettings = new GameSettings(settings);

            // 4.5 Lua 宿主组件（M3 §2.4 接线，2.3 交付件）：挂 GameEntry 同 GameObject；
            //     Init/DoMain 在 Preload 锚点（依赖预载缓存就绪），不进 DI（Unity 组件不进容器，§3.2）
            var lua = gameObject.AddComponent<LuaComponent>();

            // 4.6 UI 壳（M4 §2.0–2.3）：ConfigService 提前到装配点（UIFormCatalog 投影依赖）；
            //     三注册表 + 目录投影 + 壳服务（逻辑解析器接 LuaBehaviourAdapter ← UI 注册表，
            //     解析失败在 UIService 内降级 NullLogic——错误语义"注册失败" §3.4）
            var config = new ConfigService((location, ct) => AssetService.LoadRawFileBytesAsync(location, ct));
            var uiRegistry = new UiLuaRegistry();
            var contentRegistry = new ContentLuaRegistry();
            var strategyRegistry = new StrategyLuaRegistry();
            var redDotRegistry = new RedDotRegistry();        // 红点规则注册口（M4 §2.5：完整红点树 = M4c）
            var uiService = new UIService(new UIFormCatalog(config),
                logicResolver: info => new LuaBehaviourAdapter(lua.Env, uiRegistry.Get(info.LuaPath)));

            // 4.7 时序执行（M4 §2.7，决策 ① 双轨分时）：逻辑轨=WorldClock（受时停/变速）/ UI 轨=UIClock（不受时停）；
            //     时间轴执行器走逻辑轨（剧情/技能=判定层）。冻结/变速语义单源时钟，执行器不自读 Time
            var logicScheduler = new LogicScheduler(worldClock);
            var uiScheduler = new UIScheduler(uiClock);
            var timelineRunner = new GameTimelineRunner(worldClock);

            // 4.8 薄壳双件（M4 §2.8/§2.9，手册步骤 6）：实体壳（池化+竞态表）/ 声音壳（组+代理）——均不转发 Lua
            var entityService = new EntityService();
            var audioService = new AudioService();

            // 4.9 表现层：VFX 服务（《VFX服务实施指导》，M11）——世界空间粒子（加载/池化/挂点跟随/预算）。
            //     加载口注入 AssetService（LiteSim.View 不反向依赖 LiteGame）；到期走世界时钟（时停即冻结）。
            //     接缝（SimView 观察层）属 M11 C3，本处只装配服务本体。
            var vfxService = new VfxService(
                loader: (location, ct) => AssetService.LoadAssetAsync<GameObject>(location, ct),
                clock: worldClock,
                catalog: new VfxCatalog(),
                budget: VfxBudget.Default());

            // 5. 注册（**注册顺序 = 驱动顺序**：MainThreadDispatcher 帧首泵最先 → 时钟 → FSM；
            //    注册即发现自动收集 ITickable/IModuleStats，无需手工维护列表）
            s_container.RegisterInstance<IMainThreadDispatcher>(new MainThreadDispatcher());
            s_container.RegisterInstance<IWorldClock>(worldClock);
            s_container.RegisterInstance<IUIClock>(uiClock);
            s_container.RegisterInstance<IWallClock>(wallClock);
            s_container.RegisterInstance<IEventCenter>(events);
            s_container.RegisterInstance<StageMachine<ProcedureId, ProcedureArgs>>(s_fsm = CreateMachine(config, lua, events, uiService, uiRegistry, contentRegistry, strategyRegistry, redDotRegistry, logicScheduler, uiScheduler, timelineRunner, entityService, audioService, vfxService));
            s_container.RegisterInstance<SettingService>(settings);
            s_container.RegisterInstance<GameSettings>(gameSettings);

            // 6. 调试组件注入（同 GameObject；均为可选——未挂即跳过）
            //    守卫与 DebugTuner/M0SelfTestRunner 定义处一致：release Player 下类型被条件编译移除。
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            GetComponent<DebugTuner>()?.Inject(worldClock, uiClock, events);
            GetComponent<M0SelfTestRunner>()?.Inject(s_container.Tickables, s_container.Stats);
#endif
            // DevHUD 跨程序集（LiteGame.DevHUD → LiteGame.Runtime 单向），自拉取：见 DevHUD.Start

            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 流程四阶段（M2；2026-09-17 A 路线：`Fsm&lt;TOwner&gt;` → `StageMachine&lt;ProcedureId, ProcedureArgs&gt;`）。
        /// 业务服务在 ProcedureLaunch 装配（注册 IConfigService/SceneService → Seal）；
        /// 流程依赖在装配点（本 Awake）构造注入存为流程字段——依赖不从 payload 取（局部服务定位器同罪）。
        /// 流程间传参走 `ProcedureArgs` payload（编译期强类型；Owner 载体已退休）。
        /// </summary>
        private static StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ConfigService config, LuaComponent lua, EventCenter events,
            UIService uiService, UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry,
            StrategyLuaRegistry strategyRegistry, RedDotRegistry redDotRegistry,
            ILogicScheduler logicScheduler, IUIScheduler uiScheduler, GameTimelineRunner timelineRunner,
            EntityService entityService, AudioService audioService, VfxService vfxService)
        {
            var scenes = new SceneService();
            var filler = new RegistryFiller(config, lua, uiRegistry, contentRegistry, strategyRegistry);
            var refill = new LuaRegistryRefillService(lua, uiService, config, uiRegistry, contentRegistry, strategyRegistry);
            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(s_container, config, scenes, uiRegistry, contentRegistry, strategyRegistry, uiService, redDotRegistry, logicScheduler, uiScheduler, timelineRunner, entityService, audioService, vfxService, refill)),
                (ProcedureId.Preload, new ProcedurePreload(config, lua, filler, events)),
                (ProcedureId.Main, new ProcedureMain()),
                (ProcedureId.Error, new ProcedureError()));
        }

        /// <summary>状态机启动放 Start：晚于全部组件 Awake 的 RegisterInstance——流程顺序契约
        /// （ProcedureLaunch 会 Seal 封注册面，密封后组件注册即违例）。</summary>
        private void Start()
        {
            if (!_active) return;                            // 重复引导件：Awake 已销毁，不参与
            TakeContainer();                                 // 断言已装配
            s_fsm.Start(ProcedureId.Launch);
        }

        private void Update()
        {
            if (!_active) return;                            // 重复引导件销毁前的最后一帧不驱动
            // 统一喂真实帧间隔：变速由 IGameClock 内部缩放（勿用 Time.deltaTime 二次乘）
            foreach (var t in s_container.Tickables) t.Tick(Time.unscaledDeltaTime);
        }
    }
}
