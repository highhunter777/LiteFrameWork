namespace LiteView
{
    /// <summary>
    /// 命中反馈消费者（分发器注册面）：伤害数字为首个消费者；命中特效/音效/受击死亡动画随各自批注册。
    /// 消费时机＝逻辑帧边界、静默门之后（回滚重放/和解段已被门挡掉——一次性副作用不重播）。
    /// 实现者只读，不改 Sim。
    /// </summary>
    public interface IHitFeedbackConsumer
    {
        /// <summary>单个命中反馈事件（按注册序调用；未覆盖的事件类型不处理即可）。</summary>
        void OnHitFeedback(in HitFeedbackContext ctx);
    }

}
