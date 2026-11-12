using System.Collections.Generic;
using LiteGameNext.Platform.Config;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>角色动画播放器：把动画态映射到 Animator 参数；无 Animator 时静默跳过。</summary>
    public sealed class CharacterAnimPlayer
    {
        private const string SpeedParam = "Speed";
        private const string AimingParam = "Aiming";
        private const string DeadParam = "Dead";
        private const string HitParam = "Hit";
        private const string ReloadParam = "Reload";

        private readonly Animator _animator;
        private readonly IReadOnlyDictionary<string, AnimProfile> _profiles;
        private CharacterAnimState _current = CharacterAnimState.Idle;

        /// <param name="profiles">`tbanimationprofile` 投影；null = 用内置速度阈值。</param>
        public CharacterAnimPlayer(Animator animator, IReadOnlyDictionary<string, AnimProfile> profiles = null)
        {
            _animator = animator;
            _profiles = profiles;
        }

        /// <summary>当前动画态（诊断/测试）。</summary>
        public CharacterAnimState Current => _current;

        /// <summary>是否已接 Animator（无则为纯逻辑模式）。</summary>
        public bool HasAnimator => _animator != null;

        /// <summary>按事实推进动画；态变化时写参数。</summary>
        public void Apply(in CharacterAnimFacts facts)
        {
            CharacterAnimState next = CharacterAnimSelector.Select(facts, _profiles);
            bool changed = next != _current;
            _current = next;
            if (_animator == null || !_animator.isActiveAndEnabled) return;

            _animator.SetFloat(SpeedParam, facts.MoveSpeed);
            _animator.SetBool(AimingParam, facts.IsAiming);
            _animator.SetBool(DeadParam, facts.IsDead);
            _animator.SetBool(ReloadParam, facts.IsReloading);
            if (changed && next == CharacterAnimState.Hit) _animator.SetTrigger(HitParam);
        }

        /// <summary>强制重置到 Idle（复活/换实体）。</summary>
        public void Reset()
        {
            _current = CharacterAnimState.Idle;
            if (_animator == null) return;
            _animator.SetFloat(SpeedParam, 0f);
            _animator.SetBool(AimingParam, false);
            _animator.SetBool(DeadParam, false);
            _animator.SetBool(ReloadParam, false);
        }
    }
}
