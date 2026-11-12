using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>角色动画态（对应 12 个片段键：Idle/Walk/Run/AimIdle/AimWalk 四向/Reload/Hit1/Die2/Evade）。</summary>
    public enum CharacterAnimState
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        AimIdle = 3,
        AimWalkForward = 4,
        AimWalkRight = 5,
        AimWalkBack = 6,
        AimWalkLeft = 7,
        Reload = 8,
        Hit = 9,
        Dead = 10,
        Evade = 11,
    }

    /// <summary>角色动画输入事实（由快照/事件推导；不读判定）。</summary>
    public readonly struct CharacterAnimFacts
    {
        public CharacterAnimFacts(bool isDead, bool isAiming, bool isReloading, float moveSpeed,
            Vector3 localVelocity, float hitAtSeconds, float nowSeconds)
        {
            IsDead = isDead;
            IsAiming = isAiming;
            IsReloading = isReloading;
            MoveSpeed = moveSpeed;
            LocalVelocity = localVelocity;
            HitAtSeconds = hitAtSeconds;
            NowSeconds = nowSeconds;
        }

        public bool IsDead { get; }
        public bool IsAiming { get; }
        public bool IsReloading { get; }
        public float MoveSpeed { get; }
        public Vector3 LocalVelocity { get; }

        /// <summary>最近一次受击时刻（用于命中动画窗口）。</summary>
        public float HitAtSeconds { get; }

        public float NowSeconds { get; }
    }

    /// <summary>角色动画态选择：死亡/受击/换弹优先，其次瞄准（四向 strafe），否则移动混合（idle/walk/run）。</summary>
    public static class CharacterAnimSelector
    {
        /// <summary>命中动画持续窗口（秒）。</summary>
        public const float HitWindowSeconds = 0.25f;

        /// <summary>低于此速度视为静止。</summary>
        public const float IdleSpeedThreshold = 0.1f;

        /// <summary>高于此速度用跑步动画。</summary>
        public const float RunSpeedThreshold = 3.5f;

        /// <summary>按事实选动画态；优先级：Dead &gt; Hit &gt; Reload &gt; Aim &gt; Move。</summary>
        public static CharacterAnimState Select(in CharacterAnimFacts facts)
        {
            if (facts.IsDead) return CharacterAnimState.Dead;
            if (facts.NowSeconds - facts.HitAtSeconds <= HitWindowSeconds) return CharacterAnimState.Hit;
            if (facts.IsReloading) return CharacterAnimState.Reload;

            if (facts.IsAiming)
            {
                if (facts.MoveSpeed < IdleSpeedThreshold) return CharacterAnimState.AimIdle;
                return AimWalkOf(facts.LocalVelocity);
            }

            if (facts.MoveSpeed < IdleSpeedThreshold) return CharacterAnimState.Idle;
            return facts.MoveSpeed >= RunSpeedThreshold ? CharacterAnimState.Run : CharacterAnimState.Walk;
        }

        /// <summary>瞄准移动四向：按速度最大轴选 strafe 片段。</summary>
        private static CharacterAnimState AimWalkOf(Vector3 velocity)
        {
            float x = Mathf.Abs(velocity.x);
            float z = Mathf.Abs(velocity.z);
            if (x >= z)
                return velocity.x >= 0f ? CharacterAnimState.AimWalkRight : CharacterAnimState.AimWalkLeft;
            return velocity.z >= 0f ? CharacterAnimState.AimWalkForward : CharacterAnimState.AimWalkBack;
        }

        /// <summary>态 → 片段键（与 `CombatGirlsAnimationClips.asset` 的 Key 一致）。</summary>
        public static string ClipKey(CharacterAnimState state)
        {
            switch (state)
            {
                case CharacterAnimState.Idle: return "Idle";
                case CharacterAnimState.Walk: return "Walk";
                case CharacterAnimState.Run: return "Run";
                case CharacterAnimState.AimIdle: return "AimIdle";
                case CharacterAnimState.AimWalkForward: return "AimWalk_F";
                case CharacterAnimState.AimWalkRight: return "AimWalk_R";
                case CharacterAnimState.AimWalkBack: return "AimWalk_B";
                case CharacterAnimState.AimWalkLeft: return "AimWalk_L";
                case CharacterAnimState.Reload: return "Reload";
                case CharacterAnimState.Hit: return "Hit1";
                case CharacterAnimState.Dead: return "Die2";
                case CharacterAnimState.Evade: return "Evade";
                default: return "Idle";
            }
        }
    }
}
