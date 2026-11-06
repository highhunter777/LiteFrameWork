using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Toolchain
{
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
