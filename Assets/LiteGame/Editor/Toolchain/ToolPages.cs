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

    public sealed class ManifestErrorPage : EditorToolPageBase
    {
        private readonly string _manifestPath;
        private readonly IReadOnlyList<string> _errors;
        private readonly Action _reload;

        public ManifestErrorPage(string manifestPath, IReadOnlyList<string> errors, Action reload)
            : base(new GUIContent("清单错误"))
        {
            _manifestPath = manifestPath;
            _errors = errors;
            _reload = reload;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox("工具清单不可用，Hub 不会显示空目录。", MessageType.Error);
            EditorGUILayout.SelectableLabel(_manifestPath, EditorStyles.textField, GUILayout.Height(20));
            for (int i = 0; i < _errors.Count; i++)
            {
                EditorGUILayout.LabelField("• " + _errors[i], EditorStyles.wordWrappedLabel);
            }
            if (GUILayout.Button("重新加载清单", GUILayout.Height(24)))
            {
                if (_reload != null) _reload();
            }
        }
    }

    public sealed class ErrorToolPage : EditorToolPageBase
    {
        private readonly string _message;

        public ErrorToolPage(string title, string message)
            : base(new GUIContent(title))
        {
            _message = message;
        }

        public override void Draw()
        {
            EditorGUILayout.HelpBox(_message, MessageType.Error);
        }
    }
}
