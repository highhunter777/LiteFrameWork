using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.View;

namespace LiteGame
{
    /// <summary>⑫ 战斗域装配段：战斗自持的表现内核（租约缓存/实体/声音/世界 VFX）。
    /// 加载口统一绑 <see cref="PrefabLeaseCache"/>（租约持有，避免悬空引用）；Shutdown 释放面
    /// （音频全局停止 + 租约归零）。
    ///
    /// **相机不在这里**：主相机与相机服务由 ② <see cref="CameraComposition"/> 产出
    /// （`CinemachineCameraService` 属 `Adapters.Unity`，该程序集反向引用本域，本段不认实现）。
    /// 表现内核只被战斗与设置装配消费，归战斗域自持——它不再住共享的"表现层"。
    /// </summary>
    public sealed class BattleComposition : IClientModule, IClientModuleGraph
    {
        private PrefabLeaseCache _prefabs;
        private AudioService _audio;
        private EntityService _entities;
        private VfxService _vfxService;

        public string Name => "Battle";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[] { "Content", "Clocks" };

        public UniTask InitializeAsync(ModuleContext context, CancellationToken ct)
        {
            // 资源归属＝本模块作用域（路线图 §4.3）：初始化中途失败（缺服务/注册冲突）时
            // 已建的租约缓存/实体/音频/VFX 也随 scope 回收。
            context.Scope.Register(new DelegatedDisposable(DisposeModuleResources));
            var content = context.Require<IContentService>();   // 依赖③——注册序即依赖序
            _prefabs = new PrefabLeaseCache(content);
            _audio = new AudioService();
            _audio.BindClocks(context.Require<IWorldClock>(), context.Require<IUIClock>());   // 淡变分域（§7 时钟表）
            _entities = new EntityService(_prefabs.GetAsync) { HostScope = context.RootScope };   // 实体作用域挂根（宿主退出级联）
            _vfxService = new VfxService(
                loader: _prefabs.GetAsync,
                clock: context.Require<IWorldClock>(),
                catalog: new VfxCatalog(),
                budget: VfxBudget.Default());
            context.Put(_prefabs);                               // 租约缓存驻留面（HeldLocations 诊断/内存实测读）
            context.Put(_entities);
            context.Put(_audio);
            context.Put(_vfxService);
            return UniTask.CompletedTask;
        }

        /// <summary>模块资源释放（由模块作用域在关闭/回滚时调用）：
        /// 实体 → 音频 → VFX → 租约（先收使用者再放租约）。</summary>
        private void DisposeModuleResources()
        {
            _entities?.Shutdown();                       // 取消在途加载 + 连锁回收活体 + 排空实例池
            _entities = null;
            _audio?.Shutdown();                          // 总线全局停止 + [Audio] 根销毁
            _audio = null;
            _vfxService?.Shutdown();                     // 取消在途加载 + 回收活体 + 排空池 + 清缓存引用
            _vfxService = null;
            _prefabs?.ReleaseAll();                      // 战斗域 prefab 租约归零（最后：先收使用者再放租约）
            _prefabs = null;
        }

        /// <summary>关闭面：战斗表现内核资源已随 <see cref="ModuleContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
