using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteGame
{
    /// <summary>标记索引构建器（路径 A，M4 §2.4）：实例化后扫根下全部 BindNode，按 BindName 登记。
    /// 空名跳过（该节点仅参与代码生成，不进运行期索引——设计允许）；重名抛（§3.4 fail-fast）。
    /// 控件目标 = BindNode.ResolveTarget()（自动检测组件优先级清单；CustomTypeName="GameObject" 回退节点 Transform）。</summary>
    public static class BindIndexBuilder
    {
        public static UIBindIndex Build(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var dict = new Dictionary<string, Component>(16);
            var nodes = root.GetComponentsInChildren<LiteCodeGen.BindNode>(true);
            foreach (var node in nodes)
            {
                var name = node.BindName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (dict.ContainsKey(name))
                    throw new InvalidOperationException($"BindName 重名:\"{name}\"（核对 prefab 标记，§3.4 fail-fast）");
                var target = node.ResolveTarget();
                dict[name] = target != null ? target : node.transform;
            }
            return new UIBindIndex(dict);
        }
    }
}
