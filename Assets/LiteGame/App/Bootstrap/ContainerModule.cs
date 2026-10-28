using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;
using LiteView;
using LiteClient;
using LiteNet.Transport;

namespace LiteGame
{
    /// <summary>
    /// 客户端装配末段：复用 Host 的 Root 来源，绑定产品能力，明确帧驱动序并预检密封。
    /// 本模块产物在 UI/内容等设施关闭前释放；场景句柄由本模块收尾。
    /// </summary>
    internal sealed class ContainerModule : IClientModule, IClientModuleGraph
    {
        private SceneService _scenes;
        private ServiceContainer _services;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        private IDisposable _gmSceneCommands;   // GM 场景命令注册（场景操作命令化批）——随模块 Shutdown 释放
        private IDisposable _weakNetCommand;    // 弱网预设命令注册
        private IDisposable _disconnectCommand; // 强制断线命令注册
        private IDisposable _reconnectCommand;  // 强制重连命令注册
        private IDisposable _suppressReconnectCommand; // 自动重连抑制命令注册
#endif

        public string Name => "Container";

        /// <summary>装配末段消费以上全部模块产物（注册序之外的第二道显式声明，供 DAG 校验）。</summary>
        public IReadOnlyList<Type> DependsOn => new[]
        {
            typeof(PlatformInfrastructureModule),
            typeof(IdentityModule),
            typeof(DevSessionModule),
            typeof(ContentModule),
            typeof(SettingsModule),
            typeof(ClocksModule),
            typeof(SchedulersModule),
            typeof(LuaHostModule),
            typeof(ConfigModule),
            typeof(UiServiceModule),
            typeof(PresentationModule),
            typeof(InputModule),
        };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            _services = context.Services;
            // 资源归属＝本模块作用域（路线图 §4.3）：装配中途失败时 Host 释放 scope 即回收
            // （命令注销 → 容器 → 场景租约，顺序与原 catch/Shutdown 一致）。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            Assemble(context);
            return UniTask.CompletedTask;
        }

        private void Assemble(ModuleContext context)
        {
            var config = context.Require<ConfigService>();
            var content = context.Require<IContentService>();
            var identity = context.Require<IdentityContext>();
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
            _services.RegisterInstance<IEventBus>(context.Require<EventBus>());
            var commands = new CommandCenter(
                context.Require<IReferencePool>(),
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                gmEnabled: true
#else
                gmEnabled: false
#endif
            );
            context.Put(commands);                                 // 产物登记＝具体键 CommandCenter（执行形态消费者经 Require<CommandCenter> 取）
            _services.RegisterInstance<ICommandCenter>(commands);  // 接口键（引擎面消费者/帧驱动表用——同实例、别名键）
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            // GM 场景命令（带参+异步，GmOnly）：注册即进发现面——场景页从直调改命令派发（加载/卸载进审计环）。
            // 注册点=装配期（app 生命周期，不依赖场景 DevStartupConfig 是否挂——GM 发现面在 dev 构建恒可用）。
            _gmSceneCommands = GmSceneCommands.RegisterAll(commands, _scenes);
            WeakNetRuntime weakNet = new WeakNetRuntime();
            context.Put(weakNet);
            // 网络调试组件（读模型 + 断线/重连动作）：会话上下文是流程私有件，经静态读口取件。
            var networkDebug = new NetworkDebug(weakNet, () => BattleDiagnostics.Current);
            context.Put(networkDebug);
            _weakNetCommand = commands.Register(new SetWeakNetPresetHandler(weakNet),
                new CommandOptions(gmOnly: true, description: "切换弱网预设（none/lan/mobile/lossy）"));
            _disconnectCommand = commands.Register(new ForceDisconnectHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "强制断线（调试：断后走自动重连）"));
            _reconnectCommand = commands.Register(new ForceReconnectHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "强制重连（仅 SuspectedLost 相位有效）"));
            _suppressReconnectCommand = commands.Register(new SetAutoReconnectSuppressedHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "抑制/恢复自动重连（断线停在 SuspectedLost）"));
            // 调试内核：读模型/动作模型/发现协议的唯一门面；投影（编辑器页/HUD/控制台）只消费它。
            var devSession = context.Require<IDevSession>();
            context.Put(new DebugKernel(commands, context.Require<IInputService>(),
                new IDebugTunableProvider[]
                {
                    new FeatureDebugTunables(devSession), new WeaponDebugTunables(devSession), weakNet,
                },
                weakNet, networkDebug, devSession));
#endif
            _services.RegisterInstance<IConfigTableSource>(config);
            _services.RegisterInstance<IConfigService>(config);
            _services.RegisterInstance<IUILuaRegistry>(uiRegistry);
            _services.RegisterInstance<IContentLuaRegistry>(contentRegistry);
            _services.RegisterInstance<IStrategyLuaRegistry>(strategyRegistry);
            _services.RegisterInstance(new LuaRegistryRefillService(lua, ui, config, UiPageCatalog.All, uiRegistry, contentRegistry, strategyRegistry));
            _services.RegisterInstance<ITimelineRunner>(context.Require<GameTimelineRunner>());
            _services.RegisterInstance<IVFXService>(context.Require<VfxService>());

            // 会话工厂（DI-3）：Account 半边零引擎依赖（L1 源链接直测）；Match 半边 Unity 面（DI-4 验收）。
            // 传输裁决经委托留给产品装配根（ProcedureMatch.CreateTransport——工厂不认识测试开关语义）；
            // **每局规则播种是显式步骤**（MatchRulesSeeder.Seed），传输工厂只接收规则实例产传输。
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
            Func<bool, MatchTransport> transportFactory =
                testRoom =>
                {
                    // 每局规则播种（显式步骤）：规则实例随传输同刻产出，经 AccountSession 显式移交给对局。
                    LiteSim.MatchTestRules rules = MatchRulesSeeder.Seed(testRoom);
                    return new MatchTransport(
                        ProcedureMatch.CreateTransport(testRoom, rules, deploy, gameplayDigestBase, maps), rules);
                };
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            transportFactory = WeakNetWiring.WrapFactory(transportFactory, weakNet);
#endif
            var accounts = new AccountSessionFactory(context.RootScope, _services, transportFactory,
                identity.Handshake);
            var matches = new MatchSessionFactory(context.Require<IInputService>(), gameplayDigestBase, maps,
                context.Require<IReferencePool>(), identity.Diagnostics);
            _services.RegisterInstance<IAccountSessionFactory>(accounts);
            _services.RegisterInstance<IMatchSessionFactory>(matches);

            // 联机入口编排件（批C-③）：未配置 Meta 基址 = client 置空（IsConfigured false——页面显性拒绝），
            // 不造"指向空址"的客户端；设备标识注入 Unity 面（服务本体零引擎依赖可直测）——`-device.id` 非空时
            // 以它为准（同机多客户端各自身份；同值 = 服务端同账号，见 MetaEntryConfig.DeviceId）。
            // 消费者＝Bridge.meta 门面（MetaBridge.Bind 直接装订）＋编辑器 GM 页「创建云端测试房」
            // （GmEditorResolve 取件；编辑器工具豁免形态，业务侧仍走 Bridge.meta）。
            Func<string> accountDeviceId = string.IsNullOrEmpty(deploy.DeviceId)
                ? (Func<string>)(() => UnityEngine.SystemInfo.deviceUniqueIdentifier)
                : () => deploy.DeviceId;
            var metaEntry = new MetaEntryService(
                deploy.HasMeta ? new UnityMetaClient(deploy.MetaUrl, deploy.TimeoutSeconds) : null,
                accountDeviceId,
                deploy.MetaUrl,
                identity);
            // 云端测试房：模板/口令随部署注入（开发者本地才有；测试者包不带 ⇒ 建不了云端测试房）
            metaEntry.ConfigureTestRoom(deploy.RoomTemplate, deploy.RoomGmKey);
            _services.RegisterInstance(metaEntry);   // GM 页云端测试房入口取件（MetaBridge 仍是主消费者）

            var dialogs = new DialogService(ui, nav: nav);
            _services.RegisterInstance(dialogs, ServiceOwnership.ContainerOwned);
            var feedback = new FeedbackService(ui, dialogs, nav);
            _services.RegisterInstance(feedback, ServiceOwnership.ContainerOwned);
            _services.RegisterInstance(new ToastTicker(() => feedback.ToastHost));

            Bridge.Bind(() => config.Tables);
            Bridge.BindRegistries(uiRegistry, contentRegistry);
            Bridge.BindUIService(ui);
            Bridge.BindNavigation(nav);
            MetaBridge.Bind(metaEntry, identity, () => config.Tables);   // Lua 联机门面后端（Bridge.meta——批C-③；表供房间行地图名显示）
            context.Require<IInputService>().RegisterBlocker(
                new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"), () => ui.IsModalOpen);

            // 模态登记（生产装配——与测试同口径，见 DialogServiceEditModeTests/FeedbackServiceEditModeTests）：
            // 设置页＝对局中的暂停菜单（打开即拦游戏输入——上面的 `ui.modal` 拦截源经 IsModalOpen 生效）；
            // 错误/确认弹窗＝模态弹窗（开着时其下不可命中、Back 先关最顶）。Loading/Toast 走射线阻断，不是模态。
            // **设置页（表单 15）不在此登记**：它须经生成页面目录反查（单源，id 不写第二份），而本装配点
            // 早于 ProcedurePreload 的 `LoadAsync`——此处查表必抛「配置未加载」阻断引导（实伤：SampleTripleChain）。
            // 登记随 ProcedurePreload 放行闸口（配置装载后）就位；此处只留不依赖表的两个弹窗 id
            // （单源在 FeedbackService 构造默认——审计 [低]「表单 id 单处」口径）。
            ui.RegisterModal(feedback.ErrorFormId);
            ui.RegisterModal(feedback.ConfirmFormId);

            // 内容身份绑定（只此一次）：内容/配置装配完成后，身份上下文用同一份 Activation/Config 事实回答。
            var activation = context.Require<ActivationTransactionStore>();
            identity.BindContent(() => new ContentIdentity(
                activation.ActiveGeneration.ReleaseId,
                activation.ActiveGeneration.Value,
                activation.Current.ConfirmedRevision,
                config.GameplayDigestBase,
                LiteSim.RootMotionCatalog.ContentDigest));

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
                typeof(DotweenUiClockDriver), typeof(IEventBus), typeof(UIService),
                typeof(ILogicScheduler), typeof(IUIScheduler), typeof(ITimelineRunner),
                typeof(IVFXService), typeof(DialogService), typeof(ToastTicker),
                typeof(ICommandCenter),
                typeof(StageMachine<ProcedureId, ProcedureArgs>));
            _services.Seal();
        }

        /// <summary>模块资源释放（由模块作用域在关闭/回滚时调用）。
        /// 顺序：命令注销 → 容器 → 场景租约（注销先于容器释放；场景句柄最后归零）。</summary>
        private void DisposeModuleResources()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _weakNetCommand?.Dispose();
            _weakNetCommand = null;
            _disconnectCommand?.Dispose();
            _disconnectCommand = null;
            _reconnectCommand?.Dispose();
            _reconnectCommand = null;
            _suppressReconnectCommand?.Dispose();
            _suppressReconnectCommand = null;
            _gmSceneCommands?.Dispose();          // 注销先于容器释放（注册/注销对称——幂等注销器）
            _gmSceneCommands = null;
#endif
            _services?.Dispose();
            _services = null;
            _scenes?.ReleaseAll();
            _scenes = null;
        }

        /// <summary>关闭面：资源已随 <see cref="ModuleContext.Scope"/> 释放（本方法无常驻清理）。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ModuleContext context,
            IContentService content, ConfigService config, LuaComponent lua,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
            PageFlow pageFlow, MetaEntryService metaEntry, FeedbackService feedback)
        {
            var filler = new RegistryFiller(config, UiPageCatalog.All, lua, uiRegistry, contentRegistry, strategyRegistry);
            var rootToken = context.RootScope.Token;
            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>())),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler,
                    context.Require<EventBus>(), context.Require<UIService>(), () => ListLuaAssetPaths(content), rootToken,
                    feedback)),
                (ProcedureId.Main, new ProcedureMain(rootToken, pageFlow, metaEntry)),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IAccountSessionFactory>(), feedback, rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(content, context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<VfxService>(),
                    rootToken, requireCamera: true, worldClock: context.Require<IWorldClock>(),
                    matchSessions: context.Require<IMatchSessionFactory>(),
                    audio: context.Require<AudioService>(), commands: context.Require<CommandCenter>(),
                    pages: pageFlow,
                    crosshairFeel: new CrosshairFeelProfile(config),     // 准心手感（tbcrosshair 惰性投影——装载后首用建）
                    scenes: _scenes,                                  // 开局入场加载 / 离场卸载对局场景组（都在 Battle）
                    maps: context.Require<IMapProvider>(),             // 多图批：场景路径查表行 scene 列 / 卸载按表枚举
                    feedback: feedback,                                // 开局入场加载遮罩（"正在进入对局…"）
                    themeProvider: context.Require<UIService>().ThemeAssetProvider,   // HUD 状态色主题注入（P2-3 B4）
                    devSession: context.Require<IDevSession>())),       // dev 投影读本局规则（实例端口，无静态当前局）
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
