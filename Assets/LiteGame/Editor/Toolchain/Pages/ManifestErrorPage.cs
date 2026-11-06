using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
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
}
