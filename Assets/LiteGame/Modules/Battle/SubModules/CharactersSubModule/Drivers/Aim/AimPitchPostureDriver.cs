using System;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 俯仰瞄准姿态（第三人称自由视角的**姿态覆写器**）：把上半身（人形 HumanBodyBones.Chest 骨）按相机 pitch 前俯/后仰。
    /// </summary>
    public sealed class AimPitchPostureDriver
    {
        private readonly Animator _animator;
        private readonly Transform _chest;
        private readonly Transform _weapon;

        /// <summary>本约束是否可用（Chest 骨经人形映射解析到才可用；不可用时 Update 显性拒绝）。</summary>
        public bool IsValid => _chest != null;

        /// <summary>已实际覆写的帧数（诊断/测试读值：射击语境内应随帧增长，语境外恒 0）。</summary>
        public int AppliedFrames { get; private set; }

        /// <summary>构造：枪骨路径取自 <paramref name="catalog"/>（grip.bin 数据产物）。
        /// catalog 必填（null 抛 <see cref="ArgumentNullException"/>）。</summary>
        public AimPitchPostureDriver(Animator animator, WeaponGripCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            _animator = animator;
            // 无 Avatar（灰盒替身/清单供片的测试视图）没有 humanoid 骨骼映射：
            // GetBoneTransform 会抛 "Avatar is null"——显性退化（IsValid=false）才是契约。
            if (animator == null || animator.avatar == null) return;
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _weapon = animator.transform.Find(catalog.WeaponPath);
        }

        /// <summary>射击语境在场时按相机 pitch 覆写胸骨俯仰，并把同一世界旋转镜像到根级枪骨；
        /// 语境外本帧不写（动画值原样保留）。不可用 → 返回 false（调用方据此计入诊断）。</summary>
        public bool Update(bool aimingStance, float cameraPitchRad)
        {
            if (!IsValid) return false;
            if (!aimingStance) return true;

            // 角色右轴正角 = 前俯（+forward 向 −up 旋转）；相机上仰（pitch 正）= 胸腔后仰 ⇒ 取负。
            // 角度 = AimPitchPosture 映射（区间内恒等、越界钳制）。
            float angleDeg = -AimPitchPosture.UpperBodyPitch(cameraPitchRad) * Mathf.Rad2Deg;
            Quaternion pitch = Quaternion.AngleAxis(angleDeg, _animator.transform.right);
            Vector3 pivot = _chest.position;
            _chest.rotation = pitch * _chest.rotation;

            // 枪骨在 root 下（非 Chest 子级）：按 Chest 同一刚体旋转绕 Chest 枢轴补一次，
            // 手随胸转后枪仍在手里；枪自身的动画局部姿态（含后坐）原样保留。
            if (_weapon != null && !_weapon.IsChildOf(_chest))
            {
                _weapon.rotation = pitch * _weapon.rotation;
                _weapon.position = pivot + pitch * (_weapon.position - pivot);
            }
            AppliedFrames++;
            return true;
        }
    }
}
