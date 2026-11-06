namespace LiteGame
{
    /// <summary>
    /// 命中反馈消费者（分发器注册面）：伤害数字为首个消费者。
    /// </summary>
    public interface IHitFeedbackConsumer
    {
        /// <summary>单个命中反馈事件（按注册序调用；未覆盖的事件类型不处理即可）。</summary>
        void OnHitFeedback(in HitFeedbackContext ctx);
    }

}
