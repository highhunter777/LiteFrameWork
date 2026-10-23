#if UNITY_EDITOR
using LiteClient.UI;
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
            Debug.Log("[UiPreview] 已加载：" + path);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(8f, 8f, 560f, 58f), GUIContent.none);
            GUI.Label(new Rect(18f, 14f, 540f, 20f),
                "UI 单页预览（布局档：无 Lua/数据）", GUI.skin.label);
            GUI.Label(new Rect(18f, 34f, 360f, 20f),
                string.IsNullOrEmpty(_loadedPath) ? "未指定目标" : _loadedPath, GUI.skin.label);
            if (GUI.Button(new Rect(388f, 30f, 80f, 22f), "重载"))
                Reload();
            if (GUI.Button(new Rect(474f, 30f, 84f, 22f), "退出预览"))
                EditorApplication.isPlaying = false;
        }
    }
}
#endif
