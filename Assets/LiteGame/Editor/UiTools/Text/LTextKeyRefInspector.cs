using System;
using System.Collections.Generic;
using LiteClient;
using LiteClient.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Text
{
    /// <summary>
    /// `LTextKeyRef` 的编辑器面（《UI 编辑器工具链专项设计》§3.4）：key 下拉直接来自 `tbltext`，
    /// 并预览中/英文本；读取失败显式提示并给出重试，不伪造空表。
    /// </summary>
    [CustomEditor(typeof(LTextKeyRef))]
    public sealed class LTextKeyRefInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty key = serializedObject.FindProperty("Key");
            EditorGUILayout.PropertyField(key, new GUIContent("Key", "tbltext 文案键；只存 key，不含参数/复数"));

            UiLTextTable.TryGetRows(out IReadOnlyDictionary<string, cfg.ltext> rows, out string error);
            if (rows == null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
                if (GUILayout.Button("重试读取 tbltext")) UiLTextTable.Reload();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            string[] keys = UiLTextTable.Keys();
            string[] options = BuildOptions(keys);
            int current = Array.IndexOf(keys, key.stringValue);
            int selected = EditorGUILayout.Popup("选择 key", current >= 0 ? current + 1 : 0, options);
            if (selected != (current >= 0 ? current + 1 : 0))
                key.stringValue = selected == 0 ? string.Empty : keys[selected - 1];

            if (!string.IsNullOrEmpty(key.stringValue))
            {
                if (rows.TryGetValue(key.stringValue, out cfg.ltext row))
                {
                    EditorGUILayout.LabelField("zh-CN", row.Zh ?? string.Empty);
                    EditorGUILayout.LabelField("en", row.En ?? string.Empty);
                }
                else
                {
                    EditorGUILayout.HelpBox("key 不存在于 tbltext：" + key.stringValue, MessageType.Error);
                }
            }

            if (GUILayout.Button("重载 tbltext")) UiLTextTable.Reload();
            serializedObject.ApplyModifiedProperties();
        }

        private static string[] BuildOptions(string[] keys)
        {
            var options = new string[keys.Length + 1];
            options[0] = "(未选择)";
            for (int i = 0; i < keys.Length; i++) options[i + 1] = keys[i];
            return options;
        }
    }
}
