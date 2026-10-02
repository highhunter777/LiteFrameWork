using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>
    /// ⑪ 容器与流程机：服务容器注册（**注册顺序 = 驱动顺序**）→ 流程机（业务装配仍由 ProcedureLaunch Seal，§12）。
    /// 场景服务归本模块所有——关闭时释放全部场景句柄（§6.2 Scene 域退出动作）。
    ///
    /// 这是装配根的**最后一段**，也是唯一同时认识 UI 运行时（<see cref="UIService"/>/导航/弹窗）与表现壳
    /// （<see cref="VfxService"/>）的地方——注册顺序即依赖序，此处不得重排时序。
    /// 模块清单与依赖序见 <see cref="GameEntry"/>。
    /// </summary>
    internal sealed class ContainerModule : IClientModule
    {
        private SceneService _scenes;

        public string Name => "Container";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            var container = new ServiceContainer();
            var config = context.Require<ConfigService>();
            var content = context.Require<IContentService>();   // Preload 经 IContentService 初始化资源（时序不变）
            _scenes = new SceneService();                    // 本模块所有——ShutdownAsync 逆序释放句柄（§6.2 Scene 域）
            // 场景能力经**抽象**暴露（§5.1 逻辑边界）：流程不认识 SceneService 具体类
            // （它是 YooAsset 适配器）。**注册归装配根**——流程不依赖适配器类型。
            container.RegisterInstance<ISceneService>(_scenes);
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

            // 注册顺序 = 驱动顺序：MainThreadDispatcher 帧首泵最先 → 时钟 → UI 动效轨泵 → FSM
            container.RegisterInstance<IMainThreadDispatcher>(new MainThreadDispatcher());
            container.RegisterInstance<IWorldClock>(context.Require<IWorldClock>());
            container.RegisterInstance<IUIClock>(context.Require<IUIClock>());
            container.RegisterInstance<IWallClock>(context.Require<IWallClock>());
            container.RegisterInstance(new DotweenUiClockDriver(context.Require<IUIClock>()));   // DOTween Manual 轨按 UIClock 派发（时停不停/暂停即停）
            container.RegisterInstance<IEventCenter>(events);
            var fsm = CreateMachine(context, container, content, config, lua, events, uiService, uiRegistry, contentRegistry,
                strategyRegistry, redDotRegistry, logicScheduler, uiScheduler, timelineRunner,
                entityService, audioService, vfxService);

            // 业务服务注册（**注册是装配职责**；UI 服务若在 Runtime 侧注册会让流程层必须认识 UI 运行时，
            // 与 Runtime → UI 成环）。注册顺序 = 驱动顺序（含 UIService **晚于** FSM 这一时序）——不得重排。
            RegisterBusinessServices(container, config, uiService, redDotRegistry, logicScheduler, uiScheduler,
                timelineRunner, entityService, audioService, vfxService);

            container.RegisterInstance<StageMachine<ProcedureId, ProcedureArgs>>(fsm);
            container.Seal();                                // 注册面冻结——装配期止于此
            context.Put(fsm);
            context.Put(container);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 业务服务注册。<b>顺序即驱动顺序</b>
        /// （<see cref="ServiceContainer.RegisterInstance{TInterface}"/> 注册即发现 ITickable）：
        /// 此处 UIService/DialogService/VfxService 均为 ITickable，顺序不可随手调整。
        /// </summary>
        private static void RegisterBusinessServices(ServiceContainer container, ConfigService config,
            UIService uiService, RedDotRegistry redDotRegistry, ILogicScheduler logicScheduler, IUIScheduler uiScheduler,
            GameTimelineRunner timelineRunner, EntityService entityService, AudioService audioService, VfxService vfxService)
        {
            var nav = new UINavigationController(uiService);      // 单写者导航（真实消费者：ProcedureMain 经装配根注入的能力）
            var dialogs = new DialogService(uiService);          // 弹窗服务（ITickable 随注册自动驱动；§4.3 ShowDialogAsync 口径）
            var feedback = new FeedbackService(uiService, dialogs, nav);   // Loading/Error/Toast 统一入口（System 层 form；Back 链首位拦截）

            container.RegisterInstance<UIService>(uiService);              // UI 壳（薄壳 = DI 注册的普通服务）
            container.RegisterInstance<UINavigationController>(nav);       // 导航
            container.RegisterInstance<RedDotRegistry>(redDotRegistry);    // 红点规则口
            container.RegisterInstance<ILogicScheduler>(logicScheduler);   // 时序双轨（逻辑轨受时停）
            container.RegisterInstance<IUIScheduler>(uiScheduler);         // UI 轨不受时停
            container.RegisterInstance<ITimelineRunner>(timelineRunner);   // 时间轴执行器（剧情/技能）
            container.RegisterInstance<EntityService>(entityService);      // 实体壳（池化+竞态表）
            container.RegisterInstance<AudioService>(audioService);        // 声音壳（组+代理）
            container.RegisterInstance<IVFXService>(vfxService);           // VFX 服务（表现层，注册即发现 ITickable → 自动驱动到期回收）
            container.RegisterInstance<DialogService>(dialogs);
            container.RegisterInstance<FeedbackService>(feedback);
            // Toast 多条计时：接 UIClock 步进（未打开时短路）；显式持有实例，不做全局单例查找
            container.RegisterInstance<ToastTicker>(new ToastTicker(() => feedback.ToastHost));
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _scenes?.ReleaseAll();                          // 宿主关闭释放面：场景句柄租约归零（不卸场景——进程已在退出路径）
            return UniTask.CompletedTask;                   // 流程机无关闭面（对局/会话关闭归 C2）
        }

        /// <summary>创建流程机（依赖全部经 context 取——依赖不从 payload 取的纪律不变）。</summary>
        private StageMachine<ProcedureId, ProcedureArgs> CreateMachine(ClientContext context, ServiceContainer container,
            IContentService content, ConfigService config, LuaComponent lua, EventCenter events, UIService uiService,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry,
            RedDotRegistry redDotRegistry, ILogicScheduler logicScheduler, IUIScheduler uiScheduler,
            GameTimelineRunner timelineRunner, EntityService entityService, AudioService audioService, VfxService vfxService)
        {
            var filler = new RegistryFiller(config, lua, uiRegistry, contentRegistry, strategyRegistry);
            var refill = new LuaRegistryRefillService(lua, uiService, config, uiRegistry, contentRegistry, strategyRegistry);
            var rootToken = context.RootScope.Token;            // 统一取消链：流程阶段 CTS 链接宿主根令牌
            var activations = context.Require<ActivationTransactionStore>();   // Patch 流程消费（Content ②产物）
            var patchRunner = context.Require<PatchRunner>();                  // 热更：内容事务编排
            // Lua 侧产物就地注册 + 服务桥绑定（§5.1 逻辑边界：适配器产物由装配根接线）
            container.RegisterInstance<IConfigTableSource>(config);   // 通用面（流程/装配用）
            container.RegisterInstance<IConfigService>(config);       // Luban 面（表投影/Lua 数据桥用）
            container.RegisterInstance<IUILuaRegistry>(uiRegistry);
            container.RegisterInstance<IContentLuaRegistry>(contentRegistry);
            container.RegisterInstance<IStrategyLuaRegistry>(strategyRegistry);
            container.RegisterInstance<LuaRegistryRefillService>(refill);
            Bridge.Bind(() => config.Tables);            // 绑定配置表为 Lua 全局表
            Bridge.BindRegistries(uiRegistry, contentRegistry);   // ui/content 骨架门面查询底座（§2.5）
            Bridge.BindUIService(uiService);             // 真实门面 Show/Close/IsOpen 后端

            // 导航是 UI 运行时类型——装配根在此把它的两个能力面**经委托**交给流程（§5.1 逻辑边界：
            // 流程不认识 UI）。单写者语义不变：委托绑定的仍是同一个 UINavigationController 实例。
            var nav = context.Require<UINavigationController>();
            Func<int, CancellationToken, UniTask> openUi =
                (formId, ct) => nav.GoAsync(formId, ct: ct);

            // 输入拦截源（《UI 框架总设计》§6.2"输入由单一协调者综合模态栈、转场锁、暂停和产品策略计算"；
            // 《角色状态与动作专项设计》§3 第 2 件）。**在装配根登记一次**——产品级、跨对局常驻，
            // 流程既不持"UI 是否拦截"这类判断，也不认识具体拦截源。
            //
            // 当前唯一登记项 `ui.modal`（UI 模态栈）。
            // **刻意不按会话相位拦**：断线但未出提示 UI 时（SuspectedLost → 自动重连的短暂窗口）
            // 游戏照常预测与操作——拦下会凭空改变玩法行为、把可玩的连接瞬间变成停顿；
            // "重连提示打开"本身就是一个模态页，由本条覆盖。
            // 其余源随各自消费者接入（暂停/失焦/平台返回）。
            var input = context.Require<IInputService>();
            input.RegisterBlocker(new IntentGate.BlockerKey("ui.modal", "模态 UI 打开——游戏意图被拦截"),
                () => uiService.IsModalOpen);

            // 相机服务（⑨ 表现壳建，**常驻**：vcam 是场景对象，随场景加载/卸载而生灭）。
            // 装配期**不做"当前有没有 vcam"的裁决**——启动场景（Test.unity）通常不带 vcam，
            // 玩法场景（训练场）才带，而服务自己会在场景切换后重新解析并接线
            // （见 CinemachineCameraService 的"场景切换后重新解析"段）。
            // 进对局仍要求**那一刻**有相机：没有的话对局会"跑得动、看不见"，属最难查的静默失效，
            // 由 ProcedureBattle(requireCamera: true) 在开打前显性失败。
            ICameraService camera = context.Require<ICameraService>();

            return new StageMachine<ProcedureId, ProcedureArgs>("Procedure",
                (ProcedureId.Launch, new ProcedureLaunch(rootToken)),
                (ProcedureId.Patch, new ProcedurePatch(content, activations, patchRunner, rootToken)),
                (ProcedureId.Preload, new ProcedurePreload(content, config, lua, filler, events, () => ListLuaAssetPaths(content), rootToken)),
                (ProcedureId.Main, new ProcedureMain(openUi, _scenes, rootToken)),
                (ProcedureId.Match, new ProcedureMatch(context.RootScope, rootToken)),
                (ProcedureId.Battle, new ProcedureBattle(content, input, camera, vfxService, rootToken, requireCamera: true)),
                (ProcedureId.Error, new ProcedureError(rootToken)));
        }

        /// <summary>
        /// Lua 清单绑定（tag 查询收口于**适配器**——以发布清单替换绑定，运行时零改动）。
        ///
        /// （§5.1 逻辑边界）：tag 查询经内容适配器暴露的 `ListLuaAssetPaths()`——
        /// **tag 查询是发布约定，归适配器**；装配点只做委托绑定。
        /// </summary>
        private static string[] ListLuaAssetPaths(IContentService content)
        {
            IReadOnlyList<string> paths = content.ListAssetPathsByTag("lua");
            if (paths == null || paths.Count == 0) return Array.Empty<string>();
            var result = new string[paths.Count];          // 不用 LINQ（R4 禁）
            for (int i = 0; i < paths.Count; i++) result[i] = paths[i];
            return result;
        }
    }
}
