using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.Animation;
using LiteFramework.UI;
using LiteGame.Modules.Battle.Arena;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Config;
using LiteGame.Modules.Input;
using LiteGame.Modules.Settings;

namespace LiteGame
{
    /// <summary>⑫ 战斗域装配段：战斗自持的表现内核（租约缓存/实体/声音/世界 VFX）。
    /// 加载口统一绑 <see cref="PrefabLeaseCache"/>（租约持有，避免悬空引用）；Shutdown 释放面
    /// （音频全局停止 + 租约归零）。
    ///
    /// **相机不在这里**：主相机与相机服务由 ② <see cref="CameraSegment"/> 产出
    /// （`CinemachineCameraService` 属 `Adapters.Unity`，该程序集反向引用本域，本段不认实现）。
    /// 表现内核只被战斗与设置装配消费，归战斗域自持——它不再住共享的"表现层"。
    /// </summary>
    public sealed class BattleSegment : IClientSegment, IClientSegmentGraph
    {
        private readonly Func<IBattleDevDiagnostics> _devDiagnostics;
        private readonly Func<IBattleDevOverlay> _devOverlay;
        private readonly IAnimationBackendFactory _animationBackendFactory;   // 动画后端工厂（App 组合根注入）
        private PrefabLeaseCache _prefabs;
        private AudioService _audio;
        private EntityService _entities;
        private VfxService _vfxService;

        /// <param name="devDiagnostics">开发诊断实现提供者（App 注入开发诊断读口；
        /// null = 发布形态不登记诊断）。</param>
        public BattleSegment(Func<IBattleDevDiagnostics> devDiagnostics = null,
            Func<IBattleDevOverlay> devOverlay = null, IAnimationBackendFactory animationBackendFactory = null)
        {
            _devDiagnostics = devDiagnostics;
            _devOverlay = devOverlay;
            _animationBackendFactory = animationBackendFactory;
        }

        public string Name => "Battle";

        /// <summary>依赖的模块（与 GameEntry 注册序一致；供 Host DAG 校验）。</summary>
        public System.Collections.Generic.IReadOnlyList<string> DependsOn => new[]
        {
            "PlatformInfrastructure", "Identity", "Camera", "DevSession", "Content", "Settings", "Clocks", "Config", "UI", "Input",
        };

        public UniTask InitializeAsync(SegmentContext context, CancellationToken ct)
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
            context.Put<IVfxService>(_vfxService);               // 接口键（HUD/消费方只经端口取；与具体键同实例）
            context.Put<IBattleCommands>(new BattleCommands(context.Require<CommandCenter>()));   // 流程→战斗的命令端口
            // 根位移摘要登记口：装配期单实例；对局装载 rootmotion.bin 成功时写入摘要（身份面经端口读）
            var rootMotionDigest = new RootMotionDigestHolder();
            context.Put(rootMotionDigest);
            context.Put<IRootMotionContentDigest>(rootMotionDigest);
            // 客户端工厂端口：Battle 域是唯一 `new BattleClient` 处——会话域（⑰）经本口建客户端，
            // 不再认识具体实现（A1 解耦；装配序 ⑯ → ⑰）。
            context.Put<IBattleClientFactory>(new BattleClientFactory());
            // 对局运行工厂（A2）：Match/运行会话的构造与所有权归 Battle 域；流程只认端口。
            IdentityContext identity = context.Require<IdentityContext>();
            var matchSessions = new MatchSessionFactory(
                context.Require<IInputService>(),
                () => context.Require<IConfigService>().GameplayDigestBase,
                context.Require<IReferencePool>(),
                identity.Diagnostics);
            context.Put<IMatchSessionFactory>(matchSessions);
            context.Put<IBattleRunFactory>(new BattleRunFactory(
                matchSessions, context.Require<IDevSession>(), _devDiagnostics?.Invoke()));
            // 结算摘要端口：冻结结果 → 结算页排行行表（全序/名次与局内排行榜同一实现）——流程只认端口
            context.Put<ISettlementSummary>(new SettlementSummary());
            // 视图组工厂：战斗域自持的装配点（流程只认端口；每局再注入 account 作用域/开发叠加层/传送分发）
            context.Put<IBattleViewGroupFactory>(new BattleViewGroupFactory(
                content, context.Require<IInputService>(), context.Require<ICameraService>(),
                _vfxService, context.Require<IWorldClock>(), _audio, context.Require<CommandCenter>(),
                new CrosshairFeelProfile(context.Require<IConfigService>()),
                context.Require<UIService>().ThemeAssetProvider,      // HUD 状态色主题注入
                context.Require<IDevSession>(),                        // dev 投影读本局规则（实例端口）
                context.Require<ILookPreferences>(),                   // 视角手感偏好（设置段单实例）
                rootMotionDigest,                                      // 根位移摘要登记口（对局装载写入）
                _devOverlay,                                           // 开发叠加层来源（发布 null）
                _animationBackendFactory));                            // 动画后端工厂（适配器由组合根注入）
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

        /// <summary>关闭面：战斗表现内核资源已随 <see cref="SegmentContext.Scope"/> 释放。</summary>
        public UniTask ShutdownAsync(CancellationToken ct) => UniTask.CompletedTask;
    }
}
