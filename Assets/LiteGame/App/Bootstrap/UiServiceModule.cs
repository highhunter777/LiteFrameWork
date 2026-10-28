using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;
using LiteClient;

namespace LiteGame
{
    /// <summary>⑧ UI 服务装配：三注册表 + 红点 + UIService（逻辑解析器接 LuaBehaviourAdapter ← UI 注册表，解析失败降级 NullLogic）
    /// + LText 语言服务（tbltext→目录→服务，批C-② 生产装配——页面 SetTextKey 由"必抛"转正）
    /// + 主题资产（P2-3 B2：内容服务一次装载 → ThemeProvider 注入 → 页面装配期应用令牌现值；失败降级保持烘焙值）。
    /// prefab 加载经内容服务租约（实例持租约到销毁）；Shutdown 释放面。</summary>
    internal sealed class UiServiceModule : IClientModule, IClientModuleGraph
    {
        private RedDotRegistry _redDotRegistry;
        private UIService _uiService;
        private IDisposable _themeLease;
        private UiThemeAsset _themeAsset;

        public string Name => "UiService";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<System.Type> DependsOn => new System.Type[] { typeof(LuaHostModule), typeof(ConfigModule), typeof(ContentModule) };
        public async UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 同步资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败（主题已装载、UIService 构造抛出）
            // 由 Host 释放 scope 回收；UIService 的异步关闭仍在 ShutdownAsync（scope 只收 IDisposable）。
            context.Scope.Register(new DelegatedDisposable(DisposeSyncResources));
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

            var localeProvider = BuildLocaleProvider(config);
            var themeProvider = await TryLoadThemeProviderAsync(content, ct);   // P2-3 B2：失败降级 null（页面保持烘焙值）
            _uiService = new UIService(UiPageCatalog.Instance,
                logicResolver: info => new LuaBehaviourAdapter(lua.Env, uiRegistry.Get(info.LuaPath), ConfirmOf(context)),
                loadPrefab: (location, token) => LoadPrefabLeaseAsync(content, location, token),
                // 转场策略**必须显式传**（《客户端总设计》§5.1 逻辑边界）：UIService 默认为零动效
                // InstantTransition（通用层不认识 DOTween）；产品转场是**装配决策**，
                // 在此注入真实现。不传 = 页面瞬时切换（功能正确、无动效）。
                transitionStrategy: new FadeSlideTransition(),
                localeProvider: localeProvider,              // 页面索引 BindLocale 注入缝（批C-②）
                themeProvider: themeProvider);               // 主题注入缝（UiStyleRef/IThemeBound——P2-3 B2）
            context.Put(_uiService);
            var nav = new UINavigationController(_uiService);        // 产品导航的单写者入口
            _uiService.Focus.PlatformBack = () => nav.BackAsync(); // 平台返回统一口径（§6.2）：ESC→协调者→导航器 Back 链
            context.Put(nav);
            Bridge.BindText(localeProvider);                   // Lua 门面后端（Bridge.text——与页面索引共用同一实例）
        }

        /// <summary>同步资源释放（由模块作用域在关闭/回滚时调用）：红点登记清空 + 主题资产租约归还。
        /// 调用时机在 <see cref="ShutdownAsync"/>（页面全关）之后——页面先销毁，主题引用后释放。</summary>
        private void DisposeSyncResources()
        {
            _redDotRegistry?.Clear();
            _redDotRegistry = null;
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
        /// （样式非关键路径——Log.Warning 一次，不阻断 UI 起服）。租约在 <see cref="UiThemePaths.DefaultAssetPath"/>
        /// （生成物 SO，随 Config 收集组进包）。</summary>
        private async UniTask<Func<UiThemeAsset>> TryLoadThemeProviderAsync(IContentService content, CancellationToken ct)
        {
            try
            {
                var lease = await content.AcquireAsync<UiThemeAsset>(UiThemePaths.DefaultAssetPath, ct: ct);
                _themeLease = lease;
                _themeAsset = lease.Asset;
                UiThemeAsset cached = _themeAsset;
                return () => cached;
            }
            catch (Exception ex)
            {
                Log.Warning($"主题资产装载失败——UI 页面将保持 prefab 烘焙值（{UiThemePaths.DefaultAssetPath}）：{ex.Message}");
                return null;
            }
        }

        /// <summary>页面确认缝（<c>self.ui:ShowConfirm</c> 的服务端）：懒解析 FeedbackService——本模块装配
        /// 早于 Container 段（该服务在后段建），resolver 执行（首次开页）时已在容器；装配缺口在首次调用
        /// 即显性失败。确认链＝FeedbackService.ConfirmAsync → DialogService（首个生产消费者）。</summary>
        private static Func<string, string, string, string, UniTask<bool>> ConfirmOf(ModuleContext context)
            => (title, message, okText, cancelText)
                => context.Require<FeedbackService>().ConfirmAsync(title, message, okText, cancelText);

        /// <summary>语言服务工厂（懒建 + 单实例——SetLocale 订阅者要求实例稳定）：
        /// tbltext 行经 <see cref="LTextCatalog"/> 投射为目录；缺 key 开发期显 <c>[key]</c>（发布期回退占位，
        /// <see cref="LocalizationService"/> 口径）。表未装载时访问 Tables 会抛——LText 目录按需构建同判据
        /// （首次消费在 Show，Show 在 Preload 后）。</summary>
        private static Func<ILocalizationService> BuildLocaleProvider(ConfigService config)
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
