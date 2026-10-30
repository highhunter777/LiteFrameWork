#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using LiteFramework.UI;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 单页布局预览驱动（《UI编辑器工具专项设计》§8.4）：编辑器 Play 内直接实例化目标 prefab，
    /// 不跑 Boot/Lua/数据；资产保存后由 <c>UiPagePreview</c> 的 AssetPostprocessor 触发重载。
    /// 本件只存在于编辑器形态；预览场景不在收集组，也不进构建。
    /// </summary>
    public sealed class UiPreviewDriver : MonoBehaviour
    {
        /// <summary>目标 prefab 的 EditorPrefs 键（编辑器入口写、本件读）。</summary>
        public const string TargetPrefabKey = "LiteGame.UiPreview.TargetPrefab";

        // ---- P2-3 B3：预览参数键（编辑器侧 UiPagePreview/矩阵状态机写 SessionState；本件只读原语） ----
        public const string DeviceProfileIdKey = "LiteGame.UiPreview.DeviceProfileId";
        public const string DeviceWidthKey = "LiteGame.UiPreview.DeviceWidth";
        public const string DeviceHeightKey = "LiteGame.UiPreview.DeviceHeight";
        public const string InsetLeftKey = "LiteGame.UiPreview.InsetLeft";
        public const string InsetTopKey = "LiteGame.UiPreview.InsetTop";
        public const string InsetRightKey = "LiteGame.UiPreview.InsetRight";
        public const string InsetBottomKey = "LiteGame.UiPreview.InsetBottom";
        public const string LocaleKey = "LiteGame.UiPreview.Locale";
        public const string FontScaleKey = "LiteGame.UiPreview.FontScale";
        public const string OptionsRevisionKey = "LiteGame.UiPreview.OptionsRevision";
        public const string OptionsAppliedKey = "LiteGame.UiPreview.OptionsApplied";
        public const string HideOverlayKey = "LiteGame.UiPreview.HideOverlay";
        public const string LocaleStatusKey = "LiteGame.UiPreview.LocaleStatus";   // off | pending | ok | unavailable
        public const string DeviceProfileChoicesKey = "LiteGame.UiPreview.DeviceProfileChoices";
        public const string LocaleDataDirKey = "LiteGame.UiPreview.LocaleDataDir"; // 测试/故障注入；空＝ConfigService.DataDir
        public const string OriginalGameViewWidthKey = "LiteGame.UiPreview.OriginalGameViewWidth";
        public const string OriginalGameViewHeightKey = "LiteGame.UiPreview.OriginalGameViewHeight";

        // ---- P2-3 B3：应用/复位面（公开供 PlayMode 夹具直测；实例方法、无静态状态） ----

        private readonly List<SafeAreaReceiver> _safeAreaReceivers = new List<SafeAreaReceiver>();
        private PreviewFontScale _fontScale;
        private UiStaticTextBinder _staticText;
        private GameObject _staticRoot;
        private PreviewAuthorText _authorText;
        private ILocalizationService _localeService;
        private string _optionsFingerprint = string.Empty;
        private string _lastApplyError;
        private float _nextOptionsPoll;

        /// <summary>注入安全区模拟策略到页面内全部 SafeAreaReceiver（当前屏幕像素空间换算）。</summary>
        public void ApplySafeArea(int deviceWidth, int deviceHeight,
            float insetLeft, float insetTop, float insetRight, float insetBottom)
        {
            _safeAreaReceivers.Clear();
            var strategy = new PreviewSafeAreaStrategy(deviceWidth, deviceHeight,
                insetLeft, insetTop, insetRight, insetBottom);
            foreach (SafeAreaReceiver receiver in FindObjectsByType<SafeAreaReceiver>(FindObjectsSortMode.None))
            {
                if (receiver == null) continue;
                _safeAreaReceivers.Add(receiver);
                receiver.SetStrategy(strategy);
            }
        }

        /// <summary>对页面施加字体缩放档（记录原值；ResetPreviewOptions 恢复）。</summary>
        public void ApplyFontScale(GameObject pageRoot, float scale)
        {
            _fontScale = _fontScale ?? new PreviewFontScale();
            _fontScale.Apply(pageRoot, scale);
        }

        /// <summary>绑定语言服务到页面静态文案（LTextKeyRef；重复绑定先解绑；service=null 还原作者原串）。</summary>
        public void ApplyLocale(GameObject pageRoot, ILocalizationService service)
        {
            if (!ReferenceEquals(_staticRoot, pageRoot))
            {
                _staticText?.Unbind();
                _staticText = null;
                _staticRoot = pageRoot;
                _authorText = pageRoot != null ? new PreviewAuthorText(pageRoot) : null;
            }

            _staticText?.Unbind();
            _staticText = null;
            if (service == null)
            {
                _authorText?.Restore();
                return;
            }
            _staticText = pageRoot != null ? new UiStaticTextBinder(pageRoot.transform) : null;
            _staticText?.Bind(service);
        }

        /// <summary>复位全部预览参数（安全区回 Screen.safeArea/字体原值/文案解绑）——退出预览与切档共用。</summary>
        public void ResetPreviewOptions(GameObject pageRoot)
        {
            foreach (SafeAreaReceiver receiver in _safeAreaReceivers)
            {
                if (receiver != null) receiver.SetStrategy(null);
            }
            _safeAreaReceivers.Clear();
            _fontScale?.Restore();
            _fontScale = null;
            _staticText?.Unbind();
            _staticText = null;
            _authorText?.Restore();
            _authorText = null;
            _staticRoot = null;
            _optionsFingerprint = string.Empty;
        }

        /// <summary>从编辑器写入的 SessionState 预览参数应用档位（免重启切档；无页面实例时不动）。</summary>
        private void ApplyOptionsFromSession(bool force = false)
        {
            if (_instance == null) return;

            int width = SessionState.GetInt(DeviceWidthKey, 0);
            int height = SessionState.GetInt(DeviceHeightKey, 0);
            float fontScale = SessionState.GetFloat(FontScaleKey, 1f);
            string locale = SessionState.GetString(LocaleKey, string.Empty);
            string fingerprint = string.Join("|", width, height,
                SessionState.GetFloat(InsetLeftKey, 0f), SessionState.GetFloat(InsetTopKey, 0f),
                SessionState.GetFloat(InsetRightKey, 0f), SessionState.GetFloat(InsetBottomKey, 0f),
                fontScale, locale);
            int revision = SessionState.GetInt(OptionsRevisionKey, 0);
            int appliedRevision = SessionState.GetInt(OptionsAppliedKey, -1);
            if (!force && string.Equals(fingerprint, _optionsFingerprint))
            {
                UpdateLocaleStatus(locale);   // 同指纹也要落终态，避免 pending 被当作成功
                if (appliedRevision != revision) SessionState.SetInt(OptionsAppliedKey, revision);
                return;
            }

            try
            {
                if (width > 0 && height > 0)
                {
                    ApplySafeArea(width, height,
                        SessionState.GetFloat(InsetLeftKey, 0f), SessionState.GetFloat(InsetTopKey, 0f),
                        SessionState.GetFloat(InsetRightKey, 0f), SessionState.GetFloat(InsetBottomKey, 0f));
                }
                else
                {
                    foreach (SafeAreaReceiver receiver in _safeAreaReceivers)
                    {
                        if (receiver != null) receiver.SetStrategy(null);
                    }
                    _safeAreaReceivers.Clear();
                }

                ApplyFontScale(_instance, fontScale > 0f ? fontScale : 1f);
                ILocalizationService resolvedLocale = ResolveLocale(locale);
                SessionState.SetString(LocaleStatusKey,
                    UiPreviewLocale.StatusFor(locale, resolvedLocale != null));
                ApplyLocale(_instance, resolvedLocale);
                _optionsFingerprint = fingerprint;
                _lastApplyError = null;
                SessionState.SetInt(OptionsAppliedKey, revision);
            }
            catch (Exception exception)
            {
                // 不记指纹/已应用：下一轮轮询重试，避免异常后永久假绿；同一错误只告警一次（不刷屏）。
                string message = exception.GetType().Name + ": " + exception.Message;
                if (!string.Equals(message, _lastApplyError, StringComparison.Ordinal))
                {
                    _lastApplyError = message;
                    Log.Warning("[UiPreview] 预览参数应用失败（下轮重试）：" + message, LogTag.UI);
                }
            }
        }

        private void UpdateLocaleStatus(string locale)
            => SessionState.SetString(LocaleStatusKey,
                UiPreviewLocale.StatusFor(locale, _localeService != null));

        private ILocalizationService ResolveLocale(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return null;
            _localeService = _localeService ?? UiPreviewLocale.BuildService(
                SessionState.GetString(LocaleDataDirKey, null));
            if (_localeService == null) return null;

            _localeService.SetLocale(string.Equals(locale, "pseudo", System.StringComparison.Ordinal)
                ? LocalizationCatalog.SourceLocale
                : locale);
            return string.Equals(locale, "pseudo", System.StringComparison.Ordinal)
                ? new PseudoLocale(_localeService)
                : _localeService;
        }

        private sealed class DeviceChoice
        {
            public string Id;
            public int Width;
            public int Height;
            public float Left;
            public float Top;
            public float Right;
            public float Bottom;
        }

        private GameObject _instance;
        private string _loadedPath;

        private void Awake()
        {
            UiInput.EnsureEventSystem();
            Reload();
        }

        private void Start()
        {
            // DevHUD 是运行期自建覆盖件；布局预览只需要页面本体。
            var hud = FindAnyObjectByType<DevHUD>();
            if (hud != null) hud.gameObject.SetActive(false);
        }

        /// <summary>按当前 EditorPrefs 目标重载实例（保存热重载与手动重载共用）。</summary>
        public void Reload()
        {
            string path = EditorPrefs.GetString(TargetPrefabKey, string.Empty);
            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }

            _loadedPath = path;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[UiPreview] 未指定目标 prefab");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[UiPreview] 目标 prefab 不存在：" + path);
                return;
            }

            _instance = Instantiate(prefab);
            _instance.name = prefab.name + "(Preview)";
            _instance.SetActive(true);
            ApplyOptionsFromSession(force: true);
            Debug.Log("[UiPreview] 已加载：" + path);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextOptionsPoll) return;
            _nextOptionsPoll = Time.unscaledTime + 0.2f;
            ApplyOptionsFromSession();
        }

        private void OnDestroy()
        {
            _staticText?.Unbind();
            _authorText?.Restore();
            _fontScale?.Restore();
        }

        private void OnGUI()
        {
            if (SessionState.GetBool(HideOverlayKey, false)) return;   // 矩阵截图不带预览工具条

            GUI.Box(new Rect(8f, 8f, 720f, 112f), GUIContent.none);
            GUI.Label(new Rect(18f, 12f, 480f, 20f),
                "UI 单页预览（布局档：无 Lua/数据）", GUI.skin.label);
            if (GUI.Button(new Rect(510f, 12f, 80f, 22f), "重载")) Reload();
            if (GUI.Button(new Rect(596f, 12f, 112f, 22f), "退出预览"))
                EditorApplication.isPlaying = false;

            string deviceId = SessionState.GetString(DeviceProfileIdKey, string.Empty);
            string locale = SessionState.GetString(LocaleKey, string.Empty);
            float fontScale = SessionState.GetFloat(FontScaleKey, 1f);
            if (GUI.Button(new Rect(18f, 38f, 220f, 22f),
                    "设备：" + (string.IsNullOrEmpty(deviceId) ? "跟随预览" : deviceId) + " ▸"))
                CycleDevice();
            if (GUI.Button(new Rect(246f, 38f, 150f, 22f), "语言：" + LocaleLabel(locale) + " ▸"))
                CycleLocale();
            if (GUI.Button(new Rect(404f, 38f, 140f, 22f),
                    "字体：" + fontScale.ToString("0.##") + " ▸"))
                CycleFontScale();

            GUI.Label(new Rect(18f, 64f, 700f, 20f),
                string.IsNullOrEmpty(_loadedPath) ? "未指定目标" : _loadedPath, GUI.skin.label);
            if (string.Equals(SessionState.GetString(LocaleStatusKey, string.Empty), "unavailable",
                    StringComparison.Ordinal))
            {
                GUI.Label(new Rect(18f, 84f, 700f, 20f),
                    "语言表直载失败——静态文案保持作者文本", GUI.skin.label);
            }
        }

        private static string LocaleLabel(string locale)
            => string.IsNullOrEmpty(locale) ? "作者文本" : locale;

        private static void BumpRevision()
        {
            int revision = SessionState.GetInt(OptionsRevisionKey, 0) + 1;
            SessionState.SetInt(OptionsRevisionKey, revision);
        }

        private void CycleDevice()
        {
            List<DeviceChoice> choices = ReadDeviceChoices();
            if (choices.Count == 0) return;

            string current = SessionState.GetString(DeviceProfileIdKey, string.Empty);
            int index = choices.FindIndex(choice =>
                string.Equals(choice.Id, current, StringComparison.Ordinal));
            index++;
            if (index >= choices.Count)
            {
                SessionState.SetString(DeviceProfileIdKey, string.Empty);
                SessionState.SetInt(DeviceWidthKey, 0);
                SessionState.SetInt(DeviceHeightKey, 0);
                SessionState.SetFloat(InsetLeftKey, 0f);
                SessionState.SetFloat(InsetTopKey, 0f);
                SessionState.SetFloat(InsetRightKey, 0f);
                SessionState.SetFloat(InsetBottomKey, 0f);
                int originalWidth = SessionState.GetInt(OriginalGameViewWidthKey, 0);
                int originalHeight = SessionState.GetInt(OriginalGameViewHeightKey, 0);
                if (originalWidth > 0 && originalHeight > 0)
                    UiPreviewGameView.TrySetSize(originalWidth, originalHeight);
            }
            else
            {
                DeviceChoice choice = choices[index];
                SessionState.SetString(DeviceProfileIdKey, choice.Id);
                SessionState.SetInt(DeviceWidthKey, choice.Width);
                SessionState.SetInt(DeviceHeightKey, choice.Height);
                SessionState.SetFloat(InsetLeftKey, choice.Left);
                SessionState.SetFloat(InsetTopKey, choice.Top);
                SessionState.SetFloat(InsetRightKey, choice.Right);
                SessionState.SetFloat(InsetBottomKey, choice.Bottom);
                UiPreviewGameView.TrySetSize(choice.Width, choice.Height);
            }
            BumpRevision();
        }

        private void CycleLocale()
        {
            var options = new List<string> { string.Empty };
            options.AddRange(UiPreviewLocale.SupportedLocales);
            string current = SessionState.GetString(LocaleKey, string.Empty);
            int index = options.IndexOf(current);
            if (index < 0) index = 0;
            string next = options[(index + 1) % options.Count];
            SessionState.SetString(LocaleKey, next);
            SessionState.SetString(LocaleStatusKey, string.IsNullOrEmpty(next) ? "off" : "pending");
            BumpRevision();
        }

        private void CycleFontScale()
        {
            float current = SessionState.GetFloat(FontScaleKey, 1f);
            IReadOnlyList<float> scales = PreviewFontScale.SupportedScales;
            int index = 0;
            for (int i = 0; i < scales.Count; i++)
            {
                if (Mathf.Abs(scales[i] - current) < 0.001f)
                {
                    index = i;
                    break;
                }
            }
            SessionState.SetFloat(FontScaleKey, scales[(index + 1) % scales.Count]);
            BumpRevision();
        }

        private static List<DeviceChoice> ReadDeviceChoices()
        {
            var choices = new List<DeviceChoice>();
            string raw = SessionState.GetString(DeviceProfileChoicesKey, string.Empty);
            foreach (string part in raw.Split(';'))
            {
                string[] fields = part.Split('|');
                if (fields.Length < 7) continue;
                if (!int.TryParse(fields[1], out int width) || !int.TryParse(fields[2], out int height))
                    continue;
                if (!float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float left)
                    || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float top)
                    || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float right)
                    || !float.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float bottom))
                    continue;
                choices.Add(new DeviceChoice
                {
                    Id = fields[0], Width = width, Height = height,
                    Left = left, Top = top, Right = right, Bottom = bottom,
                });
            }
            return choices;
        }
    }
}
#endif
