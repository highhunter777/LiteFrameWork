using System;
using System.Collections.Generic;
using LiteCodeGen;
using LiteCodeGen.EditorTools;
using UnityEngine;

namespace LiteGame.Editor.UiTools.Binding
{
    /// <summary>
    /// 绑定面遍历单源（hash/契约/校验域）：一次深度优先枚举页面/模板的全部节点，并标记绑定根、Clone_ 子树、
    /// 嵌套模板实例与模板内部件。绑定 hash、页面契约生成与校验共用本遍历，禁止各写一份。
    ///
    /// 遍历核心在 <see cref="BindSurfaceWalk"/>（LiteCodeGen.Editor——与生成器域共用一份「哪些节点属于
    /// 本页/本模板」语义）；本类是 LiteGame 侧的公共面（消费域 API 稳定不变），只做节点映射。
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
            foreach (BindSurfaceNode node in BindSurfaceWalk.Walk(root))
            {
                nodes.Add(new UiSurfaceNode
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
