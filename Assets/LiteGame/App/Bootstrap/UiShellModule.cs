using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.UI;

namespace LiteGame
{
    /// <summary>⑧ UI 壳：三注册表 + 红点 + UIService（逻辑解析器接 LuaBehaviourAdapter ← UI 注册表，解析失败降级 NullLogic）。
    /// prefab 加载经内容服务租约（实例持租约到销毁）；Shutdown 释放面。</summary>
    internal sealed class UiShellModule : IClientModule
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
                loadPrefab: (location, token) => LoadPrefabLeaseAsync(content, location, token),
                // 转场策略**必须显式传**（《客户端总设计》§5.1 逻辑边界）：UIService 默认为零动效
                // InstantTransition（通用层不认识 DOTween）；产品转场是**装配决策**，
                // 在此注入真实现。不传 = 页面瞬时切换（功能正确、无动效）。
                transitionStrategy: new FadeSlideTransition());
            context.Put(_uiService);
            context.Put(new UINavigationController(_uiService));   // 产品导航的单写者入口
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _redDotRegistry?.Clear();
            return _uiService?.ShutdownAsync() ?? UniTask.CompletedTask;   // 全关+缓存销毁+租约归零+Root 销毁
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
