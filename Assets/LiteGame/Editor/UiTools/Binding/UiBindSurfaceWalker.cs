using System;
using System.Collections.Generic;
using System.IO;
using LiteCodeGen;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>
    /// 绑定面遍历单源：一次深度优先枚举页面/模板的全部节点，并标记绑定根、Clone_ 子树、
    /// 嵌套模板实例与模板内部件。绑定 hash、页面契约生成与校验共用本遍历，禁止各写一份。
    ///
    /// 语义与 <see cref="BindSearch"/> 对齐：子节点上的 `BindRoot` 是独立生成单位，其子树标记为
    /// <see cref="UiSurfaceNode.InsideNestedBindRoot"/>；嵌套 prefab 实例根标记为模板边界，内部件
    /// 标记为 <see cref="UiSurfaceNode.InsideTemplate"/>（契约不展开，hash 保留旧行为）。
    /// </summary>
    public sealed class UiSurfaceNode
    {
        public Transform Node;
        public string Path;
        public bool IsRoot;
        public bool InCloneScope;
        public bool InsideNestedBindRoot;
        public bool InsideTemplate;
        public BindNode Bind;
        public BindRoot BindRoot;
        public string TemplateAssetPath;
        public string TemplateName;
    }

    public static class UiBindSurfaceWalker
    {
        public static List<UiSurfaceNode> Walk(GameObject root)
        {
            var nodes = new List<UiSurfaceNode>();
            if (root == null) return nodes;
            WalkNode(root.transform, null, false, false, false, nodes);
            return nodes;
        }

        private static void WalkNode(Transform node, Transform parent, bool inClone,
            bool insideNestedBindRoot, bool insideTemplate, List<UiSurfaceNode> nodes)
        {
            bool isRoot = parent == null;
            bool clone = inClone || (!isRoot && node.name.StartsWith("Clone_", StringComparison.Ordinal));
            bool nestedBindRoot = insideNestedBindRoot || (!isRoot && node.GetComponent<BindRoot>() != null);

            string templateAssetPath = null;
            string templateName = null;
            if (!isRoot && IsPrefabInstanceRoot(node))
            {
                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(node.gameObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    templateAssetPath = assetPath.Replace('\\', '/');
                    templateName = Path.GetFileNameWithoutExtension(templateAssetPath);
                }
            }

            nodes.Add(new UiSurfaceNode
            {
                Node = node,
                Path = UiBindingRename.RelativePath(node),
                IsRoot = isRoot,
                InCloneScope = clone,
                InsideNestedBindRoot = nestedBindRoot,
                InsideTemplate = insideTemplate,
                Bind = node.GetComponent<BindNode>(),
                BindRoot = node.GetComponent<BindRoot>(),
                TemplateAssetPath = templateAssetPath,
                TemplateName = templateName,
            });

            bool childrenInsideTemplate = insideTemplate || templateAssetPath != null;
            for (int i = 0; i < node.childCount; i++)
                WalkNode(node.GetChild(i), node, clone, nestedBindRoot, childrenInsideTemplate, nodes);
        }

        private static bool IsPrefabInstanceRoot(Transform node)
            => PrefabUtility.IsPartOfPrefabInstance(node)
               && PrefabUtility.GetNearestPrefabInstanceRoot(node.gameObject) == node.gameObject;
    }
}
