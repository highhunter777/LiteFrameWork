using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;
using LiteView;
using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 客户端装配末段：复用 Host 的 Root 来源，绑定产品能力，明确帧驱动序并预检密封。
    /// 本模块产物在 UI/内容等设施关闭前释放；场景句柄由本模块收尾。
    /// </summary>
    internal sealed class ContainerModule : IClientModule
    {
        private SceneService _scenes;
        private ServiceContainer _services;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private IDisposable _gmSceneCommands;   // GM 场景命令注册（场景操作命令化批）——随模块 Shutdown 释放
#endif

        public string Name => "Container";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            _services = context.Services;
            try
            {
                Assemble(context);
                return UniTask.CompletedTask;
            }
            catch
            {
                _services.Dispose();
                _scenes?.ReleaseAll();
                throw;
            }
        }

        private void Assemble(ClientContext context)
        {
            var config = context.Require<ConfigService>();
            var content = context.Require<IContentService>();
            var lua = context.Require<LuaComponent>();
            var ui = context.Require<UIService>();
            var nav = context.Require<UINavigationController>();
            var uiRegistry = context.Require<UiLuaRegistry>();
            var contentRegistry = context.Require<ContentLuaRegistry>();
            var strategyRegistry = context.Require<StrategyLuaRegistry>();

            _scenes = new SceneService();
            _services.RegisterInstance<ISceneService>(_scenes);
            _services.RegisterInstance<IMainThreadDispatcher>(new MainThreadDispatcher());
            _services.RegisterInstance(new DotweenUiClockDriver(context.Require<IUIClock>()));
            _services.RegisterInstance<IEventCenter>(context.Require<EventCenter>());
            var commands = new CommandCenter(
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                true
#else
                false
#endif
            );
            context.Put(commands);                                 // 产物登记＝具体键 CommandCenter（执行形态消费者经 Require<CommandCenter> 取）
            _services.RegisterInstance<ICommandCenter>(commands);  // 接口键（引擎面消费者/帧驱动表用——同实例、别名键）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // GM 场景命令（带参+异步，GmOnly）：注册即进发现面——场景页从直调改命令派发（加载/卸载进审计环）。
            // 注册点=装配期（app 生命周期，不依赖场景 DebugTuner 是否挂——GM 发现面在 dev 构建恒可用）。
            _gmSceneCommands = GmSceneCommands.RegisterAll(commands, _scenes);
#endif
            _services.RegisterInstance<IConfigTableSource>(config);
            _services.RegisterInstance<IConfigService>(config);
            _services.RegisterInstance<IUILuaRegistry>(uiRegistry);
            _services.RegisterInstance<IContentLuaRegistry>(contentRegistry);
            _services.RegisterInstance<IStrategyLuaRegistry>(strategyRegistry);
            _services.RegisterInstance(new LuaRegistryRefillService(lua, ui, config, uiRegistry, contentRegistry, strategyRegistry));
            _services.RegisterInstance<ITimelineRunner>(context.Require<GameTimelineRunner>());
            _services.RegisterInstance<IVFXService>(context.Require<VfxService>());

            // 会话工厂（DI-3）：Account 半边零引擎依赖（L1 源链接直测）；Match 半边 Unity 面（DI-4 验收）。
            // 传输裁决经委托留给产品装配根（ProcedureMatch.CreateTransportForFactory——工厂不认识测试开关语义）。
            // 注册面＝服务接口（消费者经构造注入解析；Put 与 RegisterInstance 写同一容器——接口键只注册一次，
            // 具体类型无产物消费者不占键）。
            var accounts = new AccountSessionFactory(context.RootScope, _services,
                testRoom => ProcedureMatch.CreateTransportForFactory(testRoom));
            var matches = new MatchSessionFactory(context.Require<IInputService>());
            _services.RegisterInstance<IAccountSessionFactory>(accounts);
            _services.RegisterInstance<IMatchSessionFactory>(matches);

            var dialogs = new DialogService(ui, nav: nav);
            _services.RegisterInstance(dialogs, ServiceOwnership.ContainerOwned);
            var feedback = new FeedbackService(ui, dialogs, nav);
            _services.RegisterInstance(feedback, ServiceOwnership.ContainerOwned);
            _services.RegisterInstance(new ToastTicker(() => feedback.ToastHost));

            Bridge.Bind(() => config.Tables);
            Bridge.BindRegistries(uiRegistry, contentRegistry);
            Bridge.BindUIService(ui);
            Bridge.BindNavigation(nav);
            context.Require<IInputService>().RegisterBlocker(
                new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"), () => ui.IsModalOpen);

            var fsm = CreateMachine(context, content, config, lua, uiRegistry, contentRegistry, strategyRegistry);
            context.Put(fsm);

            // 帧驱动表独立于模块产物登记序；每个别名仍指向统一来源中的同一实例。
            // CommandCenter 置流程机前：排队/延迟命令到期派发先于机器 Tick——命令对流程机的 Request 当帧可被应用。
            _services.SetTickOrder(
                typeof(IMainThreadDispatcher), typeof(IWorldClock), typeof(IUIClock),
                typeof(DotweenUiClockDriver), typeof(IEventCenter), typeof(UIService),
                typeof(ILogicScheduler), typeof(IUIScheduler), typeof(ITimelineRunner),
                typeof(IVFXService), typeof(DialogService), typeof(ToastTicker),
                typeof(ICommandCenter),
                typeof(StageMachine<ProcedureId, ProcedureArgs>));
            _services.Seal();
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _gmSceneCommands?.Dispose();          // 注销先于容器释放（注册/注销对称——幂等注销器）
            _gmSceneCommands = null;
#endif
            _services?.Dispose();
            _scenes?.ReleaseAll();
            return UniTask.CompletedTask;
        }

        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ClientContext context,
            IContentService content, ConfigService config, LuaComponent lua,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry)
        {
            var filler = new RegistryFiller(config, lua, uiRegistry, contentRegistry, strategyRegistry);
            var rootToken = context.RootScope.Token;
            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>())),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler,
                    context.Require<EventCenter>(), () => ListLuaAssetPaths(content), rootToken)),
                (ProcedureId.Main, new ProcedureMain(_scenes, rootToken)),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IAccountSessionFactory>(), rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(content, context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<VfxService>(),
                    rootToken, requireCamera: true, worldClock: context.Require<IWorldClock>(),
                    matchSessions: context.Require<IMatchSessionFactory>(),
                    audio: context.Require<AudioService>(), commands: context.Require<CommandCenter>())),
                (ProcedureId.Error, new ProcedureError(rootToken)));
        }

        /// <summary>Lua 资源清单由内容适配器按发布约定查询，装配点绑定预载委托。</summary>
        private static string[] ListLuaAssetPaths(IContentService content)
        {
            IReadOnlyList<string> paths = content.ListAssetPathsByTag("lua");
            if (paths == null || paths.Count == 0) return Array.Empty<string>();
            var result = new string[paths.Count];
            for (int i = 0; i < paths.Count; i++) result[i] = paths[i];
            return result;
        }
    }
}
