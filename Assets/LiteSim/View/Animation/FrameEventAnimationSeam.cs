using LiteFramework.Animation;

namespace LiteSim.View.Animation
{
    /// <summary>
    /// 帧事件 → 角色动画请求的**接缝**（《动画模块专项设计》§8"表现事件"）。
    ///
    /// **接在哪**：事件由 <c>RollbackSim.OnFrameEvents</c> 在**逻辑帧边界**交付（接线见 <c>BattleContext.AttachView</c>），
    /// 经 <c>SimView.OnFrameEvents</c> 的**静默门**（回滚重放/和解段不重播一次性副作用）后放行。实现者接在
    /// 静默门之后（<c>SimView.EventSink</c>），把事件语义映射成 <c>CharacterAnimationPlayer.Play</c> /
    /// <c>PlayBlend</c> 请求——播放器只解决视觉通道归属，**不改 Sim**（§8"骨骼后端不直接引用这些服务，
    /// 更不能调用 Sim 扣血或改变库存"）。
    ///
    /// **本批只落接缝形状**（用户裁定：Fire/Reload/Hit/Death 的真实事件驱动消费归角色垂直切片 G2）。实现者要满足：
    /// ① 事件主体 → 实体槽位 → 该实体的播放器（槽位是 Sim 的稳定寻址，与 <c>SimView</c> 视图槽位同源）；
    /// ② 语义与通道**只经 <see cref="FrameEventAnimationMap"/> 查表**（不散写映射）；
    /// ③ 本地预测与权威确认的合并规则（§8"不能先预测播放一次、收到确认又重复播放一次"）；
    /// ④ FullBody 接管前先 <c>Stop(Locomotion)</c>（死亡/受击整身接管，防"尸体站起来"回归——见后端注释）；
    /// ⑤ 换弹/瞄准**不由帧事件驱动**（它们是 Sim 动作阶段/输入事实，走快照消费，不属本接缝）。
    /// </summary>
    public interface IFrameEventAnimationConsumer
    {
        /// <summary>
        /// 静默门放行后的单个帧事件（**逻辑帧边界**，每逻辑帧一次批量调用；重放/和解段已被门挡掉）。
        /// 实现者按 <see cref="FrameEventAnimationMap"/> 映射后提交播放；未覆盖的事件类型不处理即可。
        /// </summary>
        void OnFrameEvent(in FrameEvent e);
    }

    /// <summary>
    /// 帧事件种类 → 动画语义/通道的**决策表单源**（§8；纯映射，无播放副作用、零引擎依赖）。
    ///
    /// 只有 Sim 真正产生的帧事件在表内（<c>Fire</c>/<c>Hit</c>/<c>Death</c>）；
    /// <c>Crit</c>/<c>Explosion</c> 为后续里程碑预留——**不预建映射与绑定**（出现消费者再加）。
    /// 表内语义与 <see cref="CombatGirlsAnimationProfile"/> 的通道登记必须一致（用例交叉校验，防止两处漂移）。
    /// </summary>
    public static class FrameEventAnimationMap
    {
        /// <summary>
        /// 映射帧事件 → （动画语义，通道）。未覆盖的事件类型返回 false（消费方按"不处理"继续）。
        /// 开火是**上半身叠加**（腿部继续走跑）；受击/死亡是**全身覆盖**。
        /// </summary>
        public static bool TryMap(FrameEventKind kind, out AnimationId id, out AnimationChannel channel)
        {
            switch (kind)
            {
                case FrameEventKind.Fire:
                    id = CharacterAnimationIds.Fire;
                    channel = AnimationChannel.UpperBody;
                    return true;
                case FrameEventKind.Hit:
                    id = CharacterAnimationIds.Hit;
                    channel = AnimationChannel.FullBody;
                    return true;
                case FrameEventKind.Death:
                    id = CharacterAnimationIds.Death;
                    channel = AnimationChannel.FullBody;
                    return true;
                default:
                    id = default;
                    channel = default;
                    return false;
            }
        }
    }
}