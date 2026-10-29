using System;
using LiteFramework;
using LiteView;
using LiteBattle;

namespace LiteGame
{
    /// <summary>
    /// 对局开发叠加层实现：爆头区域常驻绘制（只读预测态）+ 爆头自动验证件（替换输入源，验端到端链路）。
    /// 相位同拆分前：装配（Register 进命中分发）→ 采样前驱动（自瞄要早于本帧采样换源）→ 拆除（先还原输入源）。
    /// </summary>
    public sealed class DevBattleOverlay : IBattleDevOverlay
    {
        private BattleHeadshotDebugDrawer _headshotDebug;
        private AutoHeadshotRig _autoHeadshot;
        private IInputService _input;

        public void Attach(BattleContext context, IInputService input, Action<IHitFeedbackConsumer> register)
        {
            _input = input;
            _headshotDebug = new BattleHeadshotDebugDrawer(context.Sim, () => context.MatchRules);
            _autoHeadshot = new AutoHeadshotRig(context.Sim.State, context.LocalEntityId);
            register?.Invoke(_autoHeadshot);
        }

        public void TickPreSample()
        {
            _headshotDebug?.Tick();
            if (TestModeRuntime.Current.AutoHeadshot)
            {
                if (_autoHeadshot != null && !_autoHeadshot.Active) _autoHeadshot.Enable(_input);
            }
            else if (_autoHeadshot != null && _autoHeadshot.Active)
            {
                _autoHeadshot.Disable();            // 还原设备源（与离场清理同一路径）
            }
            _autoHeadshot?.Tick();
        }

        public void Detach()
        {
            _headshotDebug = null;
            _autoHeadshot?.Disable();               // 先关闭（还原设备源），再弃引用
            _autoHeadshot = null;
            _input = null;
        }
    }
}
