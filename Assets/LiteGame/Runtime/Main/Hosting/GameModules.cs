using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>
    /// GameEntry 引导装配的模块序列（C1：《商业级通用客户端框架总设计》§6.1——
    /// "GameEntry 最终收敛为 Unity 引导适配器，真正的启动与关闭由 ClientHost 持有"）。
    ///
    /// **注册顺序 = 初始化顺序 = 关闭逆序**：模块间依赖只能引用更早模块的产物（经 ClientContext），
    /// 装配方用调用序表达依赖图，Host 不推断。产物经 context.Put/Get 传递——不用字典做服务定位，
    /// 同型唯一、类型编译期可见（装配错误显性失败）。
    ///
    /// 关闭语义（每模块只调已存在的能力，缺口归 U1/后续批）：Settings 退出保存；Lua 宿主 Shutdown；
    /// 红点注册清空；其余壳（VFX/Entity/Audio/FSM）无可关闭面 = no-op 并登记边界。
    /// 全部 Initialize/Shutdown 当前为同步完成（UniTask.CompletedTask）——首批保持行为等价，
    /// 异步化（真租约/热更）随 U1/C1 后续批替换签名内部实现，不影响 Host 编排。
    /// </summary>
    internal static class GameModules
    {
        /// <summary>① 平台基础设施：本地持久化 + 日志宿主（先于一切 IO；Log 未注入静默丢弃）。</summary>
        internal sealed class PlatformInfrastructure : IClientModule
        {
            public string Name => "PlatformInfrastructure";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                FileSys.Init(new UnityPathProvider(), new NewtonsoftJsonSerializer());
                Log.SetHelper(new UnityLogHelper());
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 日志落盘/崩溃上报归 C3
        }

        /// <summary>② 内容服务：IContentService 注入装配（YooAsset 适配 + 共享加载协调器 + 租约）——
        /// 配置/Lua 字节通道与 U1/C2 租约消费者依赖它（注册序即依赖序：先于 Config/Presentation）；
        /// 资源包初始化仍由 ProcedurePreload 经本服务触发；Host 下载/激活事务归批次D。</summary>
        internal sealed class Content : IClientModule
        {
            private YooAssetContentService _content;

            public string Name => "Content";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _content = new YooAssetContentService();
                context.Put<IContentService>(_content);
                context.Put(new ActivationTransactionStore(new FileActivationRecordIO()));   // C1-⑩：启动恢复决策（Patch 流程消费）
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
                => _content?.ShutdownAsync(ct) ?? UniTask.CompletedTask;   // 释放面：剩余租约/句柄归零
        }

        /// <summary>③ 设置：加载玩家偏好（先于容器——UI/声音壳注册时就要读）；退出前保存（§6.1 刷新钩子语义）。</summary>
        internal sealed class Settings : IClientModule
        {
            private SettingService _settings;

            public string Name => "Settings";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _settings = new SettingService();
                _settings.Load();
                context.Put(_settings);
                context.Put(new GameSettings(_settings));
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _settings?.Save();
                return UniTask.CompletedTask;
            }
        }

        /// <summary>④ 事件与时钟：双轨时钟分域（逻辑轨受时停/变速，UI 轨不受）+ 墙钟。</summary>
        internal sealed class Clocks : IClientModule
        {
            private EventCenter _events;
            private WorldClock _worldClock;
            private UIClock _uiClock;
            private SystemWallClock _wallClock;

            public string Name => "Clocks";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _events = new EventCenter();
                _worldClock = new WorldClock();
                _uiClock = new UIClock();
                _wallClock = new SystemWallClock();
                context.Put(_events);
                context.Put<IWorldClock>(_worldClock);
                context.Put<IUIClock>(_uiClock);
                context.Put<IWallClock>(_wallClock);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 时钟无关闭面（U1 Scope 统一取消）
        }

        /// <summary>⑤ 时序执行：逻辑/UI 调度器 + 时间轴执行器（依赖④时钟——模块序即依赖序）。</summary>
        internal sealed class Schedulers : IClientModule
        {
            public string Name => "Schedulers";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                context.Put<ILogicScheduler>(new LogicScheduler(context.Require<IWorldClock>()));
                context.Put<IUIScheduler>(new UIScheduler(context.Require<IUIClock>()));
                context.Put(new GameTimelineRunner(context.Require<IWorldClock>()));
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 同上——U1 统一取消
        }

        /// <summary>⑥ Lua 宿主（同 GameObject 组件）：Init/DoMain 在 Preload 锚点；退出 Shutdown 释放 env/回调。</summary>
        internal sealed class LuaHost : IClientModule
        {
            private readonly UnityEngine.GameObject _hostObject;
            private LuaComponent _lua;

            public string Name => "LuaHost";

            public LuaHost(UnityEngine.GameObject hostObject) => _hostObject = hostObject;

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                _lua = _hostObject.AddComponent<LuaComponent>();
                context.Put(_lua);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _lua?.Shutdown();
                return UniTask.CompletedTask;
            }
        }

        /// <summary>⑦ 配置：表投影数据源（UIFormCatalog/注册表填充依赖）。字节通道经 ②内容服务租约
        /// （C1-⑨：代次/引用统一——提取 bytes 即释放源资产，热更专项 §9）；候选校验→原子发布见 ConfigService。</summary>
        internal sealed class Config : IClientModule
        {
            public string Name => "Config";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
                context.Put(new ConfigService((location, token) => LoadBytesViaContentLease(content, location, token)));
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 配置重发布/事务归热更批

            /// <summary>经内容租约读表字节（TextAsset 提取 bytes 后即释放——复制数据不留源引用）。</summary>
            private static async UniTask<byte[]> LoadBytesViaContentLease(IContentService content, string location, CancellationToken ct)
            {
                var lease = await content.AcquireAsync<UnityEngine.TextAsset>(location, ct: ct);
                try { return lease.Asset.bytes; }
                finally { lease.Dispose(); }
            }
        }

        /// <summary>⑧ UI 壳：三注册表 + 红点 + UIService（逻辑解析器接 LuaBehaviourAdapter ← UI 注册表，解析失败降级 NullLogic）。
        /// U1-②：prefab 加载经内容服务租约（UI-05——实例持租约到销毁）；Shutdown 释放面（UI-06）。</summary>
        internal sealed class UiShell : IClientModule
        {
            private RedDotRegistry _redDotRegistry;
            private UIService _uiService;

            public string Name => "UiShell";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                var config = context.Require<ConfigService>();
                var lua = context.Require<LuaComponent>();
                var content = context.Require<IContentService>();   // 依赖②内容服务——注册序即依赖序
                _redDotRegistry = new RedDotRegistry();
                var uiRegistry = new UiLuaRegistry();
                var contentRegistry = new ContentLuaRegistry();
                var strategyRegistry = new StrategyLuaRegistry();
                context.Put(uiRegistry);
                context.Put(contentRegistry);
                context.Put(strategyRegistry);
                context.Put(_redDotRegistry);

                _uiService = new UIService(new UIFormCatalog(config),
                    logicResolver: info => new LuaBehaviourAdapter(lua.Env, uiRegistry.Get(info.LuaPath)),
                    loadPrefab: (location, token) => LoadPrefabLeaseAsync(content, location, token));
                context.Put(_uiService);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _redDotRegistry?.Clear();
                return _uiService?.ShutdownAsync() ?? UniTask.CompletedTask;   // U1-②：全关+缓存销毁+租约归零+Root 销毁
            }

            /// <summary>prefab 租约通道（UI-05）：内容服务 Acquire → AssetLease 转 IUIPrefabLease——
            /// UIService 不感知内容服务类型，只认可释放句柄（§3 资源适配边界）。</summary>
            private static async UniTask<IUIPrefabLease> LoadPrefabLeaseAsync(
                IContentService content, string location, CancellationToken ct)
            {
                var lease = await content.AcquireAsync<UnityEngine.GameObject>(location, ct: ct);
                return new ContentPrefabLease(lease);
            }
        }

        /// <summary>⑨ 表现壳：实体/声音/世界 VFX（加载口注入 AssetService，到期走逻辑时钟）。</summary>
        internal sealed class Presentation : IClientModule
        {
            public string Name => "Presentation";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                context.Put(new EntityService());
                context.Put(new AudioService());
                var vfxService = new VfxService(
                    loader: (location, token) => AssetService.LoadAssetAsync<UnityEngine.GameObject>(location, token),
                    clock: context.Require<IWorldClock>(),
                    catalog: new VfxCatalog(),
                    budget: VfxBudget.Default());
                context.Put(vfxService);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;   // 实例/租约释放归 U1/M11
        }

        /// <summary>⑩ 容器与流程机：注册（**注册顺序 = 驱动顺序**）→ 流程机（业务装配仍由 ProcedureLaunch Seal，§12）。
        /// 场景服务归本模块所有——关闭时释放全部场景句柄（§6.2 Scene 域退出动作，C1-⑦ 补宿主关闭释放面）。</summary>
        internal sealed class Container : IClientModule
        {
            private SceneService _scenes;

            public string Name => "Container";

            public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
            {
                var container = new ServiceContainer();
                var config = context.Require<ConfigService>();
                var content = context.Require<IContentService>();   // C1-⑧：Preload 经 IContentService 初始化资源（时序不变）
                _scenes = new SceneService();                    // 本模块所有——ShutdownAsync 逆序释放句柄（§6.2 Scene 域）
                var lua = context.Require<LuaComponent>();
                var events = context.Require<EventCenter>();
                var uiService = context.Require<UIService>();
                var uiRegistry = context.Require<UiLuaRegistry>();
                var contentRegistry = context.Require<ContentLuaRegistry>();
                var strategyRegistry = context.Require<StrategyLuaRegistry>();
                var redDotRegistry = context.Require<RedDotRegistry>();
                var logicScheduler = context.Require<ILogicScheduler>();
                var uiScheduler = context.Require<IUIScheduler>();
                var timelineRunner = context.Require<GameTimelineRunner>();
                var entityService = context.Require<EntityService>();
                var audioService = context.Require<AudioService>();
                var vfxService = context.Require<VfxService>();

                // 注册顺序 = 驱动顺序：MainThreadDispatcher 帧首泵最先 → 时钟 → FSM
                container.RegisterInstance<IMainThreadDispatcher>(new MainThreadDispatcher());
                container.RegisterInstance<IWorldClock>(context.Require<IWorldClock>());
                container.RegisterInstance<IUIClock>(context.Require<IUIClock>());
                container.RegisterInstance<IWallClock>(context.Require<IWallClock>());
                container.RegisterInstance<IEventCenter>(events);
                var fsm = CreateMachine(context, container, _scenes, content, config, lua, events, uiService, uiRegistry, contentRegistry,
                    strategyRegistry, redDotRegistry, logicScheduler, uiScheduler, timelineRunner,
                    entityService, audioService, vfxService);
                container.RegisterInstance<StageMachine<ProcedureId, ProcedureArgs>>(fsm);
                context.Put(fsm);
                context.Put(container);
                return UniTask.CompletedTask;
            }

            public UniTask ShutdownAsync(CancellationToken ct)
            {
                _scenes?.ReleaseAll();                          // 宿主关闭释放面：场景句柄租约归零（不卸场景——进程已在退出路径）
                return UniTask.CompletedTask;                   // 流程机无关闭面（对局/会话关闭归 C2）
            }

            /// <summary>原 GameEntry.CreateMachine 平移（依赖全部经 context 取——依赖不从 payload 取的纪律不变）。</summary>
            private static StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ClientContext context, ServiceContainer container,
                SceneService scenes, IContentService content, ConfigService config, LuaComponent lua, EventCenter events, UIService uiService,
                UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
                RedDotRegistry redDotRegistry, ILogicScheduler logicScheduler, IUIScheduler uiScheduler,
                GameTimelineRunner timelineRunner, EntityService entityService, AudioService audioService, VfxService vfxService)
            {
                var filler = new RegistryFiller(config, lua, uiRegistry, contentRegistry, strategyRegistry);
                var refill = new LuaRegistryRefillService(lua, uiService, config, uiRegistry, contentRegistry, strategyRegistry);
                var rootToken = context.RootScope.Token;            // C1-⑦ 统一取消链：流程阶段 CTS 链接宿主根令牌
                var activations = context.Require<ActivationTransactionStore>();   // C1-⑩：Patch 流程消费（Content ②产物）
                return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                    (ProcedureId.Launch, new ProcedureLaunch(container, config, scenes, uiRegistry, contentRegistry, strategyRegistry, uiService, redDotRegistry, logicScheduler, uiScheduler, timelineRunner, entityService, audioService, vfxService, refill, rootToken)),
                    (ProcedureId.Patch, new ProcedurePatch(content, activations, rootToken)),
                    (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler, events, rootToken)),
                    (ProcedureId.Main, new ProcedureMain(rootToken)),
                    (ProcedureId.Error, new ProcedureError(rootToken)));
            }
        }
    }
}
