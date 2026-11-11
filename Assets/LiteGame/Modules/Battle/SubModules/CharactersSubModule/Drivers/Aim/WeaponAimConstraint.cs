using System;
using UnityEngine;

namespace LiteGame.Modules.Battle.SubModules.CharactersSubModule.Drivers.Aim
{
    /// <summary>
    /// 瞄准枪口对齐（TPS **ADS / 腰射**的上半身姿态修正）：把枪口前向指向准心世界点， 但**旋转施加在胸腔 + 根级枪骨这一对刚体上**——手是胸腔子骨，跟着一起转。
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

        /// <summary>构造：枪骨路径取自 <paramref name="catalog"/>（grip.bin 数据产物）。
        /// catalog 必填（null 抛 <see cref="ArgumentNullException"/>）。</summary>
        public WeaponAimConstraint(Animator animator, WeaponGripCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            // 无 Avatar（灰盒替身/清单供片的测试视图）没有 humanoid 骨骼映射：GetBoneTransform 会抛
            // "Avatar is null"——显性退化（IsValid=false）才是契约（同 AimPitchPostureDriver）。
            if (animator == null || animator.avatar == null) return;
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _weapon = animator.transform.Find(catalog.WeaponPath);
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
