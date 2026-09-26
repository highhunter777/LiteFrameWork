using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;

namespace LiteGame
{
    /// <summary>⑨ 表现壳：实体/声音/世界 VFX。G1 通用表现批：加载口统一绑 <see cref="PrefabLeaseCache"/>
    /// （租约持有——纠正静态门面"返回前 Release"的悬空引用）；Shutdown 释放面（音频全局停止 + 租约归零）。</summary>
    internal sealed class PresentationModule : IClientModule
    {
        private PrefabLeaseCache _prefabs;
        private AudioService _audio;
        private EntityService _entities;
        private VfxService _vfxService;

        public string Name => "Presentation";

        public UniTask InitializeAsync(ClientContext context, CancellationToken ct)
        {
            var content = context.Require<IContentService>();   // 依赖②——注册序即依赖序
            _prefabs = new PrefabLeaseCache(content);
            _audio = new AudioService();
            _audio.BindClocks(context.Require<IWorldClock>(), context.Require<IUIClock>());   // 淡变分域（§7 时钟表）
            _entities = new EntityService(_prefabs.GetAsync) { HostScope = context.RootScope };   // 实体作用域挂根（宿主退出级联）
            _vfxService = new VfxService(
                loader: _prefabs.GetAsync,
                clock: context.Require<IWorldClock>(),
                catalog: new VfxCatalog(),
                budget: VfxBudget.Default());
            context.Put(_entities);
            context.Put(_audio);
            context.Put(_vfxService);
            return UniTask.CompletedTask;
        }

        public UniTask ShutdownAsync(CancellationToken ct)
        {
            _entities?.Shutdown();                       // 取消在途加载 + 连锁回收活体 + 排空实例池
            _audio?.Shutdown();                          // 总线全局停止 + [Audio] 根销毁
            _vfxService?.Shutdown();                     // 取消在途加载 + 回收活体 + 排空池 + 清缓存引用
            _prefabs?.ReleaseAll();                      // 表现壳 prefab 租约归零（最后：先收使用者再放租约）
            return UniTask.CompletedTask;
        }
    }
}
