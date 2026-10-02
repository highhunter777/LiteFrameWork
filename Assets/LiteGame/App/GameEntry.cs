using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;
using UnityEngine;

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
    [DefaultExecutionOrder(-1000)]   // 引导件必须最先 Awake：FileSys/Log/容器由本件经 ClientHost 建立，其余组件依赖这套基础设施
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
            LiteGame.UI.UiAnimationClock.ResetForEditorReload();
        }

        private ClientHost _host;
        private ServiceContainer _container;   // 实例全程持有，随引导件销毁
        private StageMachine<ProcedureId, ProcedureArgs> _fsm;
        private CancellationTokenSource _bootCts;
        private bool _active;

        /// <summary>把装配权传给起始流程。ProcedureLaunch 是**唯一受信装配点**：注册业务服务 → Seal。</summary>
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
                Log.Warning($"检测到重复 GameEntry（场景 {gameObject.scene.name} 误含引导件）——已销毁，保留首个引导", "GameEntry");
                Destroy(gameObject);
                return;
            }
            s_booted = true;
            _active = true;
            Debug.Log("[GE] awake begin");                  // 临时诊断：Player 启动卡点定位

            // ClientHost + 平台桥（AppLifetime 同 GameObject：接管 Pause/Focus/LowMemory/Quit 与退出善后）
            _host = new ClientHost();
            _host.ModuleTrace = (name, phase) => UnityEngine.Debug.Log($"[Host] {name} {phase}");
            gameObject.AddComponent<AppLifetime>().Bind(_host);

            // 装配（§6.1：注册顺序 = 初始化顺序 = 关闭逆序——依赖图由调用序表达）
            RegisterModules(_host);

            _bootCts = new CancellationTokenSource();

            // 跨场景存活（引导件被场景卸载销毁 = 泵停转 = 全局静默冻结）
            DontDestroyOnLoad(gameObject);

            RunBootstrapAsync(_bootCts.Token).Forget();
        }

        /// <summary>宿主初始化（首批全同步完成 → 产物在 Awake 内即就绪）；失败进入确定错误态（§C1）。</summary>
        private async UniTaskVoid RunBootstrapAsync(CancellationToken ct)
        {
            try
            {
                Debug.Log("[GE] bootstrap start");              // 临时诊断
                await _host.InitializeAsync(ct);
                _container = _host.Product<ServiceContainer>();
                _fsm = _host.Product<StageMachine<ProcedureId, ProcedureArgs>>();
                Debug.Log("[GE] bootstrap done");               // 临时诊断
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                // 调试组件注入（同 GameObject；可选——未挂即跳过；守卫与定义处一致：release Player 下类型被条件编译移除）
                GetComponent<DebugTuner>()?.Inject(_host.Product<IWorldClock>(), _host.Product<IUIClock>(), _host.Product<EventCenter>());
#endif
            }
            catch (OperationCanceledException)
            {
                // 引导取消：进入确定空闲态（Host 已回滚），不起流程
                Log.Warning("GameEntry 引导被取消（Host 已回滚到确定状态）", "GameEntry");
            }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 引导失败：{ex.Message}", "GameEntry");
                ProcedureError.ShowBootstrapError(ex.Message);
            }
        }

        /// <summary>流程四阶段：业务服务在 ProcedureLaunch 装配（注册 IConfigService/SceneService → Seal）；
        /// 流程依赖在模块装配点构造注入——依赖不从 payload 取（局部服务定位器同罪）。</summary>
        private void Start()
        {
            if (!_active) return;                            // 重复引导件：Awake 已销毁，不参与
            TakeContainer();                                 // 断言已装配
            if (_fsm == null)
            {
                Log.Error("宿主初始化未完成——流程机缺失（进入 Error 态）", "GameEntry");
                ProcedureError.ShowBootstrapError("host bootstrap incomplete");
                return;
            }
            _fsm.Start(ProcedureId.Launch);
        }

        /// <summary>
        /// 模块装配序列（《客户端总设计》§6.1"按依赖顺序初始化，失败时只关闭已经成功初始化的模块"）。
        ///
        /// **注册顺序 = 初始化顺序 = 关闭逆序**：模块间依赖只能引用更早模块的产物（经
        /// <see cref="ClientContext"/> 的 Put/Require 传递——不用字典做服务定位，同型唯一、
        /// 类型编译期可见，装配错误显性失败）。这份调用序**就是**依赖图，Host 不推断。
        /// </summary>
        private void RegisterModules(ClientHost host)
        {
            host.AddModule(new PlatformInfrastructureModule())   // ① 本地持久化 + 日志（先于一切 IO）
                .AddModule(new ContentModule())                  // ② IContentService（配置/Lua 字节通道的租约来源）
                .AddModule(new SettingsModule())                 // ③ 玩家偏好（先于容器——UI/声音壳注册时就要读）
                .AddModule(new ClocksModule())                   // ④ 双轨时钟 + 事件中心
                .AddModule(new SchedulersModule())               // ⑤ 时序执行（依赖 ④）
                .AddModule(new LuaHostModule(gameObject))        // ⑥ 脚本宿主（同 GameObject 组件）
                .AddModule(new ConfigModule())                   // ⑦ 表投影数据源（依赖 ② 的租约通道）
                .AddModule(new UiShellModule())                  // ⑧ UI 壳（依赖 ⑥⑦②）
                .AddModule(new PresentationModule())             // ⑨ 表现壳 + 相机服务（依赖 ②④）
                .AddModule(new InputModule())                    // ⑩ 输入服务（设备源；瞄准相机取自 ⑨ 登记的主相机）
                .AddModule(new ContainerModule());               // ⑪ 容器与流程机（消费以上全部——装配根最后一段）
        }

        private void Update()
        {
            if (!_active || _container == null) return;      // 重复引导件销毁前 / 引导未完成不驱动
            // 统一喂真实帧间隔：变速由 IGameClock 内部缩放（勿用 Time.deltaTime 二次乘）
            foreach (var t in _container.Tickables) t.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (!_active) return;                            // 重复引导件销毁前的最后一步不参与
            _bootCts?.Cancel();                              // 引导期残留异步取消（AppLifetime 负责完整关闭）
            _active = false;
            Debug.Log("[GE] destroyed");                     // 临时诊断
        }
    }
}
