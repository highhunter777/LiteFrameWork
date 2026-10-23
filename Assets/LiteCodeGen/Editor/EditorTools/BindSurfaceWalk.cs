//------------------------------------------------------------
// LiteCodeGen - 绑定面遍历核心（单源）
//------------------------------------------------------------
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace LiteCodeGen.EditorTools
{
    /// <summary>
    /// 绑定面遍历的一个节点（核心标记：哪些节点属于本页/本模板）。
    /// </summary>
    public sealed class BindSurfaceNode
    {
        public Transform Node;
        /// <summary>相对根的路径（根自身为空串；段间 '/' 连接——与各消费域既有路径口径逐字节一致）。</summary>
        public string Path;
        public bool IsRoot;
        /// <summary>在 <c>Clone_</c> 子树内（运行时克隆源不属于本页接口面）。</summary>
        public bool InCloneScope;
        /// <summary>在嵌套 <c>BindRoot</c> 子树内（含该根自身——独立生成单位，不入父列表）。</summary>
        public bool InsideNestedBindRoot;
        /// <summary>在模板（prefab 实例）内部（实例根的子树——属模板自己的生成单位）。</summary>
        public bool InsideTemplate;
        /// <summary>prefab 实例根的资产路径（非实例节点为 null；实例根自身非 InsideTemplate）。</summary>
        public string TemplateAssetPath;
        public string TemplateName;
        public BindNode Bind;
        public BindRoot BindRoot;
    }

    /// <summary>
    /// 绑定面遍历**单源**：一次深度优先枚举根下全部节点并标记克隆作用域、嵌套生成单位与模板边界。
    /// 生成器域（<see cref="BindSearch"/> 的标记收集）与 hash/契约/校验域（LiteGame 侧遍历包装）共用本核心，
    /// 「哪些节点属于本页/本模板」只此一份语义——禁止各域再写一份遍历。
    ///
    /// 语义：子节点上的 <c>BindRoot</c> 是独立生成单位（其子树含根标记 <see cref="BindSurfaceNode.InsideNestedBindRoot"/>）；
    /// <c>Clone_</c> 前缀进入克隆作用域；prefab 实例根标记模板边界（其子树 <see cref="BindSurfaceNode.InsideTemplate"/>），
    /// 非 <c>Tmp_</c> 嵌套 prefab 同样按不透明边界处理（是否告警由消费域决定）。
    /// </summary>
    public static class BindSurfaceWalk
    {
        public static List<BindSurfaceNode> Walk(GameObject root)
        {
            var nodes = new List<BindSurfaceNode>();
            if (root == null) return nodes;
            WalkNode(root.transform, null, string.Empty, false, false, false, nodes);
            return nodes;
        }

        private static void WalkNode(Transform node, Transform parent, string parentPath, bool inClone,
            bool insideNestedBindRoot, bool insideTemplate, List<BindSurfaceNode> nodes)
        {
            bool isRoot = parent == null;
            bool clone = inClone || (!isRoot && node.name.StartsWith("Clone_", StringComparison.Ordinal));
            bool nestedBindRoot = insideNestedBindRoot || (!isRoot && node.GetComponent<BindRoot>() != null);
            string path = isRoot ? string.Empty : (parentPath.Length == 0 ? node.name : parentPath + "/" + node.name);

            string templateAssetPath = null;
            string templateName = null;
            if (!isRoot && IsPrefabInstanceRoot(node))
            {
                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(node.gameObject);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    templateAssetPath = assetPath.Replace('\\', '/');
                    templateName = System.IO.Path.GetFileNameWithoutExtension(templateAssetPath);
                }
            }

            nodes.Add(new BindSurfaceNode
            {
                Node = node,
                Path = path,
                IsRoot = isRoot,
                InCloneScope = clone,
                InsideNestedBindRoot = nestedBindRoot,
                InsideTemplate = insideTemplate,
                TemplateAssetPath = templateAssetPath,
                TemplateName = templateName,
                Bind = node.GetComponent<BindNode>(),
                BindRoot = node.GetComponent<BindRoot>(),
            });

            bool childrenInsideTemplate = insideTemplate || templateAssetPath != null;
            for (int i = 0; i < node.childCount; i++)
                WalkNode(node.GetChild(i), node, path, clone, nestedBindRoot, childrenInsideTemplate, nodes);
        }

        private static bool IsPrefabInstanceRoot(Transform node)
            => PrefabUtility.IsPartOfPrefabInstance(node)
               && PrefabUtility.GetNearestPrefabInstanceRoot(node.gameObject) == node.gameObject;
    }
}
