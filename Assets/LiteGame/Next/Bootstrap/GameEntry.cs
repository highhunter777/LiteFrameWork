using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using UnityEngine;

namespace LiteGameNext.Bootstrap
{
    /// <summary>Next 子树进程壳（AOT）：平台初始化 → 反射装载热更 ClientApp → 初始化 → 每帧 Tick → 关闭；不引用热更类型。</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameEntry : MonoBehaviour
    {
        /// <summary>热更入口类型程序集限定名（与 ClientApp 的命名空间一致；Player 侧随热更清单接线）。</summary>
        public const string ClientAppTypeName = "LiteGameNext.ClientApp, LiteGameNext";

        private static bool s_booted;

        // 必须全限定：热更侧存在 LiteGameNext.Platform 命名空间，简单名 Platform 会被其遮蔽（CS0118）。
        private LiteFrameworkNext.Platform _platform;
        private IClientApp _clientApp;
        private CancellationTokenSource _shutdown;
        private bool _active;

        /// <summary>编辑器 Domain-Reload-Off 复位：静态守卫跨 Play 残留会把第二次引导当重复件销毁。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForEditorReload() => s_booted = false;

        private void Awake()
        {
            if (s_booted)
            {
                Log.Warning("检测到重复 GameEntry（场景 " + gameObject.scene.name + " 误含引导件）——已销毁，保留首个引导");
                Destroy(gameObject);
                return;
            }

            s_booted = true;
            _active = true;
            DontDestroyOnLoad(gameObject);
            _shutdown = new CancellationTokenSource();
            _platform = new LiteFrameworkNext.Platform("LiteGameNext", Application.version);

            try
            {
                _platform.Initialize();
                _clientApp = LoadClientApp(_platform);
            }
            catch (Exception ex)
            {
                Log.Error("引导失败：" + ex);
                ShutdownState();
            }
        }

        // 异步流程必须在 Start 启动：Awake 期 UniTask 的 PlayerLoop 尚未泵动，
        // 续体不会推进（旧 GameEntry 同口径）。Awake 只做同步引导与入口装载。
        private void Start()
        {
            if (!_active || _clientApp == null) return;
            BootAsync(_clientApp, _shutdown.Token).Forget(ex => Log.Error("启动异步失败：" + ex));
        }

        private void Update()
        {
            if (!_active || _clientApp == null) return;
            _clientApp.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (!_active) return;
            _active = false;
            ShutdownState();
        }

        /// <summary>反射装载热更组合根：入口缺失/未实现契约即显性失败，不静默降级为空启动。</summary>
        private static IClientApp LoadClientApp(LiteFrameworkNext.Platform platform)
        {
            Type type = Type.GetType(ClientAppTypeName, throwOnError: false);
            if (type == null)
                throw new InvalidOperationException("热更入口无法解析：" + ClientAppTypeName + "（检查 LiteGameNext 是否参与构建）");
            if (!typeof(IClientApp).IsAssignableFrom(type))
                throw new InvalidOperationException("热更入口未实现 IClientApp：" + type.FullName);
            return (IClientApp)Activator.CreateInstance(type, platform);
        }

        private static async UniTask BootAsync(IClientApp app, CancellationToken ct)
        {
            try
            {
                await app.InitializeAsync(ct);
                await app.RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭：各功能在 finally 内完成清理。
            }
            catch (Exception ex)
            {
                Log.Error("应用流程失败：" + ex);
            }
        }

        private void ShutdownState()
        {
            _shutdown?.Cancel();
            IClientApp app = _clientApp;
            _clientApp = null;
            _active = false;
            if (app != null) ShutdownAppAsync(app, _platform).Forget(ex => Log.Error("关闭异步失败：" + ex));
            else _platform?.Shutdown();
            _shutdown?.Dispose();
            _shutdown = null;
            s_booted = false;
        }

        private static async UniTask ShutdownAppAsync(IClientApp app, LiteFrameworkNext.Platform platform)
        {
            try
            {
                await app.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Log.Error("应用关闭失败：" + ex);
            }

            platform?.Shutdown();
        }
    }
}
