using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// 应用生命周期桥（ClientHost 责任"处理 OnApplicationPause/Focus/Quit、
    /// 低内存…"）：Unity 平台消息 → <see cref="ClientHost"/> 的转发器与优雅退出的宿主端执行者。
    ///
    /// 职责边界：本件只做"桥"——不含任何业务/模块逻辑；ClientHost 是纯 C#（L1 可测），
    /// UnityEngine.Application/quitting 语义在此翻译成 Host 事件。
    ///
    /// 退出序列（关闭前刷新设置、存档、遥测和崩溃前最后日志）：
    /// <c>OnApplicationQuit → 停止 Host 新事件 → QuitIntent 收集 → 刷新钩子 → 逆序 ShutdownAsync
    /// → 完成或超时后放行退出</c>。超时保底：退出不能被单个挂死的模块无限拖住（Unity 退出在主线程等
    /// <see cref="GracefulShutdownMaxMs"/> 后放行）。
    /// </summary>
    public sealed class AppLifetime : MonoBehaviour
    {
        /// <summary>优雅关闭的等待上限（毫秒）：Quit 路径必须有界——模块善后不能把进程退出拖成挂死。</summary>
        public const int GracefulShutdownMaxMs = 3000;

        /// <summary>移动端目标帧率档：Unity 在 Android/iOS 未显式设置时按平台默认（约 30）跑，
        /// 与编辑器 60 形成"同一份构建两种手感"；射击游戏按 60 定格。
        /// 默认值单源在 <see cref="FrameRateOptions"/>（设置项与启动兜底共用，避免两处各写一份）。</summary>
        public static int MobileTargetFrameRate => FrameRateOptions.ToAppTarget(FrameRateOptions.DefaultFor(true));

        /// <summary>非移动平台沿用引擎默认（-1＝由 vSync/平台决定）——桌面出包观感不变。</summary>
        public static int DesktopTargetFrameRate => FrameRateOptions.ToAppTarget(FrameRateOptions.DefaultFor(false));

        private ClientHost _host;
        private bool _quitting;
        private bool _quitApproved;

        private void OnEnable() => Application.wantsToQuit += OnWantsToQuit;
        private void OnDisable() => Application.wantsToQuit -= OnWantsToQuit;

        /// <summary>
        /// 退出闸门：**先拦下退出**（主循环仍在跑 ⇒ 关闭链的 UniTask 续体可推进），
        /// 异步关闭完成后置放行标志并再次 <see cref="Application.Quit"/>——这次直接放行。
        /// 编辑器停播不走此路（`Application.Quit` 在编辑器不生效）——由 <see cref="OnDestroy"/> 兜底。
        /// </summary>
        private bool OnWantsToQuit()
        {
            if (_quitApproved || _host == null) return true;
            if (Application.isEditor) return true;      // 编辑器：交给 OnDestroy 兜底，避免拦下后无法再放行
            _quitting = true;
            ApproveAfterShutdownAsync().ForgetLogged("AppLifetime");
            return false;
        }

        private async UniTask ApproveAfterShutdownAsync()
        {
            await RunGracefulQuitAsync();
            _quitApproved = true;
            Application.Quit();                          // 二次退出：本次直接放行
        }

        /// <summary>桥接目标 Host。绑定后本件接管其平台事件转发与退出善后（一对一；二次绑定即装配错误）。</summary>
        public void Bind(ClientHost host)
        {
            if (_host != null) throw new InvalidOperationException("AppLifetime 已绑定 Host（一对一桥，禁止重绑）");
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// 编辑器静态清理（关闭 Domain Reload）：Unity 在进入 Play/重载前调用本入口，
        /// 转发 <see cref="ClientHost.ResetForEditorReload"/>（未来静态门面的唯一清理点）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForEditorReload()
        {
            ClientHost.ResetForEditorReload();
        }

        /// <summary>帧率档的单点决策（纯函数——用例可直接断言两态；运行时只由
        /// <see cref="ApplyPlatformFrameRate"/> 消费）。默认值取 <see cref="FrameRateOptions"/>，
        /// 与设置项首启默认同源。</summary>
        public static int ResolveTargetFrameRate(bool isMobilePlatform)
            => isMobilePlatform ? MobileTargetFrameRate : DesktopTargetFrameRate;

        /// <summary>启动期帧率档的唯一设置点（设计口径"移动 targetFrameRate 档"）。
        /// 用**运行时平台查询**而非条件编译——平台差异不得散落成 `#if`（纪律扫描 R15）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyPlatformFrameRate()
        {
            Application.targetFrameRate = ResolveTargetFrameRate(Application.isMobilePlatform);
        }

        /// <summary>Unity 生命周期消息 → Host 转发。**public 供 EditMode 直调**（编辑态 SendMessage 不派发
        /// 引擎消息——ShouldRunBehaviour 断言）；转发语义：未绑定/退出中静默。</summary>
        public void OnApplicationPause(bool paused)
        {
            if (_host == null || _quitting) return;
            _host.RaiseApplicationPause(paused);
        }

        /// <summary>Unity 生命周期消息 → Host 转发（public 原因同上）。</summary>
        public void OnApplicationFocus(bool focused)
        {
            if (_host == null || _quitting) return;
            _host.RaiseApplicationFocus(focused);
        }

        /// <summary>Unity 低内存消息 → Host 转发（public 原因同上）。</summary>
        public void OnLowMemory()
        {
            if (_host == null || _quitting) return;
            _host.RaiseLowMemory();
        }

        /// <summary>Unity 退出消息 → 优雅关闭序列（public 原因同上）。
        /// **不阻塞主线程**：正常退出已在 <see cref="OnWantsToQuit"/> 拦下并完成关闭；此入口若仍拿到未完成的
        /// 关闭链（编辑器停播/引擎直接退出），只做确定性兜底——强制释放根 Scope，不再用 sleep 空转。</summary>
        public void OnApplicationQuit()
        {
            if (_host == null || _quitting) return;
            _quitting = true;

            UniTask quitFlow = RunGracefulQuitAsync();
            if (!quitFlow.GetAwaiter().IsCompleted)
            {
                Log.Warning("退出时异步关闭未完成——强制释放根 Scope（刷新钩子可能被跳过）", LogTag.Bootstrap);
                _host.ForceReleaseRoot();
            }
        }

        private async UniTask RunGracefulQuitAsync()
        {
            using var timeoutCts = new CancellationTokenSource();
            UniTask shutdownFlow = RunShutdownFlowAsync();
            UniTask timeout = UniTask.Delay(
                TimeSpan.FromMilliseconds(GracefulShutdownMaxMs),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                timeoutCts.Token);

            // 有界退出：模块善后挂死时不再无限等待——超时即强制释放根 Scope 并放行退出。
            int winner = await UniTask.WhenAny(shutdownFlow, timeout);
            if (winner == 1)
            {
                Log.Warning($"优雅关闭超过 {GracefulShutdownMaxMs}ms——强制释放根 Scope 并放行退出", LogTag.Bootstrap);
                _host.ForceReleaseRoot();
            }

            timeoutCts.Cancel();
        }

        private async UniTask RunShutdownFlowAsync()
        {
            try
            {
                await _host.RaiseQuitIntentAsync();
                await _host.ShutdownAsync();
            }
            catch (Exception)
            {
                // 退出路径异常不向上抛（Host 已在内部聚合 ShutdownFailures；此处只保证不阻塞退出）
            }
        }

        private void OnDestroy()
        {
            // 非 Quit 场景的宿主销毁（场景切换误删引导件等）：同样走完整关闭，保证不因销毁路径泄漏模块。
            if (_host == null || _quitting) return;
            _quitting = true;
            UniTask destructionFlow = RunGracefulQuitAsync();
            if (!destructionFlow.GetAwaiter().IsCompleted)
            {
                Log.Warning("宿主销毁时异步关闭未完成——强制释放根 Scope（刷新钩子可能被跳过）", LogTag.Bootstrap);
                _host.ForceReleaseRoot();
            }
        }
    }
}
