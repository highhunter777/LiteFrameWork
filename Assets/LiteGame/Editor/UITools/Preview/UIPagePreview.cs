using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>
    /// 单页预览入口（《UI编辑器工具专项设计》§8.4）：布局档。打开编辑器专用预览场景并进 Play，
    /// 由 <see cref="UIPreviewDriver"/> 实例化目标 prefab；保存 prefab 触发热重载。
    /// 退出 Play 后自动回到进入预览前的场景。
    /// </summary>
    [InitializeOnLoad]
    public static class UIPagePreview
    {
        public const string ScenePath = "Assets/LiteGame/Editor/UITools/Preview/UIPreview.unity";
        private const string ReturnSceneKey = "LiteGame.UIPreview.ReturnScene";

        static UIPagePreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>
        /// 打开目标 prefab 的布局预览；<paramref name="deviceProfileId"/> 为空＝跟随预览（全屏/默认安全区），
        /// <paramref name="locale"/> 为空＝静态文案保持作者文本（不加载语言表）。
        /// 返回 false 表示用户取消或目标非法。
        /// </summary>
        public static bool Open(string prefabPath, string deviceProfileId = null, string locale = null)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("UI 预览", "请先退出当前 Play，再打开单页预览。", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(prefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                EditorUtility.DisplayDialog("UI 预览", "目标 prefab 不存在：\n" + prefabPath, "OK");
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            EnsureScene();
            var active = SceneManager.GetActiveScene();
            if (!string.Equals(active.path, ScenePath, StringComparison.Ordinal))
            {
                SessionState.SetString(ReturnSceneKey, active.path);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            ApplyPreviewOptions(deviceProfileId, locale);
            EditorPrefs.SetString(UIPreviewDriver.TargetPrefabKey, prefabPath);
            EditorApplication.isPlaying = true;
            return true;
        }

        private static void ApplyPreviewOptions(string deviceProfileId, string locale)
        {
            SessionState.SetInt(UIPreviewDriver.OptionsRevisionKey, 0);
            SessionState.SetInt(UIPreviewDriver.OptionsAppliedKey, 0);
            SessionState.SetBool(UIPreviewDriver.HideOverlayKey, false);
            SessionState.SetFloat(UIPreviewDriver.FontScaleKey, 1f);
            SessionState.SetString(UIPreviewDriver.LocaleKey, locale ?? string.Empty);
            SessionState.SetString(UIPreviewDriver.LocaleStatusKey,
                string.IsNullOrEmpty(locale) ? "off" : "pending");
            SessionState.SetString(UIPreviewDriver.LocaleDataDirKey, string.Empty);
            if (UIPreviewGameView.TryGetSize(out int originalWidth, out int originalHeight))
            {
                SessionState.SetInt(UIPreviewDriver.OriginalGameViewWidthKey, originalWidth);
                SessionState.SetInt(UIPreviewDriver.OriginalGameViewHeightKey, originalHeight);
            }
            else
            {
                SessionState.SetInt(UIPreviewDriver.OriginalGameViewWidthKey, 0);
                SessionState.SetInt(UIPreviewDriver.OriginalGameViewHeightKey, 0);
            }

            UIDeviceProfileDocument document = null;
            try
            {
                document = UIDeviceProfileCatalog.Load();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UIPreview] 设备档案不可用：" + exception.Message);
            }
            SessionState.SetString(UIPreviewDriver.DeviceProfileChoicesKey,
                document != null ? BuildDeviceChoices(document) : string.Empty);

            if (document != null && !string.IsNullOrEmpty(deviceProfileId)
                && UIDeviceProfileCatalog.TryGet(document, deviceProfileId, out UIDeviceProfile profile))
            {
                SessionState.SetString(UIPreviewDriver.DeviceProfileIdKey, profile.Id);
                SessionState.SetInt(UIPreviewDriver.DeviceWidthKey, profile.Width);
                SessionState.SetInt(UIPreviewDriver.DeviceHeightKey, profile.Height);
                SessionState.SetFloat(UIPreviewDriver.InsetLeftKey, profile.SafeInsets?.Left ?? 0f);
                SessionState.SetFloat(UIPreviewDriver.InsetTopKey, profile.SafeInsets?.Top ?? 0f);
                SessionState.SetFloat(UIPreviewDriver.InsetRightKey, profile.SafeInsets?.Right ?? 0f);
                SessionState.SetFloat(UIPreviewDriver.InsetBottomKey, profile.SafeInsets?.Bottom ?? 0f);
                if (!UIPreviewGameView.TrySetSize(profile.Width, profile.Height))
                    Debug.LogWarning("[UIPreview] GameView 分辨率切换失败（降级为仅模拟安全区）：" + profile.Id);
                return;
            }
            if (document != null && !string.IsNullOrEmpty(deviceProfileId))
                Debug.LogWarning("[UIPreview] 设备档不存在，回退跟随预览：" + deviceProfileId);

            SessionState.SetString(UIPreviewDriver.DeviceProfileIdKey, string.Empty);
            SessionState.SetInt(UIPreviewDriver.DeviceWidthKey, 0);
            SessionState.SetInt(UIPreviewDriver.DeviceHeightKey, 0);
            SessionState.SetFloat(UIPreviewDriver.InsetLeftKey, 0f);
            SessionState.SetFloat(UIPreviewDriver.InsetTopKey, 0f);
            SessionState.SetFloat(UIPreviewDriver.InsetRightKey, 0f);
            SessionState.SetFloat(UIPreviewDriver.InsetBottomKey, 0f);
        }

        private static string BuildDeviceChoices(UIDeviceProfileDocument document)
        {
            return string.Join(";", UIDeviceProfileCatalog.MatrixProfiles(document).Select(profile =>
                string.Join("|", profile.Id, profile.Width, profile.Height,
                    (profile.SafeInsets?.Left ?? 0f).ToString(CultureInfo.InvariantCulture),
                    (profile.SafeInsets?.Top ?? 0f).ToString(CultureInfo.InvariantCulture),
                    (profile.SafeInsets?.Right ?? 0f).ToString(CultureInfo.InvariantCulture),
                    (profile.SafeInsets?.Bottom ?? 0f).ToString(CultureInfo.InvariantCulture))));
        }

        private static void EnsureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            EnsureFolder(Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));

            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                EditorSceneManager.SetActiveScene(scene);

                var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 1f);
                camera.orthographic = true;

                var host = new GameObject("UiPreview");
                host.AddComponent<UIPreviewDriver>();

                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            RestoreGameViewSize();
            string returnScene = SessionState.GetString(ReturnSceneKey, string.Empty);
            if (string.IsNullOrEmpty(returnScene)) return;
            SessionState.EraseString(ReturnSceneKey);

            var active = SceneManager.GetActiveScene();
            if (!string.Equals(active.path, ScenePath, StringComparison.Ordinal)) return;
            if (!File.Exists(returnScene)) return;
            EditorSceneManager.OpenScene(returnScene, OpenSceneMode.Single);
        }

        private static void RestoreGameViewSize()
        {
            int width = SessionState.GetInt(UIPreviewDriver.OriginalGameViewWidthKey, 0);
            int height = SessionState.GetInt(UIPreviewDriver.OriginalGameViewHeightKey, 0);
            SessionState.SetInt(UIPreviewDriver.OriginalGameViewWidthKey, 0);
            SessionState.SetInt(UIPreviewDriver.OriginalGameViewHeightKey, 0);
            if (width > 0 && height > 0) UIPreviewGameView.TrySetSize(width, height);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>保存 prefab 即热重载当前预览实例（只在 Play 内、且导入路径正是目标时触发）。</summary>
    internal sealed class UIPreviewHotReload : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!EditorApplication.isPlaying) return;
            string target = EditorPrefs.GetString(UIPreviewDriver.TargetPrefabKey, string.Empty);
            if (string.IsNullOrEmpty(target)) return;
            if (!importedAssets.Any(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase))) return;

            var driver = UnityEngine.Object.FindAnyObjectByType<UIPreviewDriver>();
            if (driver != null) driver.Reload();
        }
    }
}
