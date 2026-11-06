namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放后端契约（引擎后端：已接受的状态切换、参数与采样命令 → 姿态、引擎对象释放、受控表现标记）。
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
        /// 提交一次**同通道多片段按权重混合**（普通混合器：无 Mask、纯权重；权重由 Driver/消费者裁决， 后端只执行——业务优先级由 Sim/Driver 解释）。
        /// </summary>
        bool TryPlayBlend(in AnimationResolvedBlend blend);

        /// <summary>
        /// **就地更新**当前混合播放的权重（连续参数更新按实际需要提供独立接口）： 权重逐帧随速度/方向变化时走这里，**不要**每帧调 <see cref="TryPlayBlend"/>——后者是"换一次播放"。
        /// </summary>
        bool TrySetBlendWeights(AnimationChannel channel, float[] weights);

        /// <summary>
        /// **就地更新**混合节点的播放倍率（步频同步——移动腰射：混合片段的原生步频 × 倍率 = 实际脚程， 免"走姿步频配跑速"的滑步）。
        /// </summary>
        bool TrySetBlendSpeed(AnimationChannel channel, float speedScale);

        /// <summary>
        /// **就地更新**混合节点的**逐输入净播放速率**（步幅对齐——调用方按"相位比 × 脚程换算"给出 各片段的最终倍率：步态片段相位锁定、非步态片段可给恒定 1 豁免换算）。
        /// </summary>
        bool TrySetBlendInputSpeeds(AnimationChannel channel, float[] speeds);

        /// <summary>停止某通道的当前播放（幂等；通道无播放时返回 false）。</summary>
        bool TryStop(AnimationChannel channel);

        /// <summary>通道是否正被本后端占用。</summary>
        bool IsChannelActive(AnimationChannel channel);

        /// <summary>按绑定键取片段时长（秒）；键不可解析返回 false。</summary>
        bool TryGetClipSeconds(string binding, out float seconds);

        /// <summary>当前活跃通道数（诊断）。</summary>
        int ActiveChannels { get; }

        /// <summary>当前 Playable 节点数（诊断；节点稳定性观测）。</summary>
        int PlayableCount { get; }

        /// <summary>清单中登记但片段引用缺失的键数（诊断/降级观测）。</summary>
        int MissingManifestClips { get; }

        /// <summary>未知绑定键数（诊断）。</summary>
        int UnknownBindings { get; }

        /// <summary>被截断的混合提交数（诊断）。</summary>
        int TruncatedBlends { get; }

        /// <summary>
        /// 采样推进（**唯一驱动入口**——Graph Evaluate 只由一个驱动器调用）。
        /// </summary>
        AnimationChannelMask Tick(float deltaSeconds);

        /// <summary>释放后端持有的引擎对象/绑定（销毁顺序的最后引擎步骤）。</summary>
        void Dispose();
    }
}
