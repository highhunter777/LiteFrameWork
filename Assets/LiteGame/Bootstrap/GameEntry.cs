using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 进程壳（AOT 侧，B1.5）：只做**场景入口 + 宿主生命周期 + 入口装载 + 错误面**。
    /// 模块装配与启动续体归产品组合根（LiteGame.ClientEntry，经 ClientEntryLoader
    /// 按程序集限定名反射装载）——本件不认识任何 LiteGame.Modules.* 实现。
    ///
    /// - Awake：建 ClientHost + AppLifetime 桥 → 装载入口并 Configure
    ///   （登记装配段/组合根）→ 启动宿主初始化（失败 → BootstrapErrorPresenter 确定错误态）。
    /// - Start：宿主初始化完成后调用入口 StartAsync（开发调参/探针/流程机启动由产品侧负责）。
    /// - Update：驱动容器 Tickables（变速由 IGameClock 内部缩放，勿用 Time.deltaTime 二次乘）。
    /// - 退出/宿主销毁：AppLifetime 桥接优雅关闭（刷新钩子 + 逆序段关闭 + 根 Scope 释放）。
    ///
    /// 静态状态仅 s_booted 幂等标记（重复引导守卫；不持真实生命周期）。
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
            LiteFramework.UI.UIAnimationClock.ResetForEditorReload();
        }

        private ClientHost _host;
        private IClientEntry _entry;              // 产品入口（反射装载；本件只认 IClientEntry）
        private ServiceContainer _container;      // 实例全程持有，随引导件销毁
        private CancellationTokenSource _bootCts;
        private UniTask<bool> _hostInit;          // Awake 启动、Start 等待；失败已出错误面
        private bool _active;

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

        /// <summary>只读统计列表（DevHud 跨程序集拉取用）：**不是解析入口**，不含容器语义。</summary>
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

            _bootCts = new CancellationTokenSource();

            // 入口装载：产品组合根（LiteGame.ClientEntry）登记装配段与 ComposeRoot；失败即冷启动错误面。
            try
            {
                _entry = ClientEntryLoader.Load();
                _entry.Configure(_host, gameObject);
            }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 入口装载失败：{ex.Message}", LogTag.GameEntry);
                BootstrapErrorPresenter.Show(ex);
                _active = false;
                return;
            }

            // 跨场景存活（引导件被场景卸载销毁 = 泵停转 = 全局静默冻结）
            DontDestroyOnLoad(gameObject);
            _hostInit = InitializeHostAsync(_bootCts.Token);
        }

        /// <summary>宿主初始化（首批全同步完成 → 产物在 Awake 内即就绪）；失败进入确定错误态（§C1）。</summary>
        private async UniTask<bool> InitializeHostAsync(CancellationToken ct)
        {
            try
            {
                await _host.InitializeAsync(ct);
                _container = _host.Product<ServiceContainer>();
                return true;
            }
            catch (OperationCanceledException)
            {
                // 引导取消：进入确定空闲态（Host 已回滚），不起流程
                Log.Warning("GameEntry 引导被取消（Host 已回滚到确定状态）", LogTag.GameEntry);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 引导失败：{ex.Message}", LogTag.GameEntry);
                BootstrapErrorPresenter.Show(ex);   // 分类展示（未标记 = Fatal）
                return false;
            }
        }

        /// <summary>Unity Start：宿主可能异步完成——等待初始化结果后再调用产品启动续体。</summary>
        private void Start()
        {
            if (!_active) return;
            StartEntryAsync(_bootCts.Token).ForgetLogged("GameEntry");
        }

        private async UniTask StartEntryAsync(CancellationToken ct)
        {
            if (!await _hostInit) return;
            try
            {
                await _entry.StartAsync(ct);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Log.Error($"GameEntry 启动续体失败：{ex.Message}", LogTag.GameEntry);
                BootstrapErrorPresenter.Show(ex);
            }
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
            // 宿主关停后 UI 动效时钟必须清绑：静态残留会指向已销毁时钟，后续裸建 UIService
            // （PlayMode 夹具/工具）仍走 Manual 轨而无人泵 → 转场 2s 超时。
            LiteFramework.UI.UIAnimationClock.ResetForEditorReload();
            s_booted = false;                                 // 同一 Play 会话允许重新引导（测试/编辑器重建）
        }
    }
}
