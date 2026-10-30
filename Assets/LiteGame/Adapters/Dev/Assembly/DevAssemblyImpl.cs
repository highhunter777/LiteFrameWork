using System;
using LiteFramework;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Dev;

namespace LiteGame
{
    /// <summary>
    /// 开发装配实现（M3-6b 3b）：产品程序集经 <see cref="DevHooks"/> 只认端口，本类在开发程序集内落地
    /// "会话实现 / 容器装饰（GM 命令 + 弱网 + 调试内核）/ 传输包装 / 诊断登记 / 对局叠加层"。
    ///
    /// **绑定时机**：`BeforeSceneLoad` —— 早于场景引导件（GameEntry.Awake），装配根取件时钩子已在位；
    /// `SubsystemRegistration` 复位（Domain-Reload-Off 下静态不跨 Play 残留）。
    /// </summary>
    public sealed class DevAssemblyImpl : IDevAssembly
    {
        private readonly DevBattleDiagnostics _diagnostics = new DevBattleDiagnostics();
        private WeakNetRuntime _weakNet;
        private IDisposable _decoration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BindHooks() => DevHooks.Bind(new DevAssemblyImpl());

#if UNITY_EDITOR
        /// <summary>编辑器常驻绑定：EditMode 用例/编辑器工具不经 PlayMode 也要拿到开发装配（域重载后重绑）。</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void BindInEditor() => BindHooks();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHooks()
        {
            DevHooks.Unbind();
            DevRuntime.Reset();
        }

        public IDevSession CreateSession() => new DevSession();

        public IBattleDevDiagnostics BattleDiagnostics => _diagnostics;

        public IBattleDevOverlay CreateBattleOverlay() => new DevBattleOverlay();

        public void InjectRuntimeTuners(GameObject host, IWorldClock worldClock, IUIClock uiClock,
            EventBus events, CommandCenter commands)
        {
            if (host == null) return;
            host.GetComponent<DebugRuntimeTuner>()
                ?.Inject(worldClock, uiClock, events, commands);
        }

        public Func<bool, MatchTransport> WrapTransportFactory(Func<bool, MatchTransport> inner)
            => WeakNetWiring.WrapFactory(inner, _weakNet);

        public IDisposable Decorate(DevServices services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            _decoration?.Dispose();

            // GM 场景命令（带参+异步，GmOnly）：注册即进发现面——加载/卸载进审计环。
            IDisposable gmSceneCommands = GmSceneCommands.RegisterAll(services.Commands, services.Scenes);

            _weakNet = new WeakNetRuntime();
            services.Context.Put(_weakNet);

            // 网络调试组件（读模型 + 断线/重连动作）：会话上下文是流程私有件，经诊断静态读口取件。
            var networkDebug = new NetworkDebug(_weakNet, () => LiteGame.BattleDiagnostics.Current);
            services.Context.Put(networkDebug);

            IDisposable weakCmd = services.Commands.Register(new SetWeakNetPresetHandler(_weakNet),
                new CommandOptions(gmOnly: true, description: "切换弱网预设（none/lan/mobile/lossy）"));
            IDisposable disconnectCmd = services.Commands.Register(new ForceDisconnectHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "强制断线（调试：断后走自动重连）"));
            IDisposable reconnectCmd = services.Commands.Register(new ForceReconnectHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "强制重连（仅 SuspectedLost 相位有效）"));
            IDisposable suppressCmd = services.Commands.Register(new SetAutoReconnectSuppressedHandler(networkDebug),
                new CommandOptions(gmOnly: true, description: "抑制/恢复自动重连（断线停在 SuspectedLost）"));

            // 调试内核：读模型/动作模型/发现协议的唯一门面；投影（编辑器页/HUD/控制台）只消费它。
            var kernel = new DebugKernel(services.Commands, services.Input,
                new IDebugTunableProvider[]
                {
                    new FeatureDebugTunables(services.Session),
                    new WeaponDebugTunables(services.Session),
                    _weakNet,
                },
                _weakNet, networkDebug, services.Session);
            services.Context.Put(kernel);
            DevRuntime.SetKernel(kernel);

            _decoration = new Decoration(gmSceneCommands, weakCmd, disconnectCmd, reconnectCmd, suppressCmd);
            return _decoration;
        }

        /// <summary>装饰句柄：注销顺序与旧装配根一致（命令先行→网关），并清 Dev 静态读口。</summary>
        private sealed class Decoration : IDisposable
        {
            private IDisposable _gmSceneCommands;
            private IDisposable _weakNetCommand;
            private IDisposable _disconnectCommand;
            private IDisposable _reconnectCommand;
            private IDisposable _suppressReconnectCommand;

            public Decoration(IDisposable gmSceneCommands, IDisposable weakNetCommand,
                IDisposable disconnectCommand, IDisposable reconnectCommand, IDisposable suppressReconnectCommand)
            {
                _gmSceneCommands = gmSceneCommands;
                _weakNetCommand = weakNetCommand;
                _disconnectCommand = disconnectCommand;
                _reconnectCommand = reconnectCommand;
                _suppressReconnectCommand = suppressReconnectCommand;
            }

            public void Dispose()
            {
                _weakNetCommand?.Dispose(); _weakNetCommand = null;
                _disconnectCommand?.Dispose(); _disconnectCommand = null;
                _reconnectCommand?.Dispose(); _reconnectCommand = null;
                _suppressReconnectCommand?.Dispose(); _suppressReconnectCommand = null;
                _gmSceneCommands?.Dispose(); _gmSceneCommands = null;
                DevRuntime.Reset();                         // 内核读口随装配卸载（HUD 先空态）
            }
        }
    }

    /// <summary>开发侧运行期读口（投影件取调试内核/弱网实例；随装配装饰句柄收口）。</summary>
    public static class DevRuntime
    {
        /// <summary>调试内核（DevHUD/GM 页/控制台的数据面；未装配 = null）。</summary>
        public static DebugKernel Kernel { get; private set; }

        public static void SetKernel(DebugKernel kernel) => Kernel = kernel;

        public static void Reset() => Kernel = null;
    }
}
