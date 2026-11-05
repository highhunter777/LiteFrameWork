//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiteCodeGen.EditorTools
{
    /// <summary>
    /// 把根节点下所有 BindNode 标记解析出的引用,写回目标组件的序列化字段。
    /// 幂等:可重复执行;新加的标记补齐,移除的标记不再处理。
    /// </summary>
    public static class BindSerializer
    {
        /// <summary>
        /// 将生成的类组件挂到根节点(缺则 AddComponent),并把绑定引用回填。
        /// </summary>
        public static bool TryFill(GameObject root, string namespaceName, string className, out string error)
        {
            error = string.Empty;
            if (root == null)
            {
                error = "根节点为空";
                return false;
            }

            var type = FindScriptType(namespaceName, className);
            if (type == null)
            {
                error = string.Format("找不到已编译的脚本类型 '{0}.{1}'(请确认脚本已生成且编译通过)",
                    string.IsNullOrEmpty(namespaceName) ? "(无命名空间)" : namespaceName, className);
                return false;
            }

            Component comp = root.GetComponent(type);
            if (comp == null)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    error = string.Format("类型 '{0}' 不是 MonoBehaviour,无法挂到根节点", type.FullName);
                    return false;
                }

                try
                {
                    comp = root.AddComponent(type);
                }
                catch (Exception e)
                {
                    error = "AddComponent 失败:" + e.Message;
                    return false;
                }
            }

            var errors = new List<string>();
            var marks = BindSearch.Search(root.transform, errors);

            var serializedObject = new SerializedObject(comp);
            var any = false;
            foreach (var mark in marks)
            {
                var prop = serializedObject.FindProperty(mark.MemberName);
                if (prop == null)
                {
                    errors.Add(string.Format("组件上找不到字段 '{0}'——很可能脚本还没编译或命名空间/类名不一致", mark.MemberName));
                    continue;
                }

                var target = ResolveObjectReference(mark);
                if (target == null)
                {
                    errors.Add(string.Format("节点 '{0}' 解析目标组件失败(类型 {1})", mark.MemberName, mark.TypeFullName));
                    continue;
                }

                prop.objectReferenceValue = target;
                any = true;
            }

            if (any)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            if (errors.Count > 0)
            {
                error = string.Join("\n", errors.ToArray());
                return false;
            }

            EditorUtility.SetDirty(root);
            return true;
        }

        /// <summary>按命名空间+类名在整个 AppDomain 里找已编译类型。</summary>
        public static Type FindScriptType(string namespaceName, string className)
        {
            if (string.IsNullOrEmpty(className))
            {
                return null;
            }

            var fullName = string.IsNullOrEmpty(namespaceName) ? className : namespaceName + "." + className;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                Type t = null;
                try
                {
                    t = assembly.GetType(fullName);
                }
                catch
                {
                    // 部分程序集在编辑器里 GetType 可能抛异常,忽略继续
                }

                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// 对 prefab 资产执行回填(通过 LoadPrefabContents 读写,安全且不破坏场景实例)。
        /// ancestorNames 用于定位资产内部的嵌套根;传 null 表示 prefab 根本身就是目标根。
        /// </summary>
        public static bool TryFillPrefabAsset(string prefabPath, string[] ancestorNames, string ns, string className,
            out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(prefabPath))
            {
                error = "prefab 路径为空";
                return false;
            }

            var contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (contentsRoot == null)
            {
                error = "加载 prefab 失败:" + prefabPath;
                return false;
            }

            try
            {
                var targetRoot = ResolveRootByChain(contentsRoot.transform, ancestorNames);
                if (targetRoot == null)
                {
                    error = "prefab 内找不到目标根节点:" + prefabPath;
                    return false;
                }

                return TryFill(targetRoot.gameObject, ns, className, out error);
            }
            finally
            {
                // 2022.3 没有 PrefabUtility.SavePrefabContents,用 SaveAsPrefabAsset + Unload 组合
                PrefabUtility.SaveAsPrefabAsset(contentsRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }
        }

        private static Transform ResolveRootByChain(Transform top, string[] chain)
        {
            if (top == null)
            {
                return null;
            }

            Transform t = top;
            if (chain != null && chain.Length > 0)
            {
                if (t.name != chain[0])
                {
                    return null;
                }

                for (var i = 1; i < chain.Length && t != null; i++)
                {
                    t = FindChildByName(t, chain[i]);
                }
            }

            return t;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static Object ResolveObjectReference(BindMark mark)
        {
            var comp = mark.Bind.ResolveTarget();
            if (comp != null)
            {
                return comp;
            }

            // 手动指定绑定为 GameObject 类型时,引用节点自身
            var typeName = mark.Bind.ResolvedTypeName;
            if (typeName == "GameObject" || typeName == "UnityEngine.GameObject")
            {
                return mark.Bind.gameObject;
            }

            // 组件解析失败交给上层报错,避免把错误类型塞进字段
            return null;
        }
    }
}
