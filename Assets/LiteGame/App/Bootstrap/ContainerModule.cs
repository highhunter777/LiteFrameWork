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
            var deploy = MetaDeployConfig.Load(Environment.GetCommandLineArgs(), DeployBake.ReadArgs());
            // 联机部署三源注入（-meta.url/-envelope.key——命令行 > 包内 deploy.args 烙入 > 默认；单次快照）
            // 玩法数据身份基摘要提供者（批 E）：装配期只装**提供者**（本模块早于 Preload 装载配置——
            // 取值点=进房/LocalServer 建房，恒在装载之后）；生产消费者=Match 会话工厂 + 本地服房
            // 来源同源（同一 ConfigService 实例）——两端摘要由构造保证同源。
            Func<uint> gameplayDigestBase = () => config.GameplayDigestBase;
            var maps = new MapProvider(config);            // 地图提供者（D9）：已装载表链 → 判定图（两端同源投影）
            _services.RegisterInstance<IMapProvider>(maps);
            var accounts = new AccountSessionFactory(context.RootScope, _services,
                testRoom => ProcedureMatch.CreateTransportForFactory(testRoom, deploy, gameplayDigestBase, maps));
            var matches = new MatchSessionFactory(context.Require<IInputService>(), gameplayDigestBase, maps);
            _services.RegisterInstance<IAccountSessionFactory>(accounts);
            _services.RegisterInstance<IMatchSessionFactory>(matches);

            // 联机入口编排件（批C-③）：未配置 Meta 基址 = client 置空（IsConfigured false——页面显性拒绝），
            // 不造"指向空址"的客户端；设备标识注入 Unity 面（服务本体零引擎依赖可直测）——`-device.id` 非空时
            // 以它为准（同机多客户端各自身份；同值 = 服务端同账号，见 MetaEntryConfig.DeviceId）。
            // 消费者＝Bridge.meta 门面（MetaBridge.Bind 直接装订——不占容器键，无容器解析点）。
            Func<string> accountDeviceId = string.IsNullOrEmpty(deploy.DeviceId)
                ? (Func<string>)(() => UnityEngine.SystemInfo.deviceUniqueIdentifier)
                : () => deploy.DeviceId;
            var metaEntry = new MetaEntryService(
                deploy.HasMeta ? new UnityMetaClient(deploy.MetaUrl, deploy.TimeoutSeconds) : null,
                accountDeviceId,
                deploy.MetaUrl);

            var dialogs = new DialogService(ui, nav: nav);
            _services.RegisterInstance(dialogs, ServiceOwnership.ContainerOwned);
            var feedback = new FeedbackService(ui, dialogs, nav);
            _services.RegisterInstance(feedback, ServiceOwnership.ContainerOwned);
            _services.RegisterInstance(new ToastTicker(() => feedback.ToastHost));

            Bridge.Bind(() => config.Tables);
            Bridge.BindRegistries(uiRegistry, contentRegistry);
            Bridge.BindUIService(ui);
            Bridge.BindNavigation(nav);
            MetaBridge.Bind(metaEntry, () => config.Tables);   // Lua 联机门面后端（Bridge.meta——批C-③；表供房间行地图名显示）
            context.Require<IInputService>().RegisterBlocker(
                new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"), () => ui.IsModalOpen);

            // 模态登记（生产装配——与测试同口径，见 DialogServiceEditModeTests/FeedbackServiceEditModeTests）：
            // 设置页＝对局中的暂停菜单（打开即拦游戏输入——上面的 `ui.modal` 拦截源经 IsModalOpen 生效）；
            // 错误/确认弹窗＝模态弹窗（开着时其下不可命中、Back 先关最顶）。Loading/Toast 走射线阻断，不是模态。
            // **设置页（表单 15）不在此登记**：它须经 tbuiform 表驱动反查（单源，id 不写第二份），而本装配点
            // 早于 ProcedurePreload 的 `LoadAsync`——此处查表必抛「配置未加载」阻断引导（实伤：SampleTripleChain）。
            // 登记随 ProcedurePreload 放行闸口（配置装载后）就位；此处只留不依赖表的两个弹窗 id
            // （单源在 FeedbackService 构造默认——审计 [低]「表单 id 单处」口径）。
            ui.RegisterModal(feedback.ErrorFormId);
            ui.RegisterModal(feedback.ConfirmFormId);

            var pageFlow = new PageFlow(nav, ui, lua);    // 五页页面流（批C-④）：入口/开局关房/结算三处流程×页面接缝
            var appSettings = new ProductSettings(context.Require<GameSettings>(),
                context.Require<SettingService>(), context.Require<AudioService>());
            ProductSettings.Bind(appSettings);            // Lua 设置门面后端（Bridge.settings——批C-⑥；构造即应用持久化值）
            var fsm = CreateMachine(context, content, config, lua, uiRegistry, contentRegistry, strategyRegistry,
                pageFlow, metaEntry, feedback);
            context.Put(fsm);
            metaEntry.BindMachine(fsm);                   // 进房 = Request(Match, MetaJoin)——走既有 Join 管道

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
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
            PageFlow pageFlow, MetaEntryService metaEntry, FeedbackService feedback)
        {
            var filler = new RegistryFiller(config, lua, uiRegistry, contentRegistry, strategyRegistry);
            var rootToken = context.RootScope.Token;
            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>())),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler,
                    context.Require<EventCenter>(), context.Require<UIService>(), () => ListLuaAssetPaths(content), rootToken,
                    feedback)),
                (ProcedureId.Main, new ProcedureMain(rootToken, pageFlow, metaEntry)),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IAccountSessionFactory>(), _scenes, feedback,
                    context.Require<IMapProvider>(), rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(content, context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<VfxService>(),
                    rootToken, requireCamera: true, worldClock: context.Require<IWorldClock>(),
                    matchSessions: context.Require<IMatchSessionFactory>(),
                    audio: context.Require<AudioService>(), commands: context.Require<CommandCenter>(),
                    pages: pageFlow,
                    crosshairFeel: new CrosshairFeelBook(config),     // 准心手感（tbcrosshair 惰性投影——装载后首用建）
                    scenes: _scenes,                                  // 离场卸载对局场景组（加载在 Match）
                    maps: context.Require<IMapProvider>())),           // 多图批：卸载按表枚举地图场景清单
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
