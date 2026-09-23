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
    /// Unity 引导适配器（《商业级通用客户端框架总设计》§6.1：**GameEntry 最终收敛为 Unity 引导适配器，
    /// 真正的启动与关闭由 ClientHost 持有**——C1 批①②落地）：
    /// - Awake：建 ClientHost + AppLifetime 桥，按依赖序注册 IClientModule（装配步骤全量移入 GameModules），
    ///   启动初始化（失败 → ProcedureError.ShowBootstrapError 进入确定错误态，§C1 退出条件宿主侧）。
    /// - Start：流程机启动（Launch）——依赖宿主初始化同步完成（首批模块全同步）。
    /// - Update：驱动容器 Tickables（原语义：变速由 IGameClock 内部缩放，勿用 Time.deltaTime 二次乘）。
    /// - 退出/宿主销毁：AppLifetime 桥接优雅关闭（刷新钩子 + 逆序模块关闭 + 根 Scope 释放——原缺失面）。
    ///
    /// 静态纪律（§4 原则 8）：仅剩 `s_booted` 幂等标记（重复引导守卫；不持真实生命周期）。
    /// 容器/流程机收为实例字段，随引导件销毁；AppLifetime.OnDestroy 执行完整关闭（无遗留）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // 引导件必须最先 Awake：FileSys/Log/容器由本件经 ClientHost 建立，其余组件依赖这套基础设施
    public sealed class GameEntry : MonoBehaviour
    {
        private static bool s_booted;   // 重复引导守卫（场景误含引导件时静默销毁新件；不持生命周期）

        /// <summary>编辑器静态清理（§6.1"清理静态兼容状态，支持关闭 Domain Reload 的编辑器场景"）：
        /// Domain-Reload-Off 时静态跨 Play 残留——s_booted 不复位会把第二次 Play 的引导件当重复件静默销毁
        /// （引导死锁）。AppLifetime 已桥接 ClientHost.ResetForEditorReload；本类自有静态在此复位。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForEditorReload() => s_booted = false;

        private ClientHost _host;
        private ServiceContainer _container;   // 实例字段（原静态——§4 原则 8：实例全程持有，随引导件销毁）
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
            // 会二次引导并覆盖容器/静态设施——静默销毁重复件，保留首个引导（实测见 M2 指导实施记录）。
            if (s_booted)
            {
                Log.Warning($"检测到重复 GameEntry（场景 {gameObject.scene.name} 误含引导件）——已销毁，保留首个引导", "GameEntry");
                Destroy(gameObject);
                return;
            }
            s_booted = true;
            _active = true;
            Debug.Log("[GE] awake begin");                  // C1-③ 临时诊断：Player 启动卡点定位（验收后移除）

            // ClientHost + 平台桥（AppLifetime 同 GameObject：接管 Pause/Focus/LowMemory/Quit 与退出善后）
            _host = new ClientHost();
            _host.ModuleTrace = (name, phase) => UnityEngine.Debug.Log($"[Host] {name} {phase}");
            gameObject.AddComponent<AppLifetime>().Bind(_host);

            // 装配（§6.1：注册顺序 = 初始化顺序 = 关闭逆序——依赖图由调用序表达）
            _host.AddModule(new GameModules.PlatformInfrastructure())
                 .AddModule(new GameModules.Settings())
                 .AddModule(new GameModules.Clocks())
                 .AddModule(new GameModules.Schedulers())
                 .AddModule(new GameModules.LuaHost(gameObject))
                 .AddModule(new GameModules.Config())
                 .AddModule(new GameModules.UiShell())
                 .AddModule(new GameModules.Presentation())
                 .AddModule(new GameModules.Container());

            _bootCts = new CancellationTokenSource();

            // 跨场景存活（原 M2 行为，C1 重构时曾遗漏——引导件被场景卸载销毁 = 泵停转 = 全局静默冻结）
            DontDestroyOnLoad(gameObject);

            RunBootstrapAsync(_bootCts.Token).Forget();
        }

        /// <summary>宿主初始化（首批全同步完成 → 产物在 Awake 内即就绪）；失败进入确定错误态（§C1）。</summary>
        private async UniTaskVoid RunBootstrapAsync(CancellationToken ct)
        {
            try
            {
                Debug.Log("[GE] bootstrap start");              // C1-③ 临时诊断
                await _host.InitializeAsync(ct);
                _container = _host.Product<ServiceContainer>();
                _fsm = _host.Product<StageMachine<ProcedureId, ProcedureArgs>>();
                Debug.Log("[GE] bootstrap done");               // C1-③ 临时诊断
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
                // 调试组件注入（同 GameObject；可选——未挂即跳过；守卫与定义处一致：release Player 下类型被条件编译移除）
                GetComponent<DebugTuner>()?.Inject(_host.Product<IWorldClock>(), _host.Product<IUIClock>(), _host.Product<EventCenter>());
#endif
            }
            catch (OperationCanceledException)
            {
                // 引导取消：进入确定空闲态（Host 已回滚），不再起流程
                Log.Warning("GameEntry 引导被取消（Host 已回滚到确定状态）", "GameEntry");
            }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 引导失败：{ex.Message}", "GameEntry");
                ProcedureError.ShowBootstrapError(ex.Message);
            }
        }

        /// <summary>流程四阶段（M2；2026-09-17 A 路线）：业务服务在 ProcedureLaunch 装配（注册 IConfigService/SceneService → Seal）；
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
            Debug.Log("[GE] destroyed");                     // C1-③ 临时诊断
        }
    }
}
