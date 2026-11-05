using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Composition;
using LiteGame.Modules.Config;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;
using LiteGame.Modules.UI;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 产品组合根入口（B1.5）：承接原 <c>GameEntry</c> 的**模块装配序**与**启动续体**。
    /// 本类住 <c>LiteGame.App</c>（实现件），由 AOT 壳 <c>ClientEntryLoader</c> 按程序集限定名反射装载；
    /// AOT 壳不认识本类，也不认识任何 <c>LiteGame.Modules.*</c>。
    ///
    /// 调用序（与拆分前逐项一致）：
    /// ① <see cref="Configure"/>：建组合根/部署烙入 → 注册 18 段 → ComposeRoot（宿主初始化前一次）；
    /// ② 宿主初始化由 AOT 壳执行；
    /// ③ <see cref="StartAsync"/>（Unity Start 后）：取容器/流程机 → 开发调参注入 → 探针武装 → 流程机 Launch。
    /// </summary>
    public sealed class ClientEntry : IClientEntry
    {
        private ClientHost _host;
        private GameObject _hostObject;
        private AppComposition _appComposition;
        private DeployBake _deployBake;
        private ServiceContainer _container;
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;

        /// <inheritdoc />
        public void Configure(ClientHost host, GameObject hostObject)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _hostObject = hostObject ?? throw new ArgumentNullException(nameof(hostObject));

            // 组合根：适配器工厂 + 跨域装订（ComposeAfter）
            _appComposition = new AppComposition();
            _deployBake = new DeployBake();               // 包内烙入（内容/联机两段共用同一实例，只读一次）
            RegisterSegments(host);
            // 全部模块成功后、装配面密封前做跨域装订（桥/模态/身份/输入拦截/开发装饰/帧驱动序/密封）
            host.ComposeRoot(_appComposition.ComposeAfter);
        }

        /// <inheritdoc />
        public UniTask StartAsync(CancellationToken ct)
        {
            _container = _host.Product<ServiceContainer>();
            _fsm = _host.Product<StageMachine<ProcedureId, ProcedureArgs>>();
            if (_fsm == null)
                throw new InvalidOperationException("host bootstrap incomplete: procedure machine missing");

            // 开发运行期调节件注入（Dev 装配钩子；未绑定/未挂载 = no-op）
            DevHooks.Current?.InjectRuntimeTuners(_hostObject,
                _host.Product<IWorldClock>(), _host.Product<IUIClock>(),
                _host.Product<EventBus>(), _host.Product<CommandCenter>());
            // 内存循环实测探针：**不进 dev 条件块**——本构建为非 dev Player（dev 块在此被剔除），
            // 靠 -memloop 命令行门控（未带参数 = 一次数组查找后无操作，同 -content.cdnUrl 风格）
            MemoryLoopProbe.TryArm(_container);
            // 模态输入恢复探针：同款命令行门控（-inputmodal）——正常启动零影响
            InputModalProbe.TryArm(_host);

            _fsm.Start(ProcedureId.Launch);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 模块装配序列（《客户端总设计》§6.1"按依赖顺序初始化，失败时只关闭已经成功初始化的模块"）。
        ///
        /// **注册顺序 = 初始化顺序 = 关闭逆序**：模块间依赖只能引用更早模块的产物（经
        /// <see cref="SegmentContext"/> 的 Put/Require 传递，与根容器共享实例来源）。
        /// 模块序表达异步初始化/关闭依赖；服务声明图由容器预检，帧驱动序由装配根显式声明。
        /// </summary>
        private void RegisterSegments(ClientHost host)
        {
            host.AddSegment(new PlatformInfrastructureSegment())   // ① 本地持久化 + 日志（先于一切 IO）
                .AddSegment(new CameraSegment(_appComposition.CameraServiceFactory()))    // ② 主相机 + 相机服务（薄段）
                .AddSegment(new IdentitySegment())                 // ③ 身份上下文（Build/Schema 唯一构造点）
                .AddSegment(new DevSessionSegment())               // ④ 开发会话端口（设备源构造即消费——必须早于 ⑭）
                .AddSegment(new ContentSegment(_deployBake,        // ⑤ 内容/场景/缓存租约（配置/Lua 字节通道来源）
                    _appComposition.ContentServiceFactory(),
                    _appComposition.SceneServiceFactory(),
                    _appComposition.LuaHealthProbeFactory(),
                    _appComposition.ConfigHealthProbeFactory(),
                    _appComposition.BridgeHealthProbeFactory()))
                .AddSegment(new SettingsSegment())                 // ⑥ 玩家偏好 + 视角偏好单实例（先于 UI/音频）
                .AddSegment(new EventsSegment())                   // ⑦ 事件总线（唯一产出方；只需 ① 的池）
                .AddSegment(new ClocksSegment())                   // ⑧ 双轨时钟（事件域已独立，时钟只负责时间）
                .AddSegment(new SchedulersSegment())               // ⑨ 时序执行 + 时间轴（依赖 ⑧）
                .AddSegment(new LuaHostSegment(_hostObject,         // ⑩ 脚本宿主（同 GameObject 组件；具体适配器由组合根创建）
                    go => go.AddComponent<LiteFramework.LuaComponent>()))
                .AddSegment(new ConfigSegment(_appComposition.ConfigServiceFactory()))          // ⑪ 表投影数据源
                .AddSegment(new MapSegment())                 // ⑫ 地图提供者（依赖 ⑪ 的表链）
                .AddSegment(new UISegment(_appComposition.LuaUIBindingFactory()))         // ⑬ UI 能力：服务/导航/反馈面（Lua 桥经端口装订）
                .AddSegment(new InputSegment(_appComposition.IntentSourceFactory()))            // ⑭ 输入服务（设备源由组合根注入）
                .AddSegment(new MetaSegment(_deployBake))     // ⑮ 联机入口（部署快照 + 客户端 + 入口编排件）
                .AddSegment(new BattleSegment(() => DevHooks.Diagnostics,
                    () => DevHooks.Current?.CreateBattleOverlay(),
                    new LiteFramework.Animation.AnimatorAnimationBackendFactory()))
                                                                    // ⑯ 战斗域（表现内核 + 会话/对局工厂；诊断与动画后端由 App 注入）
                .AddSegment(new SessionSegment())             // ⑰ 会话（账号会话 + 传输端口；对局运行归 ⑯）
                .AddSegment(new ProcedureSegment(_appComposition.LuaBridgesFactory(),        // ⑱ 应用壳（流程机 + 页面流 + 恢复）
                    _appComposition.LuaRegistryFillerFactory()));
        }
    }
}
