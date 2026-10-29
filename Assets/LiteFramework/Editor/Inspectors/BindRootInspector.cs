//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using UnityEditor;
using UnityEngine;

namespace LiteCodeGen.EditorTools
{
    [CustomEditor(typeof(BindRoot))]
    public sealed class BindRootInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            var root = (BindRoot)target;
            if (root == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("生成工具(子节点上的 BindNode 标记会成为类字段)", EditorStyles.boldLabel);

            // 输出目录:可直接把文件夹拖到下方
            var folderProp = serializedObject.FindProperty("ScriptsFolder");
            var currentFolder = folderProp != null ? folderProp.stringValue : string.Empty;

            var folderObj = EditorGUILayout.ObjectField(
                new GUIContent("输出目录(可拖文件夹)"),
                LoadFolderAsset(currentFolder),
                typeof(DefaultAsset),
                false);

            if (folderObj != null)
            {
                var path = AssetDatabase.GetAssetPath(folderObj);
                if (AssetDatabase.IsValidFolder(path) && folderProp != null && path != currentFolder)
                {
                    folderProp.stringValue = path;
                }
            }

            EditorGUILayout.Space(6);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(root.ScriptName));
            if (GUILayout.Button("① 生成代码", GUILayout.Height(34)))
            {
                BindGenerator.Generate(root.gameObject);
            }

            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("② 回填引用(重新绑定,不生成)", GUILayout.Height(28)))
            {
                FillNow(root);
            }

            EditorGUILayout.Space(4);

            if (GUILayout.Button("为所有直接子物体添加 BindNode 标记", GUILayout.Height(24)))
            {
                AddBindToDirectChildren(root);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static DefaultAsset LoadFolderAsset(string assetsFolder)
        {
            if (string.IsNullOrEmpty(assetsFolder))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<DefaultAsset>(assetsFolder);
        }

        private static void FillNow(BindRoot root)
        {
            var go = root.gameObject;
            var ns = root.LastGeneratedNamespace;
            var className = root.LastGeneratedClassName;

            if (string.IsNullOrEmpty(className))
            {
                Debug.LogWarning("[LiteCodeGen] 还没生成过代码(LastGeneratedClassName 为空),请先点『生成代码』。");
                return;
            }

            string error;
            bool ok;

            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                ok = BindSerializer.TryFillPrefabAsset(
                    AssetDatabase.GetAssetPath(go),
                    BindGenerator.BuildAncestorChain(go.transform),
                    ns, className, out error);
            }
            else
            {
                ok = BindSerializer.TryFill(go, ns, className, out error);
            }

            if (ok)
            {
                Debug.Log("[LiteCodeGen] 回填完成:" + className);
            }
            else
            {
                Debug.LogWarning("[LiteCodeGen] " + error);
            }
        }

        private static void AddBindToDirectChildren(BindRoot root)
        {
            var count = 0;
            for (var i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i);
                if (child.GetComponent<BindNode>() == null)
                {
                    Undo.AddComponent<BindNode>(child.gameObject);
                    count++;
                }
            }

            Debug.Log("[LiteCodeGen] 已为 " + count + " 个子物体添加 BindNode 标记");
        }
    }
}
