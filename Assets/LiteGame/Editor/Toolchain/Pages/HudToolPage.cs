using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
    /// <summary>运行内载体（DevHud）说明页：Hub 不执行、不打开，只交代启用方式与前置。</summary>
    public sealed class HudToolPage : EditorToolPageBase
    {
        private readonly EditorToolDescriptor _descriptor;

        public HudToolPage(EditorToolDescriptor descriptor)
            : base(new GUIContent(descriptor.Title))
        {
            _descriptor = descriptor;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox(_descriptor.Tool.summary, MessageType.Info);
            EditorGUILayout.LabelField("前置：" + _descriptor.RequiresLabel, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                EditorApplication.isPlaying
                    ? "当前在 Play 模式：游戏内按 F1 开关面板；Hub 只做说明，不代执行。"
                    : "运行内工具：由游戏进程自创建，Play 模式或开发包内按 F1 开关；Hub 与 CLI 不执行、不打开。",
                MessageType.None);
            if (GUILayout.Button("打开文档", GUILayout.Height(24)))
            {
                ToolchainEditorUtility.OpenDocument(_descriptor.Tool.docs);
            }
        }
    }
}
