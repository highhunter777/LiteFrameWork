using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>
    /// 应用生命周期桥（《商业级通用客户端框架总设计》§6.1 ClientHost 责任"处理 OnApplicationPause/Focus/Quit、
    /// 低内存…"）：Unity 平台消息 → <see cref="ClientHost"/> 的转发器与优雅退出的宿主端执行者。
    ///
    /// 职责边界：本件只做"桥"——不含任何业务/模块逻辑；ClientHost 是纯 C#（L1 可测），
    /// UnityEngine.Application/quitting 语义在此翻译成 Host 事件。
    ///
    /// 退出序列（§6.1"关闭前刷新设置、存档、遥测和崩溃前最后日志"）：
    /// <c>OnApplicationQuit → 停止 Host 新事件 → QuitIntent 收集 → 刷新钩子 → 逆序 ShutdownAsync
    /// → 完成或超时后放行退出</c>。超时保底：退出不能被单个挂死的模块无限拖住（Unity 退出在主线程等
    /// <see cref="GracefulShutdownMaxMs"/> 后放行）。
    /// </summary>
    public sealed class AppLifetime : MonoBehaviour
    {
        /// <summary>优雅关闭的等待上限（毫秒）：Quit 路径必须有界——模块善后不能把进程退出拖成挂死。</summary>
        public const int GracefulShutdownMaxMs = 3000;

        private ClientHost _host;
        private bool _quitting;

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

        /// <summary>Unity 退出消息 → 优雅关闭序列（public 原因同上）。</summary>
        public void OnApplicationQuit()
        {
            if (_host == null || _quitting) return;
            _quitting = true;

            // 优雅退出：同步有界等待（Unity 退出只给主线程这一窗口）。
            // QuitIntent → ShutdownAsync（刷新钩子 + 逆序模块关闭 + 根 Scope）→ 超时放行。
            // 挂死模块不应把进程退出拖成僵尸：超时后由 OS/引擎回收残余。
            UniTask quitFlow = RunGracefulQuitAsync();
            float deadline = Time.realtimeSinceStartup + GracefulShutdownMaxMs / 1000f;
            while (!quitFlow.GetAwaiter().IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                // 主线程泵：驱动关闭流程里的异步延续（UniTask 的同步完成模式下通常瞬间完成）
                System.Threading.Thread.Sleep(1);
            }
        }

        private async UniTask RunGracefulQuitAsync()
        {
            try
            {
                await _host.RaiseQuitIntentAsync();
                await _host.ShutdownAsync();
            }
            catch (Exception)
            {
                // 退出路径异常不再上抛（Host 已在内部聚合 ShutdownFailures；此处只保证不阻塞退出）
            }
        }

        private void OnDestroy()
        {
            // 非 Quit 场景的宿主销毁（场景切换误删引导件等）：同样走完整关闭，保证不因销毁路径泄漏模块
            if (_host == null || _quitting) return;
            _quitting = true;
            UniTask destructionFlow = RunGracefulQuitAsync();
            float deadline = Time.realtimeSinceStartup + GracefulShutdownMaxMs / 1000f;
            while (!destructionFlow.GetAwaiter().IsCompleted && Time.realtimeSinceStartup < deadline)
            {
                System.Threading.Thread.Sleep(1);
            }
        }
    }
}
