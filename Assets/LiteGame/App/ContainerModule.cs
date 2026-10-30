using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;
using LiteFramework.UI;
using LiteGame.View;
using LiteFramework;
using LiteNet.Transport;
using LiteFramework;
using LiteGame;

namespace LiteGame
{
    /// <summary>
    /// 客户端装配末段：复用 Host 的 Root 来源，绑定产品能力，明确帧驱动序并预检密封。
    /// 本模块产物在 UI/内容等设施关闭前释放；场景句柄由本模块收尾。
    /// </summary>
    public sealed class ContainerModule : IClientModule, IClientModuleGraph
    {
        private SceneService _scenes;
        private ServiceContainer _services;
        private IDisposable _devDecoration;     // 开发装配装饰句柄（GM/弱网/调试内核；null = 发布形态）

        public string Name => "Container";

        /// <summary>装配末段消费以上全部模块产物（注册序之外的第二道显式声明，供 DAG 校验）。</summary>
        public IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure",
            "Identity",
            "DevSession",
            "Content",
            "Settings",
            "Events",
            "Clocks",
            "Schedulers",
            "LuaHost",
            "Config",
            "UiService",
            "Presentation",
            "Input",
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
            var config = context.Require<IConfigService>();
            var content = context.Require<IContentService>();
            var identity = context.Require<IdentityContext>();
            var lua = context.Require<LuaComponent>();
            var ui = context.Require<UIService>();
            var nav = context.Require<UINavigationController>();
            var uiRegistry = context.Require<UiLuaRegistry>();
            var contentRegistry = context.Require<ContentLuaRegistry>();
            var strategyRegistry = context.Require<StrategyLuaRegistry>();
            IDevSession devSession = context.Require<IDevSession>();   // 开发会话端口（③ 已产出；发布 = Null 实现）

            _scenes = new SceneService();
            _services.RegisterInstance<ISceneService>(_scenes);
            _services.RegisterInstance(new DotweenUiClockDriver(context.Require<IUIClock>()));
            _services.RegisterInstance<IEventBus>(context.Require<EventBus>());
            // 命令中心与主线程派发由 ① 平台基础设施的框架装配入口产出；本段只取件，
            // 不再手拼构造（形态位与构造顺序归框架）。
            var commands = context.Require<CommandCenter>();
            // 开发装配（GM 命令/弱网/调试内核）整段归 LiteGame.Dev，经钩子装饰；发布形态无 Dev 程序集 = 不装饰。
            _devDecoration = DevHooks.Current?.Decorate(new DevServices
            {
                Context = context,
                Commands = commands,
                Services = _services,
                Session = devSession,
                Scenes = _scenes,
                Input = context.Require<IInputService>(),
            });
            RegisterConfigAndLuaServices(context, config, lua, ui, uiRegistry, contentRegistry, strategyRegistry);

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
                    LiteSim.MatchTestRules rules = MatchRulesSeeder.Seed(testRoom, devSession);
                    return new MatchTransport(
                        ProcedureMatch.CreateTransport(testRoom, rules, deploy, gameplayDigestBase, maps), rules);
                };
            if (DevHooks.Current != null)
                transportFactory = DevHooks.Current.WrapTransportFactory(transportFactory);   // 弱网注入装饰器（开发形态）
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
                deploy.HasMeta ? new UnityMetaClient(deploy.MetaUrl, deploy.TimeoutSeconds, context.Require<IJsonCodec>()) : null,
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
            // 账号重入单点（《客户端依赖注入专项设计》§6.2「重新登录」）：登录失效（Meta 401）→ 清登录态 →
            // 在途对局回 Main → 登录页；Bridge.meta 是唯一调用方（凭证失效分支），页面不各自处理。
            var reentry = new AccountReentry(metaEntry, pageFlow);
            _services.RegisterInstance<IAccountReentry>(reentry);
            MetaBridge.Bind(metaEntry, identity, () => config.Tables, reentry);   // Lua 联机门面后端（Bridge.meta——批C-③；表供房间行地图名显示）
            var appSettings = new ProductSettings(context.Require<GameSettings>(),
                context.Require<SettingService>(), context.Require<AudioService>());
            ProductSettings.Bind(appSettings);            // Lua 设置门面后端（Bridge.settings——批C-⑥；构造即应用持久化值）
            // 流程端口（装配根是唯一组装点）：视图组工厂 + 产品桥装订口 + 房间会话门面——
            // 实现留在各适配/表现程序集，流程只认模块端口。
            BattleViewGroupFactory battleViews = CreateBattleViews(context, content, config, commands, devSession, appSettings);
            // 房间会话门面与产品桥共用同一实例（Battle 阶段装订的会话须被 env 重建后的门面读到）；
            // 产品桥登记为端口产物——DevReload 经容器取同一装订口重建 env。
            var roomBridge = new RoomBridge();
            var productBridge = new ProductBridges(roomBridge);
            _services.RegisterInstance<ILuaProductBridge>(productBridge);
            // 恢复动作的「清缓存」实现面：删候选内容产物 + 激活记录回退 builtin（内置包根不动）。
            var contentCache = new ContentCacheMaintenance(content, context.Require<IFileStore>(), activation);
            _services.RegisterInstance<IContentCacheMaintenance>(contentCache);
            var recovery = new ErrorRecoveryService(new AppRecoveryActions(cache: contentCache));
            context.Put(recovery);                        // 恢复执行器（Root：启动/内容/会话失败共用）
            var fsm = CreateMachine(context, content, config, lua, uiRegistry, contentRegistry, strategyRegistry,
                pageFlow, metaEntry, feedback, recovery, productBridge, roomBridge, battleViews);
            context.Put(fsm);
            metaEntry.BindMachine(fsm);                   // 进房 = Request(Match, MetaJoin)——走既有 Join 管道

            // 帧驱动表独立于模块产物登记序；每个别名仍指向统一来源中的同一实例。
            // CommandCenter 置流程机前：排队/延迟命令到期派发先于机器 Tick——命令对流程机的 Request 当帧可被应用。
            _services.SetTickOrder(
                typeof(IMainThreadDispatcher), typeof(IWorldClock), typeof(IUIClock),
                typeof(DotweenUiClockDriver), typeof(UIService),   // M3-7 7b 起 EventBus 无队列、不再实现 ITickable
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
            _devDecoration?.Dispose();            // 开发装饰：命令注销 + Dev 静态读口收口（先于容器释放）
            _devDecoration = null;
            _services?.Dispose();
            _services = null;
            _scenes?.ReleaseAll();
            _scenes = null;
        }

        /// <summary>关闭面：资源已随 <see cref="ModuleContext.Scope"/> 释放（本方法无常驻清理）。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;

        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ModuleContext context,
            IContentService content, IConfigService config, LuaComponent lua,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
            PageFlow pageFlow, MetaEntryService metaEntry, FeedbackService feedback, ErrorRecoveryService recovery,
            ILuaProductBridge productBridge, IRoomSessionBridge roomBridge, IBattleViewGroupFactory battleViews)
        {
            var filler = new RegistryFiller(config, UiPageCatalog.All, lua, uiRegistry, contentRegistry, strategyRegistry);
            var rootToken = context.RootScope.Token;
            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, context.Require<ActivationTransactionStore>(),
                    context.Require<PatchRunner>(), rootToken, context.Require<AssetsHealthProbe>(), recovery)),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler, productBridge,
                    context.Require<EventBus>(), context.Require<UIService>(), () => ListLuaAssetPaths(content), rootToken,
                    feedback)),
                (ProcedureId.Main, new ProcedureMain(rootToken, pageFlow, metaEntry,
                    dev: context.Require<IDevSession>())),
                (ProcedureId.Match, new ProcedureMatch(context.Require<IAccountSessionFactory>(), feedback, rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(context.Require<IInputService>(),
                    context.Require<ICameraService>(), context.Require<CommandCenter>(), battleViews, roomBridge,
                    rootToken, requireCamera: true,
                    matchSessions: context.Require<IMatchSessionFactory>(),
                    pages: pageFlow,
                    scenes: _scenes,                                  // 开局入场加载 / 离场卸载对局场景组（都在 Battle）
                    maps: context.Require<IMapProvider>(),             // 多图批：场景路径查表行 scene 列 / 卸载按表枚举
                    feedback: feedback,                                // 开局入场加载遮罩（"正在进入对局…"）
                    devSession: context.Require<IDevSession>())),       // dev 投影读本局规则（实例端口，无静态当前局）
                (ProcedureId.Error, new ProcedureError(rootToken, recovery)));
        }

        /// <summary>配置/注册表/时间线/VFX 接口键注册（装配期一次性；消费者经构造注入解析）。</summary>
        private void RegisterConfigAndLuaServices(ModuleContext context, IConfigService config, LuaComponent lua,
            UIService ui, UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry,
            StrategyLuaRegistry strategyRegistry)
        {
            _services.RegisterInstance<IUILuaRegistry>(uiRegistry);
            _services.RegisterInstance<IContentLuaRegistry>(contentRegistry);
            _services.RegisterInstance<IStrategyLuaRegistry>(strategyRegistry);
            _services.RegisterInstance(new LuaRegistryRefillService(lua, ui, config, UiPageCatalog.All, uiRegistry, contentRegistry, strategyRegistry));
            _services.RegisterInstance<ITimelineRunner>(context.Require<GameTimelineRunner>());
            _services.RegisterInstance<IVFXService>(context.Require<VfxService>());
        }

        /// <summary>视图组工厂装配（视图域依赖留在表现层；流程只认 <see cref="IBattleViewGroupFactory"/>）。</summary>
        private static BattleViewGroupFactory CreateBattleViews(ModuleContext context, IContentService content,
            IConfigService config, CommandCenter commands, IDevSession devSession, ProductSettings appSettings)
            => new BattleViewGroupFactory(content, context.Require<IInputService>(),
                context.Require<ICameraService>(), context.Require<VfxService>(), context.Require<IWorldClock>(),
                context.Require<AudioService>(), commands,
                crosshairFeel: new CrosshairFeelProfile(config),      // 准心手感（tbcrosshair 惰性投影——装载后首用建）
                themeProvider: context.Require<UIService>().ThemeAssetProvider,   // HUD 状态色主题注入（P2-3 B4）
                dev: devSession,                                      // dev 投影读本局规则（实例端口，无静态当前局）
                lookPreferences: appSettings.Look);                   // 视角手感偏好（设置页改值即生效）

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
