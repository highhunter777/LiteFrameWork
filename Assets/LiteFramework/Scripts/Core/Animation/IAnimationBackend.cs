using System;

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

        /// <summary>
        /// 提交一次**同通道多片段按权重混合**（普通混合器：无 Mask、纯权重；权重由 Driver/消费者裁决，
        /// 后端只执行——§6"业务优先级由 Sim/Driver 解释"）。需要 <see cref="AnimationBackendCapabilities.ClipBlending"/>；
        /// 能力位缺失时由播放器显性拒绝（§4 不静默降级为单片段播放）。
        ///
        /// <b>原子性</b>（§6"不能只占一半"）：任何一条不合法（通道未知/条目数越界/绑定未知/权重非法/
        /// 起点或速度非法）→ **整组拒绝**，通道保持原播放、不建任何节点。
        /// 返回 false = 后端拒绝/执行失败（调用方把该 Handle 记为 Failed 终态）。
        ///
        /// **不产生 Completed**：混合集合没有单一结束边界（§5）——后端**不得**把混合通道纳入
        /// <see cref="Tick"/> 的完成掩码；它只能被替换/停止/释放收终态。
        /// </summary>
        bool TryPlayBlend(in AnimationResolvedBlend blend);

        /// <summary>
        /// **就地更新**当前混合播放的权重（§5"连续参数更新…按实际需要提供独立接口"）：
        /// 权重逐帧随速度/方向变化时走这里，**不要**每帧调 <see cref="TryPlayBlend"/>——后者是"换一次播放"，
        /// 每次都会建节点、换句柄、给旧播放收 Interrupted（连续调参不该表现为反复打断）。
        ///
        /// 仅当该通道当前节点确实是混合节点、权重数与其输入数一致、逐条有限且 ≥0、总和 &gt; 0 时生效；
        /// 否则返回 false 且**不改动现有播放**（调用方回退到 <see cref="TryPlayBlend"/> 重新提交）。
        /// 只改权重：节点、输入片段、相位、句柄都不动。
        /// </summary>
        bool TrySetBlendWeights(AnimationChannel channel, float[] weights);

        /// <summary>停止某通道的当前播放（幂等；通道无播放时返回 false）。</summary>
        bool TryStop(AnimationChannel channel);

        /// <summary>通道是否正被本后端占用。</summary>
        bool IsChannelActive(AnimationChannel channel);

        /// <summary>
        /// 采样推进（**唯一驱动入口**——§7"Graph Evaluate 只由一个驱动器调用"）。
        /// <b>单次驱动全部活跃通道</b>（不能在通道循环里逐通道调——那会让累计时间被重复推进）。
        /// 返回值 = 本次采样中**自然到达结束边界**的通道集合（播放器据此逐通道收 Completed）。
        /// 已缩放的 delta 由调用方给出；后端**不再乘 Speed/TimeScale**（§7 归属分离，避免重复缩放）。
        /// </summary>
        AnimationChannelMask Tick(float deltaSeconds);

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
        /// <summary>支持**同通道多片段按权重混合**（普通混合器 `AnimationMixerPlayable`：无 Mask、纯权重；
        /// 权重由调用方（Driver/消费者）裁决，后端只执行——§6"业务优先级由 Sim/Driver 解释"）。</summary>
        ClipBlending = 1 << 4,
    }
}
