using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 通道层权重的推进与回收（《动画模块专项设计》§5 淡入淡出、§12"混合尾部必须有固定上限"）：
    /// 基础层尾部淡出、上层通道当前淡入、落位回收、通道释放后的收尾。
    ///
    /// **归属**：这里只有"权重怎么走、谁该被回收"，提交/截断计数/诊断归后端本体——
    /// 拆开是因为变化理由不同（淡化曲线 vs 提交编排）。
    /// **时长 0 = 瞬时落位**（显式传入的语义：测试与"直接切"）：不做除法、不读任何常量——
    /// 每通道用自己的 <see cref="ChannelState.BlendSeconds"/>（构造时分发的显式值）。
    /// </summary>
    internal static class ChannelBlendAdvance
    {
        /// <summary>推进本通道的层权重（每帧一次，Channels 各自调用；Evaluate 仍只由后端单次触发）。</summary>
        public static void Advance(ChannelState ch, AnimationLayerGraph graph, float dt)
        {
            if (ch.IsBase)
            {
                if (!ch.Tail.IsValid) return;
                ch.TailWeight = ch.BlendSeconds > 0f
                    ? Mathf.Max(0f, ch.TailWeight - dt / ch.BlendSeconds)
                    : 0f;
                graph.SetWeight(ch.TailLayer, ch.TailWeight);
                if (ch.TailWeight <= 0f) ch.DestroyTail(graph);           // 淡化完成 → 节点回基线
                return;
            }

            ch.CurrentWeight = ch.BlendSeconds > 0f
                ? Mathf.MoveTowards(ch.CurrentWeight, ch.TargetWeight, dt / ch.BlendSeconds)
                : ch.TargetWeight;
            if (ch.Current.IsValid) graph.SetWeight(ch.CurrentLayer, ch.CurrentWeight);

            if (ch.CurrentWeight >= 1f) ch.DestroyTail(graph);            // 新节点完全接管：旧的落位销毁

            // 通道已释放且淡出结束：销毁当前，节点回基线
            if (!ch.Active && ch.CurrentWeight <= 0f && ch.Current.IsValid) ch.DestroyCurrent(graph);
        }
    }
}