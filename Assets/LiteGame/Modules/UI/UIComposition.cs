using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Config;

namespace LiteGame.Modules.UI
{
    /// <summary>⑬ UI 能力段装配：页面目录端口 + 数据面端口 + 红点 + UIService（逻辑解析器经
    /// <see cref="ILuaUIBinding"/> 端口注入 ← 实现住 Lua 桥适配器，解析失败降级 NullLogic）
    /// + LText 语言服务（tbltext→目录→服务，批C-② 生产装配——页面 SetTextKey 由"必抛"转正）
    /// + 主题资产（P2-3 B2：内容服务一次装载 → ThemeProvider 注入 → 页面装配期应用令牌现值；失败降级保持烘焙值）。
    /// prefab 加载经内容服务租约（实例持租约到销毁）；Shutdown 释放面。
    ///
    /// **程序集边界**：本段住产品模块程序集（`LiteGame.Modules`），只经端口消费适配器——
    /// 三件 Lua 桥件（逻辑工厂/数据投射/文本门面）由装配根装订 <see cref="ILuaUIBinding"/> 注入。</summary>
    public sealed class UIComposition : IClientModule, IClientModuleGraph
    {
        private readonly Func<LuaComponent, ILuaUIBinding> _createLuaBinding;

        private RedDotRegistry _redDotRegistry;
        private ConfirmDialogRouter _confirmRouter;      // 确认缝端口（本段产出，本段内绑定真实现）
        private UIService _uiService;
        private DialogService _dialogs;
        private FeedbackService _feedback;
        private IDisposable _themeLease;
        private UIThemeAsset _themeAsset;

        /// <param name="createLuaBinding">Lua 桥装订面工厂（逻辑工厂/数据投射/文本门面——实现归适配器，
        /// 装配根产出；UI 段不直引 `LiteGame.Adapters.*`）。</param>
        public UIComposition(Func<LuaComponent, ILuaUIBinding> createLuaBinding)
            => _createLuaBinding = createLuaBinding ?? throw new ArgumentNullException(nameof(createLuaBinding));

        public string Name => "UI";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public IReadOnlyList<string> DependsOn => new[] { "LuaHost", "Config", "Content", "Clocks" };

        public async UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 同步资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败（主题已装载、UIService 构造抛出）
            // 由 Host 释放 scope 回收；UIService 的异步关闭仍在 ShutdownAsync（scope 只收 IDisposable）。
            context.Scope.Register(new DelegatedDisposable(DisposeSyncResources));
            var config = context.Require<IConfigService>();
            var lua = context.Require<LuaComponent>();
            var content = context.Require<IContentService>();   // 依赖②内容服务——注册序即依赖序
            var pageDirectory = UIPageDirectory.Instance;      // 页面目录端口（生成物适配，无状态共享实例）
            ILuaUIBinding luaBinding = _createLuaBinding(lua);
            context.Put<IPageDirectory>(pageDirectory);
            context.Put<IPageDataFactory>(luaBinding.PageData);   // 数据面端口：流程段只装配具名键

            _redDotRegistry = new RedDotRegistry();
            context.Put(_redDotRegistry);                    // 红点登记面（当前无生产规则/调用点——已登记技术债）
            _confirmRouter = new ConfirmDialogRouter();      // 确认缝：本段产口，装配根后段绑定实现
            context.Put(_confirmRouter);
            context.Put<IConfirmDialog>(_confirmRouter);

            var localeProvider = BuildLocaleProvider(config);
            var themeProvider = await TryLoadThemeProviderAsync(content, ct);   // P2-3 B2：失败降级 null（页面保持烘焙值）
            _uiService = new UIService(pageDirectory,
                logicResolver: page => luaBinding.LogicFactory.Create(page, _confirmRouter),
                loadPrefab: (location, token) => LoadPrefabLeaseAsync(content, location, token),
                // 转场策略**必须显式传**（《客户端总设计》§5.1 逻辑边界）：UIService 默认为零动效
                // InstantTransition（通用层不认识 DOTween）；产品转场是**装配决策**，
                // 在此注入真实现。不传 = 页面瞬时切换（功能正确、无动效）。
                transitionStrategy: new FadeSlideTransition(),
                localeProvider: localeProvider,              // 页面索引 BindLocale 注入缝（批C-②）
                themeProvider: themeProvider);               // 主题注入缝（UIStyleRef/IThemeBound——P2-3 B2）
            context.Put(_uiService);
            var nav = new UINavigationController(_uiService);        // 产品导航的单写者入口
            _uiService.Focus.PlatformBack = () => nav.BackAsync(); // 平台返回统一口径（§6.2）：ESC→协调者→导航器 Back 链
            context.Put(nav);

            // UI 能力段自持的配套件：UI 时钟驱动 + 弹窗/反馈/提示 + 确认缝真实现
            context.Services.RegisterInstance(new DotweenUIClockDriver(context.Require<IUIClock>()));
            _dialogs = new DialogService(_uiService, nav: nav);
            context.Services.RegisterInstance(_dialogs, ServiceOwnership.ContainerOwned);
            _feedback = new FeedbackService(_uiService, _dialogs, nav);
            context.Services.RegisterInstance(_feedback, ServiceOwnership.ContainerOwned);
            context.Services.RegisterInstance(new ToastTicker(() => _feedback.ToastHost));
            _confirmRouter.Bind((title, message, okText, cancelText)
                => _feedback.ConfirmAsync(title, message, okText, cancelText));
            luaBinding.BindTextFacade(localeProvider);          // Lua 门面后端（Bridge.text——与页面索引共用同一实例）
        }

        /// <summary>同步资源释放（由模块作用域在关闭/回滚时调用）：红点登记清空 + 主题资产租约归还。
        /// 调用时机在 <see cref="ShutdownAsync"/>（页面全关）之后——页面先销毁，主题引用后释放。</summary>
        private void DisposeSyncResources()
        {
            _redDotRegistry?.Clear();
            _redDotRegistry = null;
            _confirmRouter?.Unbind();                         // 确认缝解除绑定（不留指向已拆服务的委托）
            _confirmRouter = null;
            _dialogs = null;                                  // 反馈面随容器所有权释放（ServiceOwnership.ContainerOwned）
            _feedback = null;
            _themeLease?.Dispose();                           // 主题资产租约归还（引用计数递减）
            _themeLease = null;
            _themeAsset = null;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            UniTask shutdown = _uiService?.ShutdownAsync() ?? UniTask.CompletedTask;   // 全关+缓存销毁+租约归零+Root 销毁
            _uiService = null;
            return shutdown;
        }

        /// <summary>主题资产一次装载（P2-3 B2 / 总设计 §6.2「运行期只读解析」）：成功＝持租约到 Shutdown、
        /// provider 返回缓存实例（页面装配时零等待）；**失败降级 null**＝不注入，页面保持 prefab 烘焙值
        /// （样式非关键路径——Log.Warning 一次，不阻断 UI 起服）。租约在 <see cref="UIThemePaths.DefaultAssetPath"/>
        /// （生成物 SO，随 Config 收集组进包）。</summary>
        private async UniTask<Func<UIThemeAsset>> TryLoadThemeProviderAsync(IContentService content, CancellationToken ct)
        {
            try
            {
                var lease = await content.AcquireAsync<UIThemeAsset>(UIThemePaths.DefaultAssetPath, ct: ct);
                _themeLease = lease;
                _themeAsset = lease.Asset;
                UIThemeAsset cached = _themeAsset;
                return () => cached;
            }
            catch (Exception ex)
            {
                Log.Warning($"主题资产装载失败——UI 页面将保持 prefab 烘焙值（{UIThemePaths.DefaultAssetPath}）：{ex.Message}");
                return null;
            }
        }

        /// <summary>语言服务工厂（懒建 + 单实例——SetLocale 订阅者要求实例稳定）：
        /// tbltext 行经 <see cref="LTextCatalog"/> 投射为目录；缺 key 开发期显 `[key]`（发布期回退占位，
        /// <see cref="LocalizationService"/> 口径）。表未装载时访问 Tables 会抛——LText 目录按需构建同判据
        /// （首次消费在 Show，Show 在 Preload 后）。</summary>
        private static Func<ILocalizationService> BuildLocaleProvider(IConfigService config)
        {
            ILocalizationService service = null;
            return () =>
            {
                if (service == null)
                {
                    bool developmentMode =
                        DevBuildFacts.IsDevelopment;   // 缺 key 显示 [key] 的开发期口径（发布走占位文案）
                    var built = new LocalizationService(LTextCatalog.Build(config.Tables),
                        LocalizationCatalog.SourceLocale, developmentMode);
                    built.OnMissingKey = key => Log.Warning($"LText 缺 key:{key}", LogTag.LText);
                    service = built;
                }
                return service;
            };
        }

        /// <summary>prefab 租约通道：内容服务 Acquire → AssetLease 转 IUIPrefabLease——
        /// UIService 不感知内容服务类型，只认可释放句柄（§3 资源适配边界）。</summary>
        private static async UniTask<IUIPrefabLease> LoadPrefabLeaseAsync(
            IContentService content, string location, CancellationToken ct)
        {
            var lease = await content.AcquireAsync<UnityEngine.GameObject>(location, ct: ct);
            return new ContentPrefabLease(lease);
        }
    }
}
