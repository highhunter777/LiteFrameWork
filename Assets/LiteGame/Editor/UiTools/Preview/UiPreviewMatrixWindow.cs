using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>
    /// 截图矩阵入口（P2-3 B3 / AC3）：选页/设备档/语言/字体缩放 → 后台 Play 状态机逐组合截图。
    /// 产物固定落 `Artifacts/PreviewMatrix/`（.gitignore 排除，不入库）。
    /// </summary>
    public sealed class UiPreviewMatrixWindow : EditorWindow
    {
        private const string WindowTitle = "UI 截图矩阵";

        private string _pagePath;
        private UiDeviceProfileDocument _document;
        private readonly HashSet<string> _profiles = new HashSet<string>();
        private readonly HashSet<string> _locales = new HashSet<string>();
        private readonly HashSet<float> _scales = new HashSet<float>();
        private string _lastResult;
        private string _status;
        private Vector2 _scroll;

        [MenuItem("LiteGame/UI/预览/截图矩阵")]
        public static void Open()
        {
            GetWindow<UiPreviewMatrixWindow>(WindowTitle).Show();
        }

        private void OnEnable()
        {
            ReloadProfiles();
            if (string.IsNullOrEmpty(_pagePath)) _pagePath = SelectedPrefabPath();
        }

        private void ReloadProfiles()
        {
            try
            {
                _document = UiDeviceProfileCatalog.Load();
            }
            catch (System.Exception exception)
            {
                _document = null;
                _lastResult = "设备档案装载失败：" + exception.Message;
                return;
            }
            foreach (UiDeviceProfile profile in UiDeviceProfileCatalog.MatrixProfiles(_document))
                _profiles.Add(profile.Id);
            foreach (string locale in UiPreviewMatrix.DefaultLocales) _locales.Add(locale);
            foreach (float scale in UiPreviewMatrix.DefaultFontScales) _scales.Add(scale);
        }

        private void OnInspectorUpdate()
        {
            string next = UiPreviewMatrixRunner.CurrentStatus;
            if (!string.Equals(next, _status, System.StringComparison.Ordinal))
            {
                _status = next;
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (_document == null)
            {
                EditorGUILayout.HelpBox(_lastResult ?? "设备档案不可用", MessageType.Error);
                if (GUILayout.Button("重新装载档案")) ReloadProfiles();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _pagePath = EditorGUILayout.TextField("目标页", _pagePath);
                if (GUILayout.Button("使用选中 prefab", GUILayout.Width(120f)))
                    _pagePath = SelectedPrefabPath();
            }
            if (string.IsNullOrEmpty(_pagePath))
                EditorGUILayout.HelpBox("目标页为空：填 prefab 路径/页面名，或在 Project 选中页面 prefab。", MessageType.Warning);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(320f));
            EditorGUILayout.LabelField("设备档（入矩阵）", EditorStyles.boldLabel);
            foreach (UiDeviceProfile profile in UiDeviceProfileCatalog.MatrixProfiles(_document))
                DrawToggle(_profiles, profile.Id, $"{profile.Id}  {profile.Name}（{profile.Width}×{profile.Height}）");

            EditorGUILayout.LabelField("语言", EditorStyles.boldLabel);
            foreach (string locale in UiPreviewMatrix.DefaultLocales)
                DrawToggle(_locales, locale, locale == "pseudo" ? "pseudo（伪本地化长文本）" : locale);

            EditorGUILayout.LabelField("字体缩放", EditorStyles.boldLabel);
            foreach (float scale in UiPreviewMatrix.DefaultFontScales)
                DrawToggle(_scales, scale, scale.ToString("0.##"));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("产物", "Artifacts/PreviewMatrix/<页>/（.gitignore 已排除，不入库）");
            using (new EditorGUI.DisabledScope(UiPreviewMatrixRunner.IsRunning))
            {
                if (GUILayout.Button("开始矩阵（Play 内逐组合截图）", GUILayout.Height(28f)))
                    StartRun();
            }
            if (UiPreviewMatrixRunner.IsRunning && GUILayout.Button("中断（保留已完成组合）"))
            {
                UiPreviewMatrixRunner.Cancel();
                _lastResult = "已请求中断";
            }

            EditorGUILayout.Space();
            string live = UiPreviewMatrixRunner.CurrentStatus;
            string display = string.IsNullOrEmpty(_lastResult) ? live : _lastResult + "\n当前：" + live;
            EditorGUILayout.HelpBox(display, MessageType.Info);
        }

        private void StartRun()
        {
            if (_profiles.Count == 0 || _locales.Count == 0 || _scales.Count == 0)
            {
                _lastResult = "至少选择一个设备档、一种语言和一个字体缩放档";
                return;
            }
            var options = new UiPreviewMatrixOptions
            {
                PagePath = _pagePath,
                ProfileIds = _profiles.OrderBy(id => id, System.StringComparer.Ordinal).ToList(),
                Locales = _locales.OrderBy(id => id, System.StringComparer.Ordinal).ToList(),
                FontScales = _scales.OrderBy(value => value).ToList(),
            };
            _lastResult = UiPreviewMatrixRunner.Start(options);
            _status = UiPreviewMatrixRunner.CurrentStatus;
        }

        private static void DrawToggle(ISet<string> set, string value, string label)
        {
            bool on = set.Contains(value);
            bool next = EditorGUILayout.ToggleLeft(label, on);
            if (next == on) return;
            if (next) set.Add(value);
            else set.Remove(value);
        }

        private static void DrawToggle(ISet<float> set, float value, string label)
        {
            bool on = set.Contains(value);
            bool next = EditorGUILayout.ToggleLeft(label, on);
            if (next == on) return;
            if (next) set.Add(value);
            else set.Remove(value);
        }

        private static string SelectedPrefabPath()
        {
            var selected = Selection.activeObject as GameObject;
            string path = selected != null ? AssetDatabase.GetAssetPath(selected) : null;
            return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }
    }
}
