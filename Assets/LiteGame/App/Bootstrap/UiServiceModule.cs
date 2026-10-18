using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient.UI;
using LiteClient;

namespace LiteGame
{
    /// <summary>⑧ UI 服务装配：三注册表 + 红点 + UIService（逻辑解析器接 LuaBehaviourAdapter ← UI 注册表，解析失败降级 NullLogic）
    /// + LText 语言服务（tbltext→目录→服务，批C-② 生产装配——页面 SetTextKey 由"必抛"转正）。
    /// prefab 加载经内容服务租约（实例持租约到销毁）；Shutdown 释放面。</summary>
    internal sealed class UiServiceModule : IClientModule
    {
        private RedDotRegistry _redDotRegistry;
        private UIService _uiService;

        public string Name => "UiService";

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

            var localeProvider = BuildLocaleProvider(config);
            _uiService = new UIService(new UIFormCatalog(config),
                logicResolver: info => new LuaBehaviourAdapter(lua.Env, uiRegistry.Get(info.LuaPath), ConfirmOf(context)),
                loadPrefab: (location, token) => LoadPrefabLeaseAsync(content, location, token),
                // 转场策略**必须显式传**（《客户端总设计》§5.1 逻辑边界）：UIService 默认为零动效
                // InstantTransition（通用层不认识 DOTween）；产品转场是**装配决策**，
                // 在此注入真实现。不传 = 页面瞬时切换（功能正确、无动效）。
                transitionStrategy: new FadeSlideTransition(),
                localeProvider: localeProvider);              // 页面索引 BindLocale 注入缝（批C-②）
            context.Put(_uiService);
            var nav = new UINavigationController(_uiService);        // 产品导航的单写者入口
            _uiService.Focus.PlatformBack = () => nav.BackAsync(); // 平台返回统一口径（§6.2）：ESC→协调者→导航器 Back 链
            context.Put(nav);
            Bridge.BindText(localeProvider);                   // Lua 门面后端（Bridge.text——与页面索引共用同一实例）
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _redDotRegistry?.Clear();
            return _uiService?.ShutdownAsync() ?? UniTask.CompletedTask;   // 全关+缓存销毁+租约归零+Root 销毁
        }

        /// <summary>页面确认缝（<c>self.ui:ShowConfirm</c> 的服务端）：懒解析 FeedbackService——本模块装配
        /// 早于 Container 段（该服务在后段建），resolver 执行（首次开页）时已在容器；装配缺口在首次调用
        /// 即显性失败。确认链＝FeedbackService.ConfirmAsync → DialogService（首个生产消费者）。</summary>
        private static Func<string, string, string, string, UniTask<bool>> ConfirmOf(ClientContext context)
            => (title, message, okText, cancelText)
                => context.Require<FeedbackService>().ConfirmAsync(title, message, okText, cancelText);

        /// <summary>语言服务工厂（懒建 + 单实例——SetLocale 订阅者要求实例稳定）：
        /// tbltext 行经 <see cref="LTextCatalog"/> 投射为目录；缺 key 开发期显 <c>[key]</c>（发布期回退占位，
        /// <see cref="LocalizationService"/> 口径）。表未装载时访问 Tables 会抛——与 UIFormCatalog 懒投影同判据
        /// （首次消费在 Show，Show 在 Preload 后）。</summary>
        private static Func<ILocalizationService> BuildLocaleProvider(ConfigService config)
        {
            ILocalizationService service = null;
            return () =>
            {
                if (service == null)
                {
                    bool developmentMode =
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                        true;
#else
                        false;
#endif
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
