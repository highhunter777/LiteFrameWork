using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Config;

namespace LiteGame.Modules.Map
{
    /// <summary>⑫ 地图：地图提供者（已装载表链 → 判定图，两端同源投影）。只提供能力，不含业务。</summary>
    public sealed class MapComposition : IClientModule, IClientModuleGraph
    {
        public string Name => "Map";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "Config" };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            context.Put<IMapProvider>(new MapProvider(context.Require<IConfigService>()));
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
