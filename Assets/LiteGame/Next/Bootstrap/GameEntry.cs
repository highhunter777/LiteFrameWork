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
        /// <summary>命令行截图探针：-s8shot &lt;绝对路径&gt; [-s8shotDelay 秒]。仅开发构建用，默认关闭。</summary>
        private float _shotAt = -1f;
        private string _shotPath;
        private string _clickBind;
        private float _clickAt = -1f;
        private bool _clicked;

        private void Start()
        {
            if (!_active || _clientApp == null) return;
            ArmScreenshotProbe();
            BootAsync(_clientApp, _shutdown.Token).Forget(ex => Log.Error("启动异步失败：" + ex));
        }

        private void Update()
        {
            if (!_active || _clientApp == null) return;
            _clientApp.Tick(Time.unscaledDeltaTime);
            TickScreenshotProbe();
        }

        /// <summary>解析 -s8shot 参数并定时；未传则永不动作。</summary>
        private void ArmScreenshotProbe()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-s8shot") continue;
                _shotPath = args[i + 1];
                float delay = 12f;
                for (int j = 0; j < args.Length - 1; j++)
                    if (args[j] == "-s8shotDelay" && float.TryParse(args[j + 1], out float parsed))
                        delay = parsed;
                _shotAt = Time.realtimeSinceStartup + delay;
                Log.Info("[S8Shot] 将在 " + delay + "s 后截图到 " + _shotPath);
                break;
            }

            // 可选：截图前用真实 EventSystem 射线点击指定 BindName 控件。
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-s8click") continue;
                _clickBind = args[i + 1];
                _clickAt = Time.realtimeSinceStartup + 8f;
                Log.Info("[S8Shot] 将在 8s 后真实点击 " + _clickBind);
                break;
            }
        }

        /// <summary>到点截图并退出（探针专用；正常启动不会进入）。</summary>
        private void TickScreenshotProbe()
        {
            if (!_clicked && _clickAt > 0f && Time.realtimeSinceStartup >= _clickAt)
            {
                _clicked = true;
                ClickByBindName(_clickBind);
            }
            if (_shotAt < 0f || Time.realtimeSinceStartup < _shotAt) return;
            _shotAt = -1f;
            Log.Info("[S8Shot] 截图：" + _shotPath);
            UnityEngine.ScreenCapture.CaptureScreenshot(_shotPath);
            StartCoroutine(QuitAfterShot());
        }

        /// <summary>真实指针点击：经 EventSystem 射线命中目标控件后派发 pointerClick（不直调页面 API）。</summary>
        private static void ClickByBindName(string bindName)
        {
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Log.Warning("[S8Shot] 无 EventSystem"); return; }
            foreach (UnityEngine.GameObject go in UnityEngine.Object.FindObjectsOfType<UnityEngine.GameObject>())
            {
                if (!TryMatchBindName(go, bindName)) continue;
                var rt = go.GetComponent<UnityEngine.RectTransform>();
                if (rt == null) continue;
                var pointer = new UnityEngine.EventSystems.PointerEventData(es)
                {
                    position = UnityEngine.RectTransformUtility.WorldToScreenPoint(null, rt.position),
                };
                var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(pointer, hits);
                if (hits.Count == 0) { Log.Warning("[S8Shot] 射线未命中 " + bindName); return; }
                Log.Info("[S8Shot] 命中 " + hits[0].gameObject.name + "，派发点击");
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer,
                    UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                return;
            }
            Log.Warning("[S8Shot] 未找到控件 " + bindName);
        }

        /// <summary>反射读 BindName.Name（Bootstrap 是 AOT 侧，不能引 hot 的 BindName 类型）。</summary>
        private static bool TryMatchBindName(UnityEngine.GameObject go, string bindName)
        {
            foreach (UnityEngine.Component component in go.GetComponents<UnityEngine.Component>())
            {
                System.Type type = component.GetType();
                if (type.FullName != "LiteGameNext.Platform.Ui.BindName") continue;
                var prop = type.GetProperty("Name");
                return prop != null && (prop.GetValue(component) as string) == bindName;
            }
            return false;
        }

        private System.Collections.IEnumerator QuitAfterShot()
        {
            yield return new WaitForSecondsRealtime(3f);
            Application.Quit();
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
