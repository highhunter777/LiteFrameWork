using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放后端契约（《动画模块专项设计》§3"引擎后端：已接受的状态切换、参数与采样命令 →
    /// 姿态、引擎对象释放、受控表现标记"）。**框架 Core 只认这个接口**——Animator/PlayableGraph
    /// 等 Unity 类型留在后端实现里（§3"所有 Unity 类型留在 Unity/View/Shell 层"）。
    ///
    /// 契约（§5/§6/§9 对后端的约束）：
    /// - 提交**原子**：多通道请求要么整体取得所需通道，要么不动（§6"不能只占一半"）；
    /// - 后端执行失败必须回报（成为该 Handle 的 Failed 终态），不能静默留在旧姿态假装成功；
    /// - 底层层叠加混合尾部上限由实现承担（§12），播放器不替后端猜。
    /// </summary>
    public interface IAnimationBackend
    {
        /// <summary>后端能力标记（§4"不把不支持的能力静默降级"——播放器据此拒绝而非假装支持）。</summary>
        AnimationBackendCapabilities Capabilities { get; }

        /// <summary>
        /// 提交一次播放。
        /// 返回 false = 后端拒绝/执行失败（调用方把该 Handle 记为 Failed 终态）。
        /// </summary>
        bool TryPlay(in AnimationResolvedPlayback playback);

        /// <summary>停止某通道的当前播放（幂等；通道无播放时返回 false）。</summary>
        bool TryStop(AnimationChannel channel);

        /// <summary>通道是否正被本后端占用。</summary>
        bool IsChannelActive(AnimationChannel channel);

        /// <summary>
        /// 采样推进（唯一驱动入口——§7"Graph Evaluate 只由一个驱动器调用"）。
        /// 返回 true = 通道上的当前播放已自然到达结束边界（播放器据此收 Completed）。
        /// </summary>
        bool Tick(AnimationChannel channel, float deltaSeconds);

        /// <summary>释放后端持有的引擎对象/绑定（§9 销毁顺序的最后引擎步骤）。</summary>
        void Dispose();
    }

    /// <summary>后端能力标记（§4 Fallback：缺失能力要"拒绝、回退或保持"三选一，不能静默降级）。</summary>
    [Flags]
    public enum AnimationBackendCapabilities
    {
        None = 0,
        /// <summary>支持循环播放。</summary>
        Looping = 1 << 0,
        /// <summary>支持带起点的提交（BeginPlay 的 normalizedTime）。</summary>
        StartAtNormalized = 1 << 1,
        /// <summary>支持速度倍率。</summary>
        SpeedOverride = 1 << 2,
        /// <summary>支持通道叠加（上半身层）。</summary>
        LayeredChannels = 1 << 3,
    }

    /// <summary>
    /// 单通道的播放槽（§6"首版每通道最多一个待提交请求和一个当前逻辑播放"）。
    /// 没有队列——新请求替换旧请求，旧请求立即取得 Interrupted 终态。
    /// </summary>
    internal sealed class AnimationChannelSlot
    {
        public AnimationHandle Current;          // 当前逻辑播放（已提交或提交中）
        public AnimationId CurrentId;
        public bool Loading;                     // 已接受但尚未提交成功
        public bool Active;

        /// <summary>替换当前播放：旧 Handle 得 Interrupted 终态（旧待提交 Handle 一并终止——§6）。</summary>
        public bool HasCurrent => Active && Current.IsValid;
    }
}
