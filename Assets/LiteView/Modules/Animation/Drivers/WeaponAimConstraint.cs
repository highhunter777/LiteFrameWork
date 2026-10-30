using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 瞄准枪口对齐（TPS **ADS / 腰射**的上半身姿态修正）：把枪口前向指向准心世界点，
    /// 但**旋转施加在胸腔 + 根级枪骨这一对刚体上**——手是胸腔子骨，跟着一起转，
    /// 握持关系不动（只转枪骨会让手留在原地 ⇒ 手/枪分离）。
    ///
    /// **为什么是胸腔**：枪骨 <c>root/add_weapon_r</c> 与持枪手在动画里是同一握持，
    /// 但枪骨不是 Chest 子级；把同一世界旋转同时施加到 Chest 与枪骨（枪骨绕 Chest 枢轴
    /// 平移+旋转），等价于把枪"焊"在上半身，手/枪相对位姿保持不变。
    ///
    /// **角度上限**：ADS 20°、腰射 45°——动画 AimIdle/FireIdle 本身应大致朝前，修正只吃残余；
    /// 超出上限说明姿态/锚点资源错了，不能靠硬掰掩盖。
    ///
    /// **写序**：必须在动画 Evaluate + <see cref="AimPitchPostureDriver"/> 之后；只在瞄准/开火窗写，
    /// 退出即停写，下一帧动画自然接管（与持枪约束同一单一写者契约）。
    /// </summary>
    public sealed class WeaponAimConstraint
    {
        /// <summary>ADS 单帧最大修正角（度）——AimIdle 已大致朝前，只吃残余。</summary>
        public const float MaxAdsCorrectionDegrees = 20f;

        /// <summary>腰射单帧最大修正角（度）——FireIdle 比 AimIdle 低，允许更大的抬枪量。</summary>
        public const float MaxHipCorrectionDegrees = 45f;

        private readonly Transform _chest;
        private readonly Transform _weapon;
        private readonly Transform _muzzle;

        /// <summary>Chest / 枪骨 / Muzzle 都解析到才可用（灰盒/异形 Rig 显性跳过）。</summary>
        public bool IsValid => _chest != null && _weapon != null && _muzzle != null;

        /// <summary>已实际修正的帧数（诊断/测试读值；非射击语境恒 0）。</summary>
        public int AppliedFrames { get; private set; }

        public WeaponAimConstraint(Animator animator)
        {
            if (animator == null) return;
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _weapon = animator.transform.Find(GripBake.WeaponPath);
            _muzzle = FindDeep(animator.transform, "Muzzle");
        }

        /// <summary>瞄准/开火语境下把枪口前向对齐到 <paramref name="aimPoint"/>；两会话都不在则不写。
        /// ADS 与腰射用不同修正上限（腰射 FireIdle 更低）。不可用 → 返回 false。</summary>
        public bool Update(bool aiming, bool firing, Vector3 aimPoint)
        {
            if (!IsValid) return false;
            if (!aiming && !firing) return true;

            float maxCorrection = aiming ? MaxAdsCorrectionDegrees : MaxHipCorrectionDegrees;
            Vector3 toTarget = aimPoint - _muzzle.position;
            if (toTarget.sqrMagnitude < 1e-6f) return true;

            Quaternion delta = Quaternion.FromToRotation(_muzzle.forward, toTarget.normalized);
            if (Quaternion.Angle(Quaternion.identity, delta) > maxCorrection)
                delta = Quaternion.RotateTowards(Quaternion.identity, delta, maxCorrection);

            // 胸腔与枪骨同刚体：Chest 绕自身枢轴转，枪骨绕同一枢轴平移+旋转；手是 Chest 子骨，
            // 因此握持关系保持。枪骨若已是 Chest 子级则只转 Chest（避免二次旋转）。
            Vector3 pivot = _chest.position;
            _chest.rotation = delta * _chest.rotation;
            if (!_weapon.IsChildOf(_chest))
            {
                _weapon.rotation = delta * _weapon.rotation;
                _weapon.position = pivot + delta * (_weapon.position - pivot);
            }

            AppliedFrames++;
            return true;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
