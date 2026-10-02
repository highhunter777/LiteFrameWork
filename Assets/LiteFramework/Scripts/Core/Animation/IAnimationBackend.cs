namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放后端契约（引擎后端：已接受的状态切换、参数与采样命令 →
    /// 姿态、引擎对象释放、受控表现标记）。**框架 Core 只认这个接口**——Animator/PlayableGraph
    /// 等 Unity 类型留在后端实现里。
    ///
    /// 契约：
    /// - 提交**原子**：多通道请求要么整体取得所需通道，要么不动（不能只占一半）；
    /// - 后端执行失败必须回报（成为该 Handle 的 Failed 终态），不能静默留在旧姿态假装成功；
    /// - 底层层叠加混合尾部上限由实现承担，播放器不替后端猜。
    /// </summary>
    public interface IAnimationBackend
    {
        /// <summary>后端能力标记（不把不支持的能力静默降级——播放器据此拒绝而非假装支持）。</summary>
        AnimationBackendCapabilities Capabilities { get; }

        /// <summary>
        /// 提交一次播放。
        /// 返回 false = 后端拒绝/执行失败（调用方把该 Handle 记为 Failed 终态）。
        /// </summary>
        bool TryPlay(in AnimationResolvedPlayback playback);

        /// <summary>
        /// 提交一次**同通道多片段按权重混合**（普通混合器：无 Mask、纯权重；权重由 Driver/消费者裁决，
        /// 后端只执行——业务优先级由 Sim/Driver 解释）。需要 <see cref="AnimationBackendCapabilities.ClipBlending"/>；
        /// 能力位缺失时由播放器显性拒绝（不静默降级为单片段播放）。
        ///
        /// <b>原子性</b>（不能只占一半）：任何一条不合法（通道未知/条目数越界/绑定未知/权重非法/
        /// 起点或速度非法）→ **整组拒绝**，通道保持原播放、不建任何节点。
        /// 返回 false = 后端拒绝/执行失败（调用方把该 Handle 记为 Failed 终态）。
        ///
        /// **不产生 Completed**：混合集合没有单一结束边界——后端**不得**把混合通道纳入
        /// <see cref="Tick"/> 的完成掩码；它只能被替换/停止/释放收终态。
        /// </summary>
        bool TryPlayBlend(in AnimationResolvedBlend blend);

        /// <summary>
        /// **就地更新**当前混合播放的权重（连续参数更新按实际需要提供独立接口）：
        /// 权重逐帧随速度/方向变化时走这里，**不要**每帧调 <see cref="TryPlayBlend"/>——后者是"换一次播放"，
        /// 每次都会建节点、换句柄、给旧播放收 Interrupted（连续调参不该表现为反复打断）。
        ///
        /// 仅当该通道当前节点确实是混合节点、权重数与其输入数一致、逐条有限且 ≥0、总和 &gt; 0 时生效；
        /// 否则返回 false 且**不改动现有播放**（调用方回退到 <see cref="TryPlayBlend"/> 重新提交）。
        /// 只改权重：节点、输入片段、相位、句柄都不动。
        /// </summary>
        bool TrySetBlendWeights(AnimationChannel channel, float[] weights);

        /// <summary>
        /// **就地更新**混合节点的播放倍率（步频同步——移动腰射：混合片段的原生步频 × 倍率 = 实际脚程，
        /// 免"走姿步频配跑速"的滑步）。倍率乘在混合器节点上，各输入片段的 Speed 不动（Playable 速度
        /// 沿图相乘，等效整体变速）。
        ///
        /// 仅当该通道当前节点确实是混合节点、倍率有限且 &gt; 0 时生效；否则返回 false 且**不改动现状**。
        /// 与 <see cref="TrySetBlendWeights"/> 同款"就地调参"纪律：节点、输入片段、相位、句柄都不动。
        /// </summary>
        bool TrySetBlendSpeed(AnimationChannel channel, float speedScale);

        /// <summary>停止某通道的当前播放（幂等；通道无播放时返回 false）。</summary>
        bool TryStop(AnimationChannel channel);

        /// <summary>通道是否正被本后端占用。</summary>
        bool IsChannelActive(AnimationChannel channel);

        /// <summary>
        /// 采样推进（**唯一驱动入口**——Graph Evaluate 只由一个驱动器调用）。
        /// <b>单次驱动全部活跃通道</b>（不能在通道循环里逐通道调——那会让累计时间被重复推进）。
        /// 返回值 = 本次采样中**自然到达结束边界**的通道集合（播放器据此逐通道收 Completed）。
        /// 已缩放的 delta 由调用方给出；后端**不乘 Speed/TimeScale**（归属分离，避免重复缩放）。
        /// </summary>
        AnimationChannelMask Tick(float deltaSeconds);

        /// <summary>释放后端持有的引擎对象/绑定（销毁顺序的最后引擎步骤）。</summary>
        void Dispose();
    }
}
