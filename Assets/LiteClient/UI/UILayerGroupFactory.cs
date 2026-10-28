using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteClient.UI
{
    /// <summary>
    /// UI 运行时根与层级组装配（从 <see cref="UIService"/> 外提的"装配"域，M3-6 §4.5）：
    /// 建 DDoL Root、按语义层名建组节点并分配深度、装输入底座（EventSystem）。
    /// 只做构建、不持有后续状态；实例由服务持有。
    /// </summary>
    internal static class UILayerGroupFactory
    {
        /// <summary>建根与全部层级组（顺序＝语义层索引，即 <see cref="PageDefinition.Layer"/>）。</summary>
        public static UILayerGroup[] Create(IReadOnlyList<string> groupNames, ILayerStrategy strategy, out Transform root)
        {
            if (groupNames == null) throw new ArgumentNullException(nameof(groupNames));
            root = new GameObject("[UIRoot]").transform;
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root.gameObject);   // EditMode 用例不得走 DDOL

            var groups = new UILayerGroup[groupNames.Count];
            for (int i = 0; i < groupNames.Count; i++)
            {
                var node = new GameObject(groupNames[i]).transform;
                node.SetParent(root, false);
                groups[i] = new UILayerGroup(groupNames[i], (i + 1) * UILayerGroup.DepthStride, node,
                    strategy ?? new DefaultLayerStrategy());
            }
            UiInput.EnsureEventSystem();          // 输入底座（幂等；§7 基础设施例外见 UiInput 判据）
            return groups;
        }
    }
}
