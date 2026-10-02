using System;

namespace LiteSim
{
    /// <summary>
    /// 60Hz 逻辑帧泵（与 <see cref="FrameDriver"/> 的分工）：
    /// - <see cref="FrameDriver"/>：**渲染帧驱动**，按真实 dt 追帧、上限 5 帧、超限丢余量——客户端正常路径；
    /// - <see cref="FramePump"/>：**定次驱动**，要几帧给几帧（1:1，不补不丢）。
    ///
    /// 为什么需要第二个：网络对跑/服务器权威步进/无头客户端是"每 tick 一逻辑帧"的定次形态，
    /// 用渲染帧驱动会因 dt 抖动时而补帧时而丢帧，**帧号与输入序列对不上**（实测：
    /// 无头客户端与服务器帧号漂移后，"服务器第 k 帧用的输入"在客户端是第 k+1 帧用的）。
    ///
    /// 每帧 Step 后回调（消费帧事件的唯一时机，同 FrameDriver 约定），随后清空事件缓冲。
    /// </summary>
    public sealed class FramePump
    {
        /// <summary>推进 <paramref name="steps"/> 个逻辑帧（steps ≤ 0 为无操作）。</summary>
        public void Step(int steps, SimWorldState s, in SimMapData map, SimInputFrame[] inputs,
            Action<SimWorldState> onLogicalFrame = null)
        {
            for (int i = 0; i < steps; i++)
            {
                SimStep.Step(s, map, inputs);
                if (onLogicalFrame != null) onLogicalFrame(s);
                s.Events.Clear();   // 帧内瞬态：消费后清
            }
        }
    }
}
