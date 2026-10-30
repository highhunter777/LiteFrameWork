using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 起页窗口（美术，`ui.page-scaffold`）：页面名 + 视觉基线 → prefab + 草稿清单；不写 Lua/表。
    /// 只做输入与结果呈现，判定/写面全在 <see cref="UiPageScaffold"/>。
    /// </summary>
    public sealed class UiPageScaffoldWindow : EditorWindow
    {
        private const string Title = "起页（美术）";

        private string _pageName = "Page_";
        private int _baseline;
        private bool _openStage = true;
        private Vector2 _scroll;
        private UiPageScaffold.ScaffoldResult _result;

        public static void Open() => Open(null);

        public static void Open(string prefillPageName)
        {
            var window = GetWindow<UiPageScaffoldWindow>(Title);
            window.minSize = new Vector2(460f, 320f);
            if (!string.IsNullOrEmpty(prefillPageName)) window._pageName = prefillPageName;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("从视觉基线复制为自包含 prefab，并写入页面清单草稿。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            _pageName = EditorGUILayout.TextField("页面名", _pageName);
            if (!UiPageScaffold.IsValidPageName(_pageName))
                EditorGUILayout.HelpBox("页面名须为 Page_<PascalCase>（ASCII 字母数字，首字母大写）。", MessageType.Warning);

            List<UiPageScaffold.UiPageBaseline> baselines = UiPageScaffold.Baselines.ToList();
            string[] labels = baselines.Select(item =>
                AssetDatabase.LoadAssetAtPath<GameObject>(item.Path) == null ? item.Label + "（缺失）" : item.Label).ToArray();
            _baseline = baselines.Count == 0 ? 0 : Mathf.Clamp(_baseline, 0, baselines.Count - 1);
            if (baselines.Count > 0) _baseline = EditorGUILayout.Popup("视觉基线", _baseline, labels);
            _openStage = EditorGUILayout.Toggle("完成后打开 Prefab Stage", _openStage);

            using (new EditorGUI.DisabledScope(!UiPageScaffold.IsValidPageName(_pageName) || baselines.Count == 0))
            {
                if (GUILayout.Button("创建页面", GUILayout.Height(26f)))
                {
                    string baseline = baselines.Count > 0 ? baselines[_baseline].Path : null;
                    _result = UiPageScaffold.Scaffold(_pageName, baseline, null, _openStage);
                    if (_result.Ok) _pageName = "Page_";
                }
            }

            EditorGUILayout.Space();
            DrawResult();
        }

        private void DrawResult()
        {
            if (_result == null) return;
            EditorGUILayout.HelpBox(_result.Ok ? "已创建：" + _result.PrefabPath : _result.Error,
                _result.Ok ? MessageType.Info : MessageType.Error);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(120f));
            foreach (string line in _result.Log) EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
