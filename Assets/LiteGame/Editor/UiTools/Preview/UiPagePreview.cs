using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>
    /// 单页预览入口（《UI编辑器工具专项设计》§8.4）：布局档。打开编辑器专用预览场景并进 Play，
    /// 由 <see cref="UiPreviewDriver"/> 实例化目标 prefab；保存 prefab 触发热重载。
    /// 退出 Play 后自动回到进入预览前的场景。
    /// </summary>
    [InitializeOnLoad]
    public static class UiPagePreview
    {
        public const string ScenePath = "Assets/LiteGame/Editor/UiTools/Preview/UiPreview.unity";
        private const string ReturnSceneKey = "LiteGame.UiPreview.ReturnScene";

        static UiPagePreview()
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
            EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, prefabPath);
            EditorApplication.isPlaying = true;
            return true;
        }

        private static void ApplyPreviewOptions(string deviceProfileId, string locale)
        {
            SessionState.SetInt(UiPreviewDriver.OptionsRevisionKey, 0);
            SessionState.SetInt(UiPreviewDriver.OptionsAppliedKey, 0);
            SessionState.SetBool(UiPreviewDriver.HideOverlayKey, false);
            SessionState.SetFloat(UiPreviewDriver.FontScaleKey, 1f);
            SessionState.SetString(UiPreviewDriver.LocaleKey, locale ?? string.Empty);
            SessionState.SetString(UiPreviewDriver.LocaleStatusKey,
                string.IsNullOrEmpty(locale) ? "off" : "pending");
            SessionState.SetString(UiPreviewDriver.LocaleDataDirKey, string.Empty);
            if (UiPreviewGameView.TryGetSize(out int originalWidth, out int originalHeight))
            {
                SessionState.SetInt(UiPreviewDriver.OriginalGameViewWidthKey, originalWidth);
                SessionState.SetInt(UiPreviewDriver.OriginalGameViewHeightKey, originalHeight);
            }
            else
            {
                SessionState.SetInt(UiPreviewDriver.OriginalGameViewWidthKey, 0);
                SessionState.SetInt(UiPreviewDriver.OriginalGameViewHeightKey, 0);
            }

            UiDeviceProfileDocument document = null;
            try
            {
                document = UiDeviceProfileCatalog.Load();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UiPreview] 设备档案不可用：" + exception.Message);
            }
            SessionState.SetString(UiPreviewDriver.DeviceProfileChoicesKey,
                document != null ? BuildDeviceChoices(document) : string.Empty);

            if (document != null && !string.IsNullOrEmpty(deviceProfileId)
                && UiDeviceProfileCatalog.TryGet(document, deviceProfileId, out UiDeviceProfile profile))
            {
                SessionState.SetString(UiPreviewDriver.DeviceProfileIdKey, profile.Id);
                SessionState.SetInt(UiPreviewDriver.DeviceWidthKey, profile.Width);
                SessionState.SetInt(UiPreviewDriver.DeviceHeightKey, profile.Height);
                SessionState.SetFloat(UiPreviewDriver.InsetLeftKey, profile.SafeInsets?.Left ?? 0f);
                SessionState.SetFloat(UiPreviewDriver.InsetTopKey, profile.SafeInsets?.Top ?? 0f);
                SessionState.SetFloat(UiPreviewDriver.InsetRightKey, profile.SafeInsets?.Right ?? 0f);
                SessionState.SetFloat(UiPreviewDriver.InsetBottomKey, profile.SafeInsets?.Bottom ?? 0f);
                if (!UiPreviewGameView.TrySetSize(profile.Width, profile.Height))
                    Debug.LogWarning("[UiPreview] GameView 分辨率切换失败（降级为仅模拟安全区）：" + profile.Id);
                return;
            }
            if (document != null && !string.IsNullOrEmpty(deviceProfileId))
                Debug.LogWarning("[UiPreview] 设备档不存在，回退跟随预览：" + deviceProfileId);

            SessionState.SetString(UiPreviewDriver.DeviceProfileIdKey, string.Empty);
            SessionState.SetInt(UiPreviewDriver.DeviceWidthKey, 0);
            SessionState.SetInt(UiPreviewDriver.DeviceHeightKey, 0);
            SessionState.SetFloat(UiPreviewDriver.InsetLeftKey, 0f);
            SessionState.SetFloat(UiPreviewDriver.InsetTopKey, 0f);
            SessionState.SetFloat(UiPreviewDriver.InsetRightKey, 0f);
            SessionState.SetFloat(UiPreviewDriver.InsetBottomKey, 0f);
        }

        private static string BuildDeviceChoices(UiDeviceProfileDocument document)
        {
            return string.Join(";", UiDeviceProfileCatalog.MatrixProfiles(document).Select(profile =>
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
                host.AddComponent<UiPreviewDriver>();

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
            int width = SessionState.GetInt(UiPreviewDriver.OriginalGameViewWidthKey, 0);
            int height = SessionState.GetInt(UiPreviewDriver.OriginalGameViewHeightKey, 0);
            SessionState.SetInt(UiPreviewDriver.OriginalGameViewWidthKey, 0);
            SessionState.SetInt(UiPreviewDriver.OriginalGameViewHeightKey, 0);
            if (width > 0 && height > 0) UiPreviewGameView.TrySetSize(width, height);
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
    internal sealed class UiPreviewHotReload : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!EditorApplication.isPlaying) return;
            string target = EditorPrefs.GetString(UiPreviewDriver.TargetPrefabKey, string.Empty);
            if (string.IsNullOrEmpty(target)) return;
            if (!importedAssets.Any(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase))) return;

            var driver = UnityEngine.Object.FindAnyObjectByType<UiPreviewDriver>();
            if (driver != null) driver.Reload();
        }
    }
}
