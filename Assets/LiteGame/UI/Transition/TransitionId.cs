namespace LiteGame
{
    /// <summary>
    /// 转场阶段（《UI扩展能力设计》§1.5.2）：平铺三维足够——Replace 的"两组并发"在 In 阶段内部编排。
    /// **与 <see cref="UIFormState"/> 严格分离**（§1.5.1 硬纪律）：前者是表现阶段，后者是逻辑生命周期；
    /// 把转场态塞进七态机会污染池化/遮盖语义。
    /// </summary>
    public enum TransitionId
    {
        /// <summary>无事务。</summary>
        Idle,
        /// <summary>离场（Pop）。</summary>
        Out,
        /// <summary>入场（Push）/ 切换（Replace）。</summary>
        In,
    }

    /// <summary>转场模式（§1.5.3）：由调用语义推导，不给内容层自由度。</summary>
    public enum TransitionMode
    {
        /// <summary>只播 Incoming 入场；Outgoing 保持不动的 Active。</summary>
        Push,
        /// <summary>Incoming 入场**同时** Outgoing 离场（同组全屏互斥）。</summary>
        Replace,
        /// <summary>Outgoing 离场，下方界面露出。</summary>
        Pop,
    }

    /// <summary>转场结果分类（§6.3：结果区分 Completed/Skipped/TimedOut/Cancelled/Failed——
    /// 页面操作据此决定收尾语义；动效失败可降级为立即完成，但必须先复位）。</summary>
    public enum TransitionResultKind
    {
        /// <summary>表现正常播完。</summary>
        Completed = 0,

        /// <summary>请求被排队丢弃/忽略（目标已是当前 Incoming 等——未开始事务）。</summary>
        Skipped = 1,

        /// <summary>超过 MaxDuration 强制收尾（策略工作已被取消并复位）。</summary>
        TimedOut = 2,

        /// <summary>被权威取消（外部取消/关闭——预留：U1 的取消源为超时与关闭，外部 ct 随 U2 导航接）。</summary>
        Cancelled = 3,

        /// <summary>策略抛异常/异常完成。</summary>
        Failed = 4,
    }

    /// <summary>一次转场的结果（<see cref="UITransitionRunner.PlayAsync"/> 的返回；供调用方与 Lua 事件消费）。</summary>
    public sealed class TransitionOutcome
    {
        public TransitionMode Mode;
        public UIForm Outgoing;
        public UIForm Incoming;

        /// <summary>结果分类。</summary>
        public TransitionResultKind Kind;

        /// <summary>表现是否正常播完（= <see cref="Kind"/>==Completed；超时 / 策略异常 / 请求被丢或被忽略 = false）。</summary>
        public bool Completed;

        /// <summary>是否因超过 MaxDuration 被强制收尾（§1.5.4 规则④）。</summary>
        public bool TimedOut;
    }
}
