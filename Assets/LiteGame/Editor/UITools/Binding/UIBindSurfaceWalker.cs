using System;
using System.Collections.Generic;
using LiteCodeGen;
using LiteCodeGen.EditorTools;
using UnityEngine;

namespace LiteGame.Editor.UITools.Binding
{
    /// <summary>
    /// 绑定面遍历单源（绑定 hash/插槽发现）：一次深度优先枚举页面/模板的全部节点，并标记绑定根、Clone_ 子树、 嵌套模板实例与模板内部件。
    /// </summary>
    public sealed class UISurfaceNode
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

    public static class UIBindSurfaceWalker
    {
        public static List<UISurfaceNode> Walk(GameObject root)
        {
            var nodes = new List<UISurfaceNode>();
            foreach (BindSurfaceNode node in BindSurfaceWalk.Walk(root))
            {
                nodes.Add(new UISurfaceNode
                {
                    Node = node.Node,
                    Path = node.Path,
                    IsRoot = node.IsRoot,
                    InCloneScope = node.InCloneScope,
                    InsideNestedBindRoot = node.InsideNestedBindRoot,
                    InsideTemplate = node.InsideTemplate,
                    Bind = node.Bind,
                    BindRoot = node.BindRoot,
                    TemplateAssetPath = node.TemplateAssetPath,
                    TemplateName = node.TemplateName,
                });
            }
            return nodes;
        }
    }
}
