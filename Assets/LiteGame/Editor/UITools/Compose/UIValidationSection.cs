using LiteGame.Editor.UITools.Binding;
using LiteGame.Editor.UITools.Validation;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UITools.Compose
{
    /// <summary>
    /// 校验分区（《UI编辑器工具专项设计》§5"校验/生成"）：同一校验核心 <see cref="UIValidator.Run(UIValidationContext)"/> 的窗口入口——**窄档**只查当前 prefa。
    /// </summary>
    public sealed class UIValidationSection
    {
        private UIReport _report;
        private Vector2 _scroll;
        private bool _fullScan;

        public void Draw(UIComposeTarget target)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(target == null))
                {
                    if (GUILayout.Button("校验本页（窄档）", GUILayout.ExpandWidth(false)))
                    {
                        _fullScan = false;
                        _report = RunNarrow(target);
                    }
                }
                if (GUILayout.Button("校验全部资产（全量档，较慢）", GUILayout.ExpandWidth(false)))
                {
                    _fullScan = true;
                    _report = UIValidator.Run();
                }
            }

            if (_report == null)
            {
                EditorGUILayout.HelpBox("未跑校验。窄档＝只验本 prefab 的资产面规则；全量档＝源码面 + 全部资产。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"{(_fullScan ? "全量档" : "窄档")}：问题 {_report.Issues.Count} 条（Error {_report.ErrorCount} / Warning {_report.WarningCount}）",
                _report.Passed ? EditorStyles.miniBoldLabel : EditorStyles.miniBoldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var issue in _report.Issues)
            {
                var style = issue.Severity == UISeverity.Error ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel;
                EditorGUILayout.LabelField(issue.ToString(), style);
                bool locatable = target != null && !string.IsNullOrEmpty(issue.Node)
                                 && string.Equals(issue.Asset, target.PrefabPath, System.StringComparison.Ordinal);
                if (!locatable) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16f);
                    if (GUILayout.Button("定位节点", EditorStyles.miniButton, GUILayout.Width(80f)))
                        Locate(target, issue.Node);
                }
            }
            EditorGUILayout.EndScrollView();

            foreach (var family in _report.Families)
            {
                if (family.Implemented) continue;
                EditorGUILayout.LabelField($"待接入规则族：{family.Family}（未跑——不表示通过）", EditorStyles.miniLabel);
            }
        }

        /// <summary>窄档：源码面置空、资产面＝本 prefab（只跑资产面规则）。</summary>
        private static UIReport RunNarrow(UIComposeTarget target)
        {
            var context = new UIValidationContext(UIValidator.ProjectRoot,
                sourceDirs: new string[0],
                assetPaths: new[] { target.PrefabPath });
            return UIValidator.Run(context);
        }

        private static void Locate(UIComposeTarget target, string nodePath)
        {
            var node = UIComposeTarget.FindNodeForReport(target.DisplayRoot, nodePath);
            if (node == null) return;
            Selection.activeGameObject = node.gameObject;
            EditorGUIUtility.PingObject(node.gameObject);
        }
    }
}
