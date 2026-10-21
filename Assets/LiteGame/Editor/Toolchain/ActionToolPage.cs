using System;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    public sealed class ActionToolPage : EditorToolPageBase
    {
        private readonly EditorToolDescriptor _descriptor;
        private bool _dryRun;
        private ToolResult _result;
        private Func<ToolResult> _undo;
        private Vector2 _detailsScroll;

        public ActionToolPage(EditorToolDescriptor descriptor)
            : base(new GUIContent(descriptor.Title))
        {
            _descriptor = descriptor;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox(_descriptor.Tool.summary, MessageType.Info);
            if (_descriptor.NeedsConfirmation)
            {
                EditorGUILayout.HelpBox("该动作会写入：" + _descriptor.WritesLabel, MessageType.Warning);
            }

            if (_descriptor.SupportsDryRun)
            {
                _dryRun = EditorGUILayout.ToggleLeft("仅预览（不写盘）", _dryRun);
                if (_dryRun)
                {
                    EditorGUILayout.HelpBox("预览只记录计划变更，不创建或修改文件。", MessageType.None);
                }
            }

            string label = _dryRun ? "预览变更" : "执行 " + _descriptor.Title;
            if (GUILayout.Button(label, GUILayout.Height(28)))
            {
                Execute();
            }

            if (_result != null)
            {
                DrawResult();
            }
        }

        private void Execute()
        {
            if (!_dryRun && !ToolchainEditorUtility.ConfirmRun(_descriptor)) return;
            if (_descriptor.Execute == null)
            {
                _result = ToolResult.Failed("未注册动作行为。");
                _undo = null;
                return;
            }

            var request = new ToolRequest(
                _descriptor.Id,
                _dryRun,
                _descriptor.OutputPaths,
                "hub");
            try
            {
                _result = _descriptor.Execute(request);
            }
            catch (Exception exception)
            {
                _result = ToolResult.Failed(exception.GetType().Name + ": " + exception.Message, exception.ToString());
                Debug.LogException(exception);
            }

            _undo = _result != null ? _result.Undo : null;
            if (_result == null) return;
            if (_result.Ok) Debug.Log("[Toolchain] " + _descriptor.Id + "\n" + _result.Details);
            else Debug.LogError("[Toolchain] " + _descriptor.Id + "\n" + _result.Details);
        }

        private void DrawResult()
        {
            MessageType type = _result.Status == ToolStatus.Ok
                ? MessageType.Info
                : _result.Status == ToolStatus.Warning ? MessageType.Warning : MessageType.Error;
            EditorGUILayout.HelpBox(_result.DisplayStatus + "：" + _result.Summary, type);
            if (!string.IsNullOrEmpty(_result.FixHint))
            {
                EditorGUILayout.HelpBox(_result.FixHint, MessageType.Warning);
            }

            if (_result.Changes.Count > 0)
            {
                EditorGUILayout.LabelField(_result.DryRun ? "计划变更" : "写入变更", EditorStyles.boldLabel);
                foreach (ToolChange change in _result.Changes)
                {
                    string before = string.IsNullOrEmpty(change.BeforeHash) ? "(new)" : change.BeforeHash.Substring(0, 8);
                    string after = string.IsNullOrEmpty(change.AfterHash) ? "(none)" : change.AfterHash.Substring(0, 8);
                    string state = change.Written ? "已写入" : "将写入";
                    EditorGUILayout.LabelField(
                        "• [" + (change.Kind == ToolChangeKind.Created ? "新建" : "更新") + "] "
                        + change.Path + "  " + before + " → " + after + "  " + state,
                        EditorStyles.wordWrappedLabel);
                }
            }

            if (!string.IsNullOrEmpty(_result.Details))
            {
                _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll, GUILayout.MinHeight(120));
                EditorGUILayout.TextArea(_result.Details, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            if (_result.DryRun && _result.Changes.Count > 0)
            {
                if (GUILayout.Button("应用变更（关闭预览再执行）", GUILayout.Height(24)))
                {
                    _dryRun = false;
                    Execute();
                }
            }

            if (_undo != null)
            {
                if (GUILayout.Button("回滚上次写入", GUILayout.Height(24)))
                {
                    Func<ToolResult> undo = _undo;
                    _undo = null;
                    _result = undo();
                }
            }
        }
    }
}
