using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteGame.View;
using UnityEngine;
using LiteFramework.UI;
using LiteGame.Modules.Camera;
using LiteGame.Modules.Config;
using LiteGame.Modules.Dev;
using LiteGame.Modules.Framework;
using LiteGame.Modules.Input;
using LiteGame.Modules.Map;
using LiteGame.Modules.Settings;

namespace LiteGame
{
    /// <summary>
    /// Unity 引导适配器（真正的启动与关闭由 ClientHost 持有）：
    /// - Awake：建 ClientHost + AppLifetime 桥，按依赖序注册 IClientModule（装配步骤见
    ///   <see cref="RegisterModules"/>，各模块一件，住 `App/Bootstrap/`），
    ///   启动初始化（失败 → ProcedureError.ShowBootstrapError 进入确定错误态）。
    /// - Start：流程机启动（Launch）——依赖宿主初始化同步完成（首批模块全同步）。
    /// - Update：驱动容器 Tickables（变速由 IGameClock 内部缩放，勿用 Time.deltaTime 二次乘）。
    /// - 退出/宿主销毁：AppLifetime 桥接优雅关闭（刷新钩子 + 逆序模块关闭 + 根 Scope 释放）。
    ///
    /// 静态状态仅 `s_booted` 幂等标记（重复引导守卫；不持真实生命周期）。
    /// 容器/流程机为实例字段，随引导件销毁；AppLifetime.OnDestroy 执行完整关闭（无遗留）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // 引导件必须最先 Awake：FileSystem/Log/容器由本件经 ClientHost 建立，其余组件依赖这套基础设施
    public sealed class GameEntry : MonoBehaviour
    {
        private static bool s_booted;   // 重复引导守卫（场景误含引导件时静默销毁新件；不持生命周期）

        /// <summary>编辑器静态清理（§6.1"清理静态兼容状态，支持关闭 Domain Reload 的编辑器场景"）：
        /// Domain-Reload-Off 时静态跨 Play 残留——s_booted 不复位会把第二次 Play 的引导件当重复件静默销毁
        /// （引导死锁）。AppLifetime 已桥接 ClientHost.ResetForEditorReload；本类与 UI 动效时钟的静态在此复位。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForEditorReload()
        {
            s_booted = false;
            LiteFramework.UI.UiAnimationClock.ResetForEditorReload();
        }

        private ClientHost _host;
        private AppComposition _appComposition;   // 组合根：适配器工厂 + 跨域装订（ComposeAfter）
        private DeployBake _deployBake;           // 包内烙入（内容/联机两段共用同一实例，只读一次）
        private ServiceContainer _container;   // 实例全程持有，随引导件销毁
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;
        private CancellationTokenSource _bootCts;
        private bool _active;
        private bool _startRequested;      // Start 已到（流程可以启动）；实际启动等依赖就绪
        private bool _proceduresStarted;   // 流程机已启动（幂等标记）

        /// <summary>引导与开发/测试工具取得已装配的根容器；**业务流程只消费注入的能力**。
        ///
        /// 消费面封闭（清单登记在 Docs/审计/客户端/登记/技术债.md「TakeContainer 消费清单」）：
        /// 编辑器工具（GM 取件 / DevReload / LuaDevRefill）与 PlayMode 冒烟用例；
        /// 产品运行期代码与流程阶段不得调用（否则等于运行期服务定位器，层级边界静默失效）。</summary>
        public ServiceContainer TakeContainer()
        {
            if (_container == null) throw new InvalidOperationException("未装配");
            return _container;
        }

        /// <summary>只读统计列表（DevHUD 跨程序集拉取用）：**不是解析入口**，不含容器语义。</summary>
        public IReadOnlyList<IModuleStats> Stats => _container != null
            ? _container.Stats
            : Array.Empty<IModuleStats>();

        private void Awake()
        {
            // 重复引导守卫（叠加加载场景时的真实隐患）：场景实例若误含 GameEntry，DontDestroyOnLoad + Awake
            // 会二次引导并覆盖容器/静态设施——静默销毁重复件，保留首个引导。
            if (s_booted)
            {
                Log.Warning($"检测到重复 GameEntry（场景 {gameObject.scene.name} 误含引导件）——已销毁，保留首个引导", LogTag.GameEntry);
                Destroy(gameObject);
                return;
            }
            s_booted = true;
            _active = true;

            // ClientHost + 平台桥（AppLifetime 同 GameObject：接管 Pause/Focus/LowMemory/Quit 与退出善后）
            _host = new ClientHost();
            gameObject.AddComponent<AppLifetime>().Bind(_host);

            // 装配（§6.1：注册顺序 = 初始化顺序 = 关闭逆序——依赖图由调用序表达）
            _appComposition = new AppComposition();
            _deployBake = new DeployBake();
            RegisterModules(_host);
            // 组合根：全部模块成功后、装配面密封前做跨域装订（桥/模态/身份/输入拦截/开发装饰/帧驱动序/密封）
            _host.ComposeRoot(_appComposition.ComposeAfter);

            _bootCts = new CancellationTokenSource();

            // 跨场景存活（引导件被场景卸载销毁 = 泵停转 = 全局静默冻结）
            DontDestroyOnLoad(gameObject);

            RunBootstrapAsync(_bootCts.Token).ForgetLogged("GameEntry");
        }

        /// <summary>宿主初始化（首批全同步完成 → 产物在 Awake 内即就绪）；失败进入确定错误态（§C1）。</summary>
        private async UniTask RunBootstrapAsync(CancellationToken ct)
        {
            try
            {
                await _host.InitializeAsync(ct);
                _container = _host.Product<ServiceContainer>();
                _fsm = _host.Product<StageMachine<ProcedureId, ProcedureArgs>>();
                if (_fsm == null)
                {
                    Log.Error("宿主初始化完成但流程机缺失（进入 Error 态）", LogTag.GameEntry);
                    ProcedureError.ShowBootstrapError("host bootstrap incomplete: procedure machine missing");
                    return;
                }
                // 开发运行期调节件注入（Dev 装配钩子；未绑定/未挂载 = no-op）
                DevHooks.Current?.InjectRuntimeTuners(gameObject,
                    _host.Product<IWorldClock>(), _host.Product<IUIClock>(),
                    _host.Product<EventBus>(), _host.Product<CommandCenter>());
                // 内存循环实测探针：**不进 dev 条件块**——本构建为非 dev Player（dev 块在此被剔除），
                // 靠 -memloop 命令行门控（未带参数 = 一次数组查找后无操作，同 -content.cdnUrl 风格）
                MemoryLoopProbe.TryArm(_container);
                // 模态输入恢复探针：同款命令行门控（-inputmodal）——正常启动零影响
                InputModalProbe.TryArm(_host);
                // 模块初始化若异步完成，流程机在这里启动；同步完成时 Start 已启动（幂等）。
                TryStartProcedures();
            }
            catch (OperationCanceledException)
            {
                // 引导取消：进入确定空闲态（Host 已回滚），不起流程
                Log.Warning("GameEntry 引导被取消（Host 已回滚到确定状态）", LogTag.GameEntry);
            }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 引导失败：{ex.Message}", LogTag.GameEntry);
                ProcedureError.ShowBootstrapError(ex);   // 分类展示（未标记 = Fatal）
            }
        }

        /// <summary>流程启动：业务服务已由装配根（ContainerModule）注册并 Seal；
        /// 流程依赖经构造注入消费——依赖不从 payload 取（局部服务定位器同罪）。</summary>
        private void Start()
        {
            if (!_active) return;                            // 重复引导件：Awake 已销毁，不参与
            // 模块初始化可能异步完成：这里只登记"流程可以启动"，不假设 Awake 内已装配完。
            _startRequested = true;
            TryStartProcedures();
        }

        /// <summary>依赖就绪后启动流程机（幂等）：同步完成由 Start 调用，异步完成由引导续体调用。</summary>
        private void TryStartProcedures()
        {
            if (!_startRequested || !_active || _proceduresStarted || _fsm == null) return;
            _proceduresStarted = true;
            _fsm.Start(ProcedureId.Launch);
        }

        /// <summary>
        /// 模块装配序列（《客户端总设计》§6.1"按依赖顺序初始化，失败时只关闭已经成功初始化的模块"）。
        ///
        /// **注册顺序 = 初始化顺序 = 关闭逆序**：模块间依赖只能引用更早模块的产物（经
        /// <see cref="ModuleContext"/> 的 Put/Require 传递，与根容器共享实例来源）。
        /// 模块序表达异步初始化/关闭依赖；服务声明图由容器预检，帧驱动序由装配根显式声明。
        /// </summary>
        private void RegisterModules(ClientHost host)
        {
            host.AddModule(new PlatformInfrastructureModule())   // ① 本地持久化 + 日志（先于一切 IO）
                .AddModule(new CameraComposition(_appComposition.CameraServiceFactory()))    // ② 主相机 + 相机服务（薄段）
                .AddModule(new IdentityModule())                 // ③ 身份上下文（Build/Schema 唯一构造点）
                .AddModule(new DevSessionModule())               // ④ 开发会话端口（设备源构造即消费——必须早于 ⑭）
                .AddModule(new ContentModule(_deployBake))       // ⑤ 内容/场景/缓存租约（配置/Lua 字节通道来源）
                .AddModule(new SettingsModule())                 // ⑥ 玩家偏好 + 视角偏好单实例（先于 UI/音频）
                .AddModule(new EventsModule())                   // ⑦ 事件总线（唯一产出方；只需 ① 的池）
                .AddModule(new ClocksModule())                   // ⑧ 双轨时钟（事件域已独立，时钟只负责时间）
                .AddModule(new SchedulersModule())               // ⑨ 时序执行 + 时间轴（依赖 ⑧）
                .AddModule(new LuaHostModule(gameObject))        // ⑩ 脚本宿主（同 GameObject 组件）
                .AddModule(new ConfigModule(_appComposition.ConfigServiceFactory()))          // ⑪ 表投影数据源
                .AddModule(new MapComposition())                 // ⑫ 地图提供者（依赖 ⑪ 的表链）
                .AddModule(new UiServiceModule())                // ⑬ UI 能力：服务/导航/反馈面（投影面由 ② 保证）
                .AddModule(new InputModule(_appComposition.IntentSourceFactory()))            // ⑭ 输入服务（设备源由组合根注入）
                .AddModule(new MetaComposition(_deployBake))     // ⑮ 联机入口（部署快照 + 客户端 + 入口编排件）
                .AddModule(new BattleComposition())              // ⑯ 战斗域（表现内核 + 视图组工厂；相机归 ②）
                .AddModule(new SessionComposition())             // ⑰ 会话（账号/对局工厂 + 传输端口）
                .AddModule(new ProcedureComposition(_appComposition.LuaBridgesFactory()));    // ⑱ 应用壳（流程机 + 页面流 + 恢复）
        }

        private void Update()
        {
            if (!_active || _container == null) return;      // 重复引导件销毁前 / 引导未完成不驱动
            // 统一喂真实帧间隔：变速由 IGameClock 内部缩放（勿用 Time.deltaTime 二次乘）。
            // 按索引遍历：IReadOnlyList 的接口枚举器会每帧箱化一次（60fps 零分配纪律）。
            var tickables = _container.Tickables;
            for (int i = 0; i < tickables.Count; i++) tickables[i].Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (!_active) return;                            // 重复引导件销毁前的最后一步不参与
            _bootCts?.Cancel();                              // 引导期残留异步取消（AppLifetime 负责完整关闭）
            _active = false;
        }
    }
}
