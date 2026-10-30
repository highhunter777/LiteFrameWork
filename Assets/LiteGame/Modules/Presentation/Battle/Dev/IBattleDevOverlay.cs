using System;
using LiteFramework;
using LiteView;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// 对局开发叠加层端口（M3-6b 3b）：爆头区域常驻绘制 + 爆头自动验证件（替换输入源）。
    /// 由开发装配实现，`BattleViewRig` 在装配/采样前/拆除三个相位调用；发布形态无实现 = 零开销。
    /// </summary>
    public interface IBattleDevOverlay
    {
        /// <summary>进局装配（register = 命中反馈消费者登记口——自瞄验证件要吃命中事件）。</summary>
        void Attach(BattleContext context, IInputService input, Action<IHitFeedbackConsumer> register);

        /// <summary>渲染帧采样**之前**的驱动（自瞄必须早于采样换源）。</summary>
        void TickPreSample();

        /// <summary>拆除（先还原输入源再弃引用）。</summary>
        void Detach();
    }
}
