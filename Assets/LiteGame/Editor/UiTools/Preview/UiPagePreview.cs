using System;
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

        /// <summary>打开目标 prefab 的布局预览；返回 false 表示用户取消或目标非法。</summary>
        public static bool Open(string prefabPath)
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

            EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, prefabPath);
            EditorApplication.isPlaying = true;
            return true;
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
            string returnScene = SessionState.GetString(ReturnSceneKey, string.Empty);
            if (string.IsNullOrEmpty(returnScene)) return;
            SessionState.EraseString(ReturnSceneKey);

            var active = SceneManager.GetActiveScene();
            if (!string.Equals(active.path, ScenePath, StringComparison.Ordinal)) return;
            if (!File.Exists(returnScene)) return;
            EditorSceneManager.OpenScene(returnScene, OpenSceneMode.Single);
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
