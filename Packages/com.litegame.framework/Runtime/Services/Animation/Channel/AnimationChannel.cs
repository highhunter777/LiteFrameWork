namespace LiteFramework.Animation
{
    /// <summary>
    /// 播放通道（视觉分层归属——基础层/叠加层/覆盖层，按消费者配置使用）。
    /// 通道是**视觉归属**，不是业务优先级——业务优先级由 Sim/Driver 解释，播放器只解决谁占用通道。
    /// </summary>
    public enum AnimationChannel
    {
        /// <summary>基础层（底层持续姿态——默认动画所在，其余通道在其上叠加/覆盖）。</summary>
        Base = 0,
        /// <summary>叠加层（Mask 限定作用域的局部动作，叠在基础层之上——需后端构造出该层 Mask）。</summary>
        Overlay = 1,
        /// <summary>覆盖层（整体接管——动作期间压过基础层，释放即回到基础层当前姿态）。</summary>
        Override = 2,
    }
}
