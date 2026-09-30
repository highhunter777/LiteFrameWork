using LiteFramework.Animation;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 帧事件 → 角色动画请求的**接缝**（《动画模块专项设计》§8"表现事件"；《层次动画机设计》v0.5 五次裁决）。
    ///
    /// **接在哪**：事件由 <c>RollbackSim.OnFrameEvents</c> 在**逻辑帧边界**交付，经 <c>SimView.OnFrameEvents</c> 的
    /// **静默门**（回滚重放/和解段不重播一次性副作用）后放行。实现者接在静默门之后
    /// （<c>SimView.EventSink</c>），把事件路由成状态机迁移/播放请求——播放器只解决视觉通道归属，**不改 Sim**。
    ///
    /// **映射退役（v0.5 五次裁决）**：原 <c>FrameEventAnimationMap</c> 决策表已删除——"帧事件 → 语义/通道"
    /// 即状态机的转换条件（fire 事件进 Fire 系；语义/通道由战斗层装配期从 Profile **单源**解析，
    /// 构造期校验保留），不再有第二张手工同步的表。实现者要满足：
    /// ① 事件主体 → 实体槽位 → 该槽位的播放器/状态机（槽位是 Sim 的稳定寻址，与 <c>SimView</c> 视图槽位同源）；
    /// ② 本地预测与权威确认的合并规则（§8"不能先预测播放一次、收到确认又重复播放一次"——静默门是结构性保证）；
    /// ③ 全身接管前收本根通道（战斗根 OnLeave/移动根 OnLeave——事务序先停后播）；
    /// ④ 换弹/瞄准**不由帧事件驱动**（它们是 Sim 动作阶段/输入事实，走快照消费，不属本接缝）；
    /// ⑤ 未覆盖的事件类型不处理（Hit/Death 归批C——不预建无消费者）。
    /// </summary>
    public interface IFrameEventAnimationConsumer
    {
        /// <summary>
        /// 静默门放行后的单个帧事件（**逻辑帧边界**，每逻辑帧一次批量调用；重放/和解段已被门挡掉）。
        /// 实现者按各自状态机的转换条件路由后提交播放；未覆盖的事件类型不处理即可。
        /// </summary>
        void OnFrameEvent(in FrameEvent e);
    }
}
