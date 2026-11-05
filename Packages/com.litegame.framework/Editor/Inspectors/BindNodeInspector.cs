//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    [CustomEditor(typeof(BindNode))]
    public sealed class BindNodeInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var bind = (BindNode)target;
            if (bind == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var commentProp = serializedObject.FindProperty("Comment");
            var customTypeProp = serializedObject.FindProperty("CustomTypeName");
            var bindNameProp = serializedObject.FindProperty("BindName");

            // 成员名提示
            EditorGUILayout.HelpBox("该节点将生成为成员: public <类型> " + bind.gameObject.name + ";", MessageType.None);

            EditorGUILayout.PropertyField(commentProp, new GUIContent("备注(可选,写入代码注释)"));
            EditorGUILayout.PropertyField(bindNameProp, new GUIContent("索引名(受控 API 用,留空不进运行期索引)"));
            EditorGUILayout.Space(4);

            // ------- 绑定类型选择 -------
            var components = new List<Component>();
            bind.GetComponents(components);

            var options = new List<string> { "自动(默认,优先识别 Button/Text/Image 等)" };
            var optionFullNames = new List<string> { string.Empty };

            foreach (var c in components)
            {
                if (c == null)
                {
                    continue;
                }

                var t = c.GetType();
                if (typeof(BindNode).IsAssignableFrom(t) || typeof(BindRoot).IsAssignableFrom(t) ||
                    t == typeof(Transform) || t == typeof(RectTransform))
                {
                    continue; // 自身组件不参与候选
                }

                options.Add(t.Name);
                optionFullNames.Add(t.FullName);
            }

            options.Add("GameObject(引用整节点)");
            optionFullNames.Add("GameObject");

            var autoName = bind.AutoTypeName;
            var current = bind.CustomTypeName;
            var selectedIndex = 0;
            for (var i = 1; i < optionFullNames.Count; i++)
            {
                if (optionFullNames[i] == current)
                {
                    selectedIndex = i;
                    break;
                }
            }

            var newIndex = EditorGUILayout.Popup("绑定类型", selectedIndex, options.ToArray());

            if (newIndex != selectedIndex)
            {
                customTypeProp.stringValue = optionFullNames[newIndex];
            }

            if (string.IsNullOrEmpty(current))
            {
                EditorGUILayout.LabelField("自动识别为: " + autoName);
            }
            else
            {
                var exist = bind.GetComponent(current) != null || current == "GameObject";
                if (!exist)
                {
                    EditorGUILayout.HelpBox("当前节点上没有该组件,生成后回填会失败", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("类型说明:", EditorStyles.miniLabel);
            if (GUILayout.Button("生成代码", GUILayout.Height(30)))
            {
                var rootBind = FindRoot(bind);
                if (rootBind == null)
                {
                    Debug.LogError("[LiteCodeGen] 找不到父级 BindRoot,请把根节点挂上 BindRoot");
                }
                else
                {
                    BindGenerator.Generate(rootBind.gameObject);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static BindRoot FindRoot(BindNode bind)
        {
            var t = bind.transform;
            while (t != null)
            {
                var root = t.GetComponent<BindRoot>();
                if (root != null)
                {
                    return root;
                }

                t = t.parent;
            }

            return null;
        }
    }
}
