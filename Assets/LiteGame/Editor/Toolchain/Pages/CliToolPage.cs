using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    public sealed class CliToolPage : EditorToolPageBase
    {
        private readonly EditorToolDescriptor _descriptor;

        public CliToolPage(EditorToolDescriptor descriptor)
            : base(new GUIContent(descriptor.Title))
        {
            _descriptor = descriptor;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox(_descriptor.Tool.summary, MessageType.Info);
            if (_descriptor.NeedsConfirmation)
            {
                EditorGUILayout.HelpBox("该命令会写入：" + _descriptor.WritesLabel, MessageType.Warning);
            }

            string command = _descriptor.EntryText;
            EditorGUILayout.SelectableLabel(command, EditorStyles.textField, GUILayout.Height(20));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制命令", GUILayout.Width(90)))
                {
                    ToolchainEditorUtility.CopyCommand(command);
                }
                if (GUILayout.Button("打开文档", GUILayout.Width(90)))
                {
                    ToolchainEditorUtility.OpenDocument(_descriptor.Tool.docs);
                }
            }
        }
    }
}
