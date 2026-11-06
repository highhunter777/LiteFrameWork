using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    public sealed class DocsToolPage : EditorToolPageBase
    {
        private readonly EditorToolDescriptor _descriptor;

        public DocsToolPage(EditorToolDescriptor descriptor)
            : base(new GUIContent(descriptor.Title))
        {
            _descriptor = descriptor;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox(_descriptor.Tool.summary, MessageType.Info);
            if (GUILayout.Button("打开文档", GUILayout.Height(24)))
            {
                ToolchainEditorUtility.OpenDocument(_descriptor.Tool.docs);
            }
        }
    }
}
