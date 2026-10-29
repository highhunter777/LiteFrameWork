using System.Collections.Generic;
using LiteFramework.Animation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LiteView.Animation
{
    /// <summary>
    /// 通道上的一个节点（当前/尾部共用同一表示）：**单片段**（<see cref="AnimationClipPlayable"/>）或
    /// **同通道多片段混合**（普通 <see cref="AnimationMixerPlayable"/>：无 Mask、纯权重）。
    /// 两者都能直接作为分层混合器的输入（<see cref="Root"/>），因此通道机制（让位 / 淡入淡出 / 回收）
    /// 对两种形态一视同仁——这也是"普通混合器"不必再建一套通道状态的原因。
    /// </summary>
    internal struct ChannelNode
    {
        /// <summary>层混合器的输入源（单片段 = <see cref="Clip"/>；混合 = <see cref="Mixer"/>）。</summary>
        public Playable Root;
        public AnimationClipPlayable Clip;
        /// <summary>单片段节点的资产（时长/完成判据；混合节点为 null）。</summary>
        public AnimationClip ClipAsset;
        public AnimationMixerPlayable Mixer;
        /// <summary>混合节点的输入片段（固定容量 <see cref="AnimationProfile.MaxBlendSlots"/>；单片段节点不用）。</summary>
        public AnimationClipPlayable[] Inputs;
        public int InputCount;
        public bool IsBlend;

        public bool IsValid => Root.IsValid();

        /// <summary>建单片段节点：足部 IK 关（与 Sim 位移不叠加，后置批）、速度与归一化起点显式给。</summary>
        public static ChannelNode ClipNode(PlayableGraph graph, AnimationClip clip, float speed, float startNormalized)
        {
            AnimationClipPlayable cp = AnimationClipPlayable.Create(graph, clip);
            cp.SetApplyFootIK(false);
            cp.SetSpeed(speed);                                          // 速度归 SetSpeed，Tick 不再乘（§7）
            if (startNormalized > 0f) cp.SetTime(clip.length * startNormalized);   // 起点能力
            return new ChannelNode { Root = cp, Clip = cp, ClipAsset = clip };
        }

        /// <summary>建混合节点（普通混合器）：权重按 <paramref name="weightSum"/> 归一化（集合内部总和恒 1，
        /// 通道的层权重另行表达），输入共用同一 speed、起点按各自归一化位置对齐。</summary>
        public static ChannelNode BlendNode(PlayableGraph graph, in AnimationResolvedBlend blend,
            Dictionary<string, AnimationClip> clips, float weightSum)
        {
            int count = blend.SlotCount;
            AnimationMixerPlayable mixer = AnimationMixerPlayable.Create(graph, count);
            var inputs = new AnimationClipPlayable[AnimationProfile.MaxBlendSlots];
            for (int i = 0; i < count; i++)
            {
                AnimationClip clip = clips[blend.Bindings[i]];
                AnimationClipPlayable cp = AnimationClipPlayable.Create(graph, clip);
                cp.SetApplyFootIK(false);
                cp.SetSpeed(blend.Speed);
                if (blend.StartNormalized > 0f) cp.SetTime(clip.length * blend.StartNormalized);
                graph.Connect(cp, 0, mixer, i);
                mixer.SetInputWeight(i, blend.Weights[i] / weightSum);
                inputs[i] = cp;
            }
            return new ChannelNode { Root = mixer, Mixer = mixer, Inputs = inputs, InputCount = count, IsBlend = true };
        }

        /// <summary>销毁本节点（并把自己置空）：混合节点要**先销毁它的全部输入片段**
        /// （Unity 不会替你级联销毁）。节点所有权只在通道状态的回收口收束。</summary>
        public void Destroy()
        {
            if (IsValid)
            {
                if (IsBlend)
                {
                    for (int i = 0; i < InputCount; i++)
                        if (Inputs[i].IsValid()) Inputs[i].Destroy();
                    Mixer.Destroy();
                }
                else
                {
                    Clip.Destroy();
                }
            }
            this = default;
        }
    }

    /// <summary>
    /// 单通道运行态（§6"每通道最多一个当前播放"）：当前节点 + 至多一条混合尾部。
    /// 基础层的尾部在**上层**淡出（1→0）；上层通道的尾部在**下层**保持（权重 1）而
    /// 当前节点从 0 淡入——两种角色共用同一对（当前层 / 尾部层）槽位，节点数恒定。
    /// 断开连线与销毁节点在此收口（权重推进见 <see cref="ChannelBlendAdvance"/>）。
    /// </summary>
    internal sealed class ChannelState
    {
        public readonly AnimationChannel Channel;
        public readonly bool IsBase;
        public readonly int CurrentLayer;
        public readonly int TailLayer;
        /// <summary>本通道的淡入/淡出时长（秒；**构造时显式传入**，0 = 瞬时落位）——淡化数学只用它。</summary>
        public readonly float BlendSeconds;

        public ChannelNode Current;
        public ChannelNode Tail;

        public string Binding;
        /// <summary>结束边界判定状态（纯逻辑，见 <see cref="ClipCompletionTracker"/>；混合节点不用）。</summary>
        public ClipCompletionTracker Completion;
        public float Speed = 1f;

        public float CurrentWeight;
        public float TargetWeight;
        public float TailWeight;

        public bool Active;

        public ChannelState(AnimationChannel channel, bool isBase, int currentLayer, int tailLayer, float blendSeconds)
        {
            Channel = channel;
            IsBase = isBase;
            CurrentLayer = currentLayer;
            TailLayer = tailLayer;
            BlendSeconds = blendSeconds;
        }

        /// <summary>断开尾部连线并销毁它（权重归零、尾部清空——节点回基线）。</summary>
        public void DestroyTail(AnimationLayerGraph graph)
        {
            if (Tail.IsValid)
            {
                graph.Disconnect(TailLayer);
                graph.SetWeight(TailLayer, 0f);
                Tail.Destroy();
            }
            Tail = default;
            TailWeight = 0f;
        }

        /// <summary>断开当前连线并销毁它（权重归零、完成判定复位——节点回基线）。</summary>
        public void DestroyCurrent(AnimationLayerGraph graph)
        {
            if (Current.IsValid)
            {
                graph.Disconnect(CurrentLayer);
                graph.SetWeight(CurrentLayer, 0f);
                Current.Destroy();
            }
            Current = default;
            CurrentWeight = 0f;
            Completion.Completed = false;
        }
    }
}