using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    public sealed class WindowLauncherPage : EditorToolPageBase
    {
        private readonly EditorToolDescriptor _descriptor;

        public WindowLauncherPage(EditorToolDescriptor descriptor)
            : base(new GUIContent(descriptor.Title))
        {
            _descriptor = descriptor;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox("该工具保持独立窗口；Hub 只负责发现与打开。", MessageType.Info);
            if (_descriptor.OpenWindow == null)
            {
                EditorGUILayout.HelpBox("未注册窗口打开行为。", MessageType.Error);
                return;
            }
            if (GUILayout.Button("打开 " + _descriptor.Title, GUILayout.Height(28)))
            {
                _descriptor.OpenWindow();
            }
        }
    }
}
