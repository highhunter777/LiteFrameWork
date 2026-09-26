using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 层级组（M4 §2.1/§2.2）：组内栈 + Depth 分配（分配规则经 <see cref="ILayerStrategy"/> 注入）。
    /// 组间深度以 BaseDepth 步进 100 隔离。
    /// U1-③（§6.2 统一排序）：**开序即深序**——排序按当前打开顺序计算，每次入栈、移除、
    /// BringToFront、复用后统一重算（<see cref="RecalculateOrders"/>）；废止旧"递增槽位 + 100 回卷"
    /// ——组容量不足（栈超 <see cref="DepthStride"/>）由调用方在入栈前拒绝并诊断，禁止悄悄复用 order。
    /// </summary>
    public sealed class UILayerGroup
    {
        public const int DepthStride = 100;

        public string Name { get; }
        public int BaseDepth { get; }
        public Transform Root { get; }                  // UIRoot 下的组节点（实例化挂点）
        public UIStack Stack { get; } = new UIStack();
        private readonly ILayerStrategy _layerStrategy;

        public UILayerGroup(string name, int baseDepth, Transform root, ILayerStrategy layerStrategy)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BaseDepth = baseDepth;
            Root = root ? root : throw new ArgumentNullException(nameof(root));
            _layerStrategy = layerStrategy ?? throw new ArgumentNullException(nameof(layerStrategy));
        }

        /// <summary>组容量已满（§6.2：容量不足拒绝并诊断——打开请求在入栈前被拒，不回卷复用 order）。</summary>
        public bool IsFull => Stack.Count >= DepthStride;

        /// <summary>统一重算组内排序：开序即深序（栈序号 → 策略解析 order）。
        /// 每次入栈 / 移除 / BringToFront / 复用后调用（§6.2）；O(栈深)——栈深有界（≤100）。</summary>
        public void RecalculateOrders()
        {
            var open = Stack.Open;
            for (int i = 0; i < open.Count; i++)
                open[i].AssignDepth(_layerStrategy.ResolveSortingOrder(this, i));
        }
    }
}
