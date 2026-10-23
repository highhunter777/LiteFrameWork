using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteClient.UI;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 页面登记窗口（工程，`ui.page-register`）：选草稿页 → id/layer/全屏/缓存策略 → 契约门槛校验 →
    /// 写表 + Lua + 代码生成链 + 清单状态 + 契约刷新。只做输入与结果呈现。
    /// </summary>
    public sealed class UiPageRegisterWindow : EditorWindow
    {
        private const string Title = "页面登记（工程）";
        private static readonly string[] LayerLabels = { "0 Bottom", "1 Window", "2 Top", "3 System" };

        private UiPageManifest _manifest;
        private List<UiPageManifestEntry> _drafts = new List<UiPageManifestEntry>();
        private string _error;
        private int _selected;
        private int _id = -1;
        private int _layer = 1;
        private bool _fullScreen = true;
        private int _cache;
        private Vector2 _scroll;
        private UiPageRegisterResult _result;
        private string _selectedName;

        public static void Open() => Open(null);

        public static void Open(string prefillPageName)
        {
            var window = GetWindow<UiPageRegisterWindow>(Title);
            window.minSize = new Vector2(500f, 380f);
            window._selectedName = prefillPageName;
            window.Show();
        }

        private void OnEnable() => ReloadDrafts();

        private void ReloadDrafts()
        {
            _error = null;
            _drafts = new List<UiPageManifestEntry>();
            try
            {
                _manifest = UiPageManifestStore.LoadIfExists();
            }
            catch (Exception exception)
            {
                _error = "页面清单不可读：" + exception.Message;
                return;
            }

            if (_manifest?.Pages == null) return;
            _drafts = _manifest.Pages
                .Where(page => page != null
                               && (page.Forms == null || page.Forms.Count == 0)
                               && UiPageStatusRules.TryParse(page.Status, out UiPageStatus status)
                               && status == UiPageStatus.Draft)
                .OrderBy(page => page.Name, StringComparer.Ordinal)
                .ToList();
            if (!string.IsNullOrEmpty(_selectedName))
            {
                int index = _drafts.FindIndex(page => page.Name == _selectedName);
                if (index >= 0) _selected = index;
            }
            _selected = _drafts.Count == 0 ? 0 : Mathf.Clamp(_selected, 0, _drafts.Count - 1);
            if (_id < 0 && _drafts.Count > 0) _id = UiPageTableHelper.SuggestId();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新草稿", GUILayout.Width(80f))) ReloadDrafts();
                EditorGUILayout.LabelField("草稿页 " + _drafts.Count, EditorStyles.miniLabel);
            }

            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
                return;
            }
            if (_drafts.Count == 0)
            {
                EditorGUILayout.HelpBox("没有草稿页——先在工作台「起页」并生成草稿契约。", MessageType.Info);
                return;
            }

            string[] labels = _drafts.Select(page => page.Name).ToArray();
            _selected = EditorGUILayout.Popup("草稿页", _selected, labels);
            UiPageManifestEntry selected = _drafts[_selected];
            EditorGUILayout.LabelField("prefab", selected.PrefabPath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("草稿契约", File.Exists(FullPath(UiContractPaths.ForPage(selected.Name))) ? "已存在" : "缺失（先跑 ui.contract）",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _id = EditorGUILayout.IntField("id", _id);
                if (GUILayout.Button("建议值", GUILayout.Width(70f))) _id = UiPageTableHelper.SuggestId();
            }
            _layer = EditorGUILayout.Popup("layer", _layer, LayerLabels);
            _fullScreen = EditorGUILayout.Toggle("全屏", _fullScreen);
            _cache = EditorGUILayout.Popup("缓存策略", _cache, Enum.GetNames(typeof(UICacheStrategy)));

            using (new EditorGUI.DisabledScope(_id <= 0))
            {
                if (GUILayout.Button("登记", GUILayout.Height(26f)))
                {
                    var request = new UiPageRegisterRequest
                    {
                        PageName = selected.Name,
                        Id = _id,
                        Layer = _layer,
                        FullScreen = _fullScreen,
                        CacheStrategy = Enum.GetNames(typeof(UICacheStrategy))[_cache],
                    };
                    _result = UiPageRegistration.Register(request);
                    if (_result.Ok) ReloadDrafts();
                }
            }

            EditorGUILayout.Space();
            if (_result == null) return;
            EditorGUILayout.HelpBox(_result.Ok ? "登记完成：" + _result.FormId : _result.Error,
                _result.Ok ? MessageType.Info : MessageType.Error);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(120f));
            foreach (string line in _result.Log) EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            foreach (string warning in _result.Warnings) EditorGUILayout.LabelField("告警 " + warning, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private static string FullPath(string assetPath)
            => System.IO.Path.Combine(UiValidator.ProjectRoot,
                assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }
}
