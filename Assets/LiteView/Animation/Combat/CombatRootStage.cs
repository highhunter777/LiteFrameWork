using LiteFramework;
using LiteFramework.Animation;
using LiteSim;
using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>叶/根共用的语境让位检查：已有挂起请求（事件路由进 Fire 系 / 根的退根降级）时
    /// 本帧不再发自己的请求——last-wins 语义下"后来的赢"，让位保证已挂的更高级裁决不被覆盖。</summary>
    internal static class StageGate
    {
        internal static bool Pending(IStageHost<CharacterAnimId, CombatAnimReq> m)
            => m is HierarchicalStageMachine<CharacterAnimId, CombatAnimReq> hsm && hsm.HasPending;
    }

    /// <summary>
    /// 战斗根复合态（上层）：射击窗持有者 + 退根/降级/换弹**单点裁决**（窗递减与
    /// "窗尽 → 去哪"、换弹进叶/出叶都收在此处，叶只发层内轴请求且先让位）。
    /// 优先级：死亡 &gt; 换弹 &gt; 开火/瞄准（窗）。
    /// </summary>
    internal sealed class CombatRootStage : IStage<CharacterAnimId, CombatAnimReq>
    {
        private readonly SlotAnimContext _ctx;
        private float _window;

        internal CombatRootStage(SlotAnimContext ctx)
        {
            _ctx = ctx;
            ctx.Root = this;
        }

        /// <summary>射击窗剩余（秒）——测试/诊断读值（0 = 窗不在）。</summary>
        internal float Window => _window;

        /// <summary>事件刷新＝重置满窗（上限即窗长，不累加）——驱动器在事件路由前调用（窗先于一切门控）。</summary>
        internal void RefreshWindow() => _window = _ctx.FireHoldSeconds;

        public void OnInit(IStageHost<CharacterAnimId, CombatAnimReq> m) { }
        public void OnEnter(IStageHost<CharacterAnimId, CombatAnimReq> m, in CombatAnimReq req) { }

        public void OnUpdate(IStageHost<CharacterAnimId, CombatAnimReq> m, float elapseSeconds)
        {
            // 死亡裁决（**压过一切**——含已挂起的事件路由，last-wins 后发者赢）：
            // 死亡期恒驻 Dead 叶；复活后按新的存活事实退出
            if (_ctx.IsDead)
            {
                if (m.Current != CharacterAnimId.Dead) m.Request(CharacterAnimId.Dead);
                return;
            }

            if (m.Current == CharacterAnimId.Dead)
            {
                _window = 0f;
                m.Request(_ctx.IsAiming
                    ? (_ctx.IsMoving ? CharacterAnimId.AimWalk : CharacterAnimId.AimIdle)
                    : (_ctx.IsMoving ? CharacterAnimId.Moving : CharacterAnimId.Idle));
                return;
            }

            if (StageGate.Pending(m)) return;                 // 事件路由已挂（进 Fire 系优先）——本帧不裁决

            // `IsAiming` 在场即充值窗（Sim 侧瞄准帧同步置窗——两层同源同长；
            // 长按 ADS 的松开尾巴与点按瞄准的间隙尾巴同一语义：窗内不回移动层）。
            // **窗先于换弹裁决更新**：Sim 侧 `FireStanceFrames` 在换弹期照常递减/瞄准充值——
            // 两层同源同长，换弹期间也不许分叉（否则换弹结束时视图驻留与限速解除错位）
            if (_ctx.IsAiming) _window = _ctx.FireHoldSeconds;
            else if (_window > 0f) _window = Mathf.Max(0f, _window - elapseSeconds);

            // 换弹裁决（**压过开火/瞄准**——Sim 侧换弹期不可开火，表现跟随事实）：
            // 事实在场恒驻 Reload 叶；事实清除后同帧继续走下方窗裁决选叶
            if (_ctx.IsReloading)
            {
                if (m.Current != CharacterAnimId.Reloading) m.Request(CharacterAnimId.Reloading);
                return;
            }

            // 退根/降级单点裁决（**全族同一条退根路**：窗尽 ∧ !IsAiming；
            // 移动事实永不触发退根，只选叶）：
            //   窗尽 ∧ IsAiming → Fire*/Reloading 降级 Aim 叶（同形态续播保相位；Aim 系无需请求——已在 Aim 叶）
            //   窗尽 ∧ !IsAiming → 移动根叶（窗尽才离开——窗内"不能回移动层"）
            if (_window <= 0f)
            {
                if (_ctx.IsAiming)
                {
                    if (m.Current == CharacterAnimId.FireIdle || m.Current == CharacterAnimId.FireWalk
                        || m.Current == CharacterAnimId.Reloading)
                        m.Request(_ctx.IsMoving ? CharacterAnimId.AimWalk : CharacterAnimId.AimIdle);
                    // Aim 系 + IsAiming：已在瞄准叶——无动作
                }
                else
                {
                    m.Request(_ctx.IsMoving ? CharacterAnimId.Moving : CharacterAnimId.Idle);
                }
                return;
            }

            // 窗在：换弹叶出（事实刚清除）→ 回 Fire 系持枪站姿（窗内不回移动层）
            if (m.Current == CharacterAnimId.Reloading)
                m.Request(_ctx.IsMoving ? CharacterAnimId.FireWalk : CharacterAnimId.FireIdle);
        }

        public void OnLeave(IStageHost<CharacterAnimId, CombatAnimReq> m)
        {
            // 退根收口：Override 通道所有权终止（次根同帧提交替换/淡出——事务序先退后进）；
            // 窗语义随离场清零（再进战斗根必经事件刷新或 ADS 建立）
            if (_ctx.BodyHandle.IsValid)
                _ctx.Player.Stop(_ctx.BodyHandle, AnimationStopReason.Cancelled);
            _ctx.BodyForm = default;
            _ctx.BodyHandle = default;
            _ctx.BodyIsBlend = false;
            _window = 0f;
        }
    }
}
